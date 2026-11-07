using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The belt table between two ConveyorOpenings. It is built automatically by the opening where items come out
/// (don't edit its children by hand, they are rebuilt when an opening moves).
/// At runtime it scrolls its texture, spawns the opening's item prefabs, carries loose items (and the player)
/// along, slides the big immovable items, and removes items that reach the other opening.
/// Local space: +Z = belt direction, origin = middle of the belt on the floor.
/// </summary>
[ExecuteAlways]
public class ConveyorBelt : MonoBehaviour
{
    private const float SpawnInset = 0.9f;     // spawn point, measured from the start of the belt
    private const float EndInset = 0.5f;       // items are removed this far before the end of the belt
    private const float Grip = 8f;             // how fast loose items are brought up to belt speed (m/s per second)
    private const float SideGrip = 2.5f;       // how fast sideways sliding is stopped (m/s per second)
    private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
    private static readonly Collider[] Overlaps = new Collider[64];

    [SerializeField] private ConveyorOpening source;
    [SerializeField] private ConveyorOpening target;
    [SerializeField] private float length;
    [SerializeField] private float width;
    [SerializeField] private float height;
    [SerializeField] private Renderer surface;

    private readonly HashSet<Rigidbody> carriedThisStep = new HashSet<Rigidbody>();
    private readonly List<ConveyorItem> heavyItems = new List<ConveyorItem>();
    private MaterialPropertyBlock block;
    private float scroll;
    private float spawnTimer = 0.5f;

    public float Speed => source != null ? source.BeltSpeed : 0f;

    /// <summary>World velocity of the belt surface (used to carry the player).</summary>
    public Vector3 SurfaceVelocity => transform.forward * Speed;

    // ---------------------------------------------------------------- building

    public void Build(ConveyorOpening from, ConveyorOpening to, Vector3 start, Vector3 end, float beltWidth, float beltHeight, Material surfaceMat, Material frameMat)
    {
        source = from;
        target = to;
        width = beltWidth;
        height = beltHeight;

        Vector3 flat = end - start;
        flat.y = 0f;
        length = flat.magnitude;
        Vector3 mid = (start + end) * 0.5f;
        mid.y = start.y;
        transform.SetPositionAndRotation(mid, length > 0.001f ? Quaternion.LookRotation(flat) : from.transform.rotation);
        transform.localScale = Vector3.one;

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying) Destroy(transform.GetChild(i).gameObject);
            else DestroyImmediate(transform.GetChild(i).gameObject);
        }

        // Moving surface: a quad facing up, texture V runs along the belt.
        surface = Part("Surface", PrimitiveType.Quad, new Vector3(0f, height + 0.005f, 0f), new Vector3(width, length, 1f), new Vector3(90f, 0f, 0f), surfaceMat).GetComponent<Renderer>();
        Part("Bed", PrimitiveType.Cube, new Vector3(0f, height - 0.07f, 0f), new Vector3(width + 0.1f, 0.14f, length), Vector3.zero, frameMat);
        // Side skirts down to the floor, with a low lip so small items can still be shoved off the side.
        float skirtX = width * 0.5f + 0.08f;
        Part("SkirtLeft", PrimitiveType.Cube, new Vector3(-skirtX, (height + 0.06f) * 0.5f, 0f), new Vector3(0.16f, height + 0.06f, length), Vector3.zero, frameMat);
        Part("SkirtRight", PrimitiveType.Cube, new Vector3(skirtX, (height + 0.06f) * 0.5f, 0f), new Vector3(0.16f, height + 0.06f, length), Vector3.zero, frameMat);
        // Rollers at both ends and a support every couple of metres.
        Part("RollerStart", PrimitiveType.Cylinder, new Vector3(0f, height - 0.1f, -length * 0.5f + 0.1f), new Vector3(0.22f, width * 0.5f + 0.02f, 0.22f), new Vector3(0f, 0f, 90f), frameMat);
        Part("RollerEnd", PrimitiveType.Cylinder, new Vector3(0f, height - 0.1f, length * 0.5f - 0.1f), new Vector3(0.22f, width * 0.5f + 0.02f, 0.22f), new Vector3(0f, 0f, 90f), frameMat);
        int ribs = Mathf.FloorToInt(length / 2f);
        for (int i = 1; i <= ribs; i++)
        {
            float z = -length * 0.5f + length * i / (ribs + 1);
            Part("Rib" + i, PrimitiveType.Cube, new Vector3(0f, (height + 0.1f) * 0.5f, z), new Vector3(width + 0.42f, height + 0.1f, 0.12f), Vector3.zero, frameMat);
        }

        // One collider for the whole table. Its top is the belt surface.
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null) box = gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, height * 0.5f, 0f);
        box.size = new Vector3(width + 0.32f, height, length);

        ApplyScroll();
    }

    private Transform Part(string name, PrimitiveType type, Vector3 pos, Vector3 scale, Vector3 euler, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        DestroyImmediate(go.GetComponent<Collider>());
        Transform t = go.transform;
        t.SetParent(transform, false);
        t.localPosition = pos;
        t.localRotation = Quaternion.Euler(euler);
        t.localScale = scale;
        if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        return t;
    }

    // ---------------------------------------------------------------- texture scroll

    private static PhysicsMaterial slipperyBelt;

    private void OnEnable()
    {
        ApplyScroll();

        // The belt's grip is done in code (see CarryLooseBodies), so the collider itself has no friction.
        // Otherwise PhysX friction holds resting crates in place.
        if (Application.isPlaying && TryGetComponent(out BoxCollider box))
        {
            if (slipperyBelt == null)
                slipperyBelt = new PhysicsMaterial("ConveyorBelt")
                {
                    dynamicFriction = 0f,
                    staticFriction = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                };
            box.sharedMaterial = slipperyBelt;
        }
    }

    private void Update()
    {
        if (Application.isPlaying && width > 0f)
            scroll = Mathf.Repeat(scroll - Speed / width * Time.deltaTime, 1f);
        ApplyScroll();
    }

    private void ApplyScroll()
    {
        if (surface == null || width <= 0f) return;
        block ??= new MaterialPropertyBlock();
        surface.GetPropertyBlock(block);
        // One texture tile per belt width, so the chevrons keep their shape.
        block.SetVector(BaseMapST, new Vector4(1f, length / width, 0f, scroll));
        surface.SetPropertyBlock(block);
    }

    // ---------------------------------------------------------------- runtime

    private void FixedUpdate()
    {
        if (!Application.isPlaying || source == null || length <= 0.01f) return;
        float dt = Time.fixedDeltaTime;

        spawnTimer -= dt;
        if (spawnTimer <= 0f)
            spawnTimer = TrySpawn() ? source.SpawnInterval + Random.Range(0f, source.SpawnIntervalRandom) : 0.3f;

        MoveHeavyItems(dt);
        CarryLooseBodies(dt);
    }

    private bool TrySpawn()
    {
        if (source.ItemFlow != ConveyorOpening.Flow.ItemsComeOut) return true;
        GameObject[] prefabs = source.ItemPrefabs;
        if (prefabs == null || prefabs.Length == 0) return true;
        GameObject prefab = prefabs[Random.Range(0, prefabs.Length)];
        if (prefab == null) return true;

        Vector3 spawnPoint = transform.TransformPoint(new Vector3(0f, height, -length * 0.5f + SpawnInset));

        // Wait until the spawn area is clear (also keeps big crates from overlapping).
        if (Physics.CheckBox(spawnPoint + Vector3.up * 1.1f, new Vector3(width * 0.45f, 1f, 0.95f), transform.rotation, ~0, QueryTriggerInteraction.Ignore))
            return false;

        ConveyorItem prefabItem = prefab.GetComponent<ConveyorItem>();
        bool heavy = prefabItem != null && prefabItem.Immovable;
        Quaternion rotation = heavy ? transform.rotation : transform.rotation * Quaternion.Euler(0f, Random.Range(-25f, 25f), 0f);
        GameObject go = Instantiate(prefab, spawnPoint, rotation);

        // Sit the item on the belt, centred on the spawn point.
        if (TryGetBounds(go, out Bounds b))
        {
            Vector3 offset = spawnPoint - b.center;
            offset.y = spawnPoint.y - b.min.y + 0.02f;
            go.transform.position += offset;
        }

        ConveyorItem item = go.GetComponent<ConveyorItem>();
        if (item == null) item = go.AddComponent<ConveyorItem>();
        item.OnSpawnedByBelt();
        if (item.Immovable) heavyItems.Add(item);
        return true;
    }

    private void MoveHeavyItems(float dt)
    {
        Vector3 step = transform.forward * Speed * dt;
        for (int i = heavyItems.Count - 1; i >= 0; i--)
        {
            ConveyorItem item = heavyItems[i];
            if (item == null) { heavyItems.RemoveAt(i); continue; }

            Rigidbody body = item.Body;
            if (transform.InverseTransformPoint(body.position).z > length * 0.5f - EndInset)
            {
                heavyItems.RemoveAt(i);
                Destroy(item.gameObject);
                continue;
            }

            item.MarkCarried();
            if (BlockedByPlayer(item, step)) continue; // the belt jams instead of pushing through the player
            body.MovePosition(body.position + step);
        }
    }

    private static bool BlockedByPlayer(ConveyorItem item, Vector3 step)
    {
        if (!TryGetBounds(item.gameObject, out Bounds b)) return false;
        Vector3 ahead = step.normalized * 0.08f;
        int count = Physics.OverlapBoxNonAlloc(b.center + ahead, b.extents * 0.98f, Overlaps, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (Overlaps[i] is CharacterController) return true;
        return false;
    }

    private void CarryLooseBodies(float dt)
    {
        float top = transform.position.y + height;
        Vector3 center = transform.TransformPoint(new Vector3(0f, height + 1.2f, 0f));
        int count = Physics.OverlapBoxNonAlloc(center, new Vector3(width * 0.5f, 1.2f, length * 0.5f), Overlaps, transform.rotation, ~0, QueryTriggerInteraction.Ignore);

        carriedThisStep.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider col = Overlaps[i];
            Rigidbody body = col.attachedRigidbody;
            if (body == null || body.isKinematic || !carriedThisStep.Add(body)) continue;
            if (col.bounds.min.y > top + 0.15f) continue; // in the air or stacked on something else

            ConveyorItem item = body.GetComponent<ConveyorItem>();
            if (item != null && transform.InverseTransformPoint(body.worldCenterOfMass).z > length * 0.5f - EndInset)
            {
                Destroy(body.gameObject);
                continue;
            }

            // Pull the velocity towards the belt velocity (like friction on the belt). Sideways grip is weaker,
            // so a shove from the player or a bullet slides small items off the side.
            Vector3 v = body.linearVelocity;
            Vector3 forward = transform.forward;
            Vector3 right = transform.right;
            float along = Vector3.Dot(v, forward);
            float side = Vector3.Dot(v, right);
            float newAlong = Mathf.MoveTowards(along, Speed, Grip * dt);
            float newSide = Mathf.MoveTowards(side, 0f, SideGrip * dt);
            body.AddForce(forward * (newAlong - along) + right * (newSide - side), ForceMode.VelocityChange);
            if (item != null) item.MarkCarried();
        }
    }

    private static bool TryGetBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.1f, 1f, 0.3f, 0.5f);
        Gizmos.DrawWireCube(new Vector3(0f, height + 1.2f, 0f), new Vector3(width, 2.4f, length));
    }
#endif
}
