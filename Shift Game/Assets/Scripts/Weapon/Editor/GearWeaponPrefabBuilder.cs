using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates Assets/Prefabs/GearWeapon.prefab with every piece of the gun as real objects (placeholder shapes),
/// saves their materials as assets, and puts the prefab on the Player (under PlayerCamera).
/// Runs once by itself if the prefab doesn't exist yet. Run it again from the menu: Final Shift > Rebuild Gear Weapon Prefab.
/// </summary>
[InitializeOnLoad]
public static class GearWeaponPrefabBuilder
{
    private const string WeaponPrefabPath = "Assets/Prefabs/GearWeapon.prefab";
    private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
    private const string MaterialFolder = "Assets/ProtMaterials/Weapon";

    static GearWeaponPrefabBuilder()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists(WeaponPrefabPath)) return;
            Build();
        };
    }

    [MenuItem("Final Shift/Rebuild Gear Weapon Prefab")]
    private static void RebuildFromMenu()
    {
        if (File.Exists(WeaponPrefabPath) &&
            !EditorUtility.DisplayDialog("Rebuild Gear Weapon Prefab",
                "This replaces GearWeapon.prefab with fresh placeholder shapes. Any models you swapped in will be lost.",
                "Rebuild", "Cancel"))
            return;
        Build();
    }

    private static void Build()
    {
        // 1. Build the weapon with placeholder shapes.
        GameObject root = new GameObject("GearWeapon");
        GearShiftWeapon weapon = root.AddComponent<GearShiftWeapon>();
        weapon.BuildPlaceholderPieces();

        // 2. Save the placeholder materials as assets so the prefab keeps them.
        if (!AssetDatabase.IsValidFolder("Assets/ProtMaterials")) AssetDatabase.CreateFolder("Assets", "ProtMaterials");
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/ProtMaterials", "Weapon");

        var saved = new Dictionary<Material, Material>();
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            Material src = r.sharedMaterial;
            if (src == null) continue;
            if (!saved.TryGetValue(src, out Material asset))
            {
                string path = $"{MaterialFolder}/{src.name}.mat";
                asset = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (asset == null)
                {
                    asset = new Material(src) { name = src.name };
                    AssetDatabase.CreateAsset(asset, path);
                }
                else
                {
                    asset.CopyPropertiesFromMaterial(src);
                    EditorUtility.SetDirty(asset);
                }
                saved[src] = asset;
            }
            r.sharedMaterial = asset;
        }

        // 3. Save the weapon prefab.
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, WeaponPrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();

        // 4. Put it on the Player, replacing the old GearShiftWeapon child.
        GameObject player = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            Camera cam = player.GetComponentInChildren<Camera>(true);
            Transform parent = cam != null ? cam.transform : player.transform;

            foreach (GearShiftWeapon old in player.GetComponentsInChildren<GearShiftWeapon>(true))
                Object.DestroyImmediate(old.gameObject);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.transform.localPosition = new Vector3(0.24f, -0.26f, 0.42f);
            instance.transform.localRotation = Quaternion.Euler(0f, -2f, 0f);

            PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(player);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("Final Shift: built " + WeaponPrefabPath + " and added it to the Player prefab. Open it to swap the models inside each Visual object.", prefab);
    }
}
