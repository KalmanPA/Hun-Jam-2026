using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Replaces the placeholder shapes of Assets/Prefabs/GearWeapon.prefab with the pieces of Assets/Art/Gun.fbx:
///
///   Model/Body/Visual        Gun body.004B + the side switches (Cube.001-.004)
///   Model/Indicators/Visual  GunDisplay (the screen; drawn by GunDisplayScreen)
///   Model/Gate               at the shifter gate cut into the body (5 slots, measured from the model)
///   Model/Gate/Stick/Visual  Shifter
///   Model/Barrels/Barrel_*   First = Sniper, Second = Regular, Third = Minigun, Fourth = Rocketjump,
///                            Fifth = the gold trumpet ("Reverse" in the FBX): it swings out from under the gun,
///                            hinged at its foot on the barrel ring. The 5th gate slot is the 5th gear (no reverse).
///
/// The FBX is unpacked into the prefab, so meshes still come from Gun.fbx (re-exporting it updates the shapes;
/// moved/renamed parts need this tool again). Materials are remapped in the FBX import settings the same way
/// they were set up on the Gun in OLITEST: Fém = rust_coarse_01, rosewood = Wood, Arany = Goldbrass,
/// GunDisplay = GunDisplay_Screen (the pixel display).
///
/// Menu: Final Shift > Weapon > Use Gun Model In GearWeapon Prefab (safe to run again).
/// </summary>
[InitializeOnLoad]
public static class GunModelSetupTool
{
    private const string WeaponPrefabPath = "Assets/Prefabs/GearWeapon.prefab";
    private const string GunModelPath = "Assets/Art/Gun.fbx";
    private const string DisplayBasePath = "Assets/Art/Textures/GunDisplay_Base.png";
    private const string ScreenMaterialPath = "Assets/Art/Materials/GunDisplay_Screen.mat";
    private const string TriggerFile = "Temp/FinalShift_GunModel";
    private const string ReportFile = "Temp/FinalShift_GunModel_report.txt";

    // ---- Measured on Gun.fbx (model space, metres) ----
    private const float GateY = 0.0713f;          // bottom of the gate groove (where the Shifter stands)
    private const float GateCenterZ = -0.0548f;   // middle slot (column 0)
    private const float SlotSpacing = 0.0329f;    // between slots along the barrel
    private const float SlotLength = 0.0092f;     // how far the stick moves sideways into a slot
    /// <summary>Moves the whole model so the gate + display sit where the placeholder's did (keeps the poses).</summary>
    private static readonly Vector3 ModelOffset = new Vector3(0f, -0.022f, 0.037f);

    private static readonly (Gear gear, string mesh)[] BarrelMap =
    {
        (Gear.First, "Sniper"), (Gear.Second, "Regular"), (Gear.Third, "Minigun"), (Gear.Fourth, "Rocketjump"), (Gear.Fifth, "Reverse"),
    };
    /// <summary>Pieces that swing into place instead of sliding (degrees they start away from their pose).</summary>
    private static readonly Dictionary<Gear, float> SwingAngles = new Dictionary<Gear, float> { { Gear.Fifth, 180f } };
    private const float ModelScaleInHands = 2f;
    /// <summary>Barrels that spin: axis (model space X, Y; along Z) and top speed in degrees per second.
    /// The minigun's axis is the centre of its three barrels (measured from renders rotated 0/60/120 degrees).</summary>
    private static readonly Dictionary<Gear, (Vector2 axis, float speed)> Spinners = new Dictionary<Gear, (Vector2, float)>
    {
        { Gear.Third, (new Vector2(-0.0015f, 0.0473f), 1800f) },
    };
    private static readonly string[] BodyParts = { "Gun body.004B", "Cube.001", "Cube.002", "Cube.003", "Cube.004" };
    private const string ShifterName = "Shifter";
    private const string DisplayName = "GunDisplay";

    private static readonly Dictionary<string, string> MaterialRemap = new Dictionary<string, string>
    {
        { "Fém", "Assets/Art/Materials/rust_coarse_01.mat" },
        { "rosewood_veneer1", "Assets/Art/Materials/Wood.mat" },
        { "Arany", "Assets/ProtMaterials/Goldbrass.mat" },
        { "GunDisplay", ScreenMaterialPath },
    };

    static GunModelSetupTool()
    {
        EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            if (File.Exists(TriggerFile)) { File.Delete(TriggerFile); Run(); }
        };
    }

    [MenuItem("Final Shift/Weapon/Use Gun Model In GearWeapon Prefab")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        try
        {
            SetupDisplayTexture(log);
            Material screenMat = SetupScreenMaterial(log);
            RemapMaterials(log);
            BuildPrefab(screenMat, log);
        }
        catch (System.Exception e)
        {
            log.AppendLine("ERROR " + e);
        }
        File.WriteAllText(ReportFile, log.ToString());
        Debug.Log("[FinalShift] Gun model:\n" + log);
    }

    // ------------------------------------------------------------------ assets

    private static void SetupDisplayTexture(System.Text.StringBuilder log)
    {
        var ti = AssetImporter.GetAtPath(DisplayBasePath) as TextureImporter;
        if (ti == null) throw new System.Exception(DisplayBasePath + " missing");
        ti.textureType = TextureImporterType.Default;
        ti.isReadable = true;
        ti.mipmapEnabled = false;
        ti.filterMode = FilterMode.Point;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.alphaIsTransparency = false;
        ti.SaveAndReimport();
        log.AppendLine("display texture: readable, point, uncompressed");
    }

    private static Material SetupScreenMaterial(System.Text.StringBuilder log)
    {
        Texture2D baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(DisplayBasePath);
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(ScreenMaterialPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "GunDisplay_Screen" };
            AssetDatabase.CreateAsset(mat, ScreenMaterialPath);
        }
        mat.SetTexture("_BaseMap", baseTex);
        mat.SetColor("_BaseColor", Color.white);

        // The screen's UVs only cover part of 0..1 in the FBX: stretch the display over exactly that area.
        Rect uv = ScreenUvRect();
        Vector2 tiling = new Vector2(1f / uv.width, 1f / uv.height);
        Vector2 offset = new Vector2(-uv.xMin / uv.width, -uv.yMin / uv.height);
        mat.SetTextureScale("_BaseMap", tiling);
        mat.SetTextureOffset("_BaseMap", offset);
        EditorUtility.SetDirty(mat);
        log.AppendLine("screen UV rect " + uv.ToString("F4") + " -> tiling " + tiling.ToString("F4") + " offset " + offset.ToString("F4"));
        return mat;
    }

    private static Rect ScreenUvRect()
    {
        Mesh mesh = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(GunModelPath))
            if (o is Mesh m && m.name == DisplayName) mesh = m;
        if (mesh == null) throw new System.Exception("GunDisplay mesh not found in Gun.fbx");
        var uvs = new List<Vector2>();
        mesh.GetUVs(0, uvs);
        int[] tris = mesh.GetTriangles(1);
        float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f;
        foreach (int i in tris)
        {
            Vector2 u = uvs[i];
            x0 = Mathf.Min(x0, u.x); y0 = Mathf.Min(y0, u.y);
            x1 = Mathf.Max(x1, u.x); y1 = Mathf.Max(y1, u.y);
        }
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    private static void RemapMaterials(System.Text.StringBuilder log)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(GunModelPath);
        foreach (var kv in MaterialRemap)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(kv.Value);
            if (m == null) { log.AppendLine("material missing: " + kv.Value); continue; }
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), m);
        }
        importer.SaveAndReimport();
        log.AppendLine("Gun.fbx materials remapped");
    }

    // ------------------------------------------------------------------ prefab

    private static void BuildPrefab(Material screenMat, System.Text.StringBuilder log)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(WeaponPrefabPath);
        try
        {
            var weapon = root.GetComponent<GearShiftWeapon>();
            Transform oldModel = root.transform.Find("Model");
            if (oldModel != null) Object.DestroyImmediate(oldModel.gameObject);

            // Unpacked copy of the FBX to take the parts from.
            var gun = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(GunModelPath));
            PrefabUtility.UnpackPrefabInstance(gun, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            gun.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            gun.transform.localScale = Vector3.one;

            Transform model = Piece("Model", root.transform, ModelOffset, Quaternion.identity);

            // Body
            Transform bodyVisual = Piece("Visual", Piece("Body", model, Vector3.zero, Quaternion.identity), Vector3.zero, Quaternion.identity);
            foreach (string n in BodyParts) Move(gun, n, bodyVisual, log);

            // Display
            Transform indVisual = Piece("Visual", Piece("Indicators", model, Vector3.zero, Quaternion.identity), Vector3.zero, Quaternion.identity);
            Transform display = Move(gun, DisplayName, indVisual, log);
            if (display != null)
            {
                var screen = display.gameObject.AddComponent<GunDisplayScreen>();
                var so = new SerializedObject(screen);
                so.FindProperty("baseTexture").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(DisplayBasePath);
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // Gate + stick. Gate space: +X towards the stock, +Z towards the right side (same as the placeholder).
            Transform gate = Piece("Gate", model, new Vector3(0f, GateY, GateCenterZ), Quaternion.Euler(0f, 90f, 0f));
            Piece("Visual", gate, Vector3.zero, Quaternion.identity); // the gate itself is cut into the body
            Transform stick = Piece("Stick", gate, Vector3.zero, Quaternion.identity);
            Transform stickVisual = Piece("Visual", stick, Vector3.zero, Quaternion.identity);
            Transform shifter = Move(gun, ShifterName, stickVisual, log);
            if (shifter != null)
            {
                shifter.localPosition = Vector3.zero;
                shifter.localRotation = Quaternion.identity;
            }

            // Barrels: they stay where they are in the model; the script slides each one back along -Z by its
            // length when it is not engaged (and hides it once it is fully in).
            float bodyFront = BoundsIn(model, bodyVisual).max.z;
            Transform barrels = Piece("Barrels", model, Vector3.zero, Quaternion.identity);
            var lengths = new Dictionary<Gear, float>();
            foreach (var (gear, mesh) in BarrelMap)
            {
                // Swinging pieces are hinged at the mesh's own origin (the trumpet's foot); sliding ones at the origin.
                Vector3 hinge = Vector3.zero;
                Transform src = gun.transform.Find(mesh);
                if (SwingAngles.ContainsKey(gear) && src != null) hinge = src.localPosition;
                Transform pivot = Piece("Barrel_" + gear, barrels, hinge, Quaternion.identity);
                Transform visual = Piece("Visual", pivot, -hinge, Quaternion.identity);
                Transform holder = visual;
                if (Spinners.TryGetValue(gear, out var spin))
                {
                    Vector3 local = new Vector3(spin.axis.x, spin.axis.y, 0f) - ModelSpaceOffset(visual);
                    holder = Piece("Spin", visual, local, Quaternion.identity);
                }
                Transform part = Move(gun, mesh, holder, log);
                if (part == null) continue;
                Bounds b = BoundsIn(model, visual);
                float length = Mathf.Max(0.05f, b.max.z - bodyFront + 0.01f);
                lengths[gear] = length;
                Transform muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(visual, false);
                muzzle.localPosition = new Vector3(b.center.x, b.center.y, b.max.z + 0.01f);
                if (Spinners.TryGetValue(gear, out var sAxis))  // shots leave from the spin axis, not the bounds centre
                    muzzle.localPosition = new Vector3(sAxis.axis.x, sAxis.axis.y, b.max.z + 0.01f) - ModelSpaceOffset(visual);
                log.AppendLine("barrel " + gear + " = " + mesh + ", sticks out " + length.ToString("F3") + " m, muzzle " + muzzle.localPosition.ToString("F3"));
            }

            Object.DestroyImmediate(gun);
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Weapon settings measured on the model.
            var wso = new SerializedObject(weapon);
            wso.FindProperty("slotSpacing").floatValue = SlotSpacing;
            wso.FindProperty("slotLength").floatValue = SlotLength;
            wso.FindProperty("modelScale").floatValue = ModelScaleInHands;

            // The 5th gate slot is the 5th gear (it used to be reverse).
            SerializedProperty sl = wso.FindProperty("slots");
            for (int i = 0; i < sl.arraySize; i++)
            {
                SerializedProperty g = sl.GetArrayElementAtIndex(i).FindPropertyRelative("gear");
                if (g.intValue == (int)Gear.Reverse) g.intValue = (int)Gear.Fifth;
            }

            // One barrel entry per gear in BarrelMap, with the measured lengths.
            SerializedProperty bl = wso.FindProperty("barrels");
            bl.ClearArray();
            foreach (var (gear, _) in BarrelMap)
            {
                bl.InsertArrayElementAtIndex(bl.arraySize);
                SerializedProperty e = bl.GetArrayElementAtIndex(bl.arraySize - 1);
                e.FindPropertyRelative("gear").intValue = (int)gear;
                e.FindPropertyRelative("length").floatValue = lengths.TryGetValue(gear, out float len) ? len : 0.1f;
                e.FindPropertyRelative("swingAngle").floatValue = SwingAngles.TryGetValue(gear, out float a) ? a : 0f;
                e.FindPropertyRelative("spinSpeed").floatValue = Spinners.TryGetValue(gear, out var sp) ? sp.speed : 0f;
            }
            wso.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, WeaponPrefabPath);
            log.AppendLine("saved " + WeaponPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Transform Piece(string name, Transform parent, Vector3 pos, Quaternion rot)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        t.localRotation = rot;
        return t;
    }

    /// <summary>Moves a direct child of the unpacked FBX under <paramref name="parent"/>, keeping its pose relative
    /// to the model origin (parent chains here are all unrotated, so local = model space minus the parent's offset).</summary>
    private static Transform Move(GameObject gun, string name, Transform parent, System.Text.StringBuilder log)
    {
        Transform t = gun.transform.Find(name);
        if (t == null) { log.AppendLine("MISSING part '" + name + "' in Gun.fbx"); return null; }
        Vector3 p = t.localPosition; Quaternion r = t.localRotation; Vector3 s = t.localScale;
        t.SetParent(parent, false);
        t.localPosition = p - ModelSpaceOffset(parent);
        t.localRotation = r;
        t.localScale = s;
        return t;
    }

    /// <summary>Position of <paramref name="t"/> in model space (sum of local positions up to "Model").</summary>
    private static Vector3 ModelSpaceOffset(Transform t)
    {
        Vector3 o = Vector3.zero;
        while (t != null && t.name != "Model") { o += t.localPosition; t = t.parent; }
        return o;
    }

    private static Bounds BoundsIn(Transform space, Transform root)
    {
        bool any = false;
        Bounds result = new Bounds();
        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Matrix4x4 m = space.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Bounds lb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = m.MultiplyPoint3x4(c);
                if (!any) { result = new Bounds(p, Vector3.zero); any = true; } else result.Encapsulate(p);
            }
        }
        return result;
    }
}
