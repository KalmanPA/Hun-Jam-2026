using UnityEngine;

/// <summary>
/// A robot's bullet. Visible and fairly slow so the player can read near misses and dodge.
/// It ray-casts ahead every frame, damages the first IDamageable it hits (the player through PlayerHealth),
/// and ignores the robot that fired it and other robots (no friendly fire).
/// </summary>
public class EnemyBullet : MonoBehaviour
{
    private static readonly RaycastHit[] Hits = new RaycastHit[16];

    private Vector3 direction;
    private float speed;
    private float range;
    private float damage;
    private float travelled;
    private Material material;
    private Color color;

    public static EnemyBullet Spawn(Vector3 position, Vector3 direction, float speed, float damage, float range, Material material, Color color, float size = 0.25f)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "EnemyBullet";
        DestroyImmediate(go.GetComponent<Collider>());

        Renderer r = go.GetComponent<Renderer>();
        if (material != null) r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
        go.transform.localScale = new Vector3(size, size, size * 1.6f);

        EnemyBullet b = go.AddComponent<EnemyBullet>();
        b.direction = direction.normalized;
        b.speed = speed;
        b.damage = damage;
        b.range = range;
        b.material = material;
        b.color = color;
        return b;
    }

    private void Update()
    {
        float step = speed * Time.deltaTime;
        bool lastStep = false;
        if (travelled + step >= range)
        {
            step = range - travelled;
            lastStep = true;
        }

        if (TryHit(step, out RaycastHit hit))
        {
            IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
            target?.TakeDamage(damage);
            GearExplosion.Spawn(hit.point, transform.localScale.x * 2f, material, color, 0.15f);
            GameAudio.Play(SoundId.EnemyBulletImpact, hit.point);
            Destroy(gameObject);
            return;
        }

        transform.position += direction * step;
        travelled += step;

        if (lastStep)
            Destroy(gameObject);
    }

    private bool TryHit(float distance, out RaycastHit best)
    {
        best = default;
        if (distance <= 0f) return false;

        int count = Physics.RaycastNonAlloc(transform.position, direction, Hits, distance, ~0, QueryTriggerInteraction.Ignore);
        float bestDistance = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            if (Hits[i].collider.GetComponentInParent<RobotEnemy>() != null) continue;
            if (Hits[i].distance < bestDistance)
            {
                bestDistance = Hits[i].distance;
                best = Hits[i];
                found = true;
            }
        }
        return found;
    }
}
