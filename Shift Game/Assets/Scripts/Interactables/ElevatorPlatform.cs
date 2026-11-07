using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// A flat platform that rises slowly when the player steps on it and stops for good at its end point.
/// If the player gets off during the first few seconds of the ride (for longer than a jump takes),
/// it goes back down and waits for them again. After that it is committed and finishes the ride.
/// Origin = middle of the top surface. The end point is set as an offset (yellow box in the Scene view).
/// </summary>
[DefaultExecutionOrder(-50)] // move before the player controller runs
public class ElevatorPlatform : MonoBehaviour
{
    [Header("Route")]
    [Tooltip("Where the top of the platform ends up, relative to where it starts (local space).")]
    [SerializeField] private Vector3 endOffset = new Vector3(0f, 5f, 0f);
    [Tooltip("Metres per second going up.")]
    [SerializeField] private float riseSpeed = 1.2f;
    [Tooltip("Metres per second going back down.")]
    [SerializeField] private float returnSpeed = 2.5f;
    [Tooltip("Seconds the player has to stand on it before it starts moving.")]
    [SerializeField] private float startDelay = 0.4f;

    [Header("Getting off")]
    [Tooltip("During this many seconds of the ride, getting off sends the platform back down. After that it always finishes.")]
    [SerializeField] private float returnWindow = 3f;
    [Tooltip("Being off the platform for less than this (a jump) doesn't count.")]
    [SerializeField] private float jumpTolerance = 0.9f;

    [Header("Size")]
    [Tooltip("Size of the top surface the player can stand on (X, Z).")]
    [SerializeField] private Vector2 topSize = new Vector2(3f, 3f);

    [Header("Lamps")]
    [SerializeField] private Renderer[] lamps = new Renderer[0];
    [SerializeField] private Material lampIdleMaterial;
    [SerializeField] private Material lampMovingMaterial;
    [SerializeField] private Material lampDoneMaterial;

    public enum State { Waiting, Starting, Rising, Returning, Arrived }
    public State Current { get; private set; } = State.Waiting;

    private static CharacterController player;
    private Vector3 startPos;
    private Vector3 endPos;
    private float timer;      // starting delay, then ride time
    private float offTime;
    private Material lampShown;

    private void Awake()
    {
        startPos = transform.position;
        endPos = transform.TransformPoint(endOffset);
        ShowLamp(lampIdleMaterial);
    }

    private void Update()
    {
        bool onIt = PlayerOnTop();
        float dt = Time.deltaTime;

        switch (Current)
        {
            case State.Waiting:
                if (onIt) { Current = State.Starting; timer = 0f; }
                break;

            case State.Starting:
                if (!onIt) { Current = State.Waiting; break; }
                timer += dt;
                if (timer >= startDelay) { Current = State.Rising; timer = 0f; offTime = 0f; }
                break;

            case State.Rising:
                timer += dt;
                offTime = onIt ? 0f : offTime + dt;
                if (timer <= returnWindow && offTime > jumpTolerance)
                {
                    Current = State.Returning;
                    break;
                }
                if (MoveTowards(endPos, riseSpeed, dt, onIt)) Current = State.Arrived;
                break;

            case State.Returning:
                if (onIt && timer > 0f) { Current = State.Rising; timer = 0f; offTime = 0f; break; } // hopped back on: go up again
                if (MoveTowards(startPos, returnSpeed, dt, onIt)) Current = State.Waiting;
                break;
        }

        ShowLamp(Current == State.Arrived ? lampDoneMaterial
            : Current == State.Rising || Current == State.Returning ? lampMovingMaterial : lampIdleMaterial);
    }

    /// <summary>Moves the platform (and the player standing on it). Returns true on arrival.</summary>
    private bool MoveTowards(Vector3 target, float speed, float dt, bool carryPlayer)
    {
        Vector3 before = transform.position;
        Vector3 after = Vector3.MoveTowards(before, target, speed * dt);
        transform.position = after;
        if (carryPlayer && player != null && player.enabled)
            player.Move(after - before);
        return (after - target).sqrMagnitude < 0.000001f;
    }

    private bool PlayerOnTop()
    {
        if (player == null)
        {
            FirstPersonController fpc = FindFirstObjectByType<FirstPersonController>();
            if (fpc != null) player = fpc.GetComponent<CharacterController>();
        }
        if (player == null || !player.enabled) return false;

        // Feet = bottom of the controller's capsule.
        Vector3 feet = player.transform.TransformPoint(player.center) - Vector3.up * (player.height * 0.5f);
        Vector3 local = transform.InverseTransformPoint(feet);
        float margin = player.radius * 0.5f;
        return Mathf.Abs(local.x) <= topSize.x * 0.5f + margin
            && Mathf.Abs(local.z) <= topSize.y * 0.5f + margin
            && local.y > -0.25f && local.y < 0.35f;
    }

    private void ShowLamp(Material m)
    {
        if (m == null || m == lampShown || lamps == null) return;
        lampShown = m;
        foreach (Renderer r in lamps)
            if (r != null) r.sharedMaterial = m;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // Where the top ends up.
        Vector3 start = Application.isPlaying ? startPos : transform.position;
        Vector3 end = Application.isPlaying ? endPos : transform.TransformPoint(endOffset);
        Handles.color = new Color(1f, 0.85f, 0f);
        Handles.DrawDottedLine(start, end, 4f);
        Gizmos.color = new Color(1f, 0.85f, 0f);
        Gizmos.matrix = Matrix4x4.TRS(end, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.down * 0.2f, new Vector3(topSize.x, 0.4f, topSize.y));
        Gizmos.matrix = Matrix4x4.identity;
        Handles.Label(end + Vector3.up * 0.5f, "Elevator end");
    }
#endif
}
