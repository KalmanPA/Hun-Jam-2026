using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The final boss: a big factory machine with a screen for a head and a health bar under the screen.
///  - It can only be hurt through the wire hatches on its back, high up (the player needs the gear 4 rocket jump).
///    Now and then one hatch opens for a while; its beacon turns green. Shooting the wires hurts the boss.
///  - At each shield threshold (by default 66% and 33% health) every hatch slams shut under a glass bubble and the
///    shield targets around the room stand up, each with a beam to the boss. Shooting all of them drops the shield.
///  - It fires slow fans of big bullets at the player from its two cannons.
///  - At zero health it plays the <see cref="BossEndingCutscene"/>.
/// The screen shows the robot face texture: calm, angry in the fight, the broken screen face when it dies.
/// </summary>
public class BossMachine : MonoBehaviour
{
    public enum Phase { Fighting, Shielded, Dead }

    [Header("Health")]
    [SerializeField] private float maxHealth = 1200f;
    [Tooltip("Health fractions where the shield phase starts (each once).")]
    [SerializeField] private float[] shieldPhaseAt = { 0.66f, 0.33f };

    [Header("Weak points")]
    [SerializeField] private BossWeakPoint[] weakPoints = new BossWeakPoint[0];
    [Tooltip("Seconds all hatches stay shut between two openings (random in this range).")]
    [SerializeField] private Vector2 closedTime = new Vector2(3f, 6f);
    [Tooltip("Seconds a hatch stays open.")]
    [SerializeField] private float openDuration = 7f;
    [Tooltip("Seconds before the first hatch opens.")]
    [SerializeField] private float firstOpenDelay = 6f;

    [Header("Shield phase")]
    [SerializeField] private DoorTarget[] shieldTargets = new DoorTarget[0];
    [Tooltip("One beam per target (same order), from the target to the boss. Shown while that target stands.")]
    [SerializeField] private LineRenderer[] targetBeams = new LineRenderer[0];
    [Tooltip("Seconds after the shield drops before a hatch opens.")]
    [SerializeField] private float afterShieldDelay = 2f;

    [Header("Screen")]
    [SerializeField] private Renderer screen;
    [SerializeField] private float calmFaceOffset = 0f;
    [SerializeField] private float angryFaceOffset = 0.34f;
    [SerializeField] private float deadFaceOffset = 0.67f;
    [SerializeField] private Light screenLight;

    [Header("Health bar")]
    [Tooltip("Scaled on X from 1 (full) to 0. Its pivot sits at the left end of the bar.")]
    [SerializeField] private Transform healthFill;
    [Tooltip("Trails behind the real health so a hit reads clearly.")]
    [SerializeField] private Transform healthTrail;

    [Header("Attack")]
    [SerializeField] private bool attack = true;
    [SerializeField] private Transform[] cannons = new Transform[0];
    [SerializeField] private Vector2 attackInterval = new Vector2(3.5f, 5.5f);
    [SerializeField] private float firstAttackDelay = 5f;
    [SerializeField] private int bulletsPerVolley = 5;
    [Tooltip("Degrees between two bullets of a volley.")]
    [SerializeField] private float volleySpread = 7f;
    [SerializeField] private float bulletSpeed = 13f;
    [SerializeField] private float bulletDamage = 7f;
    [SerializeField] private float bulletSize = 0.45f;
    [SerializeField] private float bulletRange = 70f;
    [SerializeField] private Material bulletMaterial;
    [SerializeField] private Color bulletColor = new Color(1f, 0.2f, 0.1f);

    [Header("End")]
    [SerializeField] private EnemySpawner[] spawners = new EnemySpawner[0];
    [SerializeField] private BossEndingCutscene ending;

    public static BossMachine Instance { get; private set; }

    public Phase CurrentPhase { get; private set; } = Phase.Fighting;
    public float Health { get; private set; }
    public float MaxHealth => maxHealth;
    public float Normalized => maxHealth > 0f ? Health / maxHealth : 0f;
    public Renderer Screen => screen;
    public float DeadFaceOffset => deadFaceOffset;

    private Material screenMaterial;
    private Color screenBaseColor = Color.white;
    private float screenFlash;
    private float trail = 1f;
    private int nextShieldIndex;
    private float nextOpenTime;
    private float closeTime;
    private BossWeakPoint openPoint;
    private float nextAttackTime;
    private Transform player;

    private void Awake()
    {
        Instance = this;
        Health = maxHealth;
        if (screen != null)
        {
            screenMaterial = screen.material;
            if (screenMaterial.HasProperty("_BaseColor")) screenBaseColor = screenMaterial.GetColor("_BaseColor");
            SetFace(calmFaceOffset);
        }
        System.Array.Sort(shieldPhaseAt, (a, b) => b.CompareTo(a)); // highest first
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        PlayerHealth ph = FindFirstObjectByType<PlayerHealth>();
        player = ph != null ? ph.transform : null;
        foreach (DoorTarget t in shieldTargets) if (t != null) t.SetDown();
        UpdateBeams();
        nextOpenTime = Time.time + firstOpenDelay;
        nextAttackTime = Time.time + firstAttackDelay;
        SetFace(angryFaceOffset);
        UpdateBar(true);
    }

    private void Update()
    {
        UpdateBar(false);
        UpdateScreen();
        if (CurrentPhase == Phase.Dead) return;

        if (CurrentPhase == Phase.Fighting) UpdateHatches();
        else UpdateShield();

        if (attack) UpdateAttack();
    }

    // ---------------------------------------------------------------- damage

    /// <summary>Wires were shot through an open hatch.</summary>
    public void TakeWeakPointDamage(float amount, BossWeakPoint from)
    {
        if (CurrentPhase != Phase.Fighting || amount <= 0f) return;
        Health = Mathf.Max(0f, Health - amount);
        screenFlash = 1f;
        GameAudio.Play(SoundId.RobotHitMetal, from.transform.position, Mathf.Clamp(amount / 20f, 0.5f, 1f));

        if (Health <= 0f) { Die(); return; }

        if (nextShieldIndex < shieldPhaseAt.Length && Normalized <= shieldPhaseAt[nextShieldIndex])
        {
            nextShieldIndex++;
            StartShield();
        }
    }

    // ---------------------------------------------------------------- hatches

    private void UpdateHatches()
    {
        if (openPoint != null)
        {
            if (Time.time >= closeTime) CloseOpenHatch();
            return;
        }
        if (Time.time < nextOpenTime || weakPoints.Length == 0) return;

        // Open a random hatch (not the same one twice in a row if there's a choice).
        var choices = new List<BossWeakPoint>();
        foreach (BossWeakPoint w in weakPoints) if (w != null && w != lastOpened) choices.Add(w);
        if (choices.Count == 0) foreach (BossWeakPoint w in weakPoints) if (w != null) choices.Add(w);
        if (choices.Count == 0) return;

        openPoint = choices[Random.Range(0, choices.Count)];
        lastOpened = openPoint;
        openPoint.SetOpen(true);
        closeTime = Time.time + openDuration;
    }

    private BossWeakPoint lastOpened;

    private void CloseOpenHatch()
    {
        if (openPoint != null) openPoint.SetOpen(false);
        openPoint = null;
        nextOpenTime = Time.time + Random.Range(closedTime.x, closedTime.y);
    }

    // ---------------------------------------------------------------- shield

    private void StartShield()
    {
        CloseOpenHatch();
        foreach (BossWeakPoint w in weakPoints) if (w != null) { w.SetOpen(false); w.SetShield(true); }
        int raised = 0;
        foreach (DoorTarget t in shieldTargets) if (t != null) { t.Restore(); raised++; }
        CurrentPhase = raised > 0 ? Phase.Shielded : Phase.Fighting;
        if (CurrentPhase == Phase.Fighting)
        {
            foreach (BossWeakPoint w in weakPoints) if (w != null) w.SetShield(false);
            return;
        }
        GameAudio.Play(SoundId.DoorUnlock, transform.position + Vector3.up * 8f);
        UpdateBeams();
    }

    private void UpdateShield()
    {
        UpdateBeams();
        foreach (DoorTarget t in shieldTargets)
            if (t != null && !t.IsDestroyed) return;

        // All targets down: shield off.
        CurrentPhase = Phase.Fighting;
        foreach (BossWeakPoint w in weakPoints) if (w != null) w.SetShield(false);
        GameAudio.Play(SoundId.ShieldBreak, transform.position + Vector3.up * 6f);
        nextOpenTime = Time.time + afterShieldDelay;
    }

    private void UpdateBeams()
    {
        for (int i = 0; i < targetBeams.Length; i++)
        {
            LineRenderer beam = targetBeams[i];
            if (beam == null) continue;
            bool on = CurrentPhase == Phase.Shielded && i < shieldTargets.Length && shieldTargets[i] != null && !shieldTargets[i].IsDestroyed;
            beam.enabled = on;
            if (on)
            {
                float w = 0.07f + Mathf.Sin(Time.time * 9f + i) * 0.02f;
                beam.startWidth = w;
                beam.endWidth = w * 1.6f;
            }
        }
    }

    // ---------------------------------------------------------------- attack

    private void UpdateAttack()
    {
        if (player == null || cannons.Length == 0 || Time.time < nextAttackTime) return;
        PlayerHealth ph = player.GetComponent<PlayerHealth>();
        if (ph != null && ph.IsDead) return;
        nextAttackTime = Time.time + Random.Range(attackInterval.x, attackInterval.y);

        Vector3 target = player.position + Vector3.up * 1.1f;
        foreach (Transform c in cannons)
        {
            if (c == null) continue;
            Vector3 dir = (target - c.position).normalized;
            for (int i = 0; i < bulletsPerVolley; i++)
            {
                float angle = (i - (bulletsPerVolley - 1) * 0.5f) * volleySpread;
                Vector3 d = Quaternion.AngleAxis(angle, Vector3.up) * dir;
                EnemyBullet.Spawn(c.position, d, bulletSpeed, bulletDamage, bulletRange, bulletMaterial, bulletColor, bulletSize);
            }
            GameAudio.Play(SoundId.EnemyBulletFire, c.position, 1f, 0.7f);
            GearExplosion.Spawn(c.position, 0.8f, bulletMaterial, bulletColor, 0.2f);
        }
    }

    // ---------------------------------------------------------------- screen + bar

    private void SetFace(float x)
    {
        if (screenMaterial == null) return;
        Vector2 o = screenMaterial.mainTextureOffset;
        o.x = x;
        screenMaterial.mainTextureOffset = o;
    }

    private void UpdateScreen()
    {
        if (screenMaterial == null) return;
        screenFlash = Mathf.MoveTowards(screenFlash, 0f, Time.deltaTime * 6f);

        // Glitch: while hurt the face jumps to the broken screen for a frame now and then.
        if (CurrentPhase != Phase.Dead)
        {
            bool glitch = screenFlash > 0.6f && Random.value < 0.35f;
            SetFace(glitch ? deadFaceOffset : angryFaceOffset);
        }
        if (screenMaterial.HasProperty("_BaseColor"))
            screenMaterial.SetColor("_BaseColor", Color.Lerp(screenBaseColor, Color.white, screenFlash * 0.5f));
        if (screenLight != null) screenLight.intensity = Mathf.Lerp(screenLight.intensity, 3f + screenFlash * 4f, Time.deltaTime * 10f);
    }

    private void UpdateBar(bool snap)
    {
        float n = Normalized;
        trail = snap ? n : Mathf.MoveTowards(trail, n, Time.deltaTime * (trail > n + 0.2f ? 0.6f : 0.25f));
        if (trail < n) trail = n;
        if (healthFill != null) healthFill.localScale = new Vector3(Mathf.Max(0.0001f, n), 1f, 1f);
        if (healthTrail != null) healthTrail.localScale = new Vector3(Mathf.Max(0.0001f, trail), 1f, 1f);
    }

    // ---------------------------------------------------------------- death

    private void Die()
    {
        if (CurrentPhase == Phase.Dead) return;
        CurrentPhase = Phase.Dead;
        CloseOpenHatch();
        foreach (BossWeakPoint w in weakPoints) if (w != null) { w.SetOpen(false); w.SetShield(false); }
        foreach (EnemySpawner s in spawners) if (s != null) s.StopSpawning();
        SetFace(deadFaceOffset);
        UpdateBeams();
        if (ending != null) ending.Play(this);
        else Debug.LogWarning("BossMachine: no ending cutscene linked.", this);
    }

    /// <summary>Debug: kills the boss right away (right-click the component header in Play mode).</summary>
    [ContextMenu("Kill Boss (test ending)")]
    private void DebugKill()
    {
        if (!Application.isPlaying) return;
        Health = 0f;
        Die();
    }

    [ContextMenu("Start Shield Phase (test)")]
    private void DebugShield()
    {
        if (Application.isPlaying && CurrentPhase == Phase.Fighting) StartShield();
    }
}
