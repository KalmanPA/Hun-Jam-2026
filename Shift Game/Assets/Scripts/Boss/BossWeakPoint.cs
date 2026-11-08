using UnityEngine;

/// <summary>
/// One wire hatch on the boss's back. Closed: the lid is shut and the wires are sunk inside. Open: the lid swings
/// up, the wires rise out and can be shot (damage goes to the boss). A beacon lamp next to it shows red while
/// closed and green while open. During the boss's shield phase a glass bubble covers it.
/// </summary>
public class BossWeakPoint : MonoBehaviour
{
    [Header("Parts (set by the boss builder)")]
    [SerializeField] private Transform lid;
    [Tooltip("Lid rotation when fully open, on top of its closed rotation.")]
    [SerializeField] private Vector3 lidOpenEuler = new Vector3(-110f, 0f, 0f);
    [SerializeField] private Transform wires;
    [Tooltip("How far the wires rise out of the hatch when it opens.")]
    [SerializeField] private float wireRise = 0.75f;
    [SerializeField] private float openTime = 0.6f;

    [Header("Beacon lamp")]
    [SerializeField] private Renderer[] lamps = new Renderer[0];
    [SerializeField] private Light lampLight;
    [SerializeField] private Material lampClosedMaterial;
    [SerializeField] private Material lampOpenMaterial;
    [SerializeField] private Color lampClosedColor = new Color(1f, 0.12f, 0.06f);
    [SerializeField] private Color lampOpenColor = new Color(0.2f, 1f, 0.25f);

    [Header("Shield")]
    [SerializeField] private GameObject shieldBubble;

    [Header("Hit effect")]
    [SerializeField] private Material sparkMaterial;
    [SerializeField] private Color sparkColor = new Color(1f, 0.85f, 0.3f);

    private BossMachine boss;
    private Quaternion lidClosed;
    private Vector3 wiresClosed;
    private float amount;       // 0 closed .. 1 open
    private bool lampShowsOpen;
    private float nextSparkTime;

    public bool IsOpen { get; private set; }
    public bool IsShielded { get; private set; }
    /// <summary>True once the hatch is open far enough for the wires to be hit.</summary>
    public bool Exposed => IsOpen && !IsShielded && amount > 0.7f;

    private void Awake()
    {
        boss = GetComponentInParent<BossMachine>();
        if (lid != null) lidClosed = lid.localRotation;
        if (wires != null) wiresClosed = wires.localPosition;
        ApplyPose();
        SetLamp(false);
        SetShield(false);
    }

    public void SetOpen(bool open)
    {
        if (open == IsOpen) return;
        IsOpen = open;
        GameAudio.Play(open ? SoundId.DoorOpen : SoundId.DoorClose, transform.position);
    }

    public void SetShield(bool on)
    {
        IsShielded = on;
        if (shieldBubble != null) shieldBubble.SetActive(on);
    }

    private void Update()
    {
        float target = IsOpen ? 1f : 0f;
        if (!Mathf.Approximately(amount, target))
        {
            amount = Mathf.MoveTowards(amount, target, Time.deltaTime / Mathf.Max(0.05f, openTime));
            ApplyPose();
        }
        bool showOpen = IsOpen && amount > 0.5f;
        if (showOpen != lampShowsOpen) SetLamp(showOpen);

        // Shield bubble shimmers.
        if (IsShielded && shieldBubble != null)
        {
            float s = 1f + Mathf.Sin(Time.time * 6f + transform.position.x) * 0.025f;
            shieldBubble.transform.localScale = bubbleScale * s;
        }
    }

    private Vector3 bubbleScale = Vector3.one;
    private void Start()
    {
        if (shieldBubble != null) bubbleScale = shieldBubble.transform.localScale;
    }

    private void ApplyPose()
    {
        float t = Mathf.SmoothStep(0f, 1f, amount);
        if (lid != null) lid.localRotation = lidClosed * Quaternion.Euler(lidOpenEuler * t);
        if (wires != null) wires.localPosition = wiresClosed + Vector3.up * (wireRise * t);
    }

    private void SetLamp(bool open)
    {
        lampShowsOpen = open;
        Material m = open ? lampOpenMaterial : lampClosedMaterial;
        if (m != null)
            foreach (Renderer r in lamps)
                if (r != null) r.sharedMaterial = m;
        if (lampLight != null) lampLight.color = open ? lampOpenColor : lampClosedColor;
    }

    /// <summary>Called by the wires when they are shot.</summary>
    public void HitWires(float damage)
    {
        if (!Exposed || boss == null) return;
        boss.TakeWeakPointDamage(damage, this);
        if (Time.time >= nextSparkTime && wires != null)
        {
            nextSparkTime = Time.time + 0.12f;
            Vector3 p = wires.position + Vector3.up * Random.Range(0.3f, 0.9f) + Random.insideUnitSphere * 0.2f;
            GearExplosion.Spawn(p, Random.Range(0.25f, 0.45f), sparkMaterial, sparkColor, 0.18f);
        }
    }
}
