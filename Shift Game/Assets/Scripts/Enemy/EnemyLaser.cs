using UnityEngine;

/// <summary>
/// Instant laser shot for sniper robots, like the player's gear 1: damages the first thing it hits
/// (the player through PlayerHealth), then the beam thins out and disappears.
/// Ignores robots, so snipers never hit each other.
/// </summary>
public class EnemyLaser : MonoBehaviour
{
    private static readonly RaycastHit[] Hits = new RaycastHit[16];

    private float width;
    private float duration;
    private float length;
    private float age;
    private Transform beam;
    private Light glow;

    public static void Fire(Vector3 origin, Vector3 direction, float range, float damage, float width, float duration, Material material, Color color)
    {
        direction.Normalize();
        float length = range;

        int count = Physics.RaycastNonAlloc(origin, direction, Hits, range, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        RaycastHit hit = default;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            if (Hits[i].collider.GetComponentInParent<RobotEnemy>() != null) continue;
            if (Hits[i].distance < best) { best = Hits[i].distance; hit = Hits[i]; found = true; }
        }

        if (found)
        {
            length = hit.distance;
            hit.collider.GetComponentInParent<IDamageable>()?.TakeDamage(damage);
            GearExplosion.Spawn(hit.point, width * 4f, material, color, 0.2f);
            GameAudio.Play(SoundId.EnemyBulletImpact, hit.point);
        }

        GameObject go = new GameObject("EnemyLaser");
        go.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));

        GameObject beamGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beamGo.name = "Beam";
        DestroyImmediate(beamGo.GetComponent<Collider>());
        Renderer r = beamGo.GetComponent<Renderer>();
        if (material != null) r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beamGo.transform.SetParent(go.transform, false);
        beamGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        EnemyLaser laser = go.AddComponent<EnemyLaser>();
        laser.width = width;
        laser.duration = Mathf.Max(0.05f, duration);
        laser.length = length;
        laser.beam = beamGo.transform;

        GameObject lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(go.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 0f, Mathf.Min(length, 3f));
        laser.glow = lightGo.AddComponent<Light>();
        laser.glow.type = LightType.Point;
        laser.glow.color = color;
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
        float w = Mathf.Max(0.0001f, width * strength);
        beam.localScale = new Vector3(w, length * 0.5f, w);
        beam.localPosition = new Vector3(0f, 0f, length * 0.5f);
        if (glow != null) glow.intensity = 3f * strength;
    }
}
