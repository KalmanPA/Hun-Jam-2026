using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the ElevatorPlatform prefab (plain colours) and puts a demo with a ledge at the top in the Kalman test scene.
/// Menu: Final Shift > Elevator > ...
/// </summary>
[InitializeOnLoad]
public static class ElevatorSetupTool
{
    private const string ScenePath = "Assets/Scenes/Kalman test scene.unity";
    private const string PrefabPath = "Assets/Prefabs/Interactables/ElevatorPlatform.prefab";
    private const string MaterialFolder = "Assets/ProtMaterials/Interactables";
    private const string DemoRootName = "Elevator Demo";
    private const string TriggerFile = "Temp/FinalShift_BuildElevator";

    static ElevatorSetupTool()
    {
        if (!File.Exists(TriggerFile)) return;
        File.Delete(TriggerFile);
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[FinalShift] Elevator setup skipped: editor is in play mode.");
                return;
            }
            BuildPrefab();
            AddDemo();
        };
    }

    [MenuItem("Final Shift/Elevator/Rebuild Elevator Prefab")]
    public static void BuildPrefab()
    {
        try
        {
            Material body = Mat("Box_Housing", new Color(0.22f, 0.24f, 0.27f), false);
            Material stripe = Mat("Box_Stripe", new Color(0.95f, 0.72f, 0.08f), false);
            Material idle = Mat("Glow_Green", new Color(0.15f, 1f, 0.25f), true);
            Material moving = Mat("Glow_Amber", new Color(1f, 0.55f, 0.05f), true);
            Material done = Mat("Spawner_LampOff", new Color(0.25f, 0.05f, 0.04f), true);

            const float w = 3f, d = 3f, h = 0.4f;
            GameObject root = new GameObject("ElevatorPlatform");
            Transform r = root.transform;
            Part("Body", r, new Vector3(0f, -h * 0.5f, 0f), new Vector3(w, h, d), body, true);
            // Hazard edging around the top.
            Part("EdgeFront", r, new Vector3(0f, -0.024f, d * 0.5f - 0.07f), new Vector3(w, 0.06f, 0.14f), stripe, false);
            Part("EdgeBack", r, new Vector3(0f, -0.024f, -d * 0.5f + 0.07f), new Vector3(w, 0.06f, 0.14f), stripe, false);
            Part("EdgeLeft", r, new Vector3(-w * 0.5f + 0.07f, -0.024f, 0f), new Vector3(0.14f, 0.06f, d - 0.28f), stripe, false);
            Part("EdgeRight", r, new Vector3(w * 0.5f - 0.07f, -0.024f, 0f), new Vector3(0.14f, 0.06f, d - 0.28f), stripe, false);
            Renderer[] lamps =
            {
                Part("LampFront", r, new Vector3(0f, -h * 0.5f, d * 0.5f + 0.02f), new Vector3(0.6f, 0.14f, 0.04f), idle, false).GetComponent<Renderer>(),
                Part("LampBack", r, new Vector3(0f, -h * 0.5f, -d * 0.5f - 0.02f), new Vector3(0.6f, 0.14f, 0.04f), idle, false).GetComponent<Renderer>(),
                Part("LampLeft", r, new Vector3(-w * 0.5f - 0.02f, -h * 0.5f, 0f), new Vector3(0.04f, 0.14f, 0.6f), idle, false).GetComponent<Renderer>(),
                Part("LampRight", r, new Vector3(w * 0.5f + 0.02f, -h * 0.5f, 0f), new Vector3(0.04f, 0.14f, 0.6f), idle, false).GetComponent<Renderer>(),
            };
            root.AddComponent<Rigidbody>().isKinematic = true; // moving collider

            ElevatorPlatform platform = root.AddComponent<ElevatorPlatform>();
            SerializedObject so = new SerializedObject(platform);
            so.FindProperty("topSize").vector2Value = new Vector2(w, d);
            SerializedProperty list = so.FindProperty("lamps");
            list.arraySize = lamps.Length;
            for (int i = 0; i < lamps.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = lamps[i];
            so.FindProperty("lampIdleMaterial").objectReferenceValue = idle;
            so.FindProperty("lampMovingMaterial").objectReferenceValue = moving;
            so.FindProperty("lampDoneMaterial").objectReferenceValue = done;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("[FinalShift] Elevator prefab saved to " + PrefabPath);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[FinalShift] Elevator build failed: " + e);
        }
    }

    [MenuItem("Final Shift/Elevator/Add Demo To Kalman Test Scene")]
    public static void AddDemo()
    {
        try
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) throw new System.Exception("Elevator prefab missing.");

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

            GameObject platform = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            platform.transform.SetPositionAndRotation(new Vector3(20f, 0.4f, -8f), Quaternion.identity);

            // A ledge level with the platform's end point (5 m up) to step off onto.
            Material wall = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/Wall_Panel.mat");
            if (wall == null) wall = Mat("Box_Housing", new Color(0.22f, 0.24f, 0.27f), false);
            Transform ledge = Part("Ledge", root.transform, new Vector3(23.5f, 2.7f, -8f), new Vector3(4f, 5.4f, 3f), wall, true);
            ledge.SetParent(root.transform, true);

            if (SceneManager.GetActiveScene() == scene) RobotSetupTool.BakeNavMesh();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!wasDirty || openedHere)
            {
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[FinalShift] Elevator demo added to the Kalman test scene and saved.");
            }
            else
            {
                Debug.LogWarning("[FinalShift] Elevator demo added. The scene already had unsaved changes, so it was left unsaved: save it yourself.");
            }
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[FinalShift] Elevator demo failed: " + e);
        }
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

    /// <summary>Plain colour material (reuses the existing one if it's already there).</summary>
    private static Material Mat(string name, Color color, bool unlit)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        mat = new Material(shader) { name = name };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
