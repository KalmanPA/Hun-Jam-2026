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
/// "Weakpoint..." for the prefab (same spot and facing), removes the old hatches on the boss's back, puts a round
/// target on the boss's torso for each weak point, runs a cable from each weak point into its target (dark while
/// closed, glowing red while open) and links the weak points to the boss.
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
    private const string TargetFaceMat = "Assets/ProtMaterials/Interactables/Target_Face.mat";
    private const string ExplosionMat ="Assets/ProtMaterials/RobotExplosion.mat";
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

    private const string RewireTrigger = "Temp/FinalShift_BossRewire";

    [InitializeOnLoadMethod]
    private static void HookRewire()
    {
        EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!File.Exists(RewireTrigger)) return;
            File.Delete(RewireTrigger);
            Rewire();
        };
    }

    /// <summary>
    /// Only redoes the cables: keeps the weak points in the scene exactly as they are (position, materials, any
    /// overrides), updates the lamp/cable settings on the prefab in place, and rebuilds the cables plus their
    /// targets on the boss. Trigger file: Temp/FinalShift_BossRewire.
    /// </summary>
    [MenuItem("Final Shift/Boss/Rewire Weak Points (keep weak points)")]
    public static void Rewire()
    {
        try
        {
            // Prefab: edited in place (other parts keep their ids), so scene overrides stay linked.
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                BossWeakPoint pwp = contents.GetComponent<BossWeakPoint>();
                SerializedObject pso = new SerializedObject(pwp);
                ApplyLampSettings(pso, Mat("Boss_Dark"), Mat("Boss_LampClosed"));
                Transform wires = pso.FindProperty("wires").objectReferenceValue as Transform;
                if (wires != null)
                {
                    pso.FindProperty("faceTarget").objectReferenceValue = BuildTargetDisc(wires);
                    pso.FindProperty("wireRise").floatValue = 0.9f;
                }
                pso.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            Scene s = OpenBossScene(out bool openedHere, out Scene previous);
            BossMachine boss = null;
            foreach (GameObject g in s.GetRootGameObjects()) if (g.GetComponent<BossMachine>() != null) boss = g.GetComponent<BossMachine>();
            if (boss == null) throw new System.Exception("No BossMachine in BossFight.");
            var wps = new List<BossWeakPoint>();
            foreach (GameObject g in s.GetRootGameObjects()) wps.AddRange(g.GetComponentsInChildren<BossWeakPoint>(true));
            wps.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (GameObject g in s.GetRootGameObjects()) if (g.name == CablesRootName) Object.DestroyImmediate(g);

            BuildAllCables(s, boss, wps);

            EditorSceneManager.MarkSceneDirty(s);
            EditorSceneManager.SaveScene(s);
            if (previous.IsValid() && previous.isLoaded && previous != s) SceneManager.SetActiveScene(previous);
            if (openedHere) EditorSceneManager.CloseScene(s, true);
            AssetDatabase.SaveAssets();
            Debug.Log($"[FinalShift] Boss weak points rewired: {wps.Count} cables into targets on the boss (weak points left as they were).");
        }
        catch (System.Exception e) { Debug.LogError("[FinalShift] Boss rewire failed: " + e); }
    }

    /// <summary>
    /// The thing to shoot inside the hatch: the round target board of the door target prefab (same cylinder +
    /// Target_Face material) standing on a stub, instead of the old little wires. Rises out when the hatch opens.
    /// Returns the pivot that turns to face the player.
    /// </summary>
    private static Transform BuildTargetDisc(Transform wires)
    {
        for (int i = wires.childCount - 1; i >= 0; i--) Object.DestroyImmediate(wires.GetChild(i).gameObject);
        Material face = AssetDatabase.LoadAssetAtPath<Material>(TargetFaceMat);
        if (face == null) Debug.LogWarning("[FinalShift] Missing " + TargetFaceMat);
        Material dark = Mat("Boss_Dark");

        Prim(PrimitiveType.Cylinder, "Stub", wires, new Vector3(0f, 0.1f, 0f), new Vector3(0.1f, 0.1f, 0.1f), dark, false);
        Transform pivot = new GameObject("Target Pivot").transform;
        pivot.SetParent(wires, false);
        pivot.localPosition = new Vector3(0f, 0.55f, 0f); // closed: fully under the lid; open (+0.9): above the rim
        // Same parts as DoorTarget's Board: a flat cylinder with the target face, here standing up (faces +/-Z).
        Transform board = Prim(PrimitiveType.Cylinder, "Board", pivot, Vector3.zero, new Vector3(0.85f, 0.03f, 0.85f), face, false);
        board.localRotation = Quaternion.Euler(90f, 0f, 0f);

        BoxCollider col = wires.GetComponent<BoxCollider>();
        if (col == null) col = wires.gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.55f, 0f);
        col.size = new Vector3(0.9f, 0.95f, 0.9f);
        if (wires.GetComponent<BossWires>() == null) wires.gameObject.AddComponent<BossWires>();
        return pivot;
    }

    private static Scene OpenBossScene(out bool openedHere, out Scene previous)
    {
        previous = SceneManager.GetActiveScene();
        Scene s = SceneManager.GetSceneByPath(ScenePath);
        openedHere = false;
        if (s.IsValid() && s.isLoaded)
        {
            if (s.isDirty) throw new System.Exception("BossFight is open with unsaved changes. Save it first, then run the boss tool again.");
        }
        else
        {
            s = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            openedHere = true;
        }
        SceneManager.SetActiveScene(s);
        return s;
    }

    private static void ApplyLampSettings(SerializedObject so, Material dark, Material red)
    {
        // Red means "shoot here": the beacon is dark while closed and glows red while open, like the cable.
        so.FindProperty("lampClosedMaterial").objectReferenceValue = dark;
        so.FindProperty("lampOpenMaterial").objectReferenceValue = red;
        so.FindProperty("lampOpenColor").colorValue = new Color(1f, 0.12f, 0.06f);
        so.FindProperty("lampLightOffWhenClosed").boolValue = true;
        so.FindProperty("cableOffMaterial").objectReferenceValue = dark;
        so.FindProperty("cableGlowMaterial").objectReferenceValue = red;
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
        Material red = Mat("Boss_LampClosed");
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
        so.FindProperty("faceTarget").objectReferenceValue = BuildTargetDisc(wires);
        so.FindProperty("wireRise").floatValue = 0.9f;
        SetList(so.FindProperty("lamps"), lamp.GetComponent<Renderer>());
        so.FindProperty("lampLight").objectReferenceValue = l;
        ApplyLampSettings(so, dark, red);
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

        var placed = new List<BossWeakPoint>();
        for (int i = 0; i < spots.Count; i++)
        {
            var sp = spots[i];
            // Face the cable socket (+X) toward the boss, unless it was already placed (keep the facing then).
            Vector3 toBoss = bossRoot.transform.position - sp.pos; toBoss.y = 0f;
            float yaw = sp.name.StartsWith(InstancePrefix) ? sp.yaw
                : Quaternion.LookRotation(Vector3.Cross(Vector3.up, toBoss.normalized) * -1f).eulerAngles.y;

            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, s);
            go.name = InstancePrefix + " " + (i + 1);
            if (sp.parent != null) go.transform.SetParent(sp.parent, true);
            go.transform.SetPositionAndRotation(sp.pos, Quaternion.Euler(0f, yaw, 0f));
            placed.Add(go.GetComponent<BossWeakPoint>());
        }
        BuildAllCables(s, boss, placed);

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

    /// <summary>
    /// Builds "== Weak Point Cables ==": for each weak point a round target on the boss's torso and a cable from the
    /// weak point's socket into it, and links cables, target light and boss on the weak point.
    /// </summary>
    private static void BuildAllCables(Scene s, BossMachine boss, List<BossWeakPoint> wps)
    {
        Transform torsoT = null;
        foreach (Transform c in boss.transform) if (c.name == "Torso") torsoT = c;
        if (torsoT == null) throw new System.Exception("The boss has no 'Torso' part to plug the cables into.");
        float floorY = FloorTop(s);
        Material dark = Mat("Boss_Dark");
        Material yellow = AssetDatabase.LoadAssetAtPath<Material>(TargetFaceMat); // door target's round board
        if (yellow == null) { Debug.LogWarning("[FinalShift] Missing " + TargetFaceMat); yellow = Mat("Boss_Yellow"); }
        Transform cablesRoot = new GameObject(CablesRootName).transform;
        SceneManager.MoveGameObjectToScene(cablesRoot.gameObject, s);
        var usedTargets = new List<Vector3>();

        for (int i = 0; i < wps.Count; i++)
        {
            BossWeakPoint wp = wps[i];
            // Target on the boss: a round socket on the torso face that looks toward this weak point.
            Vector3 a = wp.transform.TransformPoint(new Vector3(0.97f * ModelScale, HousingHeight * 0.5f * ModelScale, 0f));
            Vector3 tp = TargetSpot(torsoT, a, floorY, usedTargets, out Vector3 normal);
            usedTargets.Add(tp);
            Transform cable = new GameObject("Cable " + (i + 1)).transform;
            cable.SetParent(cablesRoot, false);
            var rends = new List<Renderer>();
            rends.Add(BuildTarget(cable, i + 1, tp, normal, dark, yellow, out Light targetLight));

            // Cable: from the socket, sagging across, then straight into the middle of the target.
            Vector3 b = tp + normal * 1.0f;
            const int pieces = 10;
            float sag = Mathf.Clamp(Vector3.Distance(a, b) * 0.08f, 0.3f, 2.5f);
            float lowest = Mathf.Min(a.y, b.y) - 0.1f; // never sag below where it starts (keeps it off the floor)
            Vector3 prev = a;
            for (int k = 1; k <= pieces; k++)
            {
                float t = k / (float)pieces;
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y = Mathf.Max(p.y - Mathf.Sin(t * Mathf.PI) * sag, Mathf.Min(lowest, p.y));
                rends.Add(Segment(cable, prev, p, dark));
                prev = p;
            }
            rends.Add(Segment(cable, b, tp + normal * 0.1f, dark));

            SerializedObject so = new SerializedObject(wp);
            SetList(so.FindProperty("cables"), rends.ToArray());
            so.FindProperty("targetLight").objectReferenceValue = targetLight;
            so.FindProperty("boss").objectReferenceValue = boss;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
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

    private static float FloorTop(Scene s)
    {
        foreach (GameObject g in s.GetRootGameObjects())
            foreach (Transform t in g.GetComponentsInChildren<Transform>(true))
                if (t.name == "Floor" && t.TryGetComponent(out Renderer r)) return r.bounds.max.y;
        return 0f;
    }

    /// <summary>
    /// A spot on the torso box's face that looks toward <paramref name="from"/>, at about chest height above the
    /// floor, kept away from the edges and from targets already placed.
    /// </summary>
    private static Vector3 TargetSpot(Transform torso, Vector3 from, float floorY, List<Vector3> used, out Vector3 normal)
    {
        Vector3 size = torso.lossyScale;
        Vector3 l = torso.InverseTransformPoint(from); // the torso is a unit cube scaled up
        float outX = (Mathf.Abs(l.x) - 0.5f) * size.x, outZ = (Mathf.Abs(l.z) - 0.5f) * size.z;
        bool xFace = outX > outZ;
        Vector3 axis = xFace ? new Vector3(Mathf.Sign(l.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(l.z));
        int tangentAxis = xFace ? 2 : 0;
        float half = 0.5f - 1.3f / Mathf.Max(0.01f, size[tangentAxis]);

        // Height: ~2 m above the floor, but below the top edge of the torso.
        float top = torso.TransformPoint(new Vector3(0f, 0.5f, 0f)).y;
        float worldY = Mathf.Min(floorY + 2f, top - 0.9f);
        float ly = Mathf.Clamp((worldY - torso.position.y) / Mathf.Max(0.01f, size.y), -0.4f, 0.4f);

        Vector3 local = axis * 0.5f;
        local.y = ly;
        local[tangentAxis] = Mathf.Clamp(l[tangentAxis], -half, half);
        Vector3 world = torso.TransformPoint(local);
        // Keep targets apart: slide toward the middle of the face while too close to another one.
        for (int n = 0; n < 4; n++)
        {
            bool clash = false;
            foreach (Vector3 u in used) if (Vector3.Distance(u, world) < 2.2f) clash = true;
            if (!clash) break;
            local[tangentAxis] = Mathf.Clamp(local[tangentAxis] - Mathf.Sign(local[tangentAxis] + 0.0001f) * 2.5f / size[tangentAxis], -half, half);
            world = torso.TransformPoint(local);
        }
        normal = torso.TransformDirection(axis).normalized;
        return world;
    }

    /// <summary>Where the cable plugs into the boss: a dark socket whose ring glows red while the weak point is open.</summary>
    private static Renderer BuildTarget(Transform parent, int index, Vector3 pos, Vector3 normal, Material dark, Material unused, out Light light)
    {
        Transform t = new GameObject("Boss Socket " + index).transform;
        t.SetParent(parent, false);
        t.SetPositionAndRotation(pos, Quaternion.FromToRotation(Vector3.up, normal));
        Prim(PrimitiveType.Cylinder, "Socket Base", t, new Vector3(0f, 0.08f, 0f), new Vector3(0.9f, 0.08f, 0.9f), dark, false);
        Transform glow = Prim(PrimitiveType.Cylinder, "Glow Ring", t, new Vector3(0f, 0.17f, 0f), new Vector3(0.6f, 0.02f, 0.6f), dark, false);
        Prim(PrimitiveType.Cylinder, "Socket", t, new Vector3(0f, 0.2f, 0f), new Vector3(0.32f, 0.04f, 0.32f), dark, false);
        foreach (Renderer r in t.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;

        GameObject lgo = new GameObject("Target Light");
        lgo.transform.SetParent(t, false);
        lgo.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        light = lgo.AddComponent<Light>();
        light.type = LightType.Point;
        light.lightmapBakeType = LightmapBakeType.Realtime;
        light.color = new Color(1f, 0.12f, 0.06f);
        light.intensity = 3f;
        light.range = 5f;
        light.shadows = LightShadows.None;
        light.enabled = false;
        return glow.GetComponent<Renderer>();
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
