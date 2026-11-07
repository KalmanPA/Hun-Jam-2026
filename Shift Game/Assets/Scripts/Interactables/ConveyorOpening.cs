using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// A tall hatch in the wall that a conveyor belt runs out of or into.
/// Place two openings with the same Belt Id and a belt table is built between them in the editor.
/// Items (the prefabs listed on the "items come out" opening) ride the belt and vanish into the other opening.
/// Origin = middle of the front edge on the floor, +Z = out of the opening (it turns to face its partner).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class ConveyorOpening : MonoBehaviour
{
    public enum Flow { ItemsComeOut, ItemsGoIn }

    // Size of the hole (the prefab's frame is built to match). Big items are up to ~1.9 m tall on a 0.8 m belt.
    public const float InnerWidth = 2.4f;
    public const float InnerHeight = 3.3f;
    public const float TunnelDepth = 1.6f;
    // How far the belt reaches into each tunnel.
    public const float BeltReach = TunnelDepth - 0.2f;

    [Tooltip("Two openings with the same id get a conveyor belt built between them.")]
    [SerializeField] private int beltId = 1;
    [Tooltip("Set one opening to Items Come Out and the other to Items Go In. The belt runs from the out opening to the in opening.")]
    [SerializeField] private Flow flow = Flow.ItemsComeOut;
    [Tooltip("Turn the opening to face its partner automatically.")]
    [SerializeField] private bool faceOtherOpening = true;

    [Header("Items (used on the opening where items come out)")]
    [Tooltip("Picked at random. Small ones can be pushed by the player and bullets; big ones (ConveyorItem > Immovable) can't.")]
    [SerializeField] private GameObject[] itemPrefabs;
    [SerializeField] private float spawnInterval = 2.5f;
    [Tooltip("Extra random seconds added to each spawn interval.")]
    [SerializeField] private float spawnIntervalRandom = 1f;
    [SerializeField] private float beltSpeed = 1.5f;

    [Header("Belt look")]
    [SerializeField] private float beltWidth = 1.8f;
    [SerializeField] private float beltHeight = 0.8f;
    [SerializeField] private Material beltSurfaceMaterial;
    [SerializeField] private Material beltFrameMaterial;

    [SerializeField, HideInInspector] private ConveyorBelt belt;
    [SerializeField, HideInInspector] private int builtSignature;

    private static readonly List<ConveyorOpening> All = new List<ConveyorOpening>();

    public int BeltId => beltId;
    public Flow ItemFlow => flow;
    public GameObject[] ItemPrefabs => itemPrefabs;
    public float SpawnInterval => spawnInterval;
    public float SpawnIntervalRandom => spawnIntervalRandom;
    public float BeltSpeed => beltSpeed;
    public ConveyorBelt Belt => belt;

    /// <summary>Where the belt ends inside this opening's tunnel.</summary>
    public Vector3 BeltEndPoint => transform.position - transform.forward * BeltReach;

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    private void Start()
    {
        // Openings spawned at runtime build their belt on start.
        if (!Application.isPlaying || belt != null) return;
        ConveyorOpening partner = FindPartner(out _);
        if (partner != null && IsOwnerWith(partner)) BuildBelt(partner);
    }

    // ---------------------------------------------------------------- pairing

    /// <summary>The other opening with the same id, or null. <paramref name="count"/> = how many others share the id.</summary>
    public ConveyorOpening FindPartner(out int count)
    {
        ConveyorOpening found = null;
        count = 0;
        foreach (ConveyorOpening o in All)
        {
            if (o == this || o == null || o.beltId != beltId || o.gameObject.scene != gameObject.scene) continue;
            count++;
            if (found == null) found = o;
        }
        return count == 1 ? found : null;
    }

    /// <summary>The out opening owns (builds and runs) the belt. If both have the same flow, one is picked.</summary>
    public bool IsOwnerWith(ConveyorOpening partner)
    {
        if (flow != partner.flow) return flow == Flow.ItemsComeOut;
        Vector3 a = transform.position, b = partner.transform.position;
        return a.x != b.x ? a.x < b.x : a.z < b.z;
    }

    // ---------------------------------------------------------------- editor sync

    private void Update()
    {
        if (Application.isPlaying) return;
#if UNITY_EDITOR
        if (PrefabUtility.IsPartOfPrefabAsset(this)) return;
        SyncInEditor();
#endif
    }

#if UNITY_EDITOR
    private void SyncInEditor()
    {
        ConveyorOpening partner = FindPartner(out _);
        if (partner != null && faceOtherOpening) FaceTowards(partner.transform.position);

        if (partner == null || !IsOwnerWith(partner))
        {
            RemoveBelt();
            return;
        }

        int signature = Signature(partner);
        if (belt != null && signature == builtSignature) return;

        BuildBelt(partner);
        builtSignature = signature;
        EditorUtility.SetDirty(this);
        if (gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    private void FaceTowards(Vector3 target)
    {
        Vector3 d = target - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude < 0.01f) return;
        Quaternion want = Quaternion.LookRotation(d);
        if (Quaternion.Angle(transform.rotation, want) < 0.01f) return;
        transform.rotation = want;
        EditorUtility.SetDirty(transform);
    }

    private void RemoveBelt()
    {
        if (belt == null)
        {
            // Clean up a belt whose reference was lost.
            ConveyorBelt stray = GetComponentInChildren<ConveyorBelt>(true);
            if (stray == null) return;
            belt = stray;
        }
        if (PrefabUtility.IsPartOfPrefabInstance(belt.gameObject) && !PrefabUtility.IsAddedGameObjectOverride(belt.gameObject))
        {
            belt.gameObject.SetActive(false);
        }
        else
        {
            DestroyImmediate(belt.gameObject);
        }
        belt = null;
        builtSignature = 0;
        EditorUtility.SetDirty(this);
        if (gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    private int Signature(ConveyorOpening partner)
    {
        int h = 17;
        h = Mix(h, transform.position);
        h = Mix(h, transform.forward);
        h = Mix(h, partner.transform.position);
        h = Mix(h, partner.transform.forward);
        h = Mix(h, beltWidth);
        h = Mix(h, beltHeight);
        h = Mix(h, beltId);
        h = Mix(h, StableHash(beltSurfaceMaterial));
        h = Mix(h, StableHash(beltFrameMaterial));
        return h == 0 ? 1 : h;
    }

    private static int Mix(int h, Vector3 v) => Mix(Mix(Mix(h, v.x), v.y), v.z);
    private static int Mix(int h, float f) => unchecked(h * 31 + Mathf.RoundToInt(f * 1000f));

    private static int StableHash(Object o)
    {
        if (o == null) return 0;
        int h = 7;
        foreach (char c in o.name) h = unchecked(h * 31 + c);
        return h;
    }

    /// <summary>Editor only: face the partner and rebuild the belt right now.</summary>
    [ContextMenu("Rebuild Belt")]
    public void RebuildBelt()
    {
        builtSignature = 0;
        SyncInEditor();
    }
#endif

    private void BuildBelt(ConveyorOpening partner)
    {
        if (belt == null)
        {
            belt = GetComponentInChildren<ConveyorBelt>(true);
            if (belt == null)
            {
                GameObject go = new GameObject("ConveyorBelt");
                go.transform.SetParent(transform, false);
                belt = go.AddComponent<ConveyorBelt>();
            }
        }
        belt.gameObject.SetActive(true);
        belt.Build(this, partner, BeltEndPoint, partner.BeltEndPoint, beltWidth, beltHeight, beltSurfaceMaterial, beltFrameMaterial);
    }

    // ---------------------------------------------------------------- gizmos

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        ConveyorOpening partner = FindPartner(out int count);
        Vector3 top = transform.position + Vector3.up * (InnerHeight + 0.6f);
        if (partner == null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(top, 0.3f);
            Handles.Label(top + Vector3.up * 0.4f, count == 0 ? $"Belt {beltId}: no partner" : $"Belt {beltId}: {count + 1} openings share this id");
            return;
        }

        bool wrong = partner.flow == flow;
        Handles.color = wrong ? Color.red : new Color(0.1f, 1f, 0.3f);
        Vector3 other = partner.transform.position + Vector3.up * (InnerHeight + 0.6f);
        Handles.DrawAAPolyLine(3f, top, other);
        if (flow == Flow.ItemsComeOut)
        {
            // Arrow head pointing at the in opening.
            Vector3 mid = Vector3.Lerp(top, other, 0.5f);
            Vector3 dir = (other - top).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, dir) * 0.4f;
            Handles.DrawAAPolyLine(3f, mid - dir * 0.6f + side, mid, mid - dir * 0.6f - side);
        }
        Handles.Label(top + Vector3.up * 0.4f, wrong ? $"Belt {beltId}: both openings are {flow}" : $"Belt {beltId}: {flow}");
    }
#endif
}
