using UnityEngine;

/// <summary>
/// Small placeholder explosion: a glowing ball that pops out fast, then shrinks away, with a short flash of light.
/// </summary>
public class GearExplosion : MonoBehaviour
{
    private float size;
    private float duration;
    private float age;
    private Light flash;
    private float flashIntensity;

    public static void Spawn(Vector3 position, float size, Material material, Color lightColor, float duration = 0.3f)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "GearExplosion";
        DestroyImmediate(go.GetComponent<Collider>());
        Renderer r = go.GetComponent<Renderer>();
        if (material != null) r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.transform.position = position;
        go.transform.localScale = Vector3.zero;

        GearExplosion e = go.AddComponent<GearExplosion>();
        e.size = size;
        e.duration = Mathf.Max(0.05f, duration);

        GameObject lightGo = new GameObject("Flash");
        lightGo.transform.SetParent(go.transform, false);
        e.flash = lightGo.AddComponent<Light>();
        e.flash.type = LightType.Point;
        e.flash.color = lightColor;
        e.flash.range = size * 6f;
        e.flashIntensity = 4f;
        e.flash.intensity = e.flashIntensity;
        e.flash.shadows = LightShadows.None;
    }

    private void Update()
    {
        age += Time.deltaTime;
        float t = Mathf.Clamp01(age / duration);

        // Pop out in the first 30%, then shrink away.
        float scale = t < 0.3f ? Mathf.SmoothStep(0f, 1f, t / 0.3f) : Mathf.SmoothStep(1f, 0f, (t - 0.3f) / 0.7f);
        transform.localScale = Vector3.one * size * scale;
        if (flash != null) flash.intensity = flashIntensity * (1f - t);

        if (t >= 1f) Destroy(gameObject);
    }
}
