using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The final boss: a big factory machine with a screen for a head and a health bar under the screen.
///  - It can only be hurt through its wire hatches (weak points).
///    Now and then one hatch opens for a while; its beacon turns green. Shooting the wires hurts the boss.
///  - It does not shoot and has no shield phase.
///  - At zero health it plays the <see cref="BossEndingCutscene"/>.
/// The screen shows the robot face texture: calm, angry in the fight, the broken screen face when it dies.
/// </summary>
public class BossMachine : MonoBehaviour
{
    public enum Phase { Fighting, Dead }

    [Header("Health")]
    [SerializeField] private float maxHealth = 600f;

    [Header("Weak points")]
    [SerializeField] private BossWeakPoint[] weakPoints = new BossWeakPoint[0];
    [Tooltip("Seconds all hatches stay shut between two openings (random in this range).")]
    [SerializeField] private Vector2 closedTime = new Vector2(3f, 6f);
    [Tooltip("Seconds a hatch stays open.")]
    [SerializeField] private float openDuration = 7f;
    [Tooltip("Seconds before the first hatch opens.")]
    [SerializeField] private float firstOpenDelay = 6f;

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
    private float nextOpenTime;
    private float closeTime;
    private BossWeakPoint openPoint;
    private BossWeakPoint lastOpened;

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
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        nextOpenTime = Time.time + firstOpenDelay;
        SetFace(angryFaceOffset);
        UpdateBar(true);
    }

    private void Update()
    {
        UpdateBar(false);
        UpdateScreen();
        if (CurrentPhase == Phase.Dead) return;
        UpdateHatches();
    }

    // ---------------------------------------------------------------- damage

    /// <summary>Wires were shot through an open hatch.</summary>
    public void TakeWeakPointDamage(float amount, BossWeakPoint from)
    {
        if (CurrentPhase != Phase.Fighting || amount <= 0f) return;
        Health = Mathf.Max(0f, Health - amount);
        screenFlash = 1f;
        GameAudio.Play(SoundId.RobotHitMetal, from.transform.position, Mathf.Clamp(amount / 20f, 0.5f, 1f));

        if (Health <= 0f) Die();
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

    private void CloseOpenHatch()
    {
        if (openPoint != null) openPoint.SetOpen(false);
        openPoint = null;
        nextOpenTime = Time.time + Random.Range(closedTime.x, closedTime.y);
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
        foreach (BossWeakPoint w in weakPoints) if (w != null) w.SetOpen(false);
        foreach (EnemySpawner s in spawners) if (s != null) s.StopSpawning();
        SetFace(deadFaceOffset);
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
}
