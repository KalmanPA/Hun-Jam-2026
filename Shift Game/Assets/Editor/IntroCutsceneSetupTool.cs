using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the intro cutscene into the Game scene (see IntroCutscene for what it does):
///  - "== Intro Cutscene ==" at the scene root with the IntroCutscene component,
///  - the player start and a workbench past the first conveyor belt (it runs behind the player), facing the second,
///  - four RobotIntro robots (no gun, 1 HP, never move) between the second and third belt, facing the player,
///  - the third belt set as the blocking belt, carrying Crate_IntroWall crates while the robots live.
/// Also makes the RobotIntro and Crate_IntroWall prefab variants and adds the long robot voice lines to the
/// sound bank (RobotBerate).
///
/// Runs once by itself the first time Unity loads this script, and from
/// Final Shift > Intro Cutscene > Rebuild Intro In Game Scene (replaces the old intro objects).
/// Everything is placed from where the belts are, measured from the player placed nearest to them.
/// </summary>
[InitializeOnLoad]
public static class IntroCutsceneSetupTool
{
    private const string ScenePath = "Assets/Scenes/Game.unity";
    private const string RootName = "== Intro Cutscene ==";
    private const string RobotBasePath = "Assets/Prefabs/Robot.prefab";
    private const string RobotIntroPath = "Assets/Prefabs/RobotIntro.prefab";
    private const string CrateBasePath = "Assets/Prefabs/Interactables/Crate_Big.prefab";
    private const string CrateWallPath = "Assets/Prefabs/Interactables/Crate_IntroWall.prefab";
    private const string MaterialFolder = "Assets/ProtMaterials/Intro";
    private const string FlatShader = "Final Shift/Flat Triplanar Grime";
    private const string GrimePath = "Assets/ShaderTest/Textures/Grime64.png";

    // Bump this to make the tool run once more automatically on every machine.
    // 2: player and bench moved past the first belt; replaces the intro built by version 1.
    private const int AutoRunVersion = 2;
    private const string AutoRunPrefKey = "FinalShift.IntroCutscene.AutoRunVersion";

    // Pieces of the robot model that make up its arm gun (same list as EnemyVariantsTool).
    private static readonly string[] GunPieces = { "Cube.016", "ShootMuzzle", "Cylinder.011" };

    // Layout
    private const float StartBehindFirstBelt = 2.6f;    // player start, measured back from the centre line of the belt in front of the player
    private const float BenchDistance = 0.8f;           // bench centre in front of the player
    private const float BenchWidth = 1.5f;
    private const float BenchDepth = 0.6f;
    private const float BenchHeight = 0.92f;
    private static readonly float[] RobotOffsets = { -3.3f, -1.1f, 1.1f, 3.3f }; // sideways, from the player's line of sight
    private static readonly Vector3 CrateWallSize = new Vector3(1.6f, 2.3f, 1.5f);

    static IntroCutsceneSetupTool()
    {
        int ran = EditorPrefs.GetInt(AutoRunPrefKey, 0);
        if (ran >= AutoRunVersion) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorPrefs.SetInt(AutoRunPrefKey, AutoRunVersion);
            Build(ran > 0); // an older version of the intro gets replaced by the new layout
        };
    }

    [MenuItem("Final Shift/Intro Cutscene/Rebuild Intro In Game Scene")]
    public static void RebuildMenu()
    {
        if (EditorUtility.DisplayDialog("Rebuild intro cutscene",
                "This replaces \"" + RootName + "\" in the Game scene (player start, workbench, intro robots), losing changes made to it.",
                "Rebuild", "Cancel"))
            Build(true);
    }

    [MenuItem("Final Shift/Intro Cutscene/Select Intro Objects")]
    public static void SelectMenu()
    {
        IntroCutscene intro = Object.FindFirstObjectByType<IntroCutscene>(FindObjectsInactive.Include);
        if (intro != null) { Selection.activeObject = intro.gameObject; EditorGUIUtility.PingObject(intro.gameObject); }
        else Debug.LogWarning("[FinalShift] No intro cutscene in the open scene. Use Final Shift > Intro Cutscene > Rebuild Intro In Game Scene.");
    }

    private static void Build(bool replace)
    {
        try
        {
            if (AudioSetupTool.BankIsMissing(SoundId.RobotBerate)) AudioSetupTool.Build();

            GameObject robotPrefab = EnsureRobotIntro();
            GameObject cratePrefab = EnsureCrateWall();
            if (robotPrefab == null) return;

            Scene scene = OpenGameScene();
            if (!scene.IsValid()) return;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != RootName) continue;
                if (!replace)
                {
                    Debug.Log("[FinalShift] Intro cutscene is already in the Game scene, left as it is.");
                    return;
                }
                Undo.DestroyObjectImmediate(root);
            }

            if (!BuildInScene(scene, robotPrefab, cratePrefab)) return;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[FinalShift] Intro cutscene setup failed: " + e);
        }
    }

    // ---------------------------------------------------------------- prefabs

    private static GameObject EnsureRobotIntro()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(RobotIntroPath);
        if (existing != null) return existing;
        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotBasePath);
        if (basePrefab == null) { Debug.LogError("[FinalShift] Intro cutscene: " + RobotBasePath + " not found."); return null; }

        return SaveVariant(basePrefab, RobotIntroPath, robot =>
        {
            foreach (Transform t in robot.GetComponentsInChildren<Transform>(true))
                if (System.Array.IndexOf(GunPieces, t.name) >= 0)
                    t.gameObject.SetActive(false);

            SerializedObject so = new SerializedObject(robot.GetComponent<RobotEnemy>());
            so.FindProperty("maxHealth").floatValue = 1f;
            so.FindProperty("canShoot").boolValue = false;
            so.FindProperty("movementMode").enumValueIndex = (int)RobotEnemy.MovementMode.Stationary;
            so.FindProperty("combatLocked").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        });
    }

    private static GameObject EnsureCrateWall()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(CrateWallPath);
        if (existing != null) return existing;
        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CrateBasePath);
        if (basePrefab == null) { Debug.LogWarning("[FinalShift] Intro cutscene: " + CrateBasePath + " not found, the blocking belt won't change."); return null; }

        // Taller and wider than Crate_Big, so a row of them hides everything behind the belt.
        // Still fits through the conveyor opening (2.4 x 3.3 m hole, 0.8 m belt).
        return SaveVariant(basePrefab, CrateWallPath, crate => crate.transform.localScale = CrateWallSize);
    }

    private static GameObject SaveVariant(GameObject basePrefab, string path, System.Action<GameObject> setUp)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab, preview);
            instance.name = Path.GetFileNameWithoutExtension(path);
            setUp(instance);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, path);
            Debug.Log("[FinalShift] Saved " + path);
            return saved;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    // ---------------------------------------------------------------- scene

    private static Scene OpenGameScene()
    {
        Scene active = SceneManager.GetActiveScene();
        if (active.path == ScenePath) return active;
        if (!File.Exists(ScenePath))
        {
            Debug.LogWarning("[FinalShift] Intro cutscene: " + ScenePath + " not found.");
            return default;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsToSave()) return default;
        return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    private struct Belt
    {
        public ConveyorOpening source;  // the "items come out" end
        public Vector3 a, b;            // centre line, on the floor

        public Vector3 Closest(Vector3 p)
        {
            Vector3 ab = b - a;
            ab.y = 0f;
            Vector3 ap = p - a;
            ap.y = 0f;
            float t = ab.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector3.Dot(ap, ab) / ab.sqrMagnitude) : 0f;
            return a + (b - a) * t;
        }

        public float Distance(Vector3 p)
        {
            Vector3 d = Closest(p) - p;
            d.y = 0f;
            return d.magnitude;
        }
    }

    private static List<Belt> FindBelts(Scene scene)
    {
        var byId = new Dictionary<int, List<ConveyorOpening>>();
        foreach (ConveyorOpening o in Object.FindObjectsByType<ConveyorOpening>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (o.gameObject.scene != scene) continue;
            if (!byId.TryGetValue(o.BeltId, out var list)) byId[o.BeltId] = list = new List<ConveyorOpening>();
            list.Add(o);
        }

        var belts = new List<Belt>();
        foreach (var pair in byId.Values)
        {
            if (pair.Count != 2) continue;
            ConveyorOpening src = pair[0].ItemFlow == ConveyorOpening.Flow.ItemsComeOut ? pair[0] : pair[1];
            ConveyorOpening dst = src == pair[0] ? pair[1] : pair[0];
            belts.Add(new Belt { source = src, a = src.BeltEndPoint, b = dst.BeltEndPoint });
        }
        return belts;
    }

    private static bool BuildInScene(Scene scene, GameObject robotPrefab, GameObject cratePrefab)
    {
        List<Belt> belts = FindBelts(scene);
        if (belts.Count < 3)
        {
            Debug.LogError("[FinalShift] Intro cutscene: the Game scene needs at least three conveyor belts.");
            return false;
        }

        // Reference: the player object placed closest to a belt (the level start), or the first belt's middle.
        Vector3 reference = Vector3.zero;
        float best = float.MaxValue;
        foreach (FirstPersonController p in Object.FindObjectsByType<FirstPersonController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (p.gameObject.scene != scene) continue;
            foreach (Belt b in belts)
            {
                float d = b.Distance(p.transform.position);
                if (d < best) { best = d; reference = p.transform.position; }
            }
        }
        if (best == float.MaxValue) reference = (belts[0].a + belts[0].b) * 0.5f;

        belts.Sort((x, y) => x.Distance(reference).CompareTo(y.Distance(reference)));
        // Belt 1 is behind the player (the bench stands past it), belt 2 is between the player and the robots,
        // belt 3 is behind the robots and blocks the view of the level.
        Belt behind = belts[0];
        Belt first = belts[1];
        Belt second = belts[2];

        Vector3 p0 = behind.Closest(reference);
        Vector3 p1 = first.Closest(p0);
        Vector3 forward = p1 - p0;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f) { Debug.LogError("[FinalShift] Intro cutscene: the first two belts overlap."); return false; }
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Quaternion facing = Quaternion.LookRotation(forward);

        Physics.SyncTransforms();
        Vector3 start = Floor(p1 - forward * StartBehindFirstBelt, reference.y);

        // Root
        GameObject root = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        Undo.RegisterCreatedObjectUndo(root, "Build Intro Cutscene");
        root.transform.SetPositionAndRotation(start, facing);
        IntroCutscene intro = root.AddComponent<IntroCutscene>();

        Transform playerStart = new GameObject("PlayerStart").transform;
        playerStart.SetParent(root.transform, false);

        BuildWorkbench(root.transform);

        // Robots: halfway between the two belts, spread sideways, facing the player.
        Transform robotsRoot = new GameObject("IntroRobots").transform;
        robotsRoot.SetParent(root.transform, false);
        var robots = new List<RobotEnemy>();
        Vector3 eye = start + Vector3.up * 1.6f;
        for (int i = 0; i < RobotOffsets.Length; i++)
        {
            Vector3 guess = p1 + right * RobotOffsets[i];
            Vector3 onFirst = first.Closest(guess);
            Vector3 onSecond = second.Closest(onFirst);
            if ((onSecond - onFirst).sqrMagnitude < 0.01f) onSecond = onFirst + forward * 4f;
            Vector3 spot = Floor((onFirst + onSecond) * 0.5f, start.y);
            Vector3 look = eye - spot;
            look.y = 0f;

            GameObject robot = (GameObject)PrefabUtility.InstantiatePrefab(robotPrefab, scene);
            robot.name = "RobotIntro " + (i + 1);
            robot.transform.SetParent(robotsRoot, true);
            robot.transform.SetPositionAndRotation(spot, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.LookRotation(-forward));
            robots.Add(robot.GetComponent<RobotEnemy>());
        }

        SerializedObject so = new SerializedObject(intro);
        so.FindProperty("playerStart").objectReferenceValue = playerStart;
        SerializedProperty list = so.FindProperty("robots");
        list.arraySize = robots.Count;
        for (int i = 0; i < robots.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = robots[i];
        so.FindProperty("blockingBelt").objectReferenceValue = second.source;
        SerializedProperty items = so.FindProperty("blockingItems");
        items.arraySize = cratePrefab != null ? 1 : 0;
        if (cratePrefab != null) items.GetArrayElementAtIndex(0).objectReferenceValue = cratePrefab;
        so.ApplyModifiedPropertiesWithoutUndo();

        Selection.activeObject = root;
        Debug.Log($"[FinalShift] Intro cutscene built in {ScenePath}: player start at {start}, " +
                  $"robots between belt {first.source.BeltId} and belt {second.source.BeltId} (the blocking belt). " +
                  "Untick Play Intro on \"" + RootName + "\" to test other parts of the level.");
        return true;
    }

    /// <summary>Drops a point onto whatever is below it (the floor), or uses the fallback height.</summary>
    private static Vector3 Floor(Vector3 p, float fallbackY)
    {
        Vector3 from = new Vector3(p.x, fallbackY + 1.5f, p.z);
        if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore))
            return hit.point;
        return new Vector3(p.x, fallbackY, p.z);
    }

    // ---------------------------------------------------------------- workbench

    private static void BuildWorkbench(Transform root)
    {
        Material wood = FlatMaterial("Intro_BenchTop", new Color(0.30f, 0.22f, 0.15f), 0.7f);
        Material steel = FlatMaterial("Intro_BenchSteel", new Color(0.22f, 0.23f, 0.25f), 0.6f);
        Material rust = FlatMaterial("Intro_Rust", new Color(0.42f, 0.22f, 0.12f), 0.6f);
        Material rag = FlatMaterial("Intro_Rag", new Color(0.45f, 0.42f, 0.36f), 0.8f);
        Material yellow = FlatMaterial("Intro_Yellow", new Color(0.78f, 0.60f, 0.12f), 0.5f);

        Transform bench = new GameObject("Workbench").transform;
        bench.SetParent(root, false);
        bench.localPosition = new Vector3(0f, 0f, BenchDistance);

        // One collider for the whole bench.
        BoxCollider col = bench.gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, BenchHeight * 0.5f, 0f);
        col.size = new Vector3(BenchWidth, BenchHeight, BenchDepth);

        float top = BenchHeight;
        Part(bench, PrimitiveType.Cube, "Top", new Vector3(0f, top - 0.03f, 0f), new Vector3(BenchWidth, 0.06f, BenchDepth), Vector3.zero, wood);
        Part(bench, PrimitiveType.Cube, "Apron", new Vector3(0f, top - 0.1f, BenchDepth * 0.5f - 0.03f), new Vector3(BenchWidth - 0.06f, 0.1f, 0.03f), Vector3.zero, steel);
        float lx = BenchWidth * 0.5f - 0.06f, lz = BenchDepth * 0.5f - 0.06f;
        foreach (Vector2 s in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
            Part(bench, PrimitiveType.Cube, "Leg", new Vector3(s.x * lx, (top - 0.06f) * 0.5f, s.y * lz), new Vector3(0.06f, top - 0.06f, 0.06f), Vector3.zero, steel);
        Part(bench, PrimitiveType.Cube, "Shelf", new Vector3(0f, 0.18f, 0f), new Vector3(BenchWidth - 0.1f, 0.03f, BenchDepth - 0.1f), Vector3.zero, steel);

        // Clutter. Whatever is being made sits at the near edge, under the camera, so it's never in frame.
        Part(bench, PrimitiveType.Cube, "ViseBase", new Vector3(-0.55f, top + 0.04f, 0.12f), new Vector3(0.16f, 0.08f, 0.2f), Vector3.zero, yellow);
        Part(bench, PrimitiveType.Cube, "ViseJaw", new Vector3(-0.55f, top + 0.12f, 0.06f), new Vector3(0.18f, 0.1f, 0.05f), Vector3.zero, steel);
        Part(bench, PrimitiveType.Cube, "ViseJaw2", new Vector3(-0.55f, top + 0.12f, 0.16f), new Vector3(0.18f, 0.1f, 0.05f), Vector3.zero, steel);
        Part(bench, PrimitiveType.Cylinder, "ViseScrew", new Vector3(-0.55f, top + 0.12f, -0.02f), new Vector3(0.02f, 0.08f, 0.02f), new Vector3(90f, 0f, 0f), steel);
        Part(bench, PrimitiveType.Cube, "Rag", new Vector3(0.42f, top + 0.006f, 0.08f), new Vector3(0.28f, 0.012f, 0.22f), new Vector3(0f, 17f, 0f), rag);
        Part(bench, PrimitiveType.Cylinder, "HammerHandle", new Vector3(0.18f, top + 0.02f, 0.14f), new Vector3(0.025f, 0.14f, 0.025f), new Vector3(90f, 62f, 0f), wood);
        Part(bench, PrimitiveType.Cube, "HammerHead", new Vector3(0.06f, top + 0.03f, 0.2f), new Vector3(0.05f, 0.05f, 0.11f), new Vector3(0f, 62f, 0f), steel);
        Part(bench, PrimitiveType.Cylinder, "Pipe", new Vector3(0.6f, top + 0.03f, -0.12f), new Vector3(0.06f, 0.16f, 0.06f), new Vector3(90f, -20f, 0f), rust);
        for (int i = 0; i < 6; i++)
        {
            Vector3 pos = new Vector3(-0.2f + i * 0.07f + (i % 2) * 0.02f, top + 0.008f, 0.2f - (i % 3) * 0.05f);
            Part(bench, PrimitiveType.Cylinder, "Bolt", pos, new Vector3(0.018f, 0.008f, 0.018f), Vector3.zero, i % 2 == 0 ? steel : rust);
        }
        Part(bench, PrimitiveType.Cube, "SpringCoil", new Vector3(-0.28f, top + 0.015f, 0.02f), new Vector3(0.12f, 0.03f, 0.03f), new Vector3(0f, -30f, 0f), rust);

        // A weak work lamp over the bench, so it reads in the dark.
        Part(bench, PrimitiveType.Cylinder, "LampPole", new Vector3(0.68f, top + 0.25f, 0.22f), new Vector3(0.02f, 0.25f, 0.02f), Vector3.zero, steel);
        Part(bench, PrimitiveType.Cube, "LampShade", new Vector3(0.58f, top + 0.5f, 0.18f), new Vector3(0.18f, 0.06f, 0.12f), new Vector3(0f, 0f, -20f), yellow);
        Light light = new GameObject("WorkLamp").AddComponent<Light>();
        light.transform.SetParent(bench, false);
        light.transform.localPosition = new Vector3(0.45f, top + 0.42f, 0.12f);
        light.type = LightType.Point;
        light.color = new Color(1f, 0.72f, 0.42f);
        light.range = 2.6f;
        light.intensity = 1.6f;
        light.shadows = LightShadows.None;
    }

    private static Transform Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Vector3 euler, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mat != null) mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        return go.transform;
    }

    private static Material FlatMaterial(string name, Color colour, float grime)
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/ProtMaterials", "Intro");

        Shader shader = Shader.Find(FlatShader);
        bool custom = shader != null;
        if (!custom) shader = Shader.Find("Universal Render Pipeline/Lit");

        string path = MaterialFolder + "/" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        mat.SetColor("_BaseColor", colour);
        if (custom)
        {
            mat.SetTexture("_GrimeTex", AssetDatabase.LoadAssetAtPath<Texture2D>(GrimePath));
            mat.SetFloat("_GrimeStrength", grime);
            mat.SetFloat("_GrimeScale", 2f);
            mat.SetColor("_Ambient", new Color(0.045f, 0.043f, 0.042f));
            mat.SetFloat("_ObjectSpace", 0f);
            mat.DisableKeyword("_OBJECT_SPACE");
        }
        else
        {
            mat.SetFloat("_Smoothness", 0.1f);
            mat.SetFloat("_Metallic", 0f);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
