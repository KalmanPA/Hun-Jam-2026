using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Cable boxes and lockdown:
///  - Builds the CableBox prefab and puts the door button, door target and sliding door onto boxes
///    (each with a CableNode), plus an amber lockdown lamp on the door. Plain colours, no textures.
///  - Updates the Kalman test scene demo: room 1 becomes a lockdown room with a spawner, and every door
///    gets cables back to its buttons/targets.
/// Menu: Final Shift > Cables > ...
/// </summary>
[InitializeOnLoad]
public static class CableSetupTool
{
    private const string ScenePath = "Assets/Scenes/Kalman test scene.unity";
    private const string PrefabFolder = "Assets/Prefabs/Interactables";
    private const string MaterialFolder = "Assets/ProtMaterials/Interactables";
    private const string TriggerFile = "Temp/FinalShift_BuildCables";

    private static Material housing, stripe, red, green, amber;

    static CableSetupTool()
    {
        if (!File.Exists(TriggerFile)) return;
        File.Delete(TriggerFile);
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[FinalShift] Cable setup skipped: editor is in play mode.");
                return;
            }
            UpgradePrefabs();
            UpdateDemo();
        };
    }

    // ---------------------------------------------------------------- prefabs

    [MenuItem("Final Shift/Cables/Build Cable Box And Put Buttons, Targets, Doors On Boxes")]
    public static void UpgradePrefabs()
    {
        try
        {
            housing = Mat("Box_Housing", new Color(0.22f, 0.24f, 0.27f), false);
            stripe = Mat("Box_Stripe", new Color(0.95f, 0.72f, 0.08f), false);
            red = Mat("Glow_Red", new Color(1f, 0.12f, 0.08f), true);
            green = Mat("Glow_Green", new Color(0.15f, 1f, 0.25f), true);
            amber = Mat("Glow_Amber", new Color(1f, 0.55f, 0.05f), true);

            BuildCableBox();
            Upgrade("DoorButton", UpgradeButton);
            Upgrade("DoorTarget", UpgradeTarget);
            Upgrade("SlidingDoor", UpgradeDoor);
            AssetDatabase.SaveAssets();
            Debug.Log("[FinalShift] Cable box built; button, target and door prefabs are on boxes now.");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[FinalShift] Cable prefab setup failed: " + e);
        }
    }

    private static void BuildCableBox()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/CableBox.prefab") != null) return; // keep edits and scene links
        GameObject root = new GameObject("CableBox");
        Transform r = root.transform;
        Part("Body", r, new Vector3(0f, 0.25f, 0f), new Vector3(0.5f, 0.5f, 0.5f), housing, true);
        Part("Stripe", r, new Vector3(0f, 0.38f, 0f), new Vector3(0.52f, 0.07f, 0.52f), stripe, false);
        Part("Lid", r, new Vector3(0f, 0.51f, 0f), new Vector3(0.36f, 0.03f, 0.36f), housing, false);
        Transform anchor = Anchor(r, new Vector3(0f, 0.25f, 0f));
        AddNode(root, anchor, true);
        PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/CableBox.prefab");
        Object.DestroyImmediate(root);
    }

    /// <summary>Edits a prefab in place so the scene's links to it keep working.</summary>
    private static void Upgrade(string name, System.Action<GameObject> edit)
    {
        string path = $"{PrefabFolder}/{name}.prefab";
        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            edit(contents);
            PrefabUtility.SaveAsPrefabAsset(contents, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static void UpgradeButton(GameObject root)
    {
        Transform r = root.transform;
        if (r.Find("Box") == null)
        {
            Part("Box", r, new Vector3(0f, 0f, -0.2f), new Vector3(0.72f, 0.72f, 0.4f), housing, true);
            Part("BoxStripe", r, new Vector3(0f, -0.3f, -0.2f), new Vector3(0.74f, 0.07f, 0.42f), stripe, false);
        }
        if (root.GetComponent<CableNode>() == null)
            AddNode(root, Anchor(r, new Vector3(0f, 0f, -0.2f)), false);
    }

    private static void UpgradeTarget(GameObject root)
    {
        Transform r = root.transform;
        foreach (string old in new[] { "Post", "Base" })
        {
            Transform t = r.Find(old);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }
        if (r.Find("Box") == null)
        {
            Part("Box", r, new Vector3(0f, 0.35f, 0f), new Vector3(0.7f, 0.7f, 0.7f), housing, true);
            Part("BoxStripe", r, new Vector3(0f, 0.6f, 0f), new Vector3(0.72f, 0.07f, 0.72f), stripe, false);
            Part("Stub", r, new Vector3(0f, 0.85f, 0f), new Vector3(0.1f, 0.35f, 0.1f), housing, false);
        }
        Transform board = r.Find("Board");
        if (board != null) board.localPosition = new Vector3(0f, 1.48f, 0f);
        if (root.GetComponent<CableNode>() == null)
            AddNode(root, Anchor(r, new Vector3(0f, 0.35f, 0f)), false);
    }

    private static void UpgradeDoor(GameObject root)
    {
        Transform r = root.transform;
        const float top = 3.4f; // door height + lintel
        Transform box = r.Find("TopBox");
        if (box == null)
        {
            box = new GameObject("TopBox").transform;
            box.SetParent(r, false);
            box.localPosition = new Vector3(0f, top + 0.35f, 0f);
            Part("Body", box, Vector3.zero, new Vector3(1.4f, 0.7f, 0.8f), housing, true);
            Part("Stripe", box, new Vector3(0f, -0.25f, 0f), new Vector3(1.42f, 0.08f, 0.82f), stripe, false);
            AddNode(box.gameObject, null, false);
        }

        // Status lamps move onto the big box.
        Transform front = r.Find("LampFront"), back = r.Find("LampBack");
        if (front != null) { front.localPosition = new Vector3(0f, top + 0.42f, 0.42f); front.localScale = new Vector3(0.9f, 0.2f, 0.04f); }
        if (back != null) { back.localPosition = new Vector3(0f, top + 0.42f, -0.42f); back.localScale = new Vector3(0.9f, 0.2f, 0.04f); }

        SerializedObject so = new SerializedObject(root.GetComponent<SlidingDoor>());
        so.FindProperty("lampLockdownMaterial").objectReferenceValue = amber;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Transform Anchor(Transform parent, Vector3 pos)
    {
        Transform a = parent.Find("CableAnchor");
        if (a != null) return a;
        a = new GameObject("CableAnchor").transform;
        a.SetParent(parent, false);
        a.localPosition = pos;
        return a;
    }

    private static void AddNode(GameObject go, Transform anchor, bool autoConnect)
    {
        CableNode node = go.AddComponent<CableNode>();
        SerializedObject so = new SerializedObject(node);
        so.FindProperty("autoConnect").boolValue = autoConnect;
        so.FindProperty("anchor").objectReferenceValue = anchor;
        so.FindProperty("inactiveMaterial").objectReferenceValue = red;
        so.FindProperty("activeMaterial").objectReferenceValue = green;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------------------------------------------------------------- demo

    [MenuItem("Final Shift/Cables/Update Kalman Test Scene Demo")]
    public static void UpdateDemo()
    {
        try
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                openedHere = true;
            }
            bool wasDirty = scene.isDirty;

            Transform doors = FindInScene(scene, "Interactables Demo/Doors");
            if (doors == null) throw new System.Exception("No 'Interactables Demo/Doors' in the Kalman test scene.");

            // Clean up anything from an earlier run, and the old front trigger zone.
            foreach (string old in new[] { "DoorTriggerZone (front)", "RoomZone (room 1)", "DoorTarget (room 1)", "EnemySpawner (room 1)" })
            {
                Transform t = doors.Find(old);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }
            for (int i = doors.childCount - 1; i >= 0; i--)
                if (doors.GetChild(i).name.StartsWith("CableBox")) Object.DestroyImmediate(doors.GetChild(i).gameObject);

            GameObject zonePrefab = Load("DoorTriggerZone");
            GameObject targetPrefab = Load("DoorTarget");
            GameObject boxPrefab = Load("CableBox");
            GameObject spawnerPrefab = Load("EnemySpawner");

            // Room 1: open until you walk in, then it shuts, robots drop in, and it only opens again once
            // they're dead and the target in the room is shot.
            SlidingDoor door1 = doors.Find("SlidingDoor (trigger)").GetComponent<SlidingDoor>();
            DoorTriggerZone inside = doors.Find("DoorTriggerZone (inside)").GetComponent<DoorTriggerZone>();

            GameObject room = Place(zonePrefab, doors, new Vector3(-11f, 1.7f, -22.4f));
            room.name = "RoomZone (room 1)";
            room.transform.localScale = new Vector3(9.6f, 3.4f, 4.2f);
            Transform marking = room.transform.Find("FloorMarking");
            if (marking != null) marking.gameObject.SetActive(false);

            GameObject roomTarget = Place(targetPrefab, doors, new Vector3(-14f, 0f, -23.6f));
            roomTarget.name = "DoorTarget (room 1)";

            GameObject spawner = Place(spawnerPrefab, doors, new Vector3(-11f, 6.5f, -22.5f));
            spawner.name = "EnemySpawner (room 1)";
            SerializedObject sp = new SerializedObject(spawner.GetComponent<EnemySpawner>());
            SetList(sp.FindProperty("triggerZones"), inside);
            sp.FindProperty("enemies").GetArrayElementAtIndex(0).FindPropertyRelative("count").intValue = 2;
            sp.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject d1 = new SerializedObject(door1);
            d1.FindProperty("startOpen").boolValue = true;
            SetList(d1.FindProperty("closeTriggers"), inside);
            d1.FindProperty("openCondition").enumValueIndex = (int)SlidingDoor.OpenCondition.DestroyAllTargets;
            SetList(d1.FindProperty("targets"), roomTarget.GetComponent<DoorTarget>());
            d1.FindProperty("stayOpen").boolValue = true;
            SetList(d1.FindProperty("roomZones"), room.GetComponent<DoorTriggerZone>());
            d1.ApplyModifiedPropertiesWithoutUndo();

            // Cable boxes (origin = bottom of the box, cables plug into the middle).
            Box(boxPrefab, doors, "CableBox (room 1)", new Vector3(-12.6f, 2.75f, -20.5f));
            Box(boxPrefab, doors, "CableBox (wall button)", new Vector3(-3f, 2.75f, -19.5f));
            Box(boxPrefab, doors, "CableBox (door 2)", new Vector3(1.5f, 0f, -18.8f));
            Box(boxPrefab, doors, "CableBox (pillar button)", new Vector3(2.4f, 0f, -15.3f));
            GameObject hub = Box(boxPrefab, doors, "CableBox (targets hub)", new Vector3(9.5f, 0f, -15.5f));
            SerializedObject hubSo = new SerializedObject(hub.GetComponent<CableNode>());
            hubSo.FindProperty("autoLinkCount").intValue = 4; // three targets + the box by the door
            hubSo.ApplyModifiedPropertiesWithoutUndo();
            GameObject door3Box = Box(boxPrefab, doors, "CableBox (door 3)", new Vector3(10.9f, 0f, -19.4f));
            SerializedObject d3 = new SerializedObject(door3Box.GetComponent<CableNode>());
            d3.FindProperty("autoConnect").boolValue = false; // a target is closer than the door here, so link by hand
            SetList(d3.FindProperty("extraLinks"),
                doors.Find("SlidingDoor (targets)").GetComponentInChildren<CableNode>(),
                hub.GetComponent<CableNode>());
            d3.ApplyModifiedPropertiesWithoutUndo();

            // Build every cable now so they're saved with the scene.
            foreach (GameObject go in scene.GetRootGameObjects())
                foreach (CableNode n in go.GetComponentsInChildren<CableNode>(true))
                    n.RebuildCables();

            if (SceneManager.GetActiveScene() == scene) RobotSetupTool.BakeNavMesh();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!wasDirty || openedHere)
            {
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[FinalShift] Cable demo added to the Kalman test scene and saved.");
            }
            else
            {
                Debug.LogWarning("[FinalShift] Cable demo added. The scene already had unsaved changes, so it was left unsaved: save it yourself.");
            }
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[FinalShift] Cable demo failed: " + e);
        }
    }

    private static GameObject Box(GameObject prefab, Transform parent, string name, Vector3 pos)
    {
        GameObject go = Place(prefab, parent, pos);
        go.name = name;
        return go;
    }

    private static void SetList(SerializedProperty list, params Object[] items)
    {
        list.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }

    private static Transform FindInScene(Scene scene, string path)
    {
        string[] parts = path.Split('/');
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name != parts[0]) continue;
            Transform t = root.transform;
            for (int i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
            if (t != null) return t;
        }
        return null;
    }

    private static GameObject Load(string name)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{name}.prefab");
        if (prefab == null) throw new System.Exception($"Missing prefab {PrefabFolder}/{name}.prefab.");
        return prefab;
    }

    private static GameObject Place(GameObject prefab, Transform parent, Vector3 position)
    {
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(position, Quaternion.identity);
        return go;
    }

    private static Transform Part(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool keepCollider)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        Transform t = go.transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        t.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return t;
    }

    /// <summary>Plain colour material, no texture.</summary>
    private static Material Mat(string name, Color color, bool unlit)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat; // keep existing ones (Glow_Red / Glow_Green are shared with the doors)
        Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        mat = new Material(shader) { name = name };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
