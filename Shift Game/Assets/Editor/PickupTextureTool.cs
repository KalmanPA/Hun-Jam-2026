using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Puts the Blender-baked textures on the pickup models.
///
/// For each model it creates (or updates) one URP Lit material with the baked texture as Base Map and
/// remaps the FBX's baked material(s) (name contains "bake"; all of them if none does) to it in the model's import settings (Model > Materials > Remapped
/// Materials). Every prefab using the FBX (HealthPickup, AmmoPickup) picks it up automatically.
///
/// Textures it looks for (just drop the baked image there and run the menu again):
///   Assets/Art/Textures/Ammobox_Baked.png      (extracted from Ammobox.fbx, AB_Final.png)
///   Assets/Art/Textures/Healthpack_Baked.png   (.png / .jpg / .tga also fine)
///
/// Menu: Final Shift > Pickups > Apply Baked Textures To Pickup Models
/// </summary>
[InitializeOnLoad]
public static class PickupTextureTool
{
    private const string TextureFolder = "Assets/Art/Textures";
    private const string MaterialFolder = "Assets/Art/Materials";
    private const string TriggerFile = "Temp/FinalShift_PickupTextures";
    private const string ReportFile = "Temp/FinalShift_PickupTextures_report.txt";

    private static readonly (string model, string name)[] Models =
    {
        ("Assets/Art/Ammobox.fbx", "Ammobox"),
        ("Assets/Art/Healthpack.fbx", "Healthpack"),
    };

    static PickupTextureTool()
    {
        EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            if (File.Exists(TriggerFile)) { File.Delete(TriggerFile); Apply(); }
        };
    }

    [MenuItem("Final Shift/Pickups/Apply Baked Textures To Pickup Models")]
    public static void Apply()
    {
        var report = new System.Text.StringBuilder();
        foreach (var (modelPath, name) in Models)
            report.AppendLine(ApplyOne(modelPath, name));
        AssetDatabase.SaveAssets();
        File.WriteAllText(ReportFile, report.ToString());
        Debug.Log("[FinalShift] Pickup textures:\n" + report);
    }

    private static string ApplyOne(string modelPath, string name)
    {
        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer == null) return name + ": model not found at " + modelPath;

        Texture2D tex = null;
        foreach (string ext in new[] { ".png", ".jpg", ".jpeg", ".tga" })
        {
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "/" + name + "_Baked" + ext);
            if (tex != null) break;
        }
        if (tex == null) return name + ": no baked texture yet (put it at " + TextureFolder + "/" + name + "_Baked.png)";

        // Baked colour map: sRGB, keep full resolution, good quality compression.
        var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex));
        if (ti.textureType != TextureImporterType.Default || !ti.sRGBTexture || ti.maxTextureSize < 2048)
        {
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.maxTextureSize = Mathf.Max(ti.maxTextureSize, 2048);
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }

        // One material for the whole model.
        string matPath = MaterialFolder + "/" + name + "_Baked.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(lit) { name = name + "_Baked" };
            AssetDatabase.CreateAsset(mat, matPath);
        }
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(mat);

        // Remap every material the FBX defines to it.
        var names = new List<string>();
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            if (o is Material m && !names.Contains(m.name)) names.Add(m.name);
        foreach (KeyValuePair<AssetImporter.SourceAssetIdentifier, Object> kv in importer.GetExternalObjectMap())
            if (kv.Key.type == typeof(Material) && !names.Contains(kv.Key.name)) names.Add(kv.Key.name);
        if (names.Count == 0) return name + ": the FBX has no materials to remap (assign " + matPath + " on the renderers by hand)";

        // Only the baked material(s) get the texture ("..._Baked"); plain ones (e.g. Blackmet) keep the colour
        // that came from Blender. If no material is named as baked, all of them are remapped.
        var baked = names.FindAll(n => n.ToLowerInvariant().Contains("bake"));
        if (baked.Count == 0) baked = names;
        foreach (string n in names)
        {
            var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), n);
            if (baked.Contains(n)) importer.AddRemap(id, mat);
            else importer.RemoveRemap(id);
        }
        importer.SaveAndReimport();
        var kept = names.FindAll(n => !baked.Contains(n));
        return name + ": " + tex.width + "x" + tex.height + " texture on " + matPath + ", baked: " + string.Join(", ", baked) +
               (kept.Count > 0 ? "; kept own material: " + string.Join(", ", kept) : "");
    }
}
