using UnityEngine;

/// <summary>
/// Instant hitscan laser beam. Damages the first thing it hits, then the beam thins out and disappears.
/// </summary>
public class GearLaser : MonoBehaviour
{
    private static readonly RaycastHit[] Hits = new RaycastHit[16];

    private float width;
    private float duration;
    private float age;
    private float length;
    private Light glow;

    public static void Fire(Vector3 origin, Vector3 direction, GearFireSettings settings, Transform owner, Material beamMaterial, Material impactMaterial)
    {
        direction.Normalize();
        float length = settings.range;
        bool hitSomething = false;
        Vector3 hitPoint = origin + direction * length;

        int count = Physics.RaycastNonAlloc(origin, direction, Hits, settings.range, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        RaycastHit hit = default;
        for (int i = 0; i < count; i++)
        {
            if (owner != null && Hits[i].transform.IsChildOf(owner)) continue;
            if (Hits[i].distance < best) { best = Hits[i].distance; hit = Hits[i]; hitSomething = true; }
        }

        if (hitSomething)
        {
            length = hit.distance;
            hitPoint = hit.point;
            IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
            target?.TakeDamage(settings.damage);
            GameAudio.Play(SoundId.LaserImpact, hit.point);
            GearBullet.PushRigidbody(hit, direction, settings.damage);
            GearExplosion.Spawn(hitPoint, settings.laserWidth * 4f, impactMaterial, settings.bulletColor, 0.2f);
        }

        // Beam: a long cylinder from the muzzle to the hit point.
        GameObject go = new GameObject("GearLaser");
        go.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));

        GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beam.name = "Beam";
        DestroyImmediate(beam.GetComponent<Collider>());
        Renderer r = beam.GetComponent<Renderer>();
        if (beamMaterial != null) r.sharedMaterial = beamMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beam.transform.SetParent(go.transform, false);
        beam.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        GearLaser laser = go.AddComponent<GearLaser>();
        laser.width = settings.laserWidth;
        laser.duration = Mathf.Max(0.05f, settings.laserDuration);
        laser.length = length;

        GameObject lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(go.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 0f, Mathf.Min(length, 3f));
        laser.glow = lightGo.AddComponent<Light>();
        laser.glow.type = LightType.Point;
        laser.glow.color = settings.bulletColor;
        laser.glow.range = 6f;
        laser.glow.intensity = 3f;
        laser.glow.shadows = LightShadows.None;

        laser.Apply(1f);
    }

    private void Update()
    {
        age += Time.deltaTime;
        float t = Mathf.Clamp01(age / duration);
        Apply(1f - t * t);
        if (t >= 1f) Destroy(gameObject);
    }

    private void Apply(float strength)
    {
        Transform beam = transform.GetChild(0);
        float w = Mathf.Max(0.0001f, width * strength);
        beam.localScale = new Vector3(w, length * 0.5f, w);
        beam.localPosition = new Vector3(0f, 0f, length * 0.5f);
        if (glow != null) glow.intensity = 3f * strength;
    }
}
