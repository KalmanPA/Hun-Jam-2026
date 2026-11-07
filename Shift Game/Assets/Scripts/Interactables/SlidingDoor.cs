using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Two-panel sliding door with an open and a closed state.
///  - Start Open: the door begins open.
///  - Close Triggers: when the player walks into one of these (place it past the door) the door shuts behind them.
///    Trigger zones only ever close a door.
///  - Open Condition: what opens it: every linked button pressed at the same time, or every linked target destroyed.
///    After a close trigger has shut the door, the open condition can open it again.
///  - Room Zones (lockdown): while living enemies are inside these zones (or a spawner in them is still dropping
///    enemies) the open condition can't open the door, however many buttons and targets are done.
/// Bright lines in the Scene view show what is linked.
/// </summary>
public class SlidingDoor : MonoBehaviour
{
    public enum OpenCondition { None, ShootAllButtons, DestroyAllTargets }

    [Tooltip("The door begins open (until a close trigger shuts it).")]
    [SerializeField] private bool startOpen;
    [Tooltip("The door shuts when the player walks into any of these. Put them past the door.")]
    [FormerlySerializedAs("triggerZones")]
    [SerializeField] private DoorTriggerZone[] closeTriggers = new DoorTriggerZone[0];

    [Tooltip("What opens the door. None = only Start Open opens it.")]
    [SerializeField] private OpenCondition openCondition = OpenCondition.None;
    [Tooltip("All of these must be pressed at the same time.")]
    [SerializeField] private DoorButton[] buttons = new DoorButton[0];
    [Tooltip("All of these must be destroyed.")]
    [SerializeField] private DoorTarget[] targets = new DoorTarget[0];
    [Tooltip("Once opened, stay open. Off: a button door closes again when a button pops back up.")]
    [SerializeField] private bool stayOpen = true;

    [Header("Lockdown")]
    [Tooltip("The room(s) this door locks down. While enemies are alive inside, the open condition can't open the door.")]
    [SerializeField] private DoorTriggerZone[] roomZones = new DoorTriggerZone[0];

    [Header("Parts")]
    [SerializeField] private Transform leftPanel;
    [SerializeField] private Transform rightPanel;
    [Tooltip("How far each panel slides sideways.")]
    [SerializeField] private float slideDistance = 1.3f;
    [Tooltip("Metres per second.")]
    [SerializeField] private float slideSpeed = 2.5f;
    [Tooltip("Lamps that show red while closed, green when open, and blink amber during lockdown.")]
    [SerializeField] private Renderer[] statusLamps = new Renderer[0];
    [SerializeField] private Material lampClosedMaterial;
    [SerializeField] private Material lampOpenMaterial;
    [SerializeField] private Material lampLockdownMaterial;

    private Vector3 leftClosed;
    private Vector3 rightClosed;
    private float openAmount; // 0 closed, 1 open
    private bool latched;
    private bool startOpenActive;
    private bool closedByTrigger;
    private Material lampShown;

    public OpenCondition Condition => openCondition;
    public bool IsOpen { get; private set; }
    public bool InLockdown { get; private set; }

    private void Awake()
    {
        if (leftPanel != null) leftClosed = leftPanel.localPosition;
        if (rightPanel != null) rightClosed = rightPanel.localPosition;
        startOpenActive = startOpen;
        if (startOpen) openAmount = 1f;
        ApplyPanels();
        ShowLamp(startOpen ? lampOpenMaterial : lampClosedMaterial);
    }

    private void Update()
    {
        if (!closedByTrigger && PlayerInCloseTrigger())
        {
            closedByTrigger = true;
            startOpenActive = false;
            latched = false;
        }

        InLockdown = RoomHasEnemies();
        bool met = ConditionMet();
        bool canOpen = met && !InLockdown;
        if (canOpen && stayOpen && !latched)
        {
            latched = true;
            if (openCondition == OpenCondition.ShootAllButtons)
                foreach (DoorButton b in buttons)
                    if (b != null) b.Lock();
        }
        IsOpen = startOpenActive || latched || canOpen;

        float step = slideDistance > 0f ? slideSpeed / slideDistance * Time.deltaTime : 1f;
        openAmount = Mathf.MoveTowards(openAmount, IsOpen ? 1f : 0f, step);
        ApplyPanels();

        if (IsOpen) ShowLamp(lampOpenMaterial);
        else if (InLockdown && lampLockdownMaterial != null) ShowLamp(Mathf.Repeat(Time.time, 0.6f) < 0.3f ? lampLockdownMaterial : lampClosedMaterial);
        else ShowLamp(lampClosedMaterial);
    }

    private void ApplyPanels()
    {
        float eased = Mathf.SmoothStep(0f, 1f, openAmount);
        if (leftPanel != null) leftPanel.localPosition = leftClosed + Vector3.left * (slideDistance * eased);
        if (rightPanel != null) rightPanel.localPosition = rightClosed + Vector3.right * (slideDistance * eased);
    }

    private bool PlayerInCloseTrigger()
    {
        if (closeTriggers == null) return false;
        foreach (DoorTriggerZone z in closeTriggers)
            if (z != null && z.PlayerInside) return true;
        return false;
    }

    private bool RoomHasEnemies()
    {
        if (roomZones == null || roomZones.Length == 0) return false;
        foreach (DoorTriggerZone zone in roomZones)
        {
            if (zone == null) continue;
            foreach (RobotEnemy r in RobotEnemy.Alive)
                if (r != null && !r.IsDead && zone.ContainsXZ(r.transform.position)) return true;
            foreach (EnemySpawner s in EnemySpawner.All)
                if (s != null && s.HasStarted && !s.IsDone && zone.ContainsXZ(s.transform.position)) return true;
        }
        return false;
    }

    private bool ConditionMet()
    {
        switch (openCondition)
        {
            case OpenCondition.ShootAllButtons:
            {
                bool any = false;
                foreach (DoorButton b in buttons)
                {
                    if (b == null) continue;
                    if (!b.IsPressed) return false;
                    any = true;
                }
                return any;
            }

            case OpenCondition.DestroyAllTargets:
            {
                foreach (DoorTarget t in targets)
                    if (t != null && !t.IsDestroyed) return false; // a deleted target counts as destroyed
                return targets.Length > 0;
            }

            default:
                return false;
        }
    }

    private void ShowLamp(Material m)
    {
        if (m == null || m == lampShown || statusLamps == null) return;
        lampShown = m;
        foreach (Renderer r in statusLamps)
            if (r != null) r.sharedMaterial = m;
    }

    // ---------------------------------------------------------------- gizmos

#if UNITY_EDITOR
    public static readonly Color TriggerLinkColor = new Color(1f, 0.9f, 0f);
    public static readonly Color ButtonLinkColor = new Color(0f, 1f, 1f);
    public static readonly Color TargetLinkColor = new Color(1f, 0.15f, 1f);
    public static readonly Color RoomLinkColor = new Color(1f, 0.5f, 0f);

    private void OnDrawGizmos()
    {
        Vector3 from = transform.position + Vector3.up * 2f;
        bool any = startOpen;
        any |= DrawLinks(from, closeTriggers, TriggerLinkColor);
        if (openCondition == OpenCondition.ShootAllButtons) any |= DrawLinks(from, buttons, ButtonLinkColor);
        if (openCondition == OpenCondition.DestroyAllTargets) any |= DrawLinks(from, targets, TargetLinkColor);
        DrawLinks(from, roomZones, RoomLinkColor);

        Gizmos.color = any ? Color.white : Color.red;
        Gizmos.DrawWireSphere(from, 0.25f);
        if (!any) Handles.Label(from + Vector3.up * 0.4f, "Door: nothing opens it");
    }

    private static bool DrawLinks(Vector3 from, Component[] links, Color color)
    {
        bool any = false;
        if (links == null) return false;
        foreach (Component link in links)
        {
            if (link == null) continue;
            any = true;
            Collider c = link.GetComponent<Collider>();
            Vector3 to = link is DoorTriggerZone && c != null ? c.bounds.center : link.transform.position;
            Handles.color = color;
            Handles.DrawAAPolyLine(5f, from, to);
            Gizmos.color = color;
            Gizmos.DrawSphere(to, 0.12f);
        }
        return any;
    }
#endif
}
