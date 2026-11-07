using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Shield robots are much wider than their body (the shield reaches ~1.5 m from their centre), so they get
/// their own NavMesh agent type, "Shielded", with a matching radius. Its NavMesh keeps them a shield's width
/// away from walls, so the shield never clips into them. No per-frame checks needed.
///
/// Every scene with a NavMeshSurface needs a second surface for this agent type, baked alongside the normal one.
/// Menu: Final Shift > NavMesh > ...
/// </summary>
[InitializeOnLoad]
public static class ShieldNavMeshTool
{
    public const string AgentTypeName = "Shielded";
    public const float AgentRadius = 1.55f;
    public const float AgentHeight = 2.9f;

    private static readonly string[] ShieldPrefabs = { "Assets/Prefabs/RobotGear3Tutorial.prefab", "Assets/Prefabs/RobotShield.prefab" };
    private const string TestScenePath = "Assets/Scenes/Kalman test scene.unity";
    // Creating this file (in the project's Temp folder) runs the full setup once on the next editor reload.
    private const string TriggerFile = "Temp/FinalShift_ShieldNavMesh";

    static ShieldNavMeshTool()
    {
        if (!File.Exists(TriggerFile)) return;
        File.Delete(TriggerFile);
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            FullSetup();
        };
    }

    [MenuItem("Final Shift/NavMesh/Set Up Shielded Agent Type And Prefabs")]
    public static void SetUpMenu()
    {
        int id = EnsureAgentType();
        AssignToPrefabs(id);
    }

    [MenuItem("Final Shift/NavMesh/Bake All Surfaces In Open Scenes (adds Shielded surface)")]
    public static void BakeOpenScenesMenu()
    {
        int id = EnsureAgentType();
        for (int i = 0; i < SceneManager.sceneCount; i++)
            BakeScene(SceneManager.GetSceneAt(i), id, true);
    }

    private static void FullSetup()
    {
        try
        {
            int id = EnsureAgentType();
            AssignToPrefabs(id);

            // Open scenes: add + bake, leave saving to the user (they may have unsaved work).
            for (int i = 0; i < SceneManager.sceneCount; i++)
                BakeScene(SceneManager.GetSceneAt(i), id);

            // The test scene, if it isn't open: open it on the side, bake, save, close.
            if (!SceneManager.GetSceneByPath(TestScenePath).isLoaded)
            {
                Scene test = EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Additive);
                BakeScene(test, id);
                EditorSceneManager.SaveScene(test);
                EditorSceneManager.CloseScene(test, true);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[FinalShift] Shielded NavMesh setup done.");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[FinalShift] Shielded NavMesh setup failed: " + e);
        }
    }

    /// <summary>Returns the id of the "Shielded" agent type, creating it in the Navigation settings if needed.</summary>
    public static int EnsureAgentType()
    {
        for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
        {
            NavMeshBuildSettings s = NavMesh.GetSettingsByIndex(i);
            if (NavMesh.GetSettingsNameFromID(s.agentTypeID) == AgentTypeName) return s.agentTypeID;
        }

        NavMeshBuildSettings created = NavMesh.CreateSettings();
        SerializedObject so = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("NavMeshProjectSettings"));
        SerializedProperty settings = so.FindProperty("m_Settings");
        SerializedProperty names = so.FindProperty("m_SettingNames");
        for (int i = 0; i < settings.arraySize; i++)
        {
            SerializedProperty s = settings.GetArrayElementAtIndex(i);
            if (s.FindPropertyRelative("agentTypeID").intValue != created.agentTypeID) continue;
            s.FindPropertyRelative("agentRadius").floatValue = AgentRadius;
            s.FindPropertyRelative("agentHeight").floatValue = AgentHeight;
            s.FindPropertyRelative("agentSlope").floatValue = 45f;
            s.FindPropertyRelative("agentClimb").floatValue = 0.4f;
            names.GetArrayElementAtIndex(i).stringValue = AgentTypeName;
        }
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        Debug.Log($"[FinalShift] Created NavMesh agent type '{AgentTypeName}' (id {created.agentTypeID}, radius {AgentRadius}).");
        return created.agentTypeID;
    }

    private static void AssignToPrefabs(int id)
    {
        foreach (string path in ShieldPrefabs)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                SetAgent(contents, id);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                Debug.Log($"[FinalShift] {path} now uses the '{AgentTypeName}' agent type.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
    }

    /// <summary>Puts a robot on the Shielded agent type.</summary>
    public static void SetAgent(GameObject robot, int id)
    {
        NavMeshAgent agent = robot.GetComponent<NavMeshAgent>();
        if (agent == null) return;
        agent.agentTypeID = id;
        agent.radius = AgentRadius;
        agent.height = AgentHeight;
    }

    /// <summary>
    /// Gives every normal NavMeshSurface in the scene a "Shielded" twin on the same object (same settings) and bakes
    /// the twin (and the normal one too if asked). Marks the scene dirty; it is not saved here.
    /// </summary>
    public static void BakeScene(Scene scene, int id, bool bakeNormalToo = false)
    {
        if (!scene.isLoaded) return;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (NavMeshSurface surface in root.GetComponentsInChildren<NavMeshSurface>(true))
            {
                if (surface.agentTypeID == id) continue;

                NavMeshSurface twin = null;
                foreach (NavMeshSurface s in surface.GetComponents<NavMeshSurface>())
                    if (s.agentTypeID == id) twin = s;
                if (twin == null)
                {
                    twin = Undo.AddComponent<NavMeshSurface>(surface.gameObject);
                    twin.agentTypeID = id;
                    twin.collectObjects = surface.collectObjects;
                    twin.useGeometry = surface.useGeometry;
                    twin.layerMask = surface.layerMask;
                    twin.defaultArea = surface.defaultArea;
                    twin.size = surface.size;
                    twin.center = surface.center;
                }

                if (bakeNormalToo) Bake(surface, scene, "");
                Bake(twin, scene, "-" + AgentTypeName);
            }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[FinalShift] Baked NavMesh surfaces (normal + {AgentTypeName}) in '{scene.name}'. Save the scene to keep them.");
    }

    private static void Bake(NavMeshSurface surface, Scene scene, string suffix)
    {
        if (surface.navMeshData != null)
        {
            string oldPath = AssetDatabase.GetAssetPath(surface.navMeshData);
            surface.RemoveData();
            surface.navMeshData = null;
            if (!string.IsNullOrEmpty(oldPath)) AssetDatabase.DeleteAsset(oldPath);
        }

        surface.BuildNavMesh();

        string parent = Path.GetDirectoryName(scene.path).Replace('\\', '/');
        string folder = parent + "/" + scene.name;
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(parent, scene.name);
        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/NavMesh-{surface.gameObject.name}{suffix}.asset");
        AssetDatabase.CreateAsset(surface.navMeshData, assetPath);
        EditorUtility.SetDirty(surface);
    }
}

