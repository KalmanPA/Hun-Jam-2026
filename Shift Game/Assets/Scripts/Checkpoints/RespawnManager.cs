using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps track of the active checkpoint and resets level segments when the player respawns.
/// Created automatically by the first Checkpoint or ChunkLoader that wakes up, so nothing needs to be placed in the scene.
///
/// Segment reset: when the scene starts, every segment a Checkpoint points at is copied into a hidden, inactive
/// "snapshot". On respawn the live segment is destroyed and a fresh copy of the snapshot (or of the checkpoint's
/// segment prefab, if one is assigned) takes its place, so enemies, doors, buttons and pickups are back to how
/// they were when the level loaded.
///
/// Anything that points at a segment (checkpoint and chunk loader lists) should call <see cref="Resolve"/> so the
/// reference follows the segment to its newest copy.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class RespawnManager : MonoBehaviour
{
    private class SegmentSlot
    {
        public GameObject original;
        public GameObject live;
        public GameObject snapshot;
        public Transform parent;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
        public int siblingIndex;
        public string name;
    }

    private static RespawnManager instance;

    private readonly Dictionary<int, SegmentSlot> slotsById = new Dictionary<int, SegmentSlot>();
    private readonly HashSet<int> rootsAtLoad = new HashSet<int>();
    private Transform snapshotHolder;
    private bool scanned;

    public static Checkpoint Active { get; private set; }

    /// <summary>The manager for the current scene (created on first use).</summary>
    public static RespawnManager Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject("[RespawnManager]");
                instance = go.AddComponent<RespawnManager>();
            }
            return instance;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(this); return; }
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
            Active = null;
        }
    }

    /// <summary>
    /// Runs once per scene: takes a snapshot of every segment any checkpoint (active or not) points at,
    /// before the segment's own scripts wake up. Called by Checkpoint and ChunkLoader in Awake.
    /// </summary>
    public static void EnsureScanned()
    {
        RespawnManager m = Instance;
        if (m.scanned) return;
        m.scanned = true;

        foreach (GameObject root in m.gameObject.scene.GetRootGameObjects())
            m.rootsAtLoad.Add(root.GetInstanceID());

        Checkpoint[] all = FindObjectsByType<Checkpoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Checkpoint cp in all)
            if (cp.Segment != null) m.Register(cp.Segment, cp.SegmentPrefab);
    }

    private SegmentSlot Register(GameObject segment, GameObject prefab)
    {
        int id = segment.GetInstanceID();
        if (slotsById.TryGetValue(id, out SegmentSlot existing)) return existing;

        if (snapshotHolder == null)
        {
            GameObject holder = new GameObject("[Segment Snapshots]");
            holder.SetActive(false); // nothing under here ever wakes up
            snapshotHolder = holder.transform;
        }

        Transform t = segment.transform;
        SegmentSlot slot = new SegmentSlot
        {
            original = segment,
            live = segment,
            parent = t.parent,
            localPosition = t.localPosition,
            localRotation = t.localRotation,
            localScale = t.localScale,
            siblingIndex = t.GetSiblingIndex(),
            name = segment.name,
        };
        // A prefab gives a clean copy. Without one, copy the scene version now, before anything in it has run.
        slot.snapshot = prefab != null ? prefab : Instantiate(segment, snapshotHolder);
        if (prefab == null) slot.snapshot.name = segment.name + " (snapshot)";

        slotsById[id] = slot;
        return slot;
    }

    /// <summary>Returns the newest live copy of a segment (or the object itself if it isn't a reset segment).</summary>
    public static GameObject Resolve(GameObject go)
    {
        if (ReferenceEquals(go, null) || instance == null) return go;
        if (instance.slotsById.TryGetValue(go.GetInstanceID(), out SegmentSlot slot)) return slot.live;
        return go;
    }

    /// <summary>Turns a chunk / segment on or off, following segments to their newest copy.</summary>
    public static void SetChunkActive(GameObject go, bool active)
    {
        GameObject target = Resolve(go);
        if (target != null && target.activeSelf != active) target.SetActive(active);
    }

    public static void Activate(Checkpoint checkpoint)
    {
        if (checkpoint == null || Active == checkpoint) return;
        Active = checkpoint;
    }

    /// <summary>
    /// Resets the world for a respawn at the active checkpoint. Returns false when there is no checkpoint,
    /// in which case the caller should reload the scene or use the level start.
    /// </summary>
    public static bool ResetToActiveCheckpoint(out Vector3 spawnPosition, out Quaternion spawnRotation)
    {
        spawnPosition = Vector3.zero;
        spawnRotation = Quaternion.identity;
        Checkpoint cp = Active;
        if (cp == null || instance == null) return false;

        Transform spawn = cp.SpawnPoint;
        spawnPosition = spawn.position;
        spawnRotation = spawn.rotation;

        // Read everything the checkpoint needs before its segment (which may contain it) is replaced.
        List<GameObject> unload = new List<GameObject>(cp.UnloadedChunks);
        List<GameObject> load = new List<GameObject>(cp.LoadedChunks);
        GameObject segment = cp.Segment;
        // ReferenceEquals: after the first reset this can point at the destroyed original, which still resolves.
        bool hasSegment = !ReferenceEquals(segment, null);
        string activePath = null;
        GameObject oldLive = hasSegment ? Resolve(segment) : null;
        if (oldLive != null && cp.transform.IsChildOf(oldLive.transform))
            activePath = PathFrom(oldLive.transform, cp.transform);

        foreach (GameObject go in unload) SetChunkActive(go, false);

        GameObject newLive = null;
        if (hasSegment) newLive = instance.ResetSegment(segment, cp.SegmentPrefab);

        foreach (GameObject go in load) SetChunkActive(go, true);
        if (newLive != null && !newLive.activeSelf) newLive.SetActive(true);

        // Keep the same checkpoint active if it lived inside the segment that was just replaced.
        if (activePath != null && newLive != null)
        {
            Transform found = FindPath(newLive.transform, activePath);
            Active = found != null ? found.GetComponent<Checkpoint>() : null;
            if (Active != null) Active.MarkReached();
        }

        instance.CleanUpRuntimeLeftovers();
        return true;
    }

    private GameObject ResetSegment(GameObject segment, GameObject prefab)
    {
        SegmentSlot slot = Register(segment, prefab);
        if (slot.live != null)
        {
            slot.live.SetActive(false); // gone from physics and view right away; destroyed at the end of the frame
            Destroy(slot.live);
        }

        GameObject copy = Instantiate(slot.snapshot, slot.parent);
        copy.name = slot.name;
        Transform t = copy.transform;
        t.localPosition = slot.localPosition;
        t.localRotation = slot.localRotation;
        t.localScale = slot.localScale;
        if (slot.parent != null) t.SetSiblingIndex(Mathf.Min(slot.siblingIndex, slot.parent.childCount - 1));

        slot.live = copy;
        slotsById[copy.GetInstanceID()] = slot;
        return copy;
    }

    /// <summary>Removes things spawned during play at the scene root: spawned robots, dropped pickups, bullets.</summary>
    private void CleanUpRuntimeLeftovers()
    {
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            if (rootsAtLoad.Contains(root.GetInstanceID()) || root == gameObject) continue;
            if (root.GetComponent<RobotEnemy>() != null || root.GetComponent<Pickup>() != null ||
                root.GetComponent<EnemyBullet>() != null || root.GetComponent<GearBullet>() != null ||
                root.GetComponent<EnemyLaser>() != null)
            {
                Destroy(root);
            }
        }
    }

    private static string PathFrom(Transform root, Transform child)
    {
        List<string> parts = new List<string>();
        for (Transform t = child; t != null && t != root; t = t.parent) parts.Insert(0, t.GetSiblingIndex().ToString());
        return string.Join("/", parts);
    }

    private static Transform FindPath(Transform root, string path)
    {
        Transform t = root;
        if (string.IsNullOrEmpty(path)) return t;
        foreach (string part in path.Split('/'))
        {
            int i = int.Parse(part);
            if (i >= t.childCount) return null;
            t = t.GetChild(i);
        }
        return t;
    }

    /// <summary>True if the scene can be reloaded (it is in the build settings).</summary>
    public static bool CanReloadScene => SceneManager.GetActiveScene().buildIndex >= 0;
}
