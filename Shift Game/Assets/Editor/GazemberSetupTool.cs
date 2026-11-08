using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds Assets/Prefabs/RobotGazember.prefab, a variant of Robot.prefab:
///  - two guns: the model's gun forearm (left arm) is copied, and a mirrored copy replaces the plain right forearm,
///  - both forearms kick back with recoil when their gun fires (shots alternate left/right),
///  - double the normal robot's health,
///  - its own voice line (SoundId.RobotGazember) when it spots the player and now and then while fighting.
/// Then swaps the "RobotGazember" robot in the ElevatorRoom scene over to the new prefab (keeping where it stands).
/// Menu: Final Shift > Enemy Variants > Build Gazember. Also runs once when Temp/FinalShift_BuildGazember exists.
/// </summary>
[InitializeOnLoad]
public static class GazemberSetupTool
{
    private const string BasePath = "Assets/Prefabs/Robot.prefab";
    private const string VariantPath = "Assets/Prefabs/RobotGazember.prefab";
    private const string ScenePath = "Assets/Scenes/ElevatorRoom.unity";
    private const string SceneObjectName = "RobotGazember";
    private const string TriggerFile = "Temp/FinalShift_BuildGazember";

    // Left arm of the model: the forearm that is the gun (moves with recoil), and its elbow joint.
    private static readonly string[] GunForearm = { "Cube.016", "ShootMuzzle", "Cylinder.011", "Cylinder.010" };
    // Right arm of the model: the plain forearm and elbow, replaced by the mirrored gun forearm.
    private static readonly string[] PlainForearm = { "Cube.011", "Cube.012", "Cylinder.007" };
    // The model is symmetric around this x (robot space): left arm at -0.703, right arm at 0.783.
    private const float MirrorX = 0.04f;

    static GazemberSetupTool()
    {
        if (!File.Exists(TriggerFile)) return;
        File.Delete(TriggerFile);
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Build();
        };
    }

    [MenuItem("Final Shift/Enemy Variants/Build Gazember (twin guns)")]
    public static void Build()
    {
        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePath);
        if (basePrefab == null) { Debug.LogWarning("[FinalShift] Gazember: " + BasePath + " not found."); return; }

        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject robot = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab, preview);
            robot.name = "RobotGazember";
            if (!SetUp(robot)) return;
            PrefabUtility.SaveAsPrefabAsset(robot, VariantPath);
            Debug.Log("[FinalShift] Saved " + VariantPath + " (twin guns with recoil, double health, Gazember voice).");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }

        SwapInScene();
    }

    private static bool SetUp(GameObject robot)
    {
        Transform root = robot.transform;
        Dictionary<string, Transform> parts = new Dictionary<string, Transform>();
        foreach (Transform t in robot.GetComponentsInChildren<Transform>(true))
            if (!parts.ContainsKey(t.name)) parts[t.name] = t;

        foreach (string n in GunForearm)
            if (!parts.ContainsKey(n)) { Debug.LogWarning("[FinalShift] Gazember: model part " + n + " not found, nothing built."); return false; }

        // Left: a copy of the gun forearm in its own group (the group is what recoils).
        Transform left = NewGroup("GunArmLeft", root);
        // Right: the same copy, mirrored across the robot's middle.
        Transform mirror = NewGroup("RightSideMirror", root);
        mirror.localPosition = new Vector3(MirrorX * 2f, 0f, 0f);
        mirror.localScale = new Vector3(-1f, 1f, 1f);
        Transform right = NewGroup("GunArmRight", mirror);

        Transform leftMuzzle = null, rightMuzzle = null;
        Light leftFlash = null;
        foreach (string n in GunForearm)
        {
            Transform original = parts[n];
            Transform l = CopyInto(original, root, left);
            Transform r = CopyInto(original, root, right);
            if (n == "ShootMuzzle") { leftMuzzle = l; rightMuzzle = r; }
            original.gameObject.SetActive(false);
            if (l.GetComponentInChildren<Light>(true) != null) leftFlash = l.GetComponentInChildren<Light>(true);
            foreach (Light extra in r.GetComponentsInChildren<Light>(true)) extra.enabled = false; // one flash light is enough
        }
        foreach (string n in PlainForearm)
            if (parts.TryGetValue(n, out Transform plain)) plain.gameObject.SetActive(false);

        // The recoil pivots at the elbow: put each group's origin there so the tip-up turns around the elbow.
        PivotAt(left, parts["Cylinder.010"].position);
        PivotAt(right, MirrorPoint(root, parts["Cylinder.010"].position));

        RobotEnemy enemy = robot.GetComponent<RobotEnemy>();
        SerializedObject so = new SerializedObject(enemy);
        SerializedProperty hp = so.FindProperty("maxHealth");
        hp.floatValue *= 2f;
        SerializedProperty muzzles = so.FindProperty("muzzles");
        muzzles.arraySize = 2;
        muzzles.GetArrayElementAtIndex(0).objectReferenceValue = leftMuzzle;
        muzzles.GetArrayElementAtIndex(1).objectReferenceValue = rightMuzzle;
        SerializedProperty recoil = so.FindProperty("recoilParts");
        recoil.arraySize = 2;
        recoil.GetArrayElementAtIndex(0).objectReferenceValue = left;
        recoil.GetArrayElementAtIndex(1).objectReferenceValue = right;
        if (leftFlash != null) so.FindProperty("muzzleFlash").objectReferenceValue = leftFlash;
        so.FindProperty("bodyRenderers").arraySize = 0; // collected on Awake, so the copied arms flash when hit too
        so.FindProperty("personalVoice").intValue = (int)SoundId.RobotGazember;
        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    private static Transform NewGroup(string name, Transform parent)
    {
        Transform g = new GameObject(name).transform;
        g.SetParent(parent, false);
        return g;
    }

    /// <summary>
    /// Copies a model part into a group with the same pose relative to the robot root as the original. Both groups
    /// start at the root's origin with no rotation, so for the mirrored group the same numbers land on the other side.
    /// </summary>
    private static Transform CopyInto(Transform original, Transform root, Transform group)
    {
        // Pose of the original in root space, split into position / rotation / scale (keeping negative scales).
        Matrix4x4 m = root.worldToLocalMatrix * original.localToWorldMatrix;
        bool flipped = m.determinant < 0f;
        if (flipped) m *= Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
        Vector3 position = m.GetColumn(3);
        Quaternion rotation = m.rotation;
        Vector3 scale = m.lossyScale;
        if (flipped) scale.x = -scale.x;

        GameObject copy = Object.Instantiate(original.gameObject, group, false);
        copy.name = original.name;
        copy.SetActive(true);
        Transform c = copy.transform;
        c.localPosition = position;
        c.localRotation = rotation;
        c.localScale = scale;
        return c;
    }

    private static Vector3 MirrorPoint(Transform root, Vector3 world)
    {
        Vector3 p = root.InverseTransformPoint(world);
        p.x = MirrorX * 2f - p.x;
        return root.TransformPoint(p);
    }

    /// <summary>Moves a group's origin to a world point without moving what's in it.</summary>
    private static void PivotAt(Transform group, Vector3 world)
    {
        Vector3 local = group.parent.InverseTransformPoint(world);
        Vector3 delta = local - group.localPosition;
        group.localPosition = local;
        foreach (Transform c in group)
        {
            Vector3 d = Vector3.Scale(delta, new Vector3(1f / group.localScale.x, 1f / group.localScale.y, 1f / group.localScale.z));
            c.localPosition -= Quaternion.Inverse(group.localRotation) * d;
        }
    }

    // ---------------------------------------------------------------- scene

    private static void SwapInScene()
    {
        GameObject variant = AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);
        if (variant == null) return;

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = false;
        if (!scene.IsValid() || !scene.isLoaded)
        {
            if (!File.Exists(ScenePath)) return;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            openedHere = true;
        }

        // Find them all first: replacing a prefab rebuilds the instance's objects.
        List<GameObject> targets = new List<GameObject>();
        foreach (GameObject go in scene.GetRootGameObjects())
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != SceneObjectName || !PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) continue;
                if (PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == variant) continue;
                targets.Add(t.gameObject);
            }
        int swapped = 0;
        foreach (GameObject target in targets)
        {
            if (target == null) continue;
            PrefabUtility.ReplacePrefabAssetOfPrefabInstance(target, variant, InteractionMode.AutomatedAction);
            swapped++;
        }

        if (swapped > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (openedHere) EditorSceneManager.SaveScene(scene);
        }
        if (openedHere) EditorSceneManager.CloseScene(scene, true);
        Debug.Log(swapped > 0
            ? $"[FinalShift] Gazember: swapped {swapped} robot(s) named {SceneObjectName} in {ScenePath} to {VariantPath}" +
              (openedHere ? " and saved the scene." : ". The scene is open: save it to keep the change.")
            : $"[FinalShift] Gazember: no robot named {SceneObjectName} to swap in {ScenePath} (or it already uses the variant).");
    }
}
