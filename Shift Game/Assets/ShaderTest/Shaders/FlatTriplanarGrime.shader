// Final Shift - Flat colour + triplanar grime shader (URP, Forward and Forward+).
//
// Each material is just a base colour. A small tiling grime texture is projected
// from world space on all three axes (triplanar), so models need no UVs.
// Lighting is quantised into a few bands and dithered with a 4x4 Bayer pattern,
// and fog is dithered the same way, for a crisp PS1-era look at native resolution.
Shader "Final Shift/Flat Triplanar Grime"
{
    Properties
    {
        [Header(Surface)]
        _BaseColor ("Base Color", Color) = (0.45, 0.24, 0.14, 1)
        [NoScaleOffset] _GrimeTex ("Grime Texture (tiling, greyscale)", 2D) = "white" {}
        _GrimeScale ("Grime Tiles Per Meter", Float) = 0.5
        _GrimeStrength ("Grime Strength", Range(0, 1)) = 0.8
        _GrimeContrast ("Grime Contrast", Range(0.2, 3)) = 1.3
        _TriplanarSharpness ("Triplanar Blend Sharpness", Range(1, 32)) = 12
        [Toggle(_OBJECT_SPACE)] _ObjectSpace ("Object Space Grime (for moving objects)", Float) = 0

        [Header(Lighting)]
        _Ambient ("Ambient Light", Color) = (0.06, 0.055, 0.05, 1)
        _LightBands ("Light Bands", Range(2, 12)) = 5
        _DitherStrength ("Dither Strength", Range(0, 1)) = 1
        [Toggle] _ReceiveShadows ("Receive Shadows", Float) = 1

        [Header(Fog)]
        _FogBands ("Fog Bands", Range(2, 16)) = 6

        [Header(PS1 Vertex Jitter)]
        [Toggle(_VERTEX_SNAP)] _VertexSnap ("Vertex Snap", Float) = 0
        _SnapResolution ("Snap Grid (x, y)", Vector) = (160, 120, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma shader_feature_local _VERTEX_SNAP
            #pragma shader_feature_local _OBJECT_SPACE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"

            // 1 brings back the grime texture and the dithered light/fog bands (the "noise"). 0 = clean flat colour.
            #define NOISE_ENABLED 0

            TEXTURE2D(_GrimeTex);
            SAMPLER(sampler_point_repeat);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _GrimeScale;
                half _GrimeStrength;
                half _GrimeContrast;
                half _TriplanarSharpness;
                half4 _Ambient;
                half _LightBands;
                half _DitherStrength;
                half _ReceiveShadows;
                half _FogBands;
                float4 _SnapResolution;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogCoord   : TEXCOORD2;
                float3 triPos     : TEXCOORD3;
                float3 triNormal  : TEXCOORD4;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);

                #if defined(_OBJECT_SPACE)
                    // Grime sticks to the object when it moves or rotates. Object scale is kept
                    // so a stretched cube still gets square texels.
                    float3 objectScale = float3(
                        length(GetObjectToWorldMatrix()._m00_m10_m20),
                        length(GetObjectToWorldMatrix()._m01_m11_m21),
                        length(GetObjectToWorldMatrix()._m02_m12_m22));
                    OUT.triPos = IN.positionOS.xyz * objectScale;
                    OUT.triNormal = IN.normalOS;
                #else
                    // Grime is locked to the world, so separate pieces of a level line up.
                    OUT.triPos = OUT.positionWS;
                    OUT.triNormal = OUT.normalWS;
                #endif

                float4 positionCS = TransformWorldToHClip(OUT.positionWS);

                #if defined(_VERTEX_SNAP)
                    // Snap the projected vertex to a coarse screen grid, like the PS1 did.
                    float2 grid = max(_SnapResolution.xy, 1.0) * 0.5;
                    float2 ndc = positionCS.xy / positionCS.w;
                    ndc = floor(ndc * grid + 0.5) / grid;
                    positionCS.xy = ndc * positionCS.w;
                #endif

                OUT.positionCS = positionCS;
                OUT.fogCoord = ComputeFogFactor(positionCS.z);
                return OUT;
            }

            // 4x4 ordered dither threshold in [0, 1).
            static const half kBayer4x4[16] =
            {
                 0.0h,  8.0h,  2.0h, 10.0h,
                12.0h,  4.0h, 14.0h,  6.0h,
                 3.0h, 11.0h,  1.0h,  9.0h,
                15.0h,  7.0h, 13.0h,  5.0h
            };

            half Bayer4x4(float2 pixel)
            {
                uint2 p = uint2(pixel) % 4u;
                return (kBayer4x4[p.y * 4u + p.x] + 0.5h) / 16.0h;
            }

            // Quantise a value into bands, using the dither threshold to pick between neighbours.
            half3 BandDither(half3 value, half bands, half threshold)
            {
                return floor(value * bands + threshold) / bands;
            }

            half SampleGrimeTriplanar(float3 position, float3 normal)
            {
                float3 p = position * _GrimeScale;
                float3 w = pow(abs(normalize(normal)), _TriplanarSharpness);
                w /= max(w.x + w.y + w.z, 1e-5);

                half gx = SAMPLE_TEXTURE2D(_GrimeTex, sampler_point_repeat, p.zy).r;
                half gy = SAMPLE_TEXTURE2D(_GrimeTex, sampler_point_repeat, p.xz).r;
                half gz = SAMPLE_TEXTURE2D(_GrimeTex, sampler_point_repeat, p.xy).r;
                half g = gx * w.x + gy * w.y + gz * w.z;

                return saturate((g - 0.5h) * _GrimeContrast + 0.5h);
            }

            half3 LambertLight(float3 normalWS, Light light)
            {
                half ndotl = saturate(dot(normalWS, light.direction));
                return ndotl * light.color * light.distanceAttenuation * light.shadowAttenuation;
            }

            half3 GatherLighting(InputData inputData)
            {
                float4 shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                if (_ReceiveShadows < 0.5h) mainLight.shadowAttenuation = 1.0h;
                half3 lighting = LambertLight(inputData.normalWS, mainLight);

                #if defined(_ADDITIONAL_LIGHTS) || defined(_CLUSTER_LIGHT_LOOP)
                    #if USE_CLUSTER_LIGHT_LOOP
                    UNITY_LOOP for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                    {
                        Light dirLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
                        lighting += LambertLight(inputData.normalWS, dirLight);
                    }
                    #endif

                    uint pixelLightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
                        lighting += LambertLight(inputData.normalWS, addLight);
                    LIGHT_LOOP_END
                #endif

                return lighting;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                // Kalman (2026-11-08): no noise at all. The grime texture and the Bayer dither (with the light/fog
                // banding it was there to hide) are switched off; the properties stay so materials keep loading.
                #if NOISE_ENABLED
                    half threshold = lerp(0.5h, Bayer4x4(IN.positionCS.xy), _DitherStrength);
                    half grime = SampleGrimeTriplanar(IN.triPos, IN.triNormal);
                    half3 albedo = _BaseColor.rgb * lerp(1.0h, grime, _GrimeStrength);
                #else
                    half3 albedo = _BaseColor.rgb;
                #endif

                half3 light = _Ambient.rgb + GatherLighting(inputData);
                #if NOISE_ENABLED
                    light = BandDither(saturate(light / 2.0h), _LightBands, threshold) * 2.0h;
                #endif
                half3 color = albedo * light;

                // Fog (uses the scene's Lighting > Fog settings).
                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                    half fog = ComputeFogIntensity(IN.fogCoord);
                    #if NOISE_ENABLED
                        fog = saturate(floor(fog * _FogBands + threshold) / _FogBands);
                    #endif
                    color = lerp(unity_FogColor.rgb, color, fog);
                #endif

                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        // Shadows and depth come from URP's Lit shader so the object casts shadows
        // and shows up in the depth prepass.
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
