using System;
using UnityEngine;

/// <summary>
/// Big shield a robot holds in front of itself (with a top part covering it from above).
/// While it is held the robot can't be hurt (see <see cref="RobotEnemy.HasShield"/>).
///  - Every hit tilts it back a little; it then tilts back upright slowly.
///  - A full gear 3 burst (many hits in very quick succession) breaks it apart: every piece flies off
///    on its own, and the robot can be hurt from then on.
/// Put this on the shield's pivot (at the robot's hands); the shield pieces are its children.
/// </summary>
public class EnemyShield : MonoBehaviour, IDamageable
{
    [Header("Knock off")]
    [Tooltip("Hits in one quick chain needed to knock the shield away. A gear 3 burst is 8 pellets.")]
    [SerializeField] private int hitsToKnockOff = 8;
    [Tooltip("Hits further apart than this (seconds) start a new chain. Gear 3 pellets come 0.033 s apart; " +
             "gear 1 and 2 shots are much slower, so they never add up.")]
    [SerializeField] private float maxGapBetweenHits = 0.12f;

    [Header("Tilt when hit")]
    [SerializeField] private float tiltPerHit = 3f;
    [SerializeField] private float maxTilt = 14f;
    [Tooltip("How fast it tilts back (degrees per second). Slow.")]
    [SerializeField] private float tiltReturnSpeed = 8f;
    [Tooltip("How fast it follows a new hit (higher = snappier kick).")]
    [SerializeField] private float tiltKickSpeed = 25f;

    [Header("Breaking apart")]
    [SerializeField] private float pieceMass = 0.6f;
    [Tooltip("How hard the pieces fly apart.")]
    [SerializeField] private float breakForce = 4f;
    [Tooltip("Seconds the pieces stay before they disappear. 0 = keep them.")]
    [SerializeField] private float pieceLifetime = 6f;

    public event Action<EnemyShield> KnockedOff;

    public bool IsHeld { get; private set; } = true;

    private float targetTilt;
    private float tilt;
    private int chain;
    private float lastHitTime = float.NegativeInfinity;
    private Quaternion baseRotation;

    private void Awake()
    {
        baseRotation = transform.localRotation;
    }

    public void TakeDamage(float amount)
    {
        if (!IsHeld || amount <= 0f) return;

        // Alert the robot holding it.
        RobotEnemy robot = GetComponentInParent<RobotEnemy>();
        if (robot != null) robot.Alert();

        chain = Time.time - lastHitTime <= maxGapBetweenHits ? chain + 1 : 1;
        lastHitTime = Time.time;

        targetTilt = Mathf.Min(maxTilt, targetTilt + tiltPerHit);
        GameAudio.Play(SoundId.ShieldHit, transform.position, Mathf.Clamp(amount / 15f, 0.5f, 1f));

        if (chain >= hitsToKnockOff) KnockOff();
    }

    private void Update()
    {
        if (!IsHeld) return;

        // Kick back quickly toward the target, while the target itself eases back to upright slowly.
        tilt = Mathf.MoveTowards(tilt, targetTilt, tiltKickSpeed * maxTilt * Time.deltaTime);
        targetTilt = Mathf.MoveTowards(targetTilt, 0f, tiltReturnSpeed * Time.deltaTime);
        // Negative X rotation tips the top back toward the robot.
        transform.localRotation = baseRotation * Quaternion.Euler(-tilt, 0f, 0f);
    }

    /// <summary>Breaks the shield apart: every piece flies off on its own and the robot is left unprotected.</summary>
    public void KnockOff()
    {
        if (!IsHeld) return;
        IsHeld = false;

        Vector3 forward = transform.forward;
        RobotEnemy robot = GetComponentInParent<RobotEnemy>();
        Collider[] robotColliders = robot != null ? robot.GetComponentsInChildren<Collider>() : new Collider[0];
        Collider[] pieces = GetComponentsInChildren<Collider>();

        // Burst point: the middle of the shield, slightly behind it, so pieces fly out toward the shooter and sideways.
        Vector3 center = Vector3.zero;
        foreach (Collider c in pieces) center += c.bounds.center;
        center = pieces.Length > 0 ? center / pieces.Length : transform.position;
        Vector3 burstPoint = center - forward * 0.4f;
        GameAudio.Play(SoundId.ShieldBreak, center);

        foreach (Collider c in pieces)
        {
            if (c is MeshCollider mc) mc.convex = true;
            Transform piece = c.transform;
            piece.SetParent(null, true);

            // Don't let pieces snag on the robot as they fly off.
            foreach (Collider rc in robotColliders)
                if (rc != null) Physics.IgnoreCollision(rc, c, true);

            Rigidbody rb = piece.gameObject.AddComponent<Rigidbody>();
            rb.mass = pieceMass;
            rb.AddExplosionForce(breakForce, burstPoint, 3f, 0.4f, ForceMode.Impulse);
            rb.AddTorque(UnityEngine.Random.insideUnitSphere * breakForce * 0.4f, ForceMode.Impulse);
            ImpactSound.AddTo(piece.gameObject);
            if (pieceLifetime > 0f) Destroy(piece.gameObject, pieceLifetime + UnityEngine.Random.Range(0f, 1.5f));
        }

        KnockedOff?.Invoke(this);
        Destroy(gameObject); // the empty pivot
    }
}
