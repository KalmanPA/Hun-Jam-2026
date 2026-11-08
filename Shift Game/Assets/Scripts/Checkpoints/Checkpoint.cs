using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Walk into the trigger to make this the active checkpoint. When the player dies they respawn on the
/// child named "SpawnPoint" (position and facing), this checkpoint's segment is rebuilt from scratch so
/// enemies, doors and pickups reset, and the chunk lists below decide which segments are on.
///
/// Tip: keep checkpoints outside the segment they reset (for example next to the segment roots).
/// A checkpoint inside its own segment also works; it is swapped for its fresh copy on respawn.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class Checkpoint : MonoBehaviour
{
    [Tooltip("Where the player respawns. Defaults to the child named SpawnPoint. The blue arrow is the way the player faces.")]
    [SerializeField] private Transform spawnPoint;

    [Header("Segment reset")]
    [Tooltip("The segment in the scene that gets rebuilt when the player respawns here (e.g. the 2_Segment instance).")]
    [SerializeField] private GameObject segment;
    [Tooltip("Optional. If set, the segment is rebuilt from this prefab. Leave empty to rebuild it exactly as it was " +
             "in the scene when the level started (keeps your scene changes to the segment).")]
    [SerializeField] private GameObject segmentPrefab;

    [Header("Chunks on respawn")]
    [Tooltip("Segments / chunks turned ON when respawning here.")]
    [SerializeField] private List<GameObject> loadedChunks = new List<GameObject>();
    [Tooltip("Segments / chunks turned OFF when respawning here.")]
    [SerializeField] private List<GameObject> unloadedChunks = new List<GameObject>();

    [Header("Player on respawn")]
    [Tooltip("Fill the ammo bar on respawn. Off = ammo goes back to what it was when this checkpoint was reached.")]
    [SerializeField] private bool refillAmmo = true;

    [Header("Activation")]
    [Tooltip("Once reached, walking through it again (for example after reaching a later checkpoint) does nothing.")]
    [SerializeField] private bool activateOnce = true;

    private bool reached;

    public Transform SpawnPoint => spawnPoint != null ? spawnPoint : transform;
    public GameObject Segment => segment;
    public GameObject SegmentPrefab => segmentPrefab;
    public IReadOnlyList<GameObject> LoadedChunks => loadedChunks;
    public IReadOnlyList<GameObject> UnloadedChunks => unloadedChunks;
    public bool RefillAmmo => refillAmmo;
    public int AmmoWhenReached { get; private set; } = -1;
    public bool IsActive => RespawnManager.Active == this;

    private void Reset()
    {
        Collider c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
        FindSpawnPoint();
    }

    private void OnValidate()
    {
        if (spawnPoint == null) FindSpawnPoint();
    }

    private void FindSpawnPoint()
    {
        Transform t = transform.Find("SpawnPoint");
        if (t != null) spawnPoint = t;
    }

    private void Awake()
    {
        if (spawnPoint == null) FindSpawnPoint();
        RespawnManager.EnsureScanned();
    }

    /// <summary>Marks it as reached without changing which checkpoint is active (used after a segment reset).</summary>
    public void MarkReached() => reached = true;

    private void OnTriggerEnter(Collider other)
    {
        if (reached && activateOnce) return;
        PlayerHealth player = other.GetComponentInParent<PlayerHealth>();
        if (player == null || player.IsDead) return;

        reached = true;
        GearShiftWeapon weapon = player.GetComponentInChildren<GearShiftWeapon>();
        AmmoWhenReached = weapon != null ? weapon.Ammo : -1;
        RespawnManager.Activate(this);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        bool active = Application.isPlaying && IsActive;
        Gizmos.color = active ? new Color(0.2f, 1f, 0.4f, 1f) : new Color(0.2f, 0.8f, 1f, 0.8f);
        DrawCollider(GetComponent<Collider>());

        Transform sp = SpawnPoint;
        Vector3 p = sp.position;
        Gizmos.color = new Color(0.2f, 0.5f, 1f);
        Gizmos.DrawWireSphere(p + Vector3.up * 0.4f, 0.4f);
        Gizmos.DrawWireSphere(p + Vector3.up * 1.4f, 0.4f);
        Vector3 f = Vector3.ProjectOnPlane(sp.forward, Vector3.up).normalized;
        Vector3 eye = p + Vector3.up * 1.6f;
        Gizmos.DrawLine(eye, eye + f * 1.2f);
        Gizmos.DrawLine(eye + f * 1.2f, eye + f * 0.9f + Vector3.Cross(Vector3.up, f) * 0.2f);
        Gizmos.DrawLine(eye + f * 1.2f, eye + f * 0.9f - Vector3.Cross(Vector3.up, f) * 0.2f);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 from = transform.position + Vector3.up;
        Gizmos.color = Color.yellow;
        if (segment != null) Gizmos.DrawLine(from, segment.transform.position);
        Gizmos.color = Color.green;
        foreach (GameObject go in loadedChunks) if (go != null) Gizmos.DrawLine(from, go.transform.position);
        Gizmos.color = Color.red;
        foreach (GameObject go in unloadedChunks) if (go != null) Gizmos.DrawLine(from, go.transform.position);
    }

    public static void DrawCollider(Collider c)
    {
        if (c is BoxCollider box)
        {
            Gizmos.matrix = c.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
            Color fill = Gizmos.color; fill.a *= 0.12f;
            Gizmos.color = fill;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.matrix = Matrix4x4.identity;
        }
        else if (c != null)
        {
            Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
        }
    }
#endif
}
