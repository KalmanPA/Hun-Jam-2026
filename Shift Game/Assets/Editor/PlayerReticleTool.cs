using UnityEditor;
using UnityEngine;

/// <summary>
/// Puts the red marker X aim reticle (the one the glasses cutscene draws) into the Player prefab, under the camera,
/// switched off, plus a PlayerReticle component on the player root that switches it on in every scene without the
/// glasses cutscene (BossFight, ElevatorRoom, test scenes). In the Game scene the cutscene still reveals it.
///
/// Runs once by itself after it compiles, and from Final Shift > Player > Add Reticle To Player Prefab.
/// </summary>
[InitializeOnLoad]
public static class PlayerReticleTool
{
    private const string PlayerPath = "Assets/Prefabs/Player.prefab";
    private const string StrokeAPath = "Assets/ProtMaterials/Intro/Reticle_StrokeA.mat";
    private const string StrokeBPath = "Assets/ProtMaterials/Intro/Reticle_StrokeB.mat";
    private const string ReticleName = "AimReticle";

    // Same as the GlassesCutscene defaults.
    private static readonly Color MarkerColor = new Color(0.95f, 0.06f, 0.04f, 1f);
    private const float HudDistance = 0.3f;
    private const float HudSize = 0.012f;

    private const int AutoRunVersion = 1;
    private const string AutoRunPrefKey = "FinalShift.PlayerReticle.AutoRunVersion";

    static PlayerReticleTool()
    {
        if (EditorPrefs.GetInt(AutoRunPrefKey, 0) >= AutoRunVersion) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorPrefs.SetInt(AutoRunPrefKey, AutoRunVersion);
            Build();
        };
    }

    [MenuItem("Final Shift/Player/Add Reticle To Player Prefab")]
    public static void Build()
    {
        Material a = AssetDatabase.LoadAssetAtPath<Material>(StrokeAPath);
        Material b = AssetDatabase.LoadAssetAtPath<Material>(StrokeBPath);
        if (a == null || b == null)
        {
            Debug.LogError("[FinalShift] Player reticle: stroke materials not found (" + StrokeAPath + "). Run Final Shift > Intro Cutscene first.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            Camera cam = root.GetComponentInChildren<Camera>(true);
            if (cam == null)
            {
                Debug.LogError("[FinalShift] Player reticle: no camera in " + PlayerPath);
                return;
            }

            // Replace an older one.
            foreach (AimReticle old in root.GetComponentsInChildren<AimReticle>(true))
                Object.DestroyImmediate(old.gameObject);

            int layer = WeaponLayer(root);
            GameObject go = new GameObject(ReticleName);
            go.layer = layer;
            Transform t = go.transform;
            t.SetParent(cam.transform, false);
            t.localPosition = new Vector3(0f, 0f, HudDistance);
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one * HudSize;

            AimReticle reticle = go.AddComponent<AimReticle>();
            SerializedObject so = new SerializedObject(reticle);
            so.FindProperty("strokeA").objectReferenceValue = Stroke("StrokeA", t, a, layer);
            so.FindProperty("strokeB").objectReferenceValue = Stroke("StrokeB", t, b, layer);
            so.FindProperty("colour").colorValue = MarkerColor;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(false);

            PlayerReticle switcher = root.GetComponent<PlayerReticle>();
            if (switcher == null) switcher = root.AddComponent<PlayerReticle>();
            SerializedObject sw = new SerializedObject(switcher);
            sw.FindProperty("reticle").objectReferenceValue = reticle;
            sw.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            Debug.Log("[FinalShift] Player reticle: added the marker X (switched off) under " + cam.name + " in " + PlayerPath + ", layer " + layer + ".");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Renderer Stroke(string name, Transform parent, Material mat, int layer)
    {
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = name;
        q.layer = layer;
        Object.DestroyImmediate(q.GetComponent<Collider>());
        q.transform.SetParent(parent, false);
        MeshRenderer r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return r;
    }

    /// <summary>The layer the weapon is drawn on (its overlay camera), same as the cutscene uses for the X.</summary>
    private static int WeaponLayer(GameObject root)
    {
        GearShiftWeapon weapon = root.GetComponentInChildren<GearShiftWeapon>(true);
        if (weapon == null) return 0;
        SerializedObject so = new SerializedObject(weapon);
        SerializedProperty useCam = so.FindProperty("useWeaponCamera");
        SerializedProperty layer = so.FindProperty("weaponLayer");
        if (useCam != null && useCam.boolValue && layer != null) return layer.intValue;
        return weapon.gameObject.layer;
    }
}
