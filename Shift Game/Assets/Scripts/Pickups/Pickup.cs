using UnityEngine;

/// <summary>
/// Health or ammo pickup dropped by robots. Spins and bobs; collected when the player walks into it.
/// It is only collected when the player actually needs it (not at full health / full ammo).
/// Robots use <see cref="Drop"/>: the pickup appears in the air after a short delay, tumbles and falls
/// like the explosion debris, lands on the floor, and only then can be picked up.
/// </summary>
public class Pickup : MonoBehaviour
{
    public enum Kind { Health, Ammo }

    [SerializeField] private Kind kind = Kind.Health;
    [SerializeField] private float amount = 25f;
    [SerializeField] private float pickupRadius = 1.1f;
    [SerializeField] private float spinSpeed = 90f;
    [SerializeField] private float bobHeight = 0.12f;
    [SerializeField] private float bobSpeed = 2.5f;
    [Tooltip("Height above the floor it rests at.")]
    [SerializeField] private float restHeight = 0.35f;
    [Tooltip("Seconds before it disappears. 0 = never.")]
    [SerializeField] private float lifetime = 0f;

    [Header("Falling")]
    [SerializeField] private float gravity = 18f;
    [Tooltip("Tumble speed (degrees per second) while falling.")]
    [SerializeField] private float tumbleSpeed = 540f;

    private static readonly RaycastHit[] Hits = new RaycastHit[16];
    private static PlayerHealth playerHealth;
    private static GearShiftWeapon playerWeapon;

    private enum State { Waiting, Falling, Landed }

    private State state = State.Landed;
    private float waitTime;
    private Vector3 velocity;
    private Vector3 tumbleAxis;
    private Renderer[] renderers;
    private Vector3 basePosition;
    private float age;
    private bool launched;

    /// <summary>
    /// Spawns a pickup that stays hidden for <paramref name="delay"/> seconds, then appears at
    /// <paramref name="position"/> and falls with <paramref name="launchVelocity"/>.
    /// </summary>
    public static Pickup Drop(GameObject prefab, Vector3 position, Vector3 launchVelocity, float delay)
    {
        if (prefab == null) return null;
        GameObject go = Instantiate(prefab, position, Random.rotation);
        Pickup p = go.GetComponent<Pickup>();
        if (p == null) return null;
        p.launched = true;
        p.velocity = launchVelocity;
        p.waitTime = delay;
        p.tumbleAxis = Random.onUnitSphere;
        p.state = delay > 0f ? State.Waiting : State.Falling;
        p.SetVisible(delay <= 0f);
        return p;
    }

    private void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
    }

    private void Start()
    {
        if (!launched) Land(); // placed by hand: settle on the floor right away
    }

    private void Update()
    {
        switch (state)
        {
            case State.Waiting:
                waitTime -= Time.deltaTime;
                if (waitTime <= 0f)
                {
                    state = State.Falling;
                    SetVisible(true);
                }
                return;

            case State.Falling:
                Fall(Time.deltaTime);
                return;
        }

        age += Time.deltaTime;
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        transform.position = basePosition + Vector3.up * (Mathf.Sin(age * bobSpeed) * bobHeight);

        if (lifetime > 0f && age > lifetime) { Destroy(gameObject); return; }

        if (playerHealth == null) playerHealth = FindFirstObjectByType<PlayerHealth>();
        if (playerHealth == null) return;

        Vector3 playerCenter = playerHealth.transform.position + Vector3.up * 0.9f;
        if ((playerCenter - transform.position).sqrMagnitude > pickupRadius * pickupRadius) return;

        if (TryApply()) Destroy(gameObject);
    }

    private void Fall(float dt)
    {
        velocity += Vector3.down * gravity * dt;
        Vector3 step = velocity * dt;
        transform.Rotate(tumbleAxis, tumbleSpeed * dt, Space.World);

        // Stop at the first solid thing below/ahead (walls stop sideways drift, the floor ends the fall).
        float distance = step.magnitude;
        if (distance > 0f && FirstHit(transform.position, step / distance, distance + restHeight, out RaycastHit hit))
        {
            if (hit.normal.y > 0.5f)
            {
                Land();
                return;
            }
            // Hit a wall: lose the sideways speed and keep falling.
            velocity = Vector3.ProjectOnPlane(velocity, hit.normal);
            velocity.x *= 0.3f;
            velocity.z *= 0.3f;
            return;
        }
        transform.position += step;

        // Safety: never fall forever.
        if (transform.position.y < -50f) Destroy(gameObject);
    }

    private void Land()
    {
        state = State.Landed;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        if (FirstHit(transform.position + Vector3.up * 0.5f, Vector3.down, 10f, out RaycastHit hit))
            transform.position = hit.point + Vector3.up * restHeight;
        basePosition = transform.position;
        age = 0f;
    }

    private bool FirstHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit best)
    {
        best = default;
        int count = Physics.RaycastNonAlloc(origin, direction, Hits, distance, ~0, QueryTriggerInteraction.Ignore);
        float bestDistance = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Collider c = Hits[i].collider;
            if (c.transform.IsChildOf(transform)) continue;          // itself
            if (c.attachedRigidbody != null) continue;               // explosion debris and other loose bits
            if (c.GetComponentInParent<Pickup>() != null) continue;  // other pickups
            if (c.GetComponentInParent<PlayerHealth>() != null) continue;
            if (Hits[i].distance < bestDistance) { bestDistance = Hits[i].distance; best = Hits[i]; found = true; }
        }
        return found;
    }

    private void SetVisible(bool visible)
    {
        if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers) if (r != null) r.enabled = visible;
    }

    private bool TryApply()
    {
        switch (kind)
        {
            case Kind.Health:
                if (playerHealth.IsDead || playerHealth.Current >= playerHealth.Max) return false;
                playerHealth.Heal(amount);
                return true;

            case Kind.Ammo:
                if (playerWeapon == null) playerWeapon = playerHealth.GetComponentInChildren<GearShiftWeapon>();
                if (playerWeapon == null || playerWeapon.Ammo >= playerWeapon.MaxAmmo) return false;
                playerWeapon.AddAmmo(Mathf.RoundToInt(amount));
                return true;
        }
        return false;
    }
}
