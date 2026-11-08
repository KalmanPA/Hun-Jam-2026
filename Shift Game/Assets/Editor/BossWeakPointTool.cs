using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the boss weak point prefab (Assets/Prefabs/Boss/BossWeakPoint.prefab): a floor-standing wire hatch with a
/// lid, shootable wires and a red/green beacon. Then, in BossFight.unity, swaps every placeholder cube named
/// "Weakpoint..." for the prefab (same spot and facing), removes the old hatches on the boss's back, runs a cable
/// from each weak point to the boss (red closed / green open) and links the weak points to the boss.
/// Menu: Final Shift > Boss > Place Weak Points. Trigger file: Temp/FinalShift_BossWeakPoints.
/// Running it again rebuilds the prefab and the cables and keeps the weak points where they are.
/// </summary>
[InitializeOnLoad]
public static class BossWeakPointTool
{
    private const string ScenePath = "Assets/Scenes/BossFight.unity";
    private const string PrefabFolder = "Assets/Prefabs/Boss";
    private const string PrefabPath = PrefabFolder + "/BossWeakPoint.prefab";
    private const string MatFolder = "Assets/ProtMaterials/Boss";
    private const string ExplosionMat = "Assets/ProtMaterials/RobotExplosion.mat";
    private const string CablesRootName = "== Weak Point Cables ==";
    private const string InstancePrefix = "Boss Weak Point";
    private const string TriggerFile = "Temp/FinalShift_BossWeakPoints";
    private const float ModelScale = 1.4f;
    private const float HousingHeight = 0.7f;

    static BossWeakPointTool() { EditorApplication.update += Poll; }

    private static void Poll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!File.Exists(TriggerFile)) return;
        File.Delete(TriggerFile);
        Run();
    }

    [MenuItem("Final Shift/Boss/Place Weak Points")]
    public static void Run()
    {
        try
        {
            GameObject prefab = BuildPrefab();
            PlaceInScene(prefab);
        }
        catch (System.Exception e) { Debug.LogError("[FinalShift] Boss weak points failed: " + e); }
    }

    // ------------------------------------------------------------------ prefab

    private static GameObject BuildPrefab()
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder("Assets/Prefabs", "Boss");
        Material body = Mat("Boss_Body"), dark = Mat("Boss_Dark"), lidMat = Mat("Boss_HatchLid"), inside = Mat("Boss_Inside");
        Material red = Mat("Boss_LampClosed"), green = Mat("Boss_LampOpen");
        Material[] wireColours = { Mat("Boss_WireRed"), Mat("Boss_WireYellow"), Mat("Boss_WireBlue"), Mat("Boss_WireBlack") };

        GameObject root = new GameObject("BossWeakPoint");
        // Model: built at the old hatch size, scaled up. Its top (the hatch opening) sits HousingHeight above the floor.
        Transform m = new GameObject("Model").transform;
        m.SetParent(root.transform, false);
        m.localPosition = new Vector3(0f, HousingHeight * ModelScale, 0f);
        m.localScale = Vector3.one * ModelScale;

        Box("Housing", m, new Vector3(0f, -HousingHeight * 0.5f, 0f), new Vector3(1.6f, HousingHeight, 1.6f), body);
        Box("Housing Foot", m, new Vector3(0f, -HousingHeight + 0.05f, 0f), new Vector3(1.75f, 0.1f, 1.75f), dark);
        Box("Wall Front", m, new Vector3(0f, 0.25f, -0.51f), new Vector3(1.1f, 0.5f, 0.08f), dark);
        Box("Wall Back", m, new Vector3(0f, 0.25f, 0.51f), new Vector3(1.1f, 0.5f, 0.08f), dark);
        Box("Wall Left", m, new Vector3(-0.51f, 0.25f, 0f), new Vector3(0.08f, 0.5f, 0.94f), dark);
        Box("Wall Right", m, new Vector3(0.51f, 0.25f, 0f), new Vector3(0.08f, 0.5f, 0.94f), dark);
        Box("Inside", m, new Vector3(0f, 0.02f, 0f), new Vector3(0.94f, 0.02f, 0.94f), inside, false);

        Transform hinge = new GameObject("Lid Hinge").transform;
        hinge.SetParent(m, false);
        hinge.localPosition = new Vector3(0f, 0.5f, -0.55f);
        Box("Lid", hinge, new Vector3(0f, 0.04f, 0.55f), new Vector3(1.16f, 0.08f, 1.16f), lidMat);
        Box("Lid Stripe", hinge, new Vector3(0f, 0.085f, 0.55f), new Vector3(0.2f, 0.01f, 1.0f), dark, false);

        Transform wires = new GameObject("Wires").transform;
        wires.SetParent(m, false);
        wires.localPosition = new Vector3(0f, -0.55f, 0f);
        Vector2[] offsets = { new Vector2(-0.22f, -0.15f), new Vector2(0.05f, -0.22f), new Vector2(0.24f, 0.05f), new Vector2(-0.1f, 0.18f), new Vector2(0.12f, 0.2f), new Vector2(-0.25f, 0.1f) };
        float[] tilt = { 5f, -6f, 3f, -4f, 7f, -2f };
        for (int i = 0; i < offsets.Length; i++)
        {
            Transform w = Prim(PrimitiveType.Cylinder, "Wire", wires, new Vector3(offsets[i].x, 0.45f, offsets[i].y),
                               new Vector3(0.09f, 0.45f, 0.09f), wireColours[i % wireColours.Length], false);
            w.localRotation = Quaternion.Euler(tilt[i], 0f, -tilt[(i + 2) % tilt.Length]);
        }
        Transform loop = Prim(PrimitiveType.Cylinder, "Wire Loop", wires, new Vector3(0f, 0.8f, 0f), new Vector3(0.07f, 0.28f, 0.07f), wireColours[0], false);
        loop.localRotation = Quaternion.Euler(0f, 0f, 90f);
        BoxCollider col = wires.gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.5f, 0f);
        col.size = new Vector3(0.75f, 1f, 0.75f);
        wires.gameObject.AddComponent<BossWires>();

        // Beacon on a stalk behind the hatch, tall enough to see from across the room.
        Prim(PrimitiveType.Cylinder, "Beacon Stalk", m, new Vector3(0f, 0.6f, 1.05f), new Vector3(0.08f, 0.9f, 0.08f), dark, false);
        Transform lamp = Box("Beacon", m, new Vector3(0f, 1.6f, 1.05f), new Vector3(0.36f, 0.3f, 0.36f), red, false);
        GameObject lightGo = new GameObject("Beacon Light");
        lightGo.transform.SetParent(m, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.6f, 0.75f);
        Light l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.lightmapBakeType = LightmapBakeType.Realtime;
        l.color = new Color(1f, 0.12f, 0.06f);
        l.intensity = 4f;
        l.range = 7f;
        l.shadows = LightShadows.None;

        // Cable socket on the side the cable leaves from (local +X).
        Box("Cable Socket", m, new Vector3(0.85f, -HousingHeight * 0.5f, 0f), new Vector3(0.18f, 0.3f, 0.3f), dark, false);

        BossWeakPoint wp = root.AddComponent<BossWeakPoint>();
        SerializedObject so = new SerializedObject(wp);
        so.FindProperty("lid").objectReferenceValue = hinge;
        so.FindProperty("wires").objectReferenceValue = wires;
        SetList(so.FindProperty("lamps"), lamp.GetComponent<Renderer>());
        so.FindProperty("lampLight").objectReferenceValue = l;
        so.FindProperty("lampClosedMaterial").objectReferenceValue = red;
        so.FindProperty("lampOpenMaterial").objectReferenceValue = green;
        so.FindProperty("sparkMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(ExplosionMat);
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject asset = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return asset;
    }

    // ------------------------------------------------------------------ scene

    private static void PlaceInScene(GameObject prefab)
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene s = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = false;
        if (s.IsValid() && s.isLoaded)
        {
            if (s.isDirty) throw new System.Exception("BossFight is open with unsaved changes. Save it, then run Final Shift > Boss > Place Weak Points.");
        }
        else
        {
            s = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            openedHere = true;
        }
        SceneManager.SetActiveScene(s);

        GameObject bossRoot = null;
        foreach (GameObject g in s.GetRootGameObjects()) if (g.GetComponent<BossMachine>() != null) bossRoot = g;
        if (bossRoot == null) throw new System.Exception("No BossMachine in BossFight.");
        BossMachine boss = bossRoot.GetComponent<BossMachine>();

        // Where the weak points go: the placeholder cubes (first run) or the weak points already placed (re-run).
        var spots = new List<(Vector3 pos, float yaw, Transform parent, string name)>();
        var remove = new List<GameObject>();
        foreach (GameObject g in s.GetRootGameObjects())
            foreach (Transform t in g.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Weakpoint") && t.GetComponent<BossWeakPoint>() == null)
                {
                    Renderer r = t.GetComponent<Renderer>();
                    Vector3 p = r != null ? new Vector3(r.bounds.center.x, r.bounds.min.y, r.bounds.center.z) : t.position;
                    spots.Add((p, t.eulerAngles.y, t.parent, t.name));
                    remove.Add(t.gameObject);
                }
                else if (t.name.StartsWith(InstancePrefix) && t.GetComponent<BossWeakPoint>() != null)
                {
                    spots.Add((t.position, t.eulerAngles.y, t.parent, t.name));
                    remove.Add(t.gameObject);
                }
            }
        // Old hatches on the boss's back are replaced by these.
        foreach (Transform c in bossRoot.transform) if (c.name.StartsWith("Wire Hatch")) remove.Add(c.gameObject);
        foreach (GameObject g in s.GetRootGameObjects()) if (g.name == CablesRootName) remove.Add(g);
        foreach (GameObject g in remove) if (g != null) Object.DestroyImmediate(g);

        if (spots.Count == 0) throw new System.Exception("No 'Weakpoint' cubes found in BossFight.");
        if (spots.Count > 3) Debug.LogWarning("[FinalShift] Found " + spots.Count + " weak point spots; using the first 3.");
        spots.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        if (spots.Count > 3) spots.RemoveRange(3, spots.Count - 3);

        Bounds torso = PartBounds(bossRoot.transform, "Torso", new Bounds(bossRoot.transform.position, Vector3.one * 8f));
        Material red = Mat("Boss_LampClosed");
        Transform cablesRoot = new GameObject(CablesRootName).transform;
        SceneManager.MoveGameObjectToScene(cablesRoot.gameObject, s);

        var placed = new List<BossWeakPoint>();
        for (int i = 0; i < spots.Count; i++)
        {
            var sp = spots[i];
            // Face the cable socket (+X) toward the boss, unless it was already placed (keep the facing then).
            Vector3 toBoss = torso.center - sp.pos; toBoss.y = 0f;
            float yaw = sp.name.StartsWith(InstancePrefix) ? sp.yaw
                : Quaternion.LookRotation(Vector3.Cross(Vector3.up, toBoss.normalized) * -1f).eulerAngles.y;

            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, s);
            go.name = InstancePrefix + " " + (i + 1);
            if (sp.parent != null) go.transform.SetParent(sp.parent, true);
            go.transform.SetPositionAndRotation(sp.pos, Quaternion.Euler(0f, yaw, 0f));
            BossWeakPoint wp = go.GetComponent<BossWeakPoint>();

            // Cable: from the socket, sagging across to the boss's torso.
            Vector3 a = go.transform.TransformPoint(new Vector3(0.97f * ModelScale, HousingHeight * 0.5f * ModelScale, 0f));
            Vector3 b = torso.ClosestPoint(a);
            b.y = Mathf.Clamp(Mathf.Max(a.y, 1.2f), torso.min.y + 0.3f, torso.max.y - 0.3f);
            Transform cable = new GameObject("Cable " + (i + 1)).transform;
            cable.SetParent(cablesRoot, false);
            var rends = new List<Renderer>();
            const int pieces = 10;
            float sag = Mathf.Clamp(Vector3.Distance(a, b) * 0.08f, 0.3f, 2.5f);
            float lowest = Mathf.Min(a.y, b.y) - 0.1f; // never sag below where it starts (keeps it off the floor)
            Vector3 prev = a;
            for (int k = 1; k <= pieces; k++)
            {
                float t = k / (float)pieces;
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y = Mathf.Max(p.y - Mathf.Sin(t * Mathf.PI) * sag, Mathf.Min(lowest, p.y));
                rends.Add(Segment(cable, prev, p, red));
                prev = p;
            }
            Transform plug = Box("Plug", cable, Vector3.zero, new Vector3(0.4f, 0.4f, 0.4f), Mat("Boss_Dark"), false);
            plug.position = b;

            SerializedObject so = new SerializedObject(wp);
            SetList(so.FindProperty("cables"), rends.ToArray());
            so.FindProperty("boss").objectReferenceValue = boss;
            so.ApplyModifiedPropertiesWithoutUndo();
            placed.Add(wp);
        }

        SerializedObject bso = new SerializedObject(boss);
        SetList(bso.FindProperty("weakPoints"), placed.ToArray());
        bso.FindProperty("closesPerWeakPoint").intValue = 2;
        bso.ApplyModifiedPropertiesWithoutUndo();
        float health = bso.FindProperty("maxHealth").floatValue;

        EditorSceneManager.MarkSceneDirty(s);
        EditorSceneManager.SaveScene(s);
        if (previous.IsValid() && previous.isLoaded && previous != s) SceneManager.SetActiveScene(previous);
        if (openedHere) EditorSceneManager.CloseScene(s, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[FinalShift] Boss weak points placed: {placed.Count} (prefab {PrefabPath}). Boss health {health}, " +
                  $"{health / Mathf.Max(1, placed.Count * 2):0} damage closes an open weak point, each must be shut twice.");
    }

    // ------------------------------------------------------------------ helpers

    private static Renderer Segment(Transform parent, Vector3 a, Vector3 b, Material mat)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        Transform t = Prim(PrimitiveType.Cylinder, "Cable", parent, Vector3.zero, new Vector3(0.13f, len * 0.5f + 0.05f, 0.13f), mat, false);
        t.position = (a + b) * 0.5f;
        t.rotation = Quaternion.FromToRotation(Vector3.up, len > 0.001f ? d / len : Vector3.up);
        MeshRenderer mr = t.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        return mr;
    }

    private static Bounds PartBounds(Transform boss, string name, Bounds fallback)
    {
        foreach (Transform c in boss)
            if (c.name == name && c.TryGetComponent(out Renderer r)) return r.bounds;
        return fallback;
    }

    private static Transform Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat, bool collider = true)
        => Prim(PrimitiveType.Cube, name, parent, localPos, size, mat, collider);

    private static Transform Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat, bool collider)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go.transform;
    }

    private static Material Mat(string name)
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/" + name + ".mat");
        if (m == null) Debug.LogWarning("[FinalShift] Missing material " + name);
        return m;
    }

    private static void SetList(SerializedProperty list, params Object[] items)
    {
        if (list == null) return;
        list.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }
}
