using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Two-panel sliding door with an open and a closed state. Pick what opens it in the inspector:
///  - Player Enters Trigger: the player walks into any of the linked DoorTriggerZones.
///  - Shoot All Buttons: every linked DoorButton is pressed at the same time (each pops back after its delay).
///  - Destroy All Targets: every linked DoorTarget is shot to pieces.
/// Place the trigger zones / buttons / targets in the scene and drag them onto the door.
/// Bright lines in the Scene view show what is linked.
/// </summary>
public class SlidingDoor : MonoBehaviour
{
    public enum OpenCondition { PlayerEntersTrigger, ShootAllButtons, DestroyAllTargets }

    [SerializeField] private OpenCondition openCondition = OpenCondition.PlayerEntersTrigger;
    [Tooltip("The door opens while the player is inside any of these.")]
    [SerializeField] private DoorTriggerZone[] triggerZones = new DoorTriggerZone[0];
    [Tooltip("All of these must be pressed at the same time.")]
    [SerializeField] private DoorButton[] buttons = new DoorButton[0];
    [Tooltip("All of these must be destroyed.")]
    [SerializeField] private DoorTarget[] targets = new DoorTarget[0];
    [Tooltip("Once opened, stay open. Off: a trigger door closes when the player leaves, a button door closes when a button pops back up.")]
    [SerializeField] private bool stayOpen = true;

    [Header("Parts")]
    [SerializeField] private Transform leftPanel;
    [SerializeField] private Transform rightPanel;
    [Tooltip("How far each panel slides sideways.")]
    [SerializeField] private float slideDistance = 1.3f;
    [Tooltip("Metres per second.")]
    [SerializeField] private float slideSpeed = 2.5f;
    [Tooltip("Little lamps that show red while closed and green when open.")]
    [SerializeField] private Renderer[] statusLamps = new Renderer[0];
    [SerializeField] private Material lampClosedMaterial;
    [SerializeField] private Material lampOpenMaterial;

    private Vector3 leftClosed;
    private Vector3 rightClosed;
    private float openAmount; // 0 closed, 1 open
    private bool latched;
    private bool lampsShownOpen;
    private bool lampsShownOnce;

    public OpenCondition Condition => openCondition;
    public bool IsOpen { get; private set; }

    private void Awake()
    {
        if (leftPanel != null) leftClosed = leftPanel.localPosition;
        if (rightPanel != null) rightClosed = rightPanel.localPosition;
        ShowLamps(false);
    }

    private void Update()
    {
        bool met = ConditionMet();
        if (met && stayOpen && !latched)
        {
            latched = true;
            if (openCondition == OpenCondition.ShootAllButtons)
                foreach (DoorButton b in buttons)
                    if (b != null) b.Lock();
        }
        IsOpen = latched || met;

        float step = slideDistance > 0f ? slideSpeed / slideDistance * Time.deltaTime : 1f;
        openAmount = Mathf.MoveTowards(openAmount, IsOpen ? 1f : 0f, step);
        float eased = Mathf.SmoothStep(0f, 1f, openAmount);
        if (leftPanel != null) leftPanel.localPosition = leftClosed + Vector3.left * (slideDistance * eased);
        if (rightPanel != null) rightPanel.localPosition = rightClosed + Vector3.right * (slideDistance * eased);

        ShowLamps(IsOpen);
    }

    private bool ConditionMet()
    {
        switch (openCondition)
        {
            case OpenCondition.PlayerEntersTrigger:
                foreach (DoorTriggerZone z in triggerZones)
                    if (z != null && z.PlayerInside) return true;
                return false;

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

            default:
            {
                bool any = false;
                foreach (DoorTarget t in targets)
                {
                    if (t == null) continue; // a deleted target counts as destroyed
                    if (!t.IsDestroyed) return false;
                    any = true;
                }
                return any || targets.Length > 0;
            }
        }
    }

    private void ShowLamps(bool open)
    {
        if (lampsShownOnce && open == lampsShownOpen) return;
        lampsShownOnce = true;
        lampsShownOpen = open;
        Material m = open ? lampOpenMaterial : lampClosedMaterial;
        if (m == null || statusLamps == null) return;
        foreach (Renderer r in statusLamps)
            if (r != null) r.sharedMaterial = m;
    }

    // ---------------------------------------------------------------- gizmos

#if UNITY_EDITOR
    public static readonly Color TriggerLinkColor = new Color(1f, 0.9f, 0f);
    public static readonly Color ButtonLinkColor = new Color(0f, 1f, 1f);
    public static readonly Color TargetLinkColor = new Color(1f, 0.15f, 1f);

    private void OnDrawGizmos()
    {
        Vector3 from = transform.position + Vector3.up * 2f;
        Color color;
        Component[] links;
        switch (openCondition)
        {
            case OpenCondition.PlayerEntersTrigger: color = TriggerLinkColor; links = triggerZones; break;
            case OpenCondition.ShootAllButtons: color = ButtonLinkColor; links = buttons; break;
            default: color = TargetLinkColor; links = targets; break;
        }

        bool anyLink = false;
        if (links != null)
        {
            foreach (Component link in links)
            {
                if (link == null) continue;
                anyLink = true;
                Vector3 to = link.transform.position;
                if (link is DoorTriggerZone) to = link.GetComponent<Collider>() != null ? link.GetComponent<Collider>().bounds.center : to;
                Handles.color = color;
                Handles.DrawAAPolyLine(5f, from, to);
                Gizmos.color = color;
                Gizmos.DrawSphere(to, 0.12f);
            }
        }

        Gizmos.color = anyLink ? color : Color.red;
        Gizmos.DrawWireSphere(from, 0.25f);
        if (!anyLink)
            Handles.Label(from + Vector3.up * 0.4f, "Door: nothing linked");
    }
#endif
}
