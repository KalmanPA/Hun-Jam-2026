using UnityEngine;

/// <summary>
/// A simple travelling bullet. It ray-casts ahead every frame so fast bullets don't pass through walls,
/// and it is removed when it reaches its gear's max range.
/// </summary>
public class GearBullet : MonoBehaviour
{
    private static readonly RaycastHit[] Hits = new RaycastHit[16];

    private Vector3 direction;
    private float speed;
    private float range;
    private float damage;
    private bool shrinkOverRange;
    private Vector3 baseScale;
    private Transform owner;
    private float travelled;
    private bool explodeOnEnd;
    private float explosionSize;
    private Material material;
    private Color color;

    public static GearBullet Spawn(Vector3 position, Vector3 direction, GearFireSettings settings, Transform owner, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "GearBullet";
        DestroyImmediate(go.GetComponent<Collider>());

        Renderer r = go.GetComponent<Renderer>();
        if (material != null) r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        float size = settings.bulletSize;
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
        go.transform.localScale = new Vector3(size, size, size * 2.5f);

        GearBullet b = go.AddComponent<GearBullet>();
        b.direction = direction.normalized;
        b.speed = settings.bulletSpeed;
        b.range = settings.range;
        b.damage = settings.damage;
        b.shrinkOverRange = settings.shrinkOverRange;
        b.baseScale = go.transform.localScale;
        b.owner = owner;
        b.explodeOnEnd = settings.explodeOnEnd;
        b.explosionSize = settings.explosionSize;
        b.material = material;
        b.color = settings.bulletColor;
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
            End(hit.point);
            return;
        }

        transform.position += direction * step;
        travelled += step;

        if (shrinkOverRange)
            transform.localScale = baseScale * Mathf.Clamp01(1f - travelled / range);

        if (lastStep)
            End(transform.position);
    }

    private void End(Vector3 position)
    {
        if (explodeOnEnd)
            GearExplosion.Spawn(position, explosionSize, material, color);
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
            if (owner != null && Hits[i].transform.IsChildOf(owner)) continue;
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
