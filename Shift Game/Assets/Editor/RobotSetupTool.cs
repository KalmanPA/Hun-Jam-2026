using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Robot setup helpers.
///  - Builds the placeholder "OldProtbot" prefab (boxy yellow robot with a gun arm) and the health/ammo pickup prefabs.
///  - Sets up the real Robot prefab (Kalman's model): mesh colliders on every model piece so they fall apart
///    on death, and bullets fired from the model's muzzle object.
///  - Puts one robot in the Kalman test scene and bakes its NavMesh.
/// Menu: Final Shift > ...
/// </summary>
[InitializeOnLoad]
public static class RobotSetupTool
{
    private const string ScenePath = "Assets/Scenes/Kalman test scene.unity";
    private const string PrefabFolder = "Assets/Prefabs";
    private const string RobotPrefabPath = PrefabFolder + "/Robot.prefab";                // Kalman's model
    private const string OldProtbotPrefabPath = PrefabFolder + "/OldProtbot.prefab";     // placeholder boxes
    private const string HealthPickupPath = PrefabFolder + "/HealthPickup.prefab";
    private const string AmmoPickupPath = PrefabFolder + "/AmmoPickup.prefab";
    private const string MaterialFolder = "Assets/ProtMaterials";
    // The old placeholder parts that are removed from the Robot prefab when it is switched to the model.
    private static readonly string[] PlaceholderParts = { "Treads", "Hips", "Torso", "ArmLeft", "HeadPivot", "GunPivot" };
    // Creating this file (in the project's Temp folder) runs the model setup once on the next editor reload.
    private const string TriggerFile = "Temp/FinalShift_RunRobotSetup";

    private static readonly Vector3 RobotPosition = new Vector3(6f, 0f, 15f);

    static RobotSetupTool()
    {
        if (!File.Exists(TriggerFile)) return;
        File.Delete(TriggerFile);
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[FinalShift] Robot setup skipped: editor is in play mode.");
                return;
            }
            SetUpRobotModelPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log("[FinalShift] Robot model setup finished.");
        };
    }

    // ---------------------------------------------------------------- menu

    [MenuItem("Final Shift/Build OldProtbot And Pickup Prefabs")]
    public static void BuildPrefabsMenu() => BuildPrefabs(true);

    [MenuItem("Final Shift/Set Up Robot Model Prefab")]
    public static void SetUpRobotModelMenu() => SetUpRobotModelPrefab();

    [MenuItem("Final Shift/Add Robot And Bake NavMesh")]
    public static void AddRobotAndBakeMenu() => Setup(false);

    /// <param name="rebuild">Replace any robots already in the scene with one fresh robot.</param>
    private static void Setup(bool rebuild)
    {
        try
        {
            BuildPrefabs(false);
            GameObject robotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefabPath);
            if (robotPrefab == null) robotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OldProtbotPrefabPath);

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (scene.isDirty)
                {
                    Debug.LogError($"[FinalShift] Robot setup needs '{ScenePath}' open. Save the current scene and open it first.");
                    return;
                }
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            RobotEnemy[] existing = Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (rebuild)
            {
                foreach (RobotEnemy r in existing) Undo.DestroyObjectImmediate(r.gameObject);
                GameObject oldGroup = GameObject.Find("Robots");
                if (oldGroup != null) Undo.DestroyObjectImmediate(oldGroup);
                existing = new RobotEnemy[0];
            }

            if (existing.Length == 0)
            {
                GameObject robot = (GameObject)PrefabUtility.InstantiatePrefab(robotPrefab, scene);
                Undo.RegisterCreatedObjectUndo(robot, "Add robot");
                robot.name = "Robot";
                robot.transform.position = RobotPosition;
                Vector3 look = new Vector3(0f, 0f, -5f) - RobotPosition; // toward the player start
                look.y = 0f;
                robot.transform.rotation = Quaternion.LookRotation(look);
                Debug.Log("[FinalShift] Added one robot to the scene.");
            }

            BakeNavMesh();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[FinalShift] Robot setup done: prefabs, robot and NavMesh saved.");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[FinalShift] Robot setup failed: " + e);
        }
    }

    [MenuItem("Final Shift/Bake NavMesh")]
    public static void BakeNavMesh()
    {
        Scene scene = SceneManager.GetActiveScene();

        NavMeshSurface surface = Object.FindFirstObjectByType<NavMeshSurface>();
        if (surface == null)
        {
            GameObject go = new GameObject("NavMesh");
            Undo.RegisterCreatedObjectUndo(go, "Add NavMesh");
            surface = go.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        }

        // The player moves, so it must not carve a hole in the NavMesh (robots have this in their prefab).
        IgnoreInBake(Object.FindFirstObjectByType<PlayerHealth>()?.gameObject);

        // Remove the old baked asset, bake, then store the new data next to the scene.
        if (surface.navMeshData != null)
        {
            string oldPath = AssetDatabase.GetAssetPath(surface.navMeshData);
            surface.RemoveData();
            surface.navMeshData = null;
            if (!string.IsNullOrEmpty(oldPath)) AssetDatabase.DeleteAsset(oldPath);
        }

        surface.BuildNavMesh();

        string parent = Path.GetDirectoryName(scene.path).Replace('\\', '/');
        string sceneName = Path.GetFileNameWithoutExtension(scene.path);
        string folder = parent + "/" + sceneName;
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(parent, sceneName);
        string assetPath = folder + "/NavMesh-" + surface.gameObject.name + ".asset";
        AssetDatabase.CreateAsset(surface.navMeshData, assetPath);
        AssetDatabase.SaveAssets();
        EditorUtility.SetDirty(surface);
        EditorSceneManager.MarkSceneDirty(scene);

        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        Debug.Log($"[FinalShift] NavMesh baked to {assetPath} ({tri.indices.Length / 3} triangles).");
    }

    private static void IgnoreInBake(GameObject go)
    {
        if (go == null) return;
        NavMeshModifier mod = go.GetComponent<NavMeshModifier>();
        if (mod == null) mod = Undo.AddComponent<NavMeshModifier>(go);
        mod.ignoreFromBuild = true;
        EditorUtility.SetDirty(mod);
    }

    // ---------------------------------------------------------------- prefabs

    private static GameObject BuildPrefabs(bool overwrite)
    {
        GameObject health = BuildPickup(HealthPickupPath, "HealthPickup", Pickup.Kind.Health, 25f,
            GetMaterial("PickupHealth", false, new Color(0.2f, 0.85f, 0.25f)), overwrite);   // green
        GameObject ammo = BuildPickup(AmmoPickupPath, "AmmoPickup", Pickup.Kind.Ammo, 30f,
            GetMaterial("PickupAmmo", false, new Color(0.95f, 0.85f, 0.1f)), overwrite);     // yellow
        return BuildRobotPrefab(overwrite, health, ammo);
    }

    private static GameObject BuildPickup(string path, string name, Pickup.Kind kind, float amount, Material material, bool overwrite)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null && !overwrite) return existing;

        GameObject root = new GameObject(name);
        try
        {
            // Art model (Assets/Art/Healthpack.fbx / Ammobox.fbx); the coloured cube is only a fallback.
            if (!PickupModelTool.ApplyModel(root, kind))
            {
                Transform cube = Box("Cube", root.transform, Vector3.zero, Vector3.one * 0.4f, material);
                Object.DestroyImmediate(cube.GetComponent<Collider>()); // picked up by distance, nothing to shoot
            }
            Pickup pickup = root.AddComponent<Pickup>();
            SerializedObject so = new SerializedObject(pickup);
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.FindProperty("amount").floatValue = amount;
            so.ApplyModifiedPropertiesWithoutUndo();
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[FinalShift] Pickup prefab saved to {path}.");
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static GameObject BuildRobotPrefab(bool overwrite, GameObject healthPickup, GameObject ammoPickup)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(OldProtbotPrefabPath);
        if (existing != null && !overwrite) return existing;

        Material body = GetMaterial("RobotBody", false, new Color(0.85f, 0.62f, 0.12f));   // hazard yellow
        Material dark = GetMaterial("RobotDark", false, new Color(0.16f, 0.16f, 0.18f));
        Material visorIdle = GetMaterial("RobotVisor", true, new Color(0.55f, 0.05f, 0.05f));
        Material visorCharge = GetMaterial("RobotVisorCharge", true, new Color(1f, 0.25f, 0.15f));
        Material flash = GetMaterial("RobotHitFlash", true, Color.white);
        Material bullet = GetMaterial("EnemyBullet", true, new Color(1f, 0.2f, 0.1f));
        Material explosion = GetMaterial("RobotExplosion", true, new Color(1f, 0.6f, 0.15f));

        GameObject root = new GameObject("OldProtbot");
        try
        {
            // Boxy yellow robot on treads.
            Box("Treads", root.transform, new Vector3(0f, 0.18f, 0f), new Vector3(0.9f, 0.36f, 1.05f), dark);
            Box("Hips", root.transform, new Vector3(0f, 0.45f, 0f), new Vector3(0.45f, 0.25f, 0.4f), dark);
            Box("Torso", root.transform, new Vector3(0f, 0.92f, 0f), new Vector3(0.8f, 0.72f, 0.55f), body);
            Box("ArmLeft", root.transform, new Vector3(-0.5f, 0.85f, 0f), new Vector3(0.16f, 0.55f, 0.16f), dark);

            Transform head = new GameObject("HeadPivot").transform;
            head.SetParent(root.transform, false);
            head.localPosition = new Vector3(0f, 1.35f, 0f);
            Box("Head", head, new Vector3(0f, 0.16f, 0f), new Vector3(0.5f, 0.32f, 0.45f), body);
            Transform visor = Box("Visor", head, new Vector3(0f, 0.18f, 0.23f), new Vector3(0.38f, 0.08f, 0.04f), visorIdle);
            Object.DestroyImmediate(visor.GetComponent<Collider>());
            Box("Antenna", head, new Vector3(0.15f, 0.42f, -0.1f), new Vector3(0.04f, 0.2f, 0.04f), dark);

            Transform gunPivot = new GameObject("GunPivot").transform;
            gunPivot.SetParent(root.transform, false);
            gunPivot.localPosition = new Vector3(0.52f, 1.05f, 0.05f);
            Box("GunArm", gunPivot, new Vector3(0f, 0f, 0.28f), new Vector3(0.18f, 0.18f, 0.7f), dark);
            Box("GunTip", gunPivot, new Vector3(0f, 0f, 0.66f), new Vector3(0.1f, 0.1f, 0.08f), body);

            Transform muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(gunPivot, false);
            muzzle.localPosition = new Vector3(0f, 0f, 0.8f);
            Light flashLight = new GameObject("MuzzleFlash").AddComponent<Light>();
            flashLight.transform.SetParent(muzzle, false);
            flashLight.type = LightType.Point;
            flashLight.color = new Color(1f, 0.35f, 0.2f);
            flashLight.range = 5f;
            flashLight.intensity = 4f;
            flashLight.shadows = LightShadows.None;
            flashLight.enabled = false;

            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.5f;
            agent.height = 1.8f;
            agent.speed = 3f;
            agent.angularSpeed = 120f;
            agent.acceleration = 10f;
            agent.stoppingDistance = 0.5f;

            // Robots move, so they never carve holes when the NavMesh is baked.
            root.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

            root.AddComponent<EnemyAim>();
            RobotEnemy robot = root.AddComponent<RobotEnemy>();

            SerializedObject so = new SerializedObject(robot);
            so.FindProperty("eye").objectReferenceValue = head;
            so.FindProperty("head").objectReferenceValue = head;
            so.FindProperty("arms").objectReferenceValue = gunPivot;
            SetArray(so.FindProperty("muzzles"), new Object[] { muzzle });
            so.FindProperty("muzzleFlash").objectReferenceValue = flashLight;
            SetArray(so.FindProperty("armTips"), new Object[] { visor.GetComponent<Renderer>() });
            SetArray(so.FindProperty("bodyRenderers"), root.GetComponentsInChildren<Renderer>());
            so.FindProperty("bulletMaterial").objectReferenceValue = bullet;
            so.FindProperty("armTipIdleMaterial").objectReferenceValue = visorIdle;
            so.FindProperty("armTipChargeMaterial").objectReferenceValue = visorCharge;
            so.FindProperty("hitFlashMaterial").objectReferenceValue = flash;
            so.FindProperty("explosionMaterial").objectReferenceValue = explosion;
            so.FindProperty("healthPickupPrefab").objectReferenceValue = healthPickup;
            so.FindProperty("ammoPickupPrefab").objectReferenceValue = ammoPickup;
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, OldProtbotPrefabPath);
            Debug.Log($"[FinalShift] OldProtbot prefab saved to {OldProtbotPrefabPath}.");
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// Switches the Robot prefab to Kalman's model: removes the placeholder boxes, puts a convex mesh collider
    /// on every model piece (they become loose physics pieces on death), and fires bullets from the
    /// model's muzzle object.
    /// </summary>
    private static void SetUpRobotModelPrefab()
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(RobotPrefabPath);
        try
        {
            Transform root = contents.transform;
            RobotEnemy robot = contents.GetComponent<RobotEnemy>();
            if (robot == null) { Debug.LogError("[FinalShift] Robot prefab has no RobotEnemy."); return; }

            foreach (string partName in PlaceholderParts)
            {
                Transform part = root.Find(partName);
                if (part != null) Object.DestroyImmediate(part.gameObject);
            }

            // The model: the active child that has renderers. Inactive model children are old,
            // replaced models and are removed.
            Transform model = null;
            System.Collections.Generic.List<GameObject> oldModels = new System.Collections.Generic.List<GameObject>();
            foreach (Transform child in root)
            {
                if (child.GetComponentInChildren<Renderer>(true) == null) continue;
                if (child.gameObject.activeSelf && model == null) model = child;
                else if (!child.gameObject.activeSelf) oldModels.Add(child.gameObject);
            }
            if (model == null) { Debug.LogError("[FinalShift] No active model found under the Robot prefab."); return; }
            foreach (GameObject old in oldModels)
            {
                Debug.Log($"[FinalShift] Removing old inactive model '{old.name}' from the Robot prefab.");
                Object.DestroyImmediate(old);
            }

            Transform muzzle = null;
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                if (t.name.ToLowerInvariant().Contains("muzzle")) { muzzle = t; break; }
            if (muzzle == null) Debug.LogWarning("[FinalShift] No object with 'muzzle' in its name found in the model.");

            // Mesh colliders on every piece (convex, so they can be physics pieces when the robot dies).
            int colliders = 0;
            System.Text.StringBuilder log = new System.Text.StringBuilder();
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                if (r is MeshRenderer && r.TryGetComponent(out MeshFilter mf)) mesh = mf.sharedMesh;
                else if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                log.Append($"\n  {r.name} ({r.GetType().Name}, mesh {(mesh != null ? mesh.name : "none")})");
                if (mesh == null) continue; // the muzzle gets one too: robot bullets ignore robot colliders

                MeshCollider mc = r.GetComponent<MeshCollider>();
                if (mc == null) mc = r.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                mc.convex = true;
                colliders++;
            }

            // Muzzle flash light on the muzzle.
            Light flash = null;
            if (muzzle != null)
            {
                Transform existing = muzzle.Find("MuzzleFlash");
                flash = existing != null ? existing.GetComponent<Light>() : null;
                if (flash == null)
                {
                    flash = new GameObject("MuzzleFlash").AddComponent<Light>();
                    flash.transform.SetParent(muzzle, false);
                    flash.type = LightType.Point;
                    flash.color = new Color(1f, 0.35f, 0.2f);
                    flash.range = 5f;
                    flash.intensity = 4f;
                    flash.shadows = LightShadows.None;
                }
                flash.enabled = false;
            }

            // Size the NavMesh agent and the eye (line of sight) to the model.
            Bounds bounds = new Bounds(root.position, Vector3.zero);
            bool first = true;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            Transform eye = root.Find("Eye");
            if (eye == null)
            {
                eye = new GameObject("Eye").transform;
                eye.SetParent(root, false);
            }
            eye.position = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.85f, bounds.center.z);

            NavMeshAgent agent = contents.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.height = Mathf.Max(0.5f, bounds.max.y - root.position.y);
                agent.radius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z), 0.3f, 1.2f);
            }

            SerializedObject so = new SerializedObject(robot);
            so.FindProperty("eye").objectReferenceValue = eye;
            so.FindProperty("head").objectReferenceValue = null;
            so.FindProperty("arms").objectReferenceValue = null;
            SetArray(so.FindProperty("muzzles"), muzzle != null ? new Object[] { muzzle } : new Object[0]);
            so.FindProperty("muzzleFlash").objectReferenceValue = flash;
            SetArray(so.FindProperty("armTips"), new Object[0]);
            SetArray(so.FindProperty("bodyRenderers"), model.GetComponentsInChildren<Renderer>(true));
            Renderer faceRenderer = null;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                if (r.name.ToLowerInvariant() == "face") { faceRenderer = r; break; }
            if (faceRenderer == null)
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                    if (r.name.ToLowerInvariant().Contains("face")) { faceRenderer = r; break; }
            if (faceRenderer == null) Debug.LogWarning("[FinalShift] No object called 'face' found in the model.");
            so.FindProperty("face").objectReferenceValue = faceRenderer;
            log.Append($"\n  face renderer: {(faceRenderer != null ? faceRenderer.name : "none")}");
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(contents, RobotPrefabPath);
            Debug.Log($"[FinalShift] Robot model prefab set up: {colliders} mesh colliders, muzzle '{(muzzle != null ? muzzle.name : "none")}', " +
                      $"model bounds {bounds.size}, agent height {(agent != null ? agent.height : 0f):0.00} radius {(agent != null ? agent.radius : 0f):0.00}. Pieces:{log}");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[FinalShift] Robot model setup failed: " + e);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static void SetArray(SerializedProperty prop, Object[] values)
    {
        prop.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static Transform Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go.transform;
    }

    private static Material GetMaterial(string name, bool unlit, Color color)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        mat = new Material(shader) { name = name };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f); // flat, PS1-ish
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets", "ProtMaterials");
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}





