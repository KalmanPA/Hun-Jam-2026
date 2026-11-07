using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Turns the Game scene into a dark underground factory:
///  - near-black ambient, no sky, dark fog, the sun switched off,
///  - hanging factory lamps placed on a grid over every floor (warm sodium, some cold fluorescent, a few flickering),
///  - a flat-colour material on every ProBuilder object, picked from its shape (floor, wall, pillar, block, stairs...).
///
/// Runs once automatically the first time Unity loads this script, and any time from
/// Final Shift > Lighting > Apply Dark Factory Look To Game Scene. Running it again rebuilds the lamps and
/// re-picks the materials, so tweak the numbers below and re-run.
/// Only ProBuilder objects that still use a placeholder material (ProBuilder default, Unity default, checker,
/// or a material made by this tool) are recoloured, so robots, doors and other themed props keep their looks.
/// </summary>
[InitializeOnLoad]
public static class DarkFactoryLightingTool
{
    private const string ScenePath = "Assets/Scenes/Game.unity";
    private const string MaterialFolder = "Assets/ProtMaterials/Level";
    private const string LightsRootName = "== Factory Lights (generated) ==";
    private const string ShaderName = "Final Shift/Flat Triplanar Grime";
    private const string GrimePath = "Assets/ShaderTest/Textures/Grime64.png";

    // Bump this to make the tool run once more automatically on every machine.
    private const int AutoRunVersion = 1;
    private const string AutoRunPrefKey = "FinalShift.DarkFactoryLighting.AutoRunVersion";

    // ---- Look ----
    private static readonly Color FogColor = new Color(0.030f, 0.028f, 0.026f);
    private static readonly Color AmbientColor = new Color(0.045f, 0.043f, 0.042f);
    private const float FogStart = 8f;
    private const float FogEnd = 38f;

    // Lamps
    private const float LampSpacing = 10f;      // metres between lamps on a floor
    private const float LampHeight = 4.2f;      // metres above the floor
    private const float LampEdgeInset = 2f;     // keep lamps this far from the floor's edge
    private const float LampRange = 13f;
    private const float WarmIntensity = 14f;
    private const float ColdIntensity = 11f;
    private const int MaxShadowLamps = 6;       // point shadows are costly, only a few lamps cast them
    private static readonly Color WarmLight = new Color(1.00f, 0.62f, 0.30f);  // sodium
    private static readonly Color ColdLight = new Color(0.70f, 0.85f, 1.00f);  // tired fluorescent

    private enum Kind { Floor, Wall, Ceiling, Pillar, Block, Stairs, Door, Lift }

    private struct Surface
    {
        public Color colour;
        public float grime;
        public Surface(Color c, float g) { colour = c; grime = g; }
    }

    // Flat colours, kept dark and dirty (Gloomwood / Iron Lung palette).
    private static readonly Dictionary<Kind, Surface> Palette = new Dictionary<Kind, Surface>
    {
        { Kind.Floor,   new Surface(new Color(0.24f, 0.23f, 0.22f), 0.6f) }, // oily steel plate
        { Kind.Wall,    new Surface(new Color(0.34f, 0.32f, 0.29f), 0.5f) }, // stained concrete
        { Kind.Ceiling, new Surface(new Color(0.14f, 0.14f, 0.15f), 0.5f) }, // soot
        { Kind.Pillar,  new Surface(new Color(0.48f, 0.25f, 0.13f), 0.6f) }, // rusted girder
        { Kind.Block,   new Surface(new Color(0.25f, 0.30f, 0.25f), 0.5f) }, // faded machine-green paint
        { Kind.Stairs,  new Surface(new Color(0.38f, 0.41f, 0.44f), 0.5f) }, // bare steel
        { Kind.Door,    new Surface(new Color(0.78f, 0.60f, 0.12f), 0.4f) }, // warning yellow
        { Kind.Lift,    new Surface(new Color(0.78f, 0.60f, 0.12f), 0.4f) }, // warning yellow
    };

    static DarkFactoryLightingTool()
    {
        if (EditorPrefs.GetInt(AutoRunPrefKey, 0) >= AutoRunVersion) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorPrefs.SetInt(AutoRunPrefKey, AutoRunVersion);
            Apply();
        };
    }

    [MenuItem("Final Shift/Lighting/Apply Dark Factory Look To Game Scene")]
    public static void Apply()
    {
        Scene scene = OpenGameScene();
        if (!scene.IsValid()) return;

        Undo.SetCurrentGroupName("Dark Factory Look");
        int undoGroup = Undo.GetCurrentGroup();

        ApplyEnvironment(scene);
        List<(Renderer r, Kind k)> classified = RecolourProBuilder(scene);
        int lamps = PlaceLamps(scene, classified);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        var counts = new Dictionary<Kind, int>();
        foreach (var c in classified) counts[c.k] = counts.TryGetValue(c.k, out int n) ? n + 1 : 1;
        var parts = new List<string>();
        foreach (var kv in counts) parts.Add(kv.Value + " " + kv.Key);
        Debug.Log("[FinalShift] Dark factory look applied to " + ScenePath + ": " + lamps + " lamps, recoloured " +
                  classified.Count + " ProBuilder objects (" + string.Join(", ", parts) + ").");
    }

    [MenuItem("Final Shift/Lighting/Remove Generated Lamps From Game Scene")]
    public static void RemoveLamps()
    {
        Scene scene = OpenGameScene();
        if (!scene.IsValid()) return;
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == LightsRootName) Undo.DestroyObjectImmediate(root);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // ------------------------------------------------------------------ scene

    private static Scene OpenGameScene()
    {
        Scene active = SceneManager.GetActiveScene();
        if (active.path == ScenePath) return active;
        if (!File.Exists(ScenePath))
        {
            Debug.LogWarning("[FinalShift] Dark factory look: " + ScenePath + " not found.");
            return default;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsToSave()) return default;
        return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    private static void ApplyEnvironment(Scene scene)
    {
        // Underground: no sky, near-black flat ambient, no sky reflections, thick dark fog.
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = AmbientColor;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = null;
        RenderSettings.reflectionIntensity = 0f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = FogColor;
        RenderSettings.fogStartDistance = FogStart;
        RenderSettings.fogEndDistance = FogEnd;
        RenderSettings.sun = null;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            // No sun underground.
            foreach (Light l in root.GetComponentsInChildren<Light>(true))
            {
                if (l.type != LightType.Directional) continue;
                Undo.RecordObject(l, "Dark Factory Look");
                l.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(l);
            }

            // Cameras clear to the fog colour so the far end of a room fades into black, not sky.
            foreach (Camera cam in root.GetComponentsInChildren<Camera>(true))
            {
                if (cam.targetTexture != null) continue;
                Undo.RecordObject(cam, "Dark Factory Look");
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = FogColor;
                PrefabUtility.RecordPrefabInstancePropertyModifications(cam);
            }
        }
    }

    // ------------------------------------------------------------- materials

    private static List<(Renderer, Kind)> RecolourProBuilder(Scene scene)
    {
        var result = new List<(Renderer, Kind)>();
        var mats = new Dictionary<Kind, Material>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == LightsRootName) continue;
            foreach (ProBuilderMesh pb in root.GetComponentsInChildren<ProBuilderMesh>(true))
            {
                Renderer r = pb.GetComponent<Renderer>();
                if (r == null || !UsesOnlyPlaceholders(r)) continue;

                Kind kind = Classify(pb.gameObject, r.bounds);
                if (!mats.TryGetValue(kind, out Material mat)) mats[kind] = mat = LevelMaterial(kind);

                int count = Mathf.Max(1, Mathf.Max(r.sharedMaterials.Length, MeshSubmeshCount(pb)));
                var arr = new Material[count];
                for (int i = 0; i < count; i++) arr[i] = mat;

                Undo.RecordObject(r, "Dark Factory Look");
                r.sharedMaterials = arr;
                PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                result.Add((r, kind));
            }
        }
        return result;
    }

    private static int MeshSubmeshCount(ProBuilderMesh pb)
    {
        MeshFilter mf = pb.GetComponent<MeshFilter>();
        return mf != null && mf.sharedMesh != null ? mf.sharedMesh.subMeshCount : 1;
    }

    private static bool UsesOnlyPlaceholders(Renderer r)
    {
        foreach (Material m in r.sharedMaterials)
            if (!IsPlaceholder(m)) return false;
        return true;
    }

    private static bool IsPlaceholder(Material m)
    {
        if (m == null) return true;
        string path = AssetDatabase.GetAssetPath(m);
        if (path.StartsWith(MaterialFolder)) return true;                  // ours, from an earlier run
        if (path.Contains("com.unity.probuilder")) return true;            // ProBuilder default
        if (path.StartsWith("Resources/unity_builtin_extra") || path.StartsWith("Library/")) return true;
        string n = m.name;
        return n.Contains("Default") || n.Contains("Checker") || n == "Lit";
    }

    /// <summary>Guesses what a ProBuilder object is from its name first, then from its world-space shape.</summary>
    private static Kind Classify(GameObject go, Bounds b)
    {
        string n = go.name.ToLowerInvariant();
        if (n.Contains("stair") || n.Contains("ramp") || n.Contains("step")) return Kind.Stairs;
        if (n.Contains("door") || n.Contains("gate")) return Kind.Door;
        if (n.Contains("elevator") || n.Contains("lift")) return Kind.Lift;
        if (n.Contains("ceiling") || n.Contains("roof")) return Kind.Ceiling;
        if (n.Contains("floor") || n.Contains("ground")) return Kind.Floor;
        if (n.Contains("wall")) return Kind.Wall;
        if (n.Contains("pillar") || n.Contains("column") || n.Contains("beam")) return Kind.Pillar;

        // Tilted slabs are ramps.
        if (Vector3.Dot(go.transform.up, Vector3.up) < 0.97f && Vector3.Dot(go.transform.up, Vector3.up) > 0.3f)
            return Kind.Stairs;

        Vector3 s = b.size;
        float minXZ = Mathf.Min(s.x, s.z);
        float maxXZ = Mathf.Max(s.x, s.z);

        if (s.y <= 1.6f && s.y < 0.5f * minXZ) return Kind.Floor;           // thin horizontal slab
        if (minXZ <= 3f && s.y > 2.5f && maxXZ >= 4f * minXZ) return Kind.Wall; // thin, tall, long
        if (maxXZ < 1.6f && s.y > 2f) return Kind.Pillar;                    // thin, tall, narrow
        return Kind.Block;                                                    // chunky: machinery, platforms
    }

    private static Material LevelMaterial(Kind kind)
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/ProtMaterials", "Level");

        Shader shader = Shader.Find(ShaderName);
        bool custom = shader != null;
        if (!custom) shader = Shader.Find("Universal Render Pipeline/Lit");

        string path = MaterialFolder + "/Level_" + kind + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;

        Surface s = Palette[kind];
        mat.SetColor("_BaseColor", s.colour);
        if (custom)
        {
            mat.SetTexture("_GrimeTex", AssetDatabase.LoadAssetAtPath<Texture2D>(GrimePath));
            mat.SetFloat("_GrimeStrength", s.grime);
            mat.SetFloat("_GrimeScale", 0.5f);
            mat.SetColor("_Ambient", AmbientColor);
            mat.SetFloat("_ObjectSpace", 0f);
            mat.DisableKeyword("_OBJECT_SPACE");
        }
        else
        {
            mat.SetFloat("_Smoothness", 0.1f);
            mat.SetFloat("_Metallic", 0f);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material UnlitMaterial(string name, Color colour)
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/ProtMaterials", "Level");
        string path = MaterialFolder + "/" + name + ".mat";
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        mat.SetColor("_BaseColor", colour);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ----------------------------------------------------------------- lamps

    private static int PlaceLamps(Scene scene, List<(Renderer r, Kind k)> classified)
    {
        foreach (GameObject old in scene.GetRootGameObjects())
            if (old.name == LightsRootName) Undo.DestroyObjectImmediate(old);

        GameObject root = new GameObject(LightsRootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        Undo.RegisterCreatedObjectUndo(root, "Dark Factory Look");

        var floors = new List<Bounds>();
        var stairs = new List<Bounds>();
        var solids = new List<Bounds>();   // things a lamp must not sit inside
        foreach (var c in classified)
        {
            Bounds b = c.r.bounds;
            if (c.k == Kind.Floor) floors.Add(b);
            else if (c.k == Kind.Stairs) stairs.Add(b);
            if (c.k != Kind.Floor) solids.Add(b);
        }
        // Floors are solid too (lamps under an upper floor must hang below it).
        var slabs = new List<Bounds>(floors);

        var spots = new List<Vector3>();
        foreach (Bounds f in floors)
        {
            float usableX = Mathf.Max(0f, f.size.x - 2f * LampEdgeInset);
            float usableZ = Mathf.Max(0f, f.size.z - 2f * LampEdgeInset);
            int nx = Mathf.Max(1, Mathf.CeilToInt(usableX / LampSpacing));
            int nz = Mathf.Max(1, Mathf.CeilToInt(usableZ / LampSpacing));
            for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
            {
                float x = f.min.x + LampEdgeInset + usableX * (ix + 0.5f) / nx;
                float z = f.min.z + LampEdgeInset + usableZ * (iz + 0.5f) / nz;
                float floorTop = f.max.y;
                float y = floorTop + LampHeight;

                // Hang below any slab directly overhead.
                foreach (Bounds s in slabs)
                {
                    if (s.min.y <= floorTop + 0.5f) continue;
                    if (x < s.min.x || x > s.max.x || z < s.min.z || z > s.max.z) continue;
                    y = Mathf.Min(y, s.min.y - 0.35f);
                }
                if (y - floorTop < 2.3f) continue; // crawl space, no lamp

                // A block standing here: hang the lamp over the block instead.
                foreach (Bounds s in solids)
                {
                    if (x < s.min.x || x > s.max.x || z < s.min.z || z > s.max.z) continue;
                    if (s.min.y > floorTop + 1f || s.max.y < floorTop + 0.5f) continue;
                    if (s.size.y > 9f) { y = float.NaN; break; } // a wall, not a block
                    y = Mathf.Max(y, s.max.y + LampHeight * 0.7f);
                }
                if (float.IsNaN(y)) continue;

                Vector3 p = new Vector3(x, y, z);
                if (InsideAny(p, solids, 0.4f)) continue;
                spots.Add(p);
            }
        }
        foreach (Bounds s in stairs)
            spots.Add(new Vector3(s.center.x, s.max.y + 2.6f, s.center.z));

        // Drop lamps that ended up too close to another one.
        var placed = new List<Vector3>();
        foreach (Vector3 p in spots)
        {
            bool tooClose = false;
            foreach (Vector3 q in placed)
                if ((p - q).sqrMagnitude < (LampSpacing * 0.55f) * (LampSpacing * 0.55f)) { tooClose = true; break; }
            if (!tooClose) placed.Add(p);
        }

        Material housing = LevelMaterial(Kind.Ceiling);
        Material warmBulb = UnlitMaterial("Lamp_Warm", new Color(1f, 0.78f, 0.45f));
        Material coldBulb = UnlitMaterial("Lamp_Cold", new Color(0.82f, 0.92f, 1f));

        int shadowEvery = Mathf.Max(1, Mathf.CeilToInt(placed.Count / (float)MaxShadowLamps));
        for (int i = 0; i < placed.Count; i++)
        {
            bool cold = i % 4 == 3;
            bool flicker = i % 7 == 5;
            bool shadows = i % shadowEvery == 0;
            BuildLamp(root.transform, i, placed[i], cold, flicker, shadows, housing, cold ? coldBulb : warmBulb);
        }
        return placed.Count;
    }

    private static bool InsideAny(Vector3 p, List<Bounds> list, float pad)
    {
        foreach (Bounds b in list)
        {
            Bounds e = b;
            e.Expand(pad * 2f);
            if (e.Contains(p)) return true;
        }
        return false;
    }

    private static void BuildLamp(Transform parent, int index, Vector3 pos, bool cold, bool flicker, bool shadows,
                                  Material housingMat, Material bulbMat)
    {
        GameObject lamp = new GameObject("Lamp " + index + (cold ? " (cold)" : " (warm)") + (flicker ? " flicker" : ""));
        lamp.transform.SetParent(parent, false);
        lamp.transform.position = pos;
        lamp.isStatic = false;

        Part(lamp.transform, PrimitiveType.Cylinder, "Cable", new Vector3(0f, 1.6f, 0f), new Vector3(0.03f, 1.5f, 0.03f), housingMat);
        Part(lamp.transform, PrimitiveType.Cylinder, "Shade", new Vector3(0f, 0.08f, 0f), new Vector3(0.9f, 0.07f, 0.9f), housingMat);
        Part(lamp.transform, PrimitiveType.Cylinder, "Bulb", new Vector3(0f, 0f, 0f), new Vector3(0.6f, 0.03f, 0.6f), bulbMat);

        GameObject lightGo = new GameObject("Light");
        lightGo.transform.SetParent(lamp.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, -0.3f, 0f);
        Light l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.lightmapBakeType = LightmapBakeType.Realtime;
        l.color = cold ? ColdLight : WarmLight;
        l.intensity = cold ? ColdIntensity : WarmIntensity;
        l.range = LampRange;
        l.shadows = shadows ? LightShadows.Hard : LightShadows.None;
        l.shadowBias = 0.05f;

        if (flicker)
        {
            var f = lightGo.AddComponent<FinalShift.ShaderTest.ShaderTestFlicker>();
            f.baseIntensity = l.intensity;
            f.flickerAmount = 0.35f;
            f.speed = 7f;
        }
    }

    private static void Part(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 scale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }
}
