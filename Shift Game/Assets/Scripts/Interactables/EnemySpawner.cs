using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// A chute hanging in the air. When the player walks into one of its trigger zones it drops enemies
/// through the hole underneath, one at a time with a delay between them. Set how many of each enemy type
/// to spawn. When the last one has dropped out, a trapdoor slides out of the side housing and closes the hole.
/// Each spawner only runs once.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [System.Serializable]
    public class SpawnEntry
    {
        public string label;
        public GameObject prefab;
        [Min(0)] public int count;
    }

    [Header("Start")]
    [Tooltip("Spawning starts when the player walks into any of these (DoorTriggerZone prefabs work).")]
    [SerializeField] private DoorTriggerZone[] triggerZones = new DoorTriggerZone[0];
    [Tooltip("Start as soon as the level starts instead of waiting for a trigger zone.")]
    [SerializeField] private bool spawnOnStart;

    [Header("Enemies")]
    [SerializeField] private SpawnEntry[] enemies =
    {
        new SpawnEntry { label = "Enemy type 1" },
        new SpawnEntry { label = "Enemy type 2" },
        new SpawnEntry { label = "Enemy type 3" },
        new SpawnEntry { label = "Enemy type 4" },
    };
    [Tooltip("Seconds from the trigger to the first enemy.")]
    [SerializeField] private float firstSpawnDelay = 0.75f;
    [Tooltip("Seconds between two enemies.")]
    [SerializeField] private float delayBetweenSpawns = 1.5f;
    [Tooltip("Off: all of type 1, then type 2, and so on. On: random order.")]
    [SerializeField] private bool shuffleOrder;

    [Header("Endless (boss fight)")]
    [Tooltip("Never runs out: keeps dropping enemies forever and never closes. The counts above become weights " +
             "(count 2 drops twice as often as count 1).")]
    [SerializeField] private bool endless;
    [Tooltip("Endless: stops dropping while this many of its enemies are still alive.")]
    [SerializeField, Min(1)] private int maxAlive = 3;

    [Header("Drop")]
    [Tooltip("Where enemies appear (inside the chute). They fall straight down from here.")]
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private float dropGravity = 20f;

    [Header("Trapdoor")]
    [SerializeField] private Transform trapdoor;
    [Tooltip("Local offset the trapdoor slides by to close the hole.")]
    [SerializeField] private Vector3 trapdoorSlide = new Vector3(-2.9f, 0f, 0f);
    [SerializeField] private float trapdoorCloseTime = 0.8f;
    [Tooltip("Seconds after the last enemy has landed before the trapdoor closes.")]
    [SerializeField] private float closeDelay = 0.5f;

    [Header("Lamps")]
    [SerializeField] private Renderer[] warningLamps = new Renderer[0];
    [SerializeField] private Material lampOffMaterial;
    [SerializeField] private Material lampOnMaterial;

    private enum State { Waiting, Spawning, Closing, Done }
    private State state = State.Waiting;
    private Vector3 trapdoorOpenPos;

    public bool HasStarted => state != State.Waiting;
    public bool IsDone => state == State.Done;
    public int TotalToSpawn
    {
        get
        {
            int n = 0;
            if (enemies != null) foreach (SpawnEntry e in enemies) if (e != null && e.prefab != null) n += Mathf.Max(0, e.count);
            return n;
        }
    }

    public static readonly List<EnemySpawner> All = new List<EnemySpawner>();

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() => All.Remove(this);

    private void Awake()
    {
        if (trapdoor != null) trapdoorOpenPos = trapdoor.localPosition;
        SetLamps(false);
    }

    private void Start()
    {
        if (spawnOnStart) Activate();
    }

    private void Update()
    {
        if (state != State.Waiting || triggerZones == null) return;
        foreach (DoorTriggerZone zone in triggerZones)
        {
            if (zone != null && zone.PlayerInside)
            {
                Activate();
                return;
            }
        }
    }

    /// <summary>Starts spawning (only once). Can also be called from other scripts.</summary>
    public void Activate()
    {
        if (state != State.Waiting) return;
        state = State.Spawning;
        StartCoroutine(Run());
    }

    /// <summary>Stops spawning for good (e.g. the boss died) and closes the hatch.</summary>
    public void StopSpawning()
    {
        if (state == State.Done || state == State.Closing) return;
        StopAllCoroutines();
        state = State.Closing;
        SetLamps(false);
        StartCoroutine(CloseTrapdoor());
    }

    private readonly List<GameObject> endlessSpawned = new List<GameObject>();

    private IEnumerator RunEndless()
    {
        Coroutine blink = StartCoroutine(Blink());
        yield return new WaitForSeconds(firstSpawnDelay);
        while (true)
        {
            endlessSpawned.RemoveAll(go => go == null || (go.TryGetComponent(out RobotEnemy r) && r.IsDead));
            GameObject prefab = PickWeighted();
            if (prefab == null) yield break;

            // Lamps blink while there's room for another enemy, and go dark while the spawner waits.
            bool room = endlessSpawned.Count < maxAlive;
            if (room && blink == null) blink = StartCoroutine(Blink());
            if (!room && blink != null) { StopCoroutine(blink); blink = null; SetLamps(false); }

            if (room)
            {
                EnemyDrop drop = Spawn(prefab);
                if (drop != null) endlessSpawned.Add(drop.gameObject);
                yield return new WaitForSeconds(delayBetweenSpawns);
            }
            else yield return new WaitForSeconds(0.5f);
        }
    }

    private GameObject PickWeighted()
    {
        int total = TotalToSpawn;
        if (total <= 0) return null;
        int roll = Random.Range(0, total);
        foreach (SpawnEntry e in enemies)
        {
            if (e == null || e.prefab == null || e.count <= 0) continue;
            if (roll < e.count) return e.prefab;
            roll -= e.count;
        }
        return null;
    }

    private IEnumerator CloseTrapdoor()
    {
        if (trapdoor != null)
        {
            GameAudio.Play(SoundId.SpawnerHatch, trapdoor.position);
            Vector3 from = trapdoor.localPosition, to = trapdoorOpenPos + trapdoorSlide;
            for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, trapdoorCloseTime))
            {
                trapdoor.localPosition = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
            trapdoor.localPosition = to;
        }
        state = State.Done;
    }

    private IEnumerator Run()
    {
        if (endless)
        {
            yield return RunEndless();
            yield break;
        }

        List<GameObject> queue = BuildQueue();
        Coroutine blink = StartCoroutine(Blink());

        yield return new WaitForSeconds(firstSpawnDelay);
        EnemyDrop last = null;
        for (int i = 0; i < queue.Count; i++)
        {
            last = Spawn(queue[i]);
            if (i < queue.Count - 1) yield return new WaitForSeconds(delayBetweenSpawns);
        }

        // Wait for the last one to fall out of the hole (the drop removes itself on landing).
        float timeout = Time.time + 6f;
        while (last != null && !last.Landed && Time.time < timeout) yield return null;
        yield return new WaitForSeconds(closeDelay);

        state = State.Closing;
        StopCoroutine(blink);
        SetLamps(false);
        yield return CloseTrapdoor();
    }

    private List<GameObject> BuildQueue()
    {
        var queue = new List<GameObject>();
        if (enemies != null)
            foreach (SpawnEntry e in enemies)
                if (e != null && e.prefab != null)
                    for (int i = 0; i < e.count; i++) queue.Add(e.prefab);

        if (shuffleOrder)
        {
            for (int i = queue.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (queue[i], queue[j]) = (queue[j], queue[i]);
            }
        }
        return queue;
    }

    private EnemyDrop Spawn(GameObject prefab)
    {
        Vector3 position = spawnPoint != null ? spawnPoint.position : transform.position;
        Quaternion rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        PlayerHealth player = FindFirstObjectByType<PlayerHealth>();
        if (player != null)
        {
            Vector3 look = player.transform.position - position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f) rotation = Quaternion.LookRotation(look);
        }

        // Spawn under an inactive holder so the enemy's NavMeshAgent can be paused before it wakes up
        // (otherwise it snaps straight down onto the NavMesh).
        GameObject holder = new GameObject("SpawnHolder");
        holder.SetActive(false);
        GameObject enemy = Instantiate(prefab, position, rotation, holder.transform);
        enemy.name = prefab.name;
        EnemyDrop drop = EnemyDrop.Begin(enemy, dropGravity);
        enemy.transform.SetParent(null, true);
        Destroy(holder);
        return drop;
    }

    private IEnumerator Blink()
    {
        while (true)
        {
            SetLamps(true);
            yield return new WaitForSeconds(0.25f);
            SetLamps(false);
            yield return new WaitForSeconds(0.25f);
        }
    }

    private void SetLamps(bool on)
    {
        Material m = on ? lampOnMaterial : lampOffMaterial;
        if (m == null || warningLamps == null) return;
        foreach (Renderer r in warningLamps)
            if (r != null) r.sharedMaterial = m;
    }

    // ---------------------------------------------------------------- gizmos

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Vector3 from = spawnPoint != null ? spawnPoint.position : transform.position;

        // Drop line down to the floor.
        Vector3 floor = from + Vector3.down * 30f;
        if (Physics.Raycast(transform.position + Vector3.down * 0.3f, Vector3.down, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore))
            floor = new Vector3(from.x, hit.point.y, from.z);
        Handles.color = new Color(1f, 0.25f, 0.1f);
        Handles.DrawDottedLine(from, floor, 4f);
        Handles.DrawWireDisc(floor + Vector3.up * 0.02f, Vector3.up, 1f);

        // Lines to the trigger zones.
        bool any = spawnOnStart;
        Handles.color = SlidingDoor.TriggerLinkColor;
        if (triggerZones != null)
        {
            foreach (DoorTriggerZone zone in triggerZones)
            {
                if (zone == null) continue;
                any = true;
                Collider c = zone.GetComponent<Collider>();
                Vector3 to = c != null ? c.bounds.center : zone.transform.position;
                Handles.DrawAAPolyLine(5f, transform.position, to);
                Gizmos.color = SlidingDoor.TriggerLinkColor;
                Gizmos.DrawSphere(to, 0.12f);
            }
        }

        string label = endless ? $"Endless spawner (max {maxAlive} alive)" : $"Spawner: {TotalToSpawn} enemies";
        if (!any) label += "\nnothing starts it";
        Handles.Label(transform.position + Vector3.up * 1.5f, label);
    }
#endif
}
