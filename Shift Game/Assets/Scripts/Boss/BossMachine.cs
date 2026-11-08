using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The final boss: a big factory machine with a screen for a head and a health bar under the screen.
///  - It can only be hurt through its wire hatches (weak points) around the room. One hatch at a time opens
///    (beacon and cable turn green) and stays open until it has taken its share of damage, then it shuts and,
///    after a short pause, another one opens. Share = maxHealth / (weak points x closesPerWeakPoint), and the hatches
///    open in shuffled rounds (each once per round), so every weak point has to be shot shut that many times.
///  - It does not shoot and has no shield phase.
///  - At zero health it plays the <see cref="BossEndingCutscene"/>.
/// The screen shows the robot face texture: calm, angry in the fight, the broken screen face when it dies.
/// With a Fight Face picture set, that picture is shown during the fight instead (it still flashes the broken
/// screen when hurt), and the screen switches to the broken face when the boss dies.
/// </summary>
public class BossMachine : MonoBehaviour
{
    public enum Phase { Fighting, Dead }

    [Header("Health")]
    [SerializeField] private float maxHealth = 600f;

    [Header("Weak points")]
    [SerializeField] private BossWeakPoint[] weakPoints = new BossWeakPoint[0];
    [Tooltip("How many times every weak point has to be shot shut before the boss dies.")]
    [SerializeField, Min(1)] private int closesPerWeakPoint = 2;
    [Tooltip("Seconds between one hatch closing and the next one opening (random in this range).")]
    [SerializeField] private Vector2 closedTime = new Vector2(2f, 3.5f);
    [Tooltip("Seconds before the first hatch opens.")]
    [SerializeField] private float firstOpenDelay = 6f;

    [Header("Screen")]
    [SerializeField] private Renderer screen;
    [SerializeField] private float calmFaceOffset = 0f;
    [SerializeField] private float angryFaceOffset = 0.34f;
    [SerializeField] private float deadFaceOffset = 0.67f;
    [Tooltip("Optional: a single picture shown during the whole fight instead of the calm/angry faces of the atlas " +
             "(e.g. Dean mustache.png). The dead face (and the hurt glitch) still comes from the atlas.")]
    [SerializeField] private Texture fightFace;
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
    /// <summary>Damage one opening of a hatch takes before it shuts.</summary>
    public float DamagePerOpening => maxHealth / Mathf.Max(1, CountWeakPoints() * closesPerWeakPoint);

    private Material screenMaterial;
    private Texture atlasTexture;
    private Vector2 atlasScale = Vector2.one;
    private Vector2 atlasOffset;
    private Color screenBaseColor = Color.white;
    private float screenFlash;
    private float trail = 1f;
    private float nextOpenTime;
    private float openDamage;
    private readonly List<BossWeakPoint> queue = new List<BossWeakPoint>();
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
            atlasTexture = screenMaterial.mainTexture;
            atlasScale = screenMaterial.mainTextureScale;
            atlasOffset = screenMaterial.mainTextureOffset;
            SetFace(calmFaceOffset);
        }
        foreach (BossWeakPoint w in weakPoints) if (w != null) w.SetBoss(this);
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
        if (CurrentPhase != Phase.Fighting || amount <= 0f || from == null || from != openPoint) return;

        // Each opening only takes its share; the hatch shuts once that is used up.
        float share = DamagePerOpening;
        float applied = Mathf.Min(amount, share - openDamage, Health);
        if (applied <= 0f) return;
        openDamage += applied;
        Health = Mathf.Max(0f, Health - applied);
        screenFlash = 1f;
        from.ShowDamage();
        GameAudio.Play(SoundId.RobotHitMetal, from.transform.position, Mathf.Clamp(amount / 20f, 0.5f, 1f));

        if (Health <= 0.01f) { Health = 0f; Die(); return; }
        if (openDamage >= share - 0.01f) CloseOpenHatch();
    }

    // ---------------------------------------------------------------- hatches

    private int CountWeakPoints()
    {
        int n = 0;
        foreach (BossWeakPoint w in weakPoints) if (w != null) n++;
        return n;
    }

    private void UpdateHatches()
    {
        if (openPoint != null || Time.time < nextOpenTime) return;
        if (queue.Count == 0) RefillQueue();
        if (queue.Count == 0) return;

        openPoint = queue[0];
        queue.RemoveAt(0);
        lastOpened = openPoint;
        openDamage = 0f;
        openPoint.SetOpen(true); // stays open until it has taken its share of damage
    }

    /// <summary>One round: every weak point once, shuffled, never the same one twice in a row.</summary>
    private void RefillQueue()
    {
        foreach (BossWeakPoint w in weakPoints) if (w != null) queue.Add(w);
        for (int i = queue.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (queue[i], queue[j]) = (queue[j], queue[i]);
        }
        if (queue.Count > 1 && queue[0] == lastOpened)
            (queue[0], queue[queue.Count - 1]) = (queue[queue.Count - 1], queue[0]);
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
        bool dead = Mathf.Approximately(x, deadFaceOffset);
        if (fightFace != null && !dead)
        {
            // The whole fight shows the single fight picture, stretched over the screen.
            if (screenMaterial.mainTexture != fightFace) screenMaterial.mainTexture = fightFace;
            screenMaterial.mainTextureScale = Vector2.one;
            screenMaterial.mainTextureOffset = Vector2.zero;
            return;
        }
        if (atlasTexture != null && screenMaterial.mainTexture != atlasTexture) screenMaterial.mainTexture = atlasTexture;
        screenMaterial.mainTextureScale = atlasScale;
        screenMaterial.mainTextureOffset = new Vector2(x, atlasOffset.y);
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
