using UnityEngine;

/// <summary>
/// One wire hatch (weak point) in the boss room, wired to the boss. Closed: the lid is shut and the wires are sunk
/// inside. Open: the lid swings up, the wires come out and can be shot (damage goes to the boss). The boss decides
/// when a hatch opens; it closes again once it has taken its share of damage.
/// The beacon lamp and the cable running to the boss show red while closed and green while open.
/// </summary>
public class BossWeakPoint : MonoBehaviour
{
    [Header("Parts")]
    [SerializeField] private Transform lid;
    [Tooltip("Lid rotation when fully open, on top of its closed rotation.")]
    [SerializeField] private Vector3 lidOpenEuler = new Vector3(-110f, 0f, 0f);
    [SerializeField] private Transform wires;
    [Tooltip("How far the wires rise out of the hatch when it opens.")]
    [SerializeField] private float wireRise = 0.75f;
    [SerializeField] private float openTime = 0.6f;

    [Header("Beacon lamp + cable to the boss")]
    [SerializeField] private Renderer[] lamps = new Renderer[0];
    [Tooltip("The cable pieces running from this hatch to the boss. Coloured like the lamp.")]
    [SerializeField] private Renderer[] cables = new Renderer[0];
    [SerializeField] private Light lampLight;
    [SerializeField] private Material lampClosedMaterial;
    [SerializeField] private Material lampOpenMaterial;
    [SerializeField] private Color lampClosedColor = new Color(1f, 0.12f, 0.06f);
    [SerializeField] private Color lampOpenColor = new Color(0.2f, 1f, 0.25f);

    [Header("Boss")]
    [Tooltip("Falls back to the boss in the scene.")]
    [SerializeField] private BossMachine boss;

    [Header("Hit effect")]
    [SerializeField] private Material sparkMaterial;
    [SerializeField] private Color sparkColor = new Color(1f, 0.85f, 0.3f);

    private Quaternion lidClosed;
    private Vector3 wiresClosed;
    private float amount;       // 0 closed .. 1 open
    private bool lampShowsOpen;
    private float nextSparkTime;
    private float lightBase = 4f;
    private float hitFlash;

    public bool IsOpen { get; private set; }
    /// <summary>True once the hatch is open far enough for the wires to be hit.</summary>
    public bool Exposed => IsOpen && amount > 0.7f;

    private void Awake()
    {
        if (boss == null) boss = GetComponentInParent<BossMachine>();
        if (lid != null) lidClosed = lid.localRotation;
        if (wires != null) wiresClosed = wires.localPosition;
        if (lampLight != null) lightBase = lampLight.intensity;
        ApplyPose();
        SetLamp(false);
    }

    public void SetBoss(BossMachine b) { if (b != null) boss = b; }

    public void SetOpen(bool open)
    {
        if (open == IsOpen) return;
        IsOpen = open;
        GameAudio.Play(open ? SoundId.DoorOpen : SoundId.DoorClose, transform.position);
    }

    /// <summary>Flashes the beacon after a hit.</summary>
    public void ShowDamage() => hitFlash = 1f;

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

        if (lampLight != null)
        {
            hitFlash = Mathf.MoveTowards(hitFlash, 0f, Time.deltaTime * 5f);
            float pulse = lampShowsOpen ? 1f + Mathf.Sin(Time.time * 7f) * 0.25f : 1f;
            lampLight.intensity = lightBase * pulse + hitFlash * 4f;
        }
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
        {
            foreach (Renderer r in lamps) if (r != null) r.sharedMaterial = m;
            foreach (Renderer r in cables) if (r != null) r.sharedMaterial = m;
        }
        if (lampLight != null) lampLight.color = open ? lampOpenColor : lampClosedColor;
    }

    /// <summary>Called by the wires when they are shot.</summary>
    public void HitWires(float damage)
    {
        if (boss == null) boss = BossMachine.Instance;
        if (!Exposed || boss == null) return;
        boss.TakeWeakPointDamage(damage, this);
        if (Time.time >= nextSparkTime && wires != null)
        {
            nextSparkTime = Time.time + 0.12f;
            Vector3 p = wires.position + wires.up * Random.Range(0.3f, 0.9f) + Random.insideUnitSphere * 0.2f;
            GearExplosion.Spawn(p, Random.Range(0.25f, 0.45f), sparkMaterial, sparkColor, 0.18f);
        }
    }
}
