using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Factory robot: boxy yellow robot on treads with a head and a gun arm.
///  - Always turns to face the player, smoothly (not snappy).
///  - Walks around obstacles on the NavMesh until the player is in range, then stops.
///  - Never flanks: if the player hides behind cover it stays put and keeps shooting around them.
///  - Where each shot goes is decided by <see cref="EnemyAim"/> (misses often, hits sometimes).
///  - Takes damage through <see cref="IDamageable"/>. On death it explodes into loose pieces
///    and drops a health and an ammo pickup.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnemyAim))]
public class RobotEnemy : MonoBehaviour, IDamageable
{
    public static event Action<RobotEnemy> AnyDied;
    public static readonly List<RobotEnemy> Alive = new List<RobotEnemy>();

    [Header("Health")]
    [SerializeField] private float maxHealth = 40f;

    [Header("Senses")]
    [Tooltip("Notices the player inside this distance (or when shot).")]
    [SerializeField] private float detectionRange = 30f;

    [Header("Movement")]
    [Tooltip("Starts walking toward the player when farther than this.")]
    [SerializeField] private float attackRange = 16f;
    [Tooltip("Once moving, walks until the player is this close, then stops.")]
    [SerializeField] private float stopRange = 13f;
    [Tooltip("How quickly the body turns to face the player. Lower = lazier.")]
    [SerializeField] private float turnSmoothing = 4f;
    [Tooltip("Turning speed cap in degrees per second.")]
    [SerializeField] private float maxTurnSpeed = 180f;

    [Header("Shooting")]
    [Tooltip("Arm tips glow this long before a burst, so the player can react.")]
    [SerializeField] private float windUp = 0.45f;
    [SerializeField] private int burstCount = 4;
    [SerializeField] private float burstInterval = 0.18f;
    [SerializeField] private Vector2 burstCooldown = new Vector2(1.2f, 2.2f);
    [Tooltip("Only shoots when the body faces the player within this angle.")]
    [SerializeField] private float fireAngle = 20f;
    [Tooltip("Slow enough to see coming.")]
    [SerializeField] private float bulletSpeed = 16f;
    [SerializeField] private float bulletSize = 0.28f;
    [SerializeField] private float bulletDamage = 8f;
    [SerializeField] private float bulletRange = 45f;
    [SerializeField] private Color bulletColor = new Color(1f, 0.2f, 0.1f);

    [Header("Parts (set by the Robot prefab)")]
    [SerializeField] private Transform eye;
    [Tooltip("Gun arm: tilts up/down toward the player.")]
    [SerializeField] private Transform arms;
    [Tooltip("Head: tilts up/down toward the player.")]
    [SerializeField] private Transform head;
    [SerializeField] private Transform[] muzzles;
    [SerializeField] private Light muzzleFlash;
    [Tooltip("Glow before a burst (the visor).")]
    [SerializeField] private Renderer[] armTips;
    [SerializeField] private Renderer[] bodyRenderers;

    [Header("Materials")]
    [SerializeField] private Material bulletMaterial;
    [SerializeField] private Material armTipIdleMaterial;
    [SerializeField] private Material armTipChargeMaterial;
    [SerializeField] private Material hitFlashMaterial;
    [SerializeField] private Material explosionMaterial;

    [Header("Death")]
    [SerializeField] private GameObject healthPickupPrefab;
    [SerializeField] private GameObject ammoPickupPrefab;
    [Tooltip("Seconds the loose pieces stay before they disappear.")]
    [SerializeField] private float debrisLifetime = 4f;
    [SerializeField] private float debrisForce = 6f;

    private static readonly RaycastHit[] SightHits = new RaycastHit[16];

    private NavMeshAgent agent;
    private EnemyAim aim;
    private Transform player;
    private PlayerHealth playerHealth;

    private float health;
    private bool dead;
    private bool aware;
    private bool moving;
    private bool canSee;
    private bool firing;
    private float nextBurstTime;
    private float nextRepathTime;
    private int nextMuzzle;

    private Material[][] originalMaterials;
    private Coroutine flashRoutine;

    public bool IsDead => dead;
    public float Health => health;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = false; // we turn the body ourselves, smoothly
        aim = GetComponent<EnemyAim>();
        health = maxHealth;
        if (eye == null) eye = transform;
        if (muzzleFlash != null) muzzleFlash.enabled = false;

        if (bodyRenderers == null || bodyRenderers.Length == 0)
            bodyRenderers = GetComponentsInChildren<Renderer>();
        originalMaterials = new Material[bodyRenderers.Length][];
        for (int i = 0; i < bodyRenderers.Length; i++)
            originalMaterials[i] = bodyRenderers[i].sharedMaterials;
    }

    private void OnEnable() => Alive.Add(this);
    private void OnDisable() => Alive.Remove(this);

    private void Start()
    {
        FindPlayer();
        nextBurstTime = Time.time + 1f;
    }

    private void FindPlayer()
    {
        playerHealth = FindFirstObjectByType<PlayerHealth>();
        player = playerHealth != null ? playerHealth.transform : null;
        aim.SetTarget(player);
    }

    // ---------------------------------------------------------------- update

    private void Update()
    {
        if (dead) return;
        if (player == null) { FindPlayer(); if (player == null) return; }

        bool playerAlive = playerHealth == null || !playerHealth.IsDead;
        float distance = FlatDistance(transform.position, player.position);
        if (distance <= detectionRange) aware = true;
        if (!playerAlive) aware = false;

        canSee = aware && CanSeePlayer();
        aim.Tick(canSee, Time.deltaTime);

        if (!aware)
        {
            Stop();
            return;
        }

        FacePlayer();
        UpdateMovement(distance);
        UpdateArms();

        // In range and standing still: shoot. With no line of sight it is suppressive fire around the player.
        if (!moving && !firing && Time.time >= nextBurstTime && FacingAngle() <= fireAngle)
            StartCoroutine(Burst());
    }

    private void UpdateMovement(float distance)
    {
        if (!agent.isOnNavMesh) return;

        // Hysteresis: start walking when out of range, keep walking until well inside it, then stop for good.
        if (!moving && distance > attackRange) moving = true;
        else if (moving && distance <= stopRange) moving = false;

        if (moving)
        {
            if (Time.time >= nextRepathTime)
            {
                nextRepathTime = Time.time + 0.25f;
                agent.isStopped = false;
                agent.SetDestination(player.position);
            }
        }
        else
        {
            Stop();
        }
    }

    private void Stop()
    {
        if (agent.isOnNavMesh && agent.hasPath)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
        moving = false;
    }

    private void FacePlayer()
    {
        Vector3 to = player.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.001f) return;

        // Ease toward the player (smooth start and finish), with a speed cap so big turns aren't instant.
        Quaternion target = Quaternion.LookRotation(to);
        Quaternion eased = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-turnSmoothing * Time.deltaTime));
        transform.rotation = Quaternion.RotateTowards(transform.rotation, eased, maxTurnSpeed * Time.deltaTime);
    }

    private void UpdateArms()
    {
        // Tilt the gun arm and head up/down toward the player; the body already handles left/right.
        TiltToward(arms, 6f);
        TiltToward(head, 8f);
    }

    private void TiltToward(Transform part, float smoothing)
    {
        if (part == null) return;
        Vector3 local = transform.InverseTransformPoint(aim.TargetCenter) - part.localPosition;
        float pitch = -Mathf.Atan2(local.y, Mathf.Max(0.5f, new Vector2(local.x, local.z).magnitude)) * Mathf.Rad2Deg;
        Quaternion target = Quaternion.Euler(Mathf.Clamp(pitch, -45f, 45f), 0f, 0f);
        part.localRotation = Quaternion.Slerp(part.localRotation, target, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
    }

    private float FacingAngle()
    {
        Vector3 to = player.position - transform.position;
        to.y = 0f;
        return Vector3.Angle(transform.forward, to);
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private bool CanSeePlayer()
    {
        Vector3 origin = eye.position;
        Vector3 target = aim.TargetCenter + Vector3.up * 0.4f; // around the player's head
        Vector3 to = target - origin;
        float distance = to.magnitude;
        if (distance < 0.01f) return true;

        int count = Physics.RaycastNonAlloc(origin, to / distance, SightHits, distance, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Transform first = null;
        for (int i = 0; i < count; i++)
        {
            if (SightHits[i].transform.IsChildOf(transform)) continue;
            if (SightHits[i].distance < best) { best = SightHits[i].distance; first = SightHits[i].transform; }
        }
        return first == null || first.IsChildOf(player);
    }

    // ---------------------------------------------------------------- shooting

    private IEnumerator Burst()
    {
        firing = true;

        // Telegraph: the arm tips glow before the shots come.
        SetArmTips(true);
        yield return new WaitForSeconds(windUp);

        for (int i = 0; i < burstCount; i++)
        {
            if (dead || moving || !aware) break;
            Fire();
            yield return new WaitForSeconds(burstInterval);
        }

        SetArmTips(false);
        nextBurstTime = Time.time + UnityEngine.Random.Range(burstCooldown.x, burstCooldown.y);
        firing = false;
    }

    private void Fire()
    {
        Transform muzzle = transform;
        if (muzzles != null && muzzles.Length > 0)
        {
            muzzle = muzzles[nextMuzzle % muzzles.Length]; // alternate arms
            nextMuzzle++;
        }

        Vector3 origin = muzzle.position;
        Vector3 point = aim.PickAimPoint(origin, canSee, out _);
        Vector3 dir = point - origin;
        if (dir.sqrMagnitude < 0.0001f) dir = muzzle.forward;
        EnemyBullet.Spawn(origin, dir, bulletSpeed, bulletDamage, bulletRange, bulletMaterial, bulletColor, bulletSize);

        if (muzzleFlash != null)
        {
            StopCoroutine(nameof(MuzzleFlash));
            StartCoroutine(nameof(MuzzleFlash));
        }
    }

    private IEnumerator MuzzleFlash()
    {
        muzzleFlash.enabled = true;
        yield return new WaitForSeconds(0.05f);
        muzzleFlash.enabled = false;
    }

    private void SetArmTips(bool charging)
    {
        if (armTips == null) return;
        Material m = charging ? armTipChargeMaterial : armTipIdleMaterial;
        if (m == null) return;
        foreach (Renderer r in armTips)
            if (r != null) r.sharedMaterial = m;
    }

    // ---------------------------------------------------------------- damage

    public void TakeDamage(float amount)
    {
        if (dead || amount <= 0f) return;

        health -= amount;
        aware = true; // getting shot gives the player away

        if (health <= 0f)
        {
            Die();
            return;
        }

        if (hitFlashMaterial != null)
        {
            if (flashRoutine != null) StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(HitFlash());
        }
    }

    private IEnumerator HitFlash()
    {
        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            if (bodyRenderers[i] == null) continue;
            Material[] mats = new Material[bodyRenderers[i].sharedMaterials.Length];
            for (int m = 0; m < mats.Length; m++) mats[m] = hitFlashMaterial;
            bodyRenderers[i].sharedMaterials = mats;
        }
        yield return new WaitForSeconds(0.07f);
        RestoreMaterials();
        flashRoutine = null;
    }

    private void RestoreMaterials()
    {
        // originalMaterials isn't serialized, so it is lost if scripts recompile during play mode.
        if (originalMaterials == null) return;
        for (int i = 0; i < bodyRenderers.Length && i < originalMaterials.Length; i++)
            if (bodyRenderers[i] != null && originalMaterials[i] != null)
                bodyRenderers[i].sharedMaterials = originalMaterials[i];
    }

    private void Die()
    {
        dead = true;
        StopAllCoroutines();
        RestoreMaterials();
        SetArmTips(false);
        if (muzzleFlash != null) muzzleFlash.enabled = false;
        if (agent.isOnNavMesh) agent.ResetPath();
        agent.enabled = false;

        Vector3 center = transform.position + Vector3.up * 0.9f;
        GearExplosion.Spawn(center, 1.6f, explosionMaterial, new Color(1f, 0.5f, 0.1f), 0.45f);

        // Explode into pieces: every part with a collider becomes a loose physics piece.
        foreach (Collider c in GetComponentsInChildren<Collider>())
        {
            Transform part = c.transform;
            part.SetParent(null, true);
            Rigidbody rb = part.gameObject.AddComponent<Rigidbody>();
            rb.mass = 0.5f;
            rb.AddExplosionForce(debrisForce, center - Vector3.up * 0.4f, 3f, 0.6f, ForceMode.Impulse);
            rb.AddTorque(UnityEngine.Random.insideUnitSphere * debrisForce * 0.3f, ForceMode.Impulse);
            Destroy(part.gameObject, debrisLifetime + UnityEngine.Random.Range(0f, 1f));
        }

        DropPickups();
        AnyDied?.Invoke(this);
        Destroy(gameObject);
    }

    private void DropPickups()
    {
        Vector3 basePos = transform.position + Vector3.up * 0.5f;
        Vector3 side = transform.right * 0.7f;
        if (healthPickupPrefab != null) Instantiate(healthPickupPrefab, basePos - side, Quaternion.identity);
        if (ammoPickupPrefab != null) Instantiate(ammoPickupPrefab, basePos + side, Quaternion.identity);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, stopRange);
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }
}
