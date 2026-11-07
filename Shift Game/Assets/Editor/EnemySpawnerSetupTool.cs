using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the placeholder EnemySpawner prefab (plain colours, no textures) and puts a demo in the Kalman test scene.
/// Menu: Final Shift > Enemy Spawner > ...
/// </summary>
[InitializeOnLoad]
public static class EnemySpawnerSetupTool
{
    private const string ScenePath = "Assets/Scenes/Kalman test scene.unity";
    private const string PrefabPath = "Assets/Prefabs/Interactables/EnemySpawner.prefab";
    private const string ZonePrefabPath = "Assets/Prefabs/Interactables/DoorTriggerZone.prefab";
    private const string RobotPrefabPath = "Assets/Prefabs/Robot.prefab";
    private const string MaterialFolder = "Assets/ProtMaterials/Interactables";
    private const string DemoRootName = "Enemy Spawner Demo";
    // Creating this file (in the project's Temp folder) builds the prefab and the demo once on the next editor reload.
    private const string TriggerFile = "Temp/FinalShift_BuildEnemySpawner";

    private const float Inner = 2.6f;   // hole size
    private const float Wall = 0.2f;

    static EnemySpawnerSetupTool()
    {
        if (!File.Exists(TriggerFile)) return;
        File.Delete(TriggerFile);
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[FinalShift] Enemy spawner setup skipped: editor is in play mode.");
                return;
            }
            BuildPrefab();
            AddDemo();
        };
    }

    [MenuItem("Final Shift/Enemy Spawner/Rebuild Spawner Prefab")]
    public static void BuildPrefab()
    {
        try
        {
            Material housing = Mat("Spawner_Housing", new Color(0.24f, 0.26f, 0.3f), false);
            Material rim = Mat("Spawner_Rim", new Color(0.95f, 0.72f, 0.08f), false);
            Material trap = Mat("Spawner_Trapdoor", new Color(0.55f, 0.3f, 0.14f), false);
            Material inside = Mat("Spawner_Inside", new Color(0.02f, 0.02f, 0.025f), true);
            Material lampOff = Mat("Spawner_LampOff", new Color(0.25f, 0.05f, 0.04f), true);
            Material lampOn = Mat("Spawner_LampOn", new Color(1f, 0.15f, 0.08f), true);
            GameObject robot = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefabPath);

            // Chute tall enough to hide the robot before it drops.
            float robotHeight = MeasureHeight(robot);
            float h = Mathf.Max(3.4f, robotHeight + 0.7f);
            const float slot = 0.15f; // gap at the bottom of the right wall the trapdoor slides through

            GameObject root = new GameObject("EnemySpawner");
            Transform r = root.transform;
            float half = Inner * 0.5f, outer = Inner + 2f * Wall;
            Part("WallFront", r, new Vector3(0f, h * 0.5f, half + Wall * 0.5f), new Vector3(outer, h, Wall), housing, true);
            Part("WallBack", r, new Vector3(0f, h * 0.5f, -half - Wall * 0.5f), new Vector3(outer, h, Wall), housing, true);
            Part("WallLeft", r, new Vector3(-half - Wall * 0.5f, h * 0.5f, 0f), new Vector3(Wall, h, Inner), housing, true);
            Part("WallRight", r, new Vector3(half + Wall * 0.5f, (h + slot) * 0.5f, 0f), new Vector3(Wall, h - slot, Inner), housing, true);
            Part("Lid", r, new Vector3(0f, h + Wall * 0.5f, 0f), new Vector3(outer, Wall, outer), housing, true);
            Transform dark = Part("InsideDark", r, new Vector3(0f, h - 0.01f, 0f), new Vector3(Inner, Inner, 1f), inside, false, PrimitiveType.Quad);
            dark.localRotation = Quaternion.Euler(-90f, 0f, 0f); // faces down into the hole

            // Yellow rim around the hole.
            Part("RimFront", r, new Vector3(0f, -0.04f, half + Wall * 0.5f), new Vector3(outer + 0.1f, 0.08f, Wall + 0.1f), rim, false);
            Part("RimBack", r, new Vector3(0f, -0.04f, -half - Wall * 0.5f), new Vector3(outer + 0.1f, 0.08f, Wall + 0.1f), rim, false);
            Part("RimLeft", r, new Vector3(-half - Wall * 0.5f, -0.04f, 0f), new Vector3(Wall + 0.1f, 0.08f, Inner), rim, false);

            // The housing on the side that the trapdoor slides out of.
            float pocketLength = Inner + 0.2f;
            float pocketX = half + Wall + pocketLength * 0.5f;
            Part("TrapdoorHousing", r, new Vector3(pocketX, -0.05f, 0f), new Vector3(pocketLength, 0.4f, outer), housing, true);
            Part("HousingStripe", r, new Vector3(pocketX, -0.05f, outer * 0.5f + 0.01f), new Vector3(pocketLength, 0.1f, 0.03f), rim, false);
            Part("HousingStripeBack", r, new Vector3(pocketX, -0.05f, -outer * 0.5f - 0.01f), new Vector3(pocketLength, 0.1f, 0.03f), rim, false);

            // Trapdoor starts hidden in the housing and slides left over the hole.
            Transform trapdoor = new GameObject("Trapdoor").transform;
            trapdoor.SetParent(r, false);
            trapdoor.localPosition = new Vector3(pocketX, 0f, 0f);
            Part("Plate", trapdoor, Vector3.zero, new Vector3(Inner + 0.1f, 0.1f, Inner + 0.1f), trap, true);
            Part("Handle", trapdoor, new Vector3(-Inner * 0.5f + 0.1f, -0.07f, 0f), new Vector3(0.12f, 0.06f, 1.2f), rim, false);
            trapdoor.gameObject.AddComponent<Rigidbody>().isKinematic = true;

            // Warning lamps that blink while it spawns.
            Renderer[] lamps =
            {
                Part("LampFront", r, new Vector3(0f, h - 0.35f, half + Wall + 0.03f), new Vector3(0.6f, 0.2f, 0.06f), lampOff, false).GetComponent<Renderer>(),
                Part("LampBack", r, new Vector3(0f, h - 0.35f, -half - Wall - 0.03f), new Vector3(0.6f, 0.2f, 0.06f), lampOff, false).GetComponent<Renderer>(),
                Part("LampLeft", r, new Vector3(-half - Wall - 0.03f, h - 0.35f, 0f), new Vector3(0.06f, 0.2f, 0.6f), lampOff, false).GetComponent<Renderer>(),
            };

            Transform spawnPoint = new GameObject("SpawnPoint").transform;
            spawnPoint.SetParent(r, false);
            spawnPoint.localPosition = new Vector3(0f, 0.3f, 0f);

            root.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

            EnemySpawner spawner = root.AddComponent<EnemySpawner>();
            SerializedObject so = new SerializedObject(spawner);
            so.FindProperty("spawnPoint").objectReferenceValue = spawnPoint;
            so.FindProperty("trapdoor").objectReferenceValue = trapdoor;
            so.FindProperty("trapdoorSlide").vector3Value = new Vector3(-pocketX, 0f, 0f);
            SerializedProperty lampList = so.FindProperty("warningLamps");
            lampList.arraySize = lamps.Length;
            for (int i = 0; i < lamps.Length; i++) lampList.GetArrayElementAtIndex(i).objectReferenceValue = lamps[i];
            so.FindProperty("lampOffMaterial").objectReferenceValue = lampOff;
            so.FindProperty("lampOnMaterial").objectReferenceValue = lampOn;
            SerializedProperty first = so.FindProperty("enemies").GetArrayElementAtIndex(0);
            first.FindPropertyRelative("label").stringValue = "Robot";
            first.FindPropertyRelative("prefab").objectReferenceValue = robot;
            first.FindPropertyRelative("count").intValue = 3;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"[FinalShift] Enemy spawner prefab saved to {PrefabPath} (chute height {h:F2} m, robot {robotHeight:F2} m).");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[FinalShift] Enemy spawner build failed: " + e);
        }
    }

    [MenuItem("Final Shift/Enemy Spawner/Add Demo To Kalman Test Scene")]
    public static void AddDemo()
    {
        try
        {
            GameObject spawnerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject zonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ZonePrefabPath);
            if (spawnerPrefab == null || zonePrefab == null) throw new System.Exception("Spawner or trigger zone prefab missing.");

            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                openedHere = true;
            }
            bool wasDirty = scene.isDirty;

            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == DemoRootName) Object.DestroyImmediate(go);

            GameObject root = new GameObject(DemoRootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            GameObject spawnerGo = (GameObject)PrefabUtility.InstantiatePrefab(spawnerPrefab, root.transform);
            spawnerGo.transform.SetPositionAndRotation(new Vector3(18f, 6f, 4f), Quaternion.identity);
            GameObject zone = (GameObject)PrefabUtility.InstantiatePrefab(zonePrefab, root.transform);
            zone.name = "SpawnerTriggerZone";
            zone.transform.SetPositionAndRotation(new Vector3(12.5f, 1.5f, -1f), Quaternion.identity);
            zone.transform.localScale = new Vector3(5f, 3f, 5f);

            SerializedObject so = new SerializedObject(spawnerGo.GetComponent<EnemySpawner>());
            SerializedProperty zones = so.FindProperty("triggerZones");
            zones.arraySize = 1;
            zones.GetArrayElementAtIndex(0).objectReferenceValue = zone.GetComponent<DoorTriggerZone>();
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!wasDirty || openedHere)
            {
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[FinalShift] Enemy spawner demo added to the Kalman test scene and saved.");
            }
            else
            {
                Debug.LogWarning("[FinalShift] Enemy spawner demo added. The scene already had unsaved changes, so it was left unsaved: save it yourself.");
            }
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[FinalShift] Enemy spawner demo failed: " + e);
        }
    }

    // ---------------------------------------------------------------- helpers

    private static float MeasureHeight(GameObject prefab)
    {
        if (prefab == null) return 2.5f;
        // Load in an isolated preview scene so the open scene is not touched.
        GameObject temp = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(prefab));
        try
        {
            temp.transform.position = Vector3.zero;
            Renderer[] renderers = temp.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 2.5f;
            Bounds b = renderers[0].bounds;
            foreach (Renderer rend in renderers) b.Encapsulate(rend.bounds);
            return b.max.y - Mathf.Min(0f, b.min.y);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(temp);
        }
    }

    private static Transform Part(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool keepCollider, PrimitiveType type = PrimitiveType.Cube)
    {
        GameObject go = GameObject.CreatePrimitive(type);
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
        Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        bool create = mat == null;
        if (create) mat = new Material(shader) { name = name };
        else mat.shader = shader;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
        if (create) AssetDatabase.CreateAsset(mat, path);
        else EditorUtility.SetDirty(mat);
        return mat;
    }
}
