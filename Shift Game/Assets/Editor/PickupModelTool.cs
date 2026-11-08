using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Puts the art models (Assets/Art/Healthpack.fbx, Assets/Art/Ammobox.fbx) into the HealthPickup and
/// AmmoPickup prefabs in place of the placeholder cubes. The model is added as a nested prefab instance
/// (re-exporting the FBX updates the pickups), centred on the pickup's pivot and scaled so its longest side is
/// <see cref="TargetSize"/>. Colliders on the model are removed: pickups are collected by distance.
///
/// Menu: Final Shift > Pickups > Use Art Models For Pickups (safe to run again).
/// RobotSetupTool uses <see cref="ApplyModel"/> too, so rebuilding the pickup prefabs keeps the models.
/// </summary>
[InitializeOnLoad]
public static class PickupModelTool
{
    public const string HealthModelPath = "Assets/Art/Healthpack.fbx";
    public const string AmmoModelPath = "Assets/Art/Ammobox.fbx";
    private const string HealthPrefabPath = "Assets/Prefabs/HealthPickup.prefab";
    private const string AmmoPrefabPath = "Assets/Prefabs/AmmoPickup.prefab";
    private const string ModelChildName = "Model";
    private const string TriggerFile = "Temp/FinalShift_PickupModels";

    /// <summary>Longest side of the model in metres (the old placeholder cube was 0.4).</summary>
    public const float TargetSize = 0.55f;
    public static string LastReport = "";

    static PickupModelTool()
    {
        EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            if (File.Exists(TriggerFile)) { File.Delete(TriggerFile); ApplyToPrefabs(); }
        };
    }

    [MenuItem("Final Shift/Pickups/Use Art Models For Pickups")]
    public static void ApplyToPrefabs()
    {
        LastReport = "";
        Apply(HealthPrefabPath, Pickup.Kind.Health);
        Apply(AmmoPrefabPath, Pickup.Kind.Ammo);
        AssetDatabase.SaveAssets();
        File.WriteAllText("Temp/FinalShift_PickupModels_report.txt", LastReport);
        Debug.Log("[FinalShift] Pickup models:\n" + LastReport);
    }

    private static void Apply(string prefabPath, Pickup.Kind kind)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
        {
            Debug.LogWarning("[FinalShift] Pickup models: " + prefabPath + " not found.");
            return;
        }
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            if (ApplyModel(root, kind))
            {
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log("[FinalShift] Pickup models: " + prefabPath + " now uses " + ModelPath(kind) + ".");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    public static string ModelPath(Pickup.Kind kind) => kind == Pickup.Kind.Health ? HealthModelPath : AmmoModelPath;

    /// <summary>Replaces the visuals under <paramref name="root"/> (placeholder cube or an older model)
    /// with the art model. Returns false if the model asset is missing.</summary>
    public static bool ApplyModel(GameObject root, Pickup.Kind kind)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(kind));
        if (model == null)
        {
            Debug.LogWarning("[FinalShift] Pickup models: " + ModelPath(kind) + " not found.");
            return false;
        }

        for (int i = root.transform.childCount - 1; i >= 0; i--)
        {
            Transform c = root.transform.GetChild(i);
            if (c.name == "Cube" || c.name == ModelChildName) Object.DestroyImmediate(c.gameObject);
        }

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        inst.name = ModelChildName;
        inst.transform.SetParent(root.transform, false);
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localRotation = Quaternion.identity;
        inst.transform.localScale = Vector3.one;

        foreach (Collider col in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);

        // Fit: longest side = TargetSize, bounds centred on the pickup pivot (it spins and bobs around it).
        Bounds b = LocalBounds(root.transform, inst);
        float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (longest > 1e-5f)
        {
            float s = TargetSize / longest;
            inst.transform.localScale = Vector3.one * s;
            b = LocalBounds(root.transform, inst);
            inst.transform.localPosition = -b.center;
            b = LocalBounds(root.transform, inst);
        }
        LastReport += kind + ": raw longest side " + longest.ToString("0.###") + " m, now " + b.size.ToString("F3") +
                      " centred at " + b.center.ToString("F3") + ", renderers " + inst.GetComponentsInChildren<Renderer>(true).Length + "\n";
        return true;
    }

    private static Bounds LocalBounds(Transform space, GameObject go)
    {
        bool any = false;
        Bounds result = new Bounds();
        foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
            Encapsulate(ref result, ref any, space, mf.transform, mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds());
        foreach (SkinnedMeshRenderer sm in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            Encapsulate(ref result, ref any, space, sm.transform, sm.sharedMesh != null ? sm.sharedMesh.bounds : sm.localBounds);
        return result;
    }

    private static void Encapsulate(ref Bounds result, ref bool any, Transform space, Transform t, Bounds local)
    {
        Matrix4x4 m = space.worldToLocalMatrix * t.localToWorldMatrix;
        Vector3 c = local.center, e = local.extents;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            Vector3 p = m.MultiplyPoint3x4(corner);
            if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
            else result.Encapsulate(p);
        }
    }
}
