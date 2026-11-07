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
///  - Face expression (the face material's texture X offset): calm before noticing the player or while
///    combat is locked (cutscenes), angry in combat, and a dying face when health runs out.
///  - Takes damage through <see cref="IDamageable"/>. At zero health it freezes with the dying face,
///    then after deathDelay explodes into loose pieces and drops a health and an ammo pickup.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnemyAim))]
public class RobotEnemy : MonoBehaviour, IDamageable
{
    public static event Action<RobotEnemy> AnyDied;
    public static readonly List<RobotEnemy> Alive = new List<RobotEnemy>();

    [Header("Health")]
    [SerializeField] private float maxHealth = 40f;

    [Header("Player detection")]
    [Tooltip("Player detection range: the robot notices the player inside this distance (or when shot), " +
             "switches to its combat face and attacks. Shown as a red circle in the Scene view.")]
    [SerializeField] private float detectionRange = 30f;

    public enum MovementMode
    {
        [Tooltip("Walks toward the player until in range, then stops.")] Chase,
        [Tooltip("Never moves, only turns to face the player.")] Stationary,
        [Tooltip("Moves only along its own left/right axis (as placed), wall to wall, to stay in front of the player.")] BlockAxis,
    }

    public enum ShotType { Bullet, Laser }

    [Header("Variant")]
    [SerializeField] private MovementMode movementMode = MovementMode.Chase;
    [SerializeField] private bool canShoot = true;
    [Tooltip("Bullet: big slow visible bullets. Laser: instant beam like the player's gear 1 (sniper).")]
    [SerializeField] private ShotType shotType = ShotType.Bullet;
    [Tooltip("Shield held in front. While it is held the robot can't be hurt.")]
    [SerializeField] private EnemyShield shield;
    [Tooltip("Keep the calm face while the shield is held; switch to the combat face once it's knocked away (gear 3 tutorial).")]
    [SerializeField] private bool calmFaceWhileShielded;

    [Header("Block axis (BlockAxis movement)")]
    [Tooltip("How far it can slide each way along its axis if no wall is found.")]
    [SerializeField] private float maxBlockDistance = 25f;

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
    [Tooltip("Laser shots: beam width and how long the beam stays visible.")]
    [SerializeField] private float laserWidth = 0.12f;
    [SerializeField] private float laserDuration = 0.25f;
    [Tooltip("Laser shots: show a thin aiming line during the wind up, so the player sees the shot coming.")]
    [SerializeField] private bool laserAimLine = true;

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

    [Header("Face")]
    [Tooltip("The model's face renderer (set by the Robot prefab setup).")]
    [SerializeField] private Renderer face;
    [Tooltip("Name of the face material whose texture X offset picks the expression.")]
    [SerializeField] private string faceMaterialName = "Deanall";
    [Tooltip("Not in combat: before the player is noticed, or while combat is locked (cutscenes).")]
    [SerializeField] private float calmFaceOffset = 0f;
    [SerializeField] private float combatFaceOffset = 0.34f;
    [Tooltip("Shown while the robot freezes before it explodes.")]
    [SerializeField] private float dyingFaceOffset = 0.67f;
    [Tooltip("Seconds the face takes to slide from calm to combat when the player is noticed.")]
    [SerializeField] private float combatFaceChangeTime = 0.6f;

    [Header("Combat lock")]
    [Tooltip("While on, the robot stays calm: it doesn't notice the player, move or shoot (e.g. the intro cutscene). " +
             "Scripts can switch it with CombatLocked.")]
    [SerializeField] private bool combatLocked;

    [Header("Death")]
    [Tooltip("Seconds the robot freezes (with the dying face) before it explodes.")]
    [SerializeField] private float deathDelay = 1f;
    [SerializeField] private GameObject healthPickupPrefab;
    [SerializeField] private GameObject ammoPickupPrefab;
    [Tooltip("Relative chances of what drops: health only, ammo only, or both.")]
    [SerializeField, Min(0f)] private float healthOnlyWeight = 1f;
    [SerializeField, Min(0f)] private float ammoOnlyWeight = 1f;
    [SerializeField, Min(0f)] private float bothWeight = 1f;
    [Tooltip("Seconds after the explosion before the drops pop out.")]
    [SerializeField] private float dropDelay = 0.3f;
    [Tooltip("Height above the robot's feet the drops appear at.")]
    [SerializeField] private float dropHeight = 1.8f;
    [Tooltip("Sideways launch speed range. Small = lands close to the robot.")]
    [SerializeField] private Vector2 dropSideSpeed = new Vector2(0.5f, 1.8f);
    [Tooltip("Upward launch speed range.")]
    [SerializeField] private Vector2 dropUpSpeed = new Vector2(3f, 5f);
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

    private Material faceMaterial;
    private float faceOffset = float.NaN;

    // Sound
    private static float nextAlertBarkTime;
    private bool wasAware;
    private float nextHurtVoiceTime;
    private SoundHandle voiceLine;  // one voice per robot: a new line cuts the old one

    public bool IsDead => dead;

    /// <summary>True while the robot still holds its shield (and can't be hurt).</summary>
    public bool HasShield => shield != null && shield.IsHeld;

    /// <summary>Makes the robot go for the player right away (used by spawners after it lands).</summary>
    public void Alert() { if (!combatLocked) aware = true; }

    /// <summary>While true the robot stays out of combat (calm face, no noticing, moving or shooting). For cutscenes.</summary>
    public bool CombatLocked
    {
        get => combatLocked;
        set
        {
            combatLocked = value;
            if (value) aware = false;
        }
    }
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
            originalMaterials[i] = bodyRenderers[i] != null ? bodyRenderers[i].sharedMaterials : null;

        SetUpFace();
    }

    // ---------------------------------------------------------------- face

    private void SetUpFace()
    {
        if (face == null) return;

        // Give this robot its own copy of the face material, so each robot shows its own expression.
        Material[] mats = face.materials;
        int index = 0;
        for (int i = 0; i < mats.Length; i++)
            if (mats[i] != null && mats[i].name.StartsWith(faceMaterialName)) { index = i; break; }
        faceMaterial = mats.Length > 0 ? mats[index] : null;

        // The hit flash restores materials from this list, so it must point at the copy too.
        for (int i = 0; i < bodyRenderers.Length; i++)
            if (bodyRenderers[i] == face) originalMaterials[i] = face.sharedMaterials;

        SetFace(calmFaceOffset);
    }

    /// <summary>Sets the face offset right away.</summary>
    private void SetFace(float xOffset)
    {
        if (faceMaterial == null || Mathf.Approximately(faceOffset, xOffset)) return;
        faceOffset = xOffset;
        Vector2 offset = faceMaterial.mainTextureOffset;
        offset.x = xOffset;
        faceMaterial.mainTextureOffset = offset;
    }

    /// <summary>Slides the face offset toward <paramref name="xOffset"/>, from calm to combat in combatFaceChangeTime.</summary>
    private void SlideFace(float xOffset)
    {
        if (faceMaterial == null || float.IsNaN(faceOffset)) { SetFace(xOffset); return; }
        float distance = Mathf.Abs(combatFaceOffset - calmFaceOffset);
        float speed = combatFaceChangeTime > 0f && distance > 0f ? distance / combatFaceChangeTime : float.PositiveInfinity;
        SetFace(Mathf.MoveTowards(faceOffset, xOffset, speed * Time.deltaTime));
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
        if (!playerAlive || combatLocked) aware = false;

        if (aware && !wasAware) BarkAlert();
        wasAware = aware;

        canSee = aware && CanSeePlayer();
        aim.Tick(canSee, Time.deltaTime);
        bool combatFace = aware && !(calmFaceWhileShielded && HasShield);
        SlideFace(combatFace ? combatFaceOffset : calmFaceOffset);

        if (!aware)
        {
            Stop();
            return;
        }

        FacePlayer();
        switch (movementMode)
        {
            case MovementMode.Chase: UpdateMovement(distance); break;
            case MovementMode.Stationary: Stop(); break;
            case MovementMode.BlockAxis: UpdateBlocking(); break;
        }
        UpdateArms();
        UpdateAimLine();

        // In range and standing still: shoot. With no line of sight it is suppressive fire around the player.
        if (canShoot && !moving && !firing && distance <= attackRange && Time.time >= nextBurstTime && FacingAngle() <= fireAngle)
            StartCoroutine(Burst());
    }

    // ---------------------------------------------------------------- block axis

    private bool blockReady;
    private Vector3 blockOrigin;
    private Vector3 blockAxis;
    private float blockMin;
    private float blockMax;

    private void SetUpBlocking()
    {
        blockReady = true;
        blockOrigin = transform.position;
        blockAxis = transform.right;
        blockAxis.y = 0f;
        blockAxis = blockAxis.sqrMagnitude > 0.001f ? blockAxis.normalized : Vector3.right;
        blockMax = WallDistance(blockAxis);
        blockMin = -WallDistance(-blockAxis);
    }

    /// <summary>
    /// How far it can slide in <paramref name="dir"/>: to the edge of its own NavMesh. Shield robots use the wide
    /// "Shielded" agent type, whose NavMesh already stays a shield's width away from walls.
    /// </summary>
    private float WallDistance(Vector3 dir)
    {
        NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = NavMesh.AllAreas };
        if (NavMesh.Raycast(blockOrigin, blockOrigin + dir * maxBlockDistance, out NavMeshHit hit, filter))
            return Mathf.Max(0f, Vector3.Dot(hit.position - blockOrigin, dir));
        return maxBlockDistance;
    }

    /// <summary>Slides along the block axis to stay between the player and the way forward.</summary>
    private void UpdateBlocking()
    {
        if (!agent.isOnNavMesh) return;
        if (!blockReady) SetUpBlocking();

        float along = Mathf.Clamp(Vector3.Dot(player.position - blockOrigin, blockAxis), blockMin, blockMax);
        Vector3 target = blockOrigin + blockAxis * along;
        if (Time.time >= nextRepathTime && (!agent.hasPath || (agent.destination - target).sqrMagnitude > 0.04f))
        {
            nextRepathTime = Time.time + 0.1f;
            agent.isStopped = false;
            agent.SetDestination(target);
        }
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

        // Telegraph: the arm tips glow (and lasers show an aiming line) before the shots come.
        SetArmTips(true);
        aiming = true;
        yield return new WaitForSeconds(windUp);
        aiming = false;
        UpdateAimLine();

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

    private Transform CurrentMuzzle()
    {
        if (muzzles == null || muzzles.Length == 0) return transform;
        Transform m = muzzles[nextMuzzle % muzzles.Length];
        return m != null ? m : transform;
    }

    private void Fire()
    {
        Transform muzzle = CurrentMuzzle();
        nextMuzzle++; // alternate muzzles

        Vector3 origin = muzzle.position;
        Vector3 point = aim.PickAimPoint(origin, canSee, out _);
        Vector3 dir = point - origin;
        if (dir.sqrMagnitude < 0.0001f) dir = muzzle.forward;
        GameAudio.Play(shotType == ShotType.Laser ? SoundId.EnemyLaserFire : SoundId.EnemyBulletFire, origin);
        if (shotType == ShotType.Laser)
            EnemyLaser.Fire(origin, dir, bulletRange, bulletDamage, laserWidth, laserDuration, bulletMaterial, bulletColor);
        else
            EnemyBullet.Spawn(origin, dir, bulletSpeed, bulletDamage, bulletRange, bulletMaterial, bulletColor, bulletSize);

        if (muzzleFlash != null)
        {
            StopCoroutine(nameof(MuzzleFlash));
            StartCoroutine(nameof(MuzzleFlash));
        }
    }

    // ---------------------------------------------------------------- laser aim line

    private bool aiming;
    private LineRenderer aimLine;

    private void UpdateAimLine()
    {
        bool show = aiming && laserAimLine && shotType == ShotType.Laser && !dead;
        if (!show)
        {
            if (aimLine != null) aimLine.enabled = false;
            return;
        }

        if (aimLine == null)
        {
            GameObject go = new GameObject("AimLine");
            go.transform.SetParent(transform, false);
            aimLine = go.AddComponent<LineRenderer>();
            aimLine.positionCount = 2;
            aimLine.useWorldSpace = true;
            aimLine.sharedMaterial = bulletMaterial;
            aimLine.startColor = aimLine.endColor = bulletColor;
            aimLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            aimLine.receiveShadows = false;
        }

        Vector3 origin = CurrentMuzzle().position;
        Vector3 to = aim.TargetCenter - origin;
        float length = Mathf.Min(to.magnitude, bulletRange);
        aimLine.enabled = true;
        aimLine.SetPosition(0, origin);
        aimLine.SetPosition(1, origin + to.normalized * length);
        aimLine.widthMultiplier = laserWidth * 0.15f;
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

        if (!combatLocked) aware = true; // getting shot gives the player away
        if (HasShield) return;           // invulnerable while holding the shield

        health -= amount;

        // Clang on the body, then (not on every bullet) a grunt.
        GameAudio.Play(SoundId.RobotHitMetal, transform.position + Vector3.up * 1f + UnityEngine.Random.insideUnitSphere * 0.3f,
            Mathf.Clamp(amount / 20f, 0.5f, 1f));

        if (health <= 0f)
        {
            Die();
            return;
        }

        if (Time.time >= nextHurtVoiceTime)
        {
            nextHurtVoiceTime = Time.time + UnityEngine.Random.Range(0.45f, 0.9f);
            SayLine(SoundId.RobotHurt);
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
        // Freeze with the dying face, then explode after deathDelay.
        dead = true;
        StopAllCoroutines();
        RestoreMaterials();
        SetArmTips(false);
        aiming = false;
        if (aimLine != null) aimLine.enabled = false;
        if (muzzleFlash != null) muzzleFlash.enabled = false;
        if (agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.velocity = Vector3.zero;
        }
        SetFace(dyingFaceOffset);
        voiceLine.Stop(0.05f);
        GameAudio.PlayAttached(SoundId.RobotDeath, transform, Vector3.up * 1.2f);
        if (UnityEngine.Random.value < 0.35f) SayLine(SoundId.RobotDeathVoice);
        StartCoroutine(ExplodeAfterDelay());
    }

    private IEnumerator ExplodeAfterDelay()
    {
        if (deathDelay > 0f) yield return new WaitForSeconds(deathDelay);
        Explode();
    }

    private void Explode()
    {
        agent.enabled = false;

        Vector3 center = transform.position + Vector3.up * 0.9f;
        GearExplosion.Spawn(center, 1.6f, explosionMaterial, new Color(1f, 0.5f, 0.1f), 0.45f);
        GameAudio.Play(SoundId.RobotExplode, center);

        // Explode into pieces: every part with a collider becomes a loose physics piece.
        foreach (Collider c in GetComponentsInChildren<Collider>())
        {
            Transform part = c.transform;
            part.SetParent(null, true);
            Rigidbody rb = part.gameObject.AddComponent<Rigidbody>();
            rb.mass = 0.5f;
            rb.AddExplosionForce(debrisForce, center - Vector3.up * 0.4f, 3f, 0.6f, ForceMode.Impulse);
            rb.AddTorque(UnityEngine.Random.insideUnitSphere * debrisForce * 0.3f, ForceMode.Impulse);
            ImpactSound.AddTo(part.gameObject);
            Destroy(part.gameObject, debrisLifetime + UnityEngine.Random.Range(0f, 1f));
        }

        DropPickups();
        AnyDied?.Invoke(this);
        Destroy(gameObject);
    }

    // ---------------------------------------------------------------- voice

    /// <summary>Says a voice line from the robot's head, cutting off whatever it was saying.</summary>
    private void SayLine(SoundId id)
    {
        SoundHandle h = GameAudio.PlayAttached(id, transform, Vector3.up * 1.4f);
        if (h.IsValid)
        {
            voiceLine.Stop(0.05f);
            voiceLine = h;
        }
    }

    /// <summary>"Stop, you criminal!" when it notices the player. Shared cooldown so a room of robots doesn't all shout.</summary>
    private void BarkAlert()
    {
        if (nextAlertBarkTime - Time.time > 10f) nextAlertBarkTime = 0f; // left over from a previous play session
        if (Time.time < nextAlertBarkTime) return;
        nextAlertBarkTime = Time.time + UnityEngine.Random.Range(5f, 9f);
        SayLine(SoundId.RobotAlert);
    }

    private void DropPickups()
    {
        // Random pick: health only, ammo only, or both.
        float total = healthOnlyWeight + ammoOnlyWeight + bothWeight;
        if (total <= 0f) return;
        float roll = UnityEngine.Random.value * total;
        bool dropHealth = roll < healthOnlyWeight || roll >= healthOnlyWeight + ammoOnlyWeight;
        bool dropAmmo = roll >= healthOnlyWeight;

        // They pop out a moment after the blast and fall like the debris, landing near the robot.
        Vector3 origin = transform.position + Vector3.up * dropHeight;
        float angle = UnityEngine.Random.value * 360f;
        if (dropHealth) LaunchDrop(healthPickupPrefab, origin, angle);
        if (dropAmmo) LaunchDrop(ammoPickupPrefab, origin, angle + 180f + UnityEngine.Random.Range(-50f, 50f));
    }

    private void LaunchDrop(GameObject prefab, Vector3 origin, float angleDegrees)
    {
        Vector3 dir = Quaternion.Euler(0f, angleDegrees, 0f) * Vector3.forward;
        Vector3 velocity = dir * UnityEngine.Random.Range(dropSideSpeed.x, dropSideSpeed.y)
                         + Vector3.up * UnityEngine.Random.Range(dropUpSpeed.x, dropUpSpeed.y);
        Pickup.Drop(prefab, origin, velocity, dropDelay + UnityEngine.Random.Range(0f, 0.15f));
    }

    private void OnDrawGizmos()
    {
        // Player detection range, always visible in the Scene view (flat circle on the floor).
        Gizmos.color = new Color(1f, 0.15f, 0.1f, 0.8f);
        const int segments = 48;
        Vector3 c = transform.position + Vector3.up * 0.05f;
        Vector3 prev = c + new Vector3(detectionRange, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            Vector3 next = c + new Vector3(Mathf.Cos(a) * detectionRange, 0f, Mathf.Sin(a) * detectionRange);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, stopRange);
        if (movementMode == MovementMode.BlockAxis)
        {
            // The line it slides along.
            Vector3 axis = Application.isPlaying && blockReady ? blockAxis : transform.right;
            Vector3 o = Application.isPlaying && blockReady ? blockOrigin : transform.position;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(o - axis * maxBlockDistance + Vector3.up * 0.1f, o + axis * maxBlockDistance + Vector3.up * 0.1f);
        }
    }
}
