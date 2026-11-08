using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// A point that cables plug into: cable boxes, the boxes under buttons and targets, and the big box on top of doors.
/// Cable boxes (Auto Connect on) link themselves to their nearest other nodes, and the cables (simple cylinders)
/// are rebuilt in the editor whenever something moves. Cables are only for show (doors are still linked to their
/// buttons and targets in the door's inspector).
/// A cable glows red while the buttons or targets feeding it are not done, and green once they all are.
/// "Feeding" = the buttons and targets on the side of the cable away from the door.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class CableNode : MonoBehaviour
{
    private enum Kind { CableBox, Button, Target, Door }

    [Tooltip("Link to the nearest nodes automatically (on for cable boxes).")]
    [SerializeField] private bool autoConnect;
    [Tooltip("How many of the nearest nodes to link to. 2 makes a chain; raise it where several cables meet.")]
    [SerializeField, Range(1, 6)] private int autoLinkCount = 2;
    [SerializeField] private float maxLinkDistance = 30f;
    [Tooltip("Links added by hand, on top of the automatic ones.")]
    [SerializeField] private CableNode[] extraLinks = new CableNode[0];
    [Tooltip("Where cables plug in. Empty = this object's position.")]
    [SerializeField] private Transform anchor;

    [Header("Cable look")]
    [SerializeField] private float cableThickness = 0.07f;
    [SerializeField] private Material inactiveMaterial;
    [SerializeField] private Material activeMaterial;

    [SerializeField, HideInInspector] private CableNode[] builtLinks = new CableNode[0];
    [SerializeField, HideInInspector] private Renderer[] builtCables = new Renderer[0];
    [SerializeField, HideInInspector] private int builtSignature;

    private static readonly List<CableNode> All = new List<CableNode>();
    private static readonly Dictionary<CableNode, List<CableNode>> Graph = new Dictionary<CableNode, List<CableNode>>();
    private static bool graphDirty = true;

    private Kind kind;
    private DoorButton button;
    private DoorTarget target;
    private SlidingDoor door;
    private bool[] shownActive;

    public Vector3 AnchorPosition => anchor != null ? anchor.position : transform.position;

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        graphDirty = true;
        DetectKind();
    }

    private void OnDisable()
    {
        All.Remove(this);
        graphDirty = true;
    }

    private void Start()
    {
        // Cable boxes hum quietly with electricity.
        if (Application.isPlaying && kind == Kind.CableBox) SoundLoopEmitter.Attach(gameObject, SoundId.CableBoxHum, true);
    }

    private void DetectKind()
    {
        button = GetComponentInParent<DoorButton>();
        target = GetComponentInParent<DoorTarget>();
        door = GetComponentInParent<SlidingDoor>();
        kind = button != null ? Kind.Button : target != null ? Kind.Target : door != null ? Kind.Door : Kind.CableBox;
    }

    private void Update()
    {
        if (Application.isPlaying)
        {
            UpdateColours();
            return;
        }
#if UNITY_EDITOR
        if (PrefabUtility.IsPartOfPrefabAsset(this)) return;
        SyncInEditor();
#endif
    }

    // ---------------------------------------------------------------- linking (editor)

    /// <summary>The nodes this one wants a cable to: the extra links plus (for cable boxes) the nearest nodes.</summary>
    private List<CableNode> WantedLinks()
    {
        var wanted = new List<CableNode>();
        if (extraLinks != null)
            foreach (CableNode n in extraLinks)
                if (n != null && n != this && !wanted.Contains(n)) wanted.Add(n);

        if (autoConnect)
        {
            var candidates = new List<CableNode>();
            foreach (CableNode n in All)
            {
                if (n == null || n == this || n.gameObject.scene != gameObject.scene || wanted.Contains(n)) continue;
                if ((n.AnchorPosition - AnchorPosition).sqrMagnitude > maxLinkDistance * maxLinkDistance) continue;
                candidates.Add(n);
            }
            Vector3 p = AnchorPosition;
            candidates.Sort((a, b) => (a.AnchorPosition - p).sqrMagnitude.CompareTo((b.AnchorPosition - p).sqrMagnitude));
            for (int i = 0; i < candidates.Count && i < autoLinkCount; i++) wanted.Add(candidates[i]);
        }
        return wanted;
    }

    /// <summary>Links this node builds the cable for (a link both ends want is built by only one of them).</summary>
    private List<CableNode> OwnedLinks()
    {
        List<CableNode> owned = WantedLinks();
        for (int i = owned.Count - 1; i >= 0; i--)
        {
            CableNode other = owned[i];
            if (other.WantedLinks().Contains(this) && !IsBefore(this, other)) owned.RemoveAt(i);
        }
        return owned;
    }

    private static bool IsBefore(CableNode a, CableNode b)
    {
        Vector3 p = a.AnchorPosition, q = b.AnchorPosition;
        if (p.x != q.x) return p.x < q.x;
        if (p.y != q.y) return p.y < q.y;
        return p.z < q.z;
    }

#if UNITY_EDITOR
    private void SyncInEditor()
    {
        List<CableNode> owned = OwnedLinks();
        int signature = Signature(owned);
        if (signature == builtSignature && builtCables != null && builtCables.Length == owned.Count && AllCablesExist()) return;

        BuildCables(owned);
        builtSignature = signature;
        graphDirty = true;
        EditorUtility.SetDirty(this);
        if (gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    private bool AllCablesExist()
    {
        foreach (Renderer r in builtCables) if (r == null) return false;
        return true;
    }

    private int Signature(List<CableNode> owned)
    {
        int h = Mix(17, AnchorPosition);
        h = Mix(h, cableThickness);
        foreach (CableNode n in owned) h = Mix(h, n.AnchorPosition);
        return h == 0 ? 1 : h;
    }

    private static int Mix(int h, Vector3 v) => Mix(Mix(Mix(h, v.x), v.y), v.z);
    private static int Mix(int h, float f) => unchecked(h * 31 + Mathf.RoundToInt(f * 1000f));

    [ContextMenu("Rebuild Cables")]
    public void RebuildCables()
    {
        builtSignature = 0;
        SyncInEditor();
    }
#endif

    private void BuildCables(List<CableNode> owned)
    {
        Transform container = transform.Find("Cables");
        if (container != null)
        {
            if (Application.isPlaying) Destroy(container.gameObject);
            else DestroyImmediate(container.gameObject);
        }
        builtLinks = owned.ToArray();
        builtCables = new Renderer[owned.Count];
        shownActive = null;
        if (owned.Count == 0) return;

        container = new GameObject("Cables").transform;
        container.SetParent(transform, false);
        Vector3 s = container.lossyScale;
        Vector3 inv = new Vector3(1f / Mathf.Max(0.0001f, s.x), 1f / Mathf.Max(0.0001f, s.y), 1f / Mathf.Max(0.0001f, s.z));

        Vector3 from = AnchorPosition;
        for (int i = 0; i < owned.Count; i++)
        {
            Vector3 to = owned[i].AnchorPosition;
            Vector3 d = to - from;
            float length = d.magnitude;
            GameObject cable = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cable.name = "Cable to " + owned[i].name;
            DestroyImmediate(cable.GetComponent<Collider>());
            cable.transform.SetParent(container, true);
            cable.transform.SetPositionAndRotation((from + to) * 0.5f, length > 0.0001f ? Quaternion.FromToRotation(Vector3.up, d / length) : Quaternion.identity);
            cable.transform.localScale = Vector3.Scale(new Vector3(cableThickness, length * 0.5f, cableThickness), inv);
            Renderer r = cable.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (inactiveMaterial != null) r.sharedMaterial = inactiveMaterial;
            builtCables[i] = r;
        }
    }

    // ---------------------------------------------------------------- colours (play mode)

    private void UpdateColours()
    {
        if (builtLinks == null || builtCables == null) return;
        if (shownActive == null || shownActive.Length != builtCables.Length) shownActive = new bool[builtCables.Length];
        for (int i = 0; i < builtLinks.Length && i < builtCables.Length; i++)
        {
            if (builtLinks[i] == null || builtCables[i] == null) continue;
            bool active = EdgeActive(this, builtLinks[i]);
            if (active == shownActive[i] && Time.frameCount > 1) continue;
            shownActive[i] = active;
            Material m = active ? activeMaterial : inactiveMaterial;
            if (m != null) builtCables[i].sharedMaterial = m;
        }
    }

    private bool SourceActive => kind == Kind.Button ? button != null && button.IsPressed
        : kind == Kind.Target ? target == null || target.IsDestroyed : false;

    private static void RebuildGraph()
    {
        Graph.Clear();
        foreach (CableNode n in All) if (n != null && !Graph.ContainsKey(n)) Graph[n] = new List<CableNode>();
        foreach (CableNode n in All)
        {
            if (n == null || n.builtLinks == null) continue;
            foreach (CableNode m in n.builtLinks)
            {
                if (m == null || !Graph.ContainsKey(m)) continue;
                if (!Graph[n].Contains(m)) Graph[n].Add(m);
                if (!Graph[m].Contains(n)) Graph[m].Add(n);
            }
        }
        graphDirty = false;
    }

    private static readonly List<CableNode> SideA = new List<CableNode>();
    private static readonly List<CableNode> SideB = new List<CableNode>();
    private static readonly HashSet<CableNode> Visited = new HashSet<CableNode>();
    private static readonly Queue<CableNode> Open = new Queue<CableNode>();

    /// <summary>
    /// Green when every button/target "upstream" of the cable is done. Upstream = the side of the cable that
    /// doesn't lead to a door. If neither (or both) sides reach a door, all sources on both sides count.
    /// </summary>
    private static bool EdgeActive(CableNode a, CableNode b)
    {
        if (graphDirty) RebuildGraph();
        bool doorA = Collect(a, b, SideA);
        bool doorB = Collect(b, a, SideB);
        if (doorA && !doorB) return AllDone(SideB);
        if (doorB && !doorA) return AllDone(SideA);
        SideA.AddRange(SideB);
        return AllDone(SideA);
    }

    /// <summary>Collects the buttons/targets reachable from start without going back over the start-excluded cable. Returns true if a door was reached.</summary>
    private static bool Collect(CableNode start, CableNode excluded, List<CableNode> sources)
    {
        sources.Clear();
        Visited.Clear();
        Open.Clear();
        bool door = false;
        Visited.Add(start);
        Open.Enqueue(start);
        while (Open.Count > 0)
        {
            CableNode n = Open.Dequeue();
            if (n.kind == Kind.Door) { door = true; continue; } // doors end the search
            if (n.kind == Kind.Button || n.kind == Kind.Target) sources.Add(n);
            if (!Graph.TryGetValue(n, out List<CableNode> next)) continue;
            foreach (CableNode m in next)
            {
                if (n == start && m == excluded) continue;
                if (m != null && Visited.Add(m)) Open.Enqueue(m);
            }
        }
        return door;
    }

    private static bool AllDone(List<CableNode> sources)
    {
        if (sources.Count == 0) return false;
        foreach (CableNode s in sources) if (!s.SourceActive) return false;
        return true;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.1f, 1f, 0.3f);
        Gizmos.DrawWireSphere(AnchorPosition, 0.2f);
        if (autoConnect)
        {
            Handles.color = new Color(0.1f, 1f, 0.3f, 0.15f);
            Handles.DrawWireDisc(AnchorPosition, Vector3.up, maxLinkDistance);
        }
    }
#endif
}
