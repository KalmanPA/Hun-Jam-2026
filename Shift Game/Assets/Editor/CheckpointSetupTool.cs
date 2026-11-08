using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the Checkpoint and ChunkLoader prefabs in Assets/Prefabs/Checkpoints.
/// They are built automatically the first time the editor loads this script (if they don't exist yet),
/// or from the menu: Final Shift > Checkpoints > ...
///
/// Checkpoint: trigger box + child "SpawnPoint" (where and which way the player respawns).
///   Fill in Segment (the segment to rebuild on respawn) and the Loaded / Unloaded Chunks lists.
/// ChunkLoader: trigger box with Turn Off / Turn On lists.
/// Both are invisible in game; they draw as coloured boxes in the Scene view.
/// </summary>
[InitializeOnLoad]
public static class CheckpointSetupTool
{
    private const string Folder = "Assets/Prefabs/Checkpoints";
    private const string CheckpointPath = Folder + "/Checkpoint.prefab";
    private const string ChunkLoaderPath = Folder + "/ChunkLoader.prefab";
    private const int IgnoreRaycastLayer = 2; // bullets and lasers fly straight through the triggers

    static CheckpointSetupTool()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists(CheckpointPath) && File.Exists(ChunkLoaderPath)) return;
            Build(false);
        };
    }

    [MenuItem("Final Shift/Checkpoints/Build Missing Prefabs")]
    public static void BuildMissing() => Build(false);

    [MenuItem("Final Shift/Checkpoints/Rebuild Prefabs (overwrites them)")]
    public static void Rebuild() => Build(true);

    private static void Build(bool overwrite)
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Prefabs", "Checkpoints");

        if (overwrite || !File.Exists(CheckpointPath)) BuildCheckpoint();
        if (overwrite || !File.Exists(ChunkLoaderPath)) BuildChunkLoader();

        AssetDatabase.SaveAssets();
        Debug.Log("[FinalShift] Checkpoint and ChunkLoader prefabs are in " + Folder);
    }

    private static void BuildCheckpoint()
    {
        GameObject root = new GameObject("Checkpoint");
        root.layer = IgnoreRaycastLayer;
        AddTrigger(root, new Vector3(4f, 3f, 1f));

        GameObject spawn = new GameObject("SpawnPoint");
        spawn.layer = IgnoreRaycastLayer;
        spawn.transform.SetParent(root.transform, false);
        spawn.transform.localPosition = new Vector3(0f, 0f, 1.5f);

        Checkpoint cp = root.AddComponent<Checkpoint>();
        SerializedObject so = new SerializedObject(cp);
        so.FindProperty("spawnPoint").objectReferenceValue = spawn.transform;
        so.ApplyModifiedPropertiesWithoutUndo();

        Save(root, CheckpointPath);
    }

    private static void BuildChunkLoader()
    {
        GameObject root = new GameObject("ChunkLoader");
        root.layer = IgnoreRaycastLayer;
        AddTrigger(root, new Vector3(4f, 3f, 1f));
        root.AddComponent<ChunkLoader>();
        Save(root, ChunkLoaderPath);
    }

    private static void AddTrigger(GameObject go, Vector3 size)
    {
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = size;
        box.center = new Vector3(0f, size.y * 0.5f, 0f);

        // A kinematic body makes sure the trigger always reports the player's CharacterController.
        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private static void Save(GameObject root, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
    }
}
