using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FinalShift.ShaderTest.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/ShaderTestScene.unity: a small dark room with flat-coloured cubes
    /// that use the "Final Shift/Flat Triplanar Grime" shader.
    /// The scene is built automatically the first time this script compiles (if it doesn't exist yet),
    /// and can be rebuilt any time from the menu: Final Shift > Shader Test > Rebuild And Open ShaderTestScene.
    /// </summary>
    [InitializeOnLoad]
    static class ShaderTestSceneBuilder
    {
        const string Root = "Assets/ShaderTest";
        const string ScenePath = "Assets/Scenes/ShaderTestScene.unity";
        const string ShaderPath = Root + "/Shaders/FlatTriplanarGrime.shader";
        const string GrimePath = Root + "/Textures/Grime64.png";
        const string MaterialFolder = Root + "/Materials";

        static readonly Color FogColor = new Color(0.035f, 0.032f, 0.03f);

        static ShaderTestSceneBuilder()
        {
            EditorApplication.delayCall += AutoBuildOnce;
        }

        static void AutoBuildOnce()
        {
            if (File.Exists(ScenePath) || EditorApplication.isPlayingOrWillChangePlaymode) return;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                {
                    Debug.Log("[ShaderTest] An unsaved scene is open, so ShaderTestScene was not built automatically. " +
                              "Use Final Shift > Shader Test > Rebuild And Open ShaderTestScene.");
                    return;
                }
            }

            Build(openAfter: false);
        }

        [MenuItem("Final Shift/Shader Test/Rebuild And Open ShaderTestScene")]
        static void RebuildAndOpen()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(openAfter: true);
        }

        static void Build(bool openAfter)
        {
            ConfigureGrimeTexture();

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Texture2D grime = AssetDatabase.LoadAssetAtPath<Texture2D>(GrimePath);
            if (shader == null || grime == null)
            {
                Debug.LogWarning("[ShaderTest] Shader or grime texture not found under " + Root + ". Scene not built.");
                return;
            }

            // Materials: just a base colour each. The grime texture and lighting come from the shader.
            Material floorMat    = MakeMaterial("Floor_OilySteel",  new Color(0.24f, 0.23f, 0.22f), shader, grime, 0.9f, false);
            Material wallMat     = MakeMaterial("Wall_Concrete",    new Color(0.34f, 0.32f, 0.29f), shader, grime, 0.8f, false);
            Material rustMat     = MakeMaterial("Cube_Rust",        new Color(0.50f, 0.25f, 0.13f), shader, grime, 0.8f, false);
            Material steelMat    = MakeMaterial("Cube_Steel_Moving", new Color(0.38f, 0.41f, 0.44f), shader, grime, 0.8f, true);
            Material warningMat  = MakeMaterial("Cube_WarningYellow", new Color(0.78f, 0.60f, 0.12f), shader, grime, 0.7f, false);
            Material darkRustMat = MakeMaterial("Crate_DarkRust",   new Color(0.30f, 0.16f, 0.10f), shader, grime, 0.9f, false);

            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                openAfter ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            // Dark room, dark fog, near-black ambient.
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.04f, 0.04f, 0.045f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = 5f;
            RenderSettings.fogEndDistance = 20f;

            // Camera you can fly around in Play mode.
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = FogColor;
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<ShaderTestFlyCamera>();
            camGo.transform.position = new Vector3(0f, 1.7f, -5f);
            camGo.transform.LookAt(new Vector3(0f, 0.7f, 0.5f));

            // Weak, cold "moonlight" through the roof.
            GameObject sunGo = new GameObject("Directional Light (dim)");
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.55f, 0.62f, 0.75f);
            sun.intensity = 0.35f;
            sun.shadows = LightShadows.Hard;
            sunGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

            // Warm, flickering factory lamp: the main light source.
            GameObject lampGo = new GameObject("Lamp (flickering)");
            Light lamp = lampGo.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.55f, 0.22f);
            lamp.intensity = 4f;
            lamp.range = 7f;
            lamp.shadows = LightShadows.Hard;
            lampGo.AddComponent<ShaderTestFlicker>();
            lampGo.transform.position = new Vector3(1.4f, 2.4f, -0.8f);

            // Room.
            MakeCube("Floor", new Vector3(0f, -0.1f, 2f), new Vector3(14f, 0.2f, 14f), floorMat);
            MakeCube("Back Wall", new Vector3(0f, 2f, 5f), new Vector3(14f, 4.2f, 0.3f), wallMat);
            MakeCube("Left Wall", new Vector3(-5f, 2f, 2f), new Vector3(0.3f, 4.2f, 14f), wallMat);

            // The flat-coloured test cubes.
            MakeCube("FlatCube_Rust", new Vector3(-1.6f, 0.5f, 0.6f), Vector3.one, rustMat);
            GameObject spinning = MakeCube("FlatCube_Steel_Spinning", new Vector3(0f, 0.9f, 0f), Vector3.one, steelMat);
            spinning.AddComponent<ShaderTestSpin>();
            MakeCube("FlatCube_WarningPillar", new Vector3(1.8f, 0.75f, 0.8f), new Vector3(0.8f, 1.5f, 0.8f), warningMat);
            MakeCube("BigCrate_DarkRust", new Vector3(-3.2f, 1f, 3f), new Vector3(2f, 2f, 2f), darkRustMat);

            EditorSceneManager.SaveScene(scene, ScenePath);

            if (!openAfter)
            {
                if (previousActive.IsValid()) SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(scene, true);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[ShaderTest] Built " + ScenePath + ". Open it and press Play to fly around (right mouse + WASD).");
        }

        static GameObject MakeCube(string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        static Material MakeMaterial(string name, Color baseColor, Shader shader, Texture2D grime, float grimeStrength, bool objectSpace)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder(Root, "Materials");

            string path = MaterialFolder + "/" + name + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = shader;
            mat.SetColor("_BaseColor", baseColor);
            mat.SetTexture("_GrimeTex", grime);
            mat.SetFloat("_GrimeStrength", grimeStrength);
            mat.SetFloat("_ObjectSpace", objectSpace ? 1f : 0f);
            if (objectSpace) mat.EnableKeyword("_OBJECT_SPACE");
            else mat.DisableKeyword("_OBJECT_SPACE");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void ConfigureGrimeTexture()
        {
            TextureImporter importer = AssetImporter.GetAtPath(GrimePath) as TextureImporter;
            if (importer == null) return;

            bool changed = importer.filterMode != FilterMode.Point
                           || importer.mipmapEnabled
                           || importer.textureCompression != TextureImporterCompression.Uncompressed
                           || importer.wrapMode != TextureWrapMode.Repeat;
            if (!changed) return;

            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
        }
    }
}
