using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the pause menu tablet prefab (Assets/Prefabs/Menu/PauseTablet.prefab) out of primitives: a rugged
/// factory tablet with a pixel "FINAL SHIFT" title, a volume slider (speaker icon), a mouse sensitivity slider
/// (mouse icon) and a start/resume button (arrow icon). Everything is flat unlit colour, no textures, no text
/// assets: the letters and icons are little cubes.
/// Then puts one instance into every scene in the build list (Game, ElevatorRoom, BossFight) that doesn't have one.
/// A scene that's open with unsaved changes is skipped (save it and run again).
/// Menu: Final Shift > Pause Menu > Build Tablet Prefab + Add To Scenes. Trigger file: Temp/FinalShift_PauseTablet.
/// </summary>
[InitializeOnLoad]
public static class PauseTabletTool
{
    private const string PrefabFolder = "Assets/Prefabs/Menu";
    private const string PrefabPath = PrefabFolder + "/PauseTablet.prefab";
    private const string MatFolder = "Assets/ProtMaterials/PauseTablet";
    private const string TriggerFile = "Temp/FinalShift_PauseTablet";
    private const string InstanceName = "Pause Tablet";
    private const int MenuLayer = 29;

    private static readonly string[] Scenes =
    {
        "Assets/Scenes/Game.unity",
        "Assets/Scenes/BossFight.unity",
        // ElevatorRoom left out on purpose (someone else is working in it, Kalman 2026-11-08).
    };

    private const string OffTriggerFile = "Temp/FinalShift_PauseTabletOff";
    private const string ElevatorScene = "Assets/Scenes/ElevatorRoom.unity";

    static PauseTabletTool() { EditorApplication.update += Poll; }

    private static void Poll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (File.Exists(OffTriggerFile))
        {
            File.Delete(OffTriggerFile);
            TurnOff();
        }
        if (!File.Exists(TriggerFile)) return;
        File.Delete(TriggerFile);
        Run();
    }

    /// <summary>Deactivates the tablet in Game and BossFight (the game runs with no pause menu) and removes it from ElevatorRoom.</summary>
    [MenuItem("Final Shift/Pause Menu/Turn Tablet Off In Scenes")]
    public static void TurnOff()
    {
        try
        {
            foreach (string path in Scenes) EditScene(path, go => go.SetActive(false), "turned off");
            EditScene(ElevatorScene, go => Object.DestroyImmediate(go), "removed");
        }
        catch (System.Exception e) { Debug.LogError("[FinalShift] Pause tablet turn off failed: " + e); }
    }

    [MenuItem("Final Shift/Pause Menu/Turn Tablet On In Scenes")]
    public static void TurnOn()
    {
        foreach (string path in Scenes) EditScene(path, go => go.SetActive(true), "turned on");
    }

    private static void EditScene(string path, System.Action<GameObject> action, string what)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) return;
        Scene s = SceneManager.GetSceneByPath(path);
        bool wasLoaded = s.IsValid() && s.isLoaded;
        if (wasLoaded && s.isDirty)
        {
            Debug.LogWarning($"[FinalShift] Pause tablet: {path} has unsaved changes, skipped.");
            return;
        }
        if (!wasLoaded) s = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        bool changed = false;
        foreach (GameObject root in s.GetRootGameObjects())
        {
            PauseTablet tablet = root.GetComponentInChildren<PauseTablet>(true);
            if (tablet == null) continue;
            action(tablet.gameObject);
            changed = true;
            break;
        }
        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(s);
            EditorSceneManager.SaveScene(s);
            Debug.Log($"[FinalShift] Pause tablet {what} in {path}.");
        }
        if (!wasLoaded) EditorSceneManager.CloseScene(s, true);
    }

    [MenuItem("Final Shift/Pause Menu/Build Tablet Prefab + Add To Scenes")]
    public static void Run()
    {
        try
        {
            GameObject prefab = BuildPrefab();
            foreach (string path in Scenes) AddToScene(path, prefab);
            Debug.Log("[FinalShift] Pause tablet built: " + PrefabPath);
        }
        catch (System.Exception e) { Debug.LogError("[FinalShift] Pause tablet failed: " + e); }
    }

    [MenuItem("Final Shift/Pause Menu/Rebuild Tablet Prefab Only")]
    public static void RebuildPrefabOnly()
    {
        try { BuildPrefab(); Debug.Log("[FinalShift] Pause tablet prefab rebuilt."); }
        catch (System.Exception e) { Debug.LogError("[FinalShift] Pause tablet failed: " + e); }
    }

    // ------------------------------------------------------------------ pixel art

    private static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
    {
        { 'F', new[] { "###", "#..", "##.", "#..", "#.." } },
        { 'I', new[] { "###", ".#.", ".#.", ".#.", "###" } },
        { 'N', new[] { "#..#", "##.#", "#.##", "#..#", "#..#" } },
        { 'A', new[] { "###", "#.#", "###", "#.#", "#.#" } },
        { 'L', new[] { "#..", "#..", "#..", "#..", "###" } },
        { 'S', new[] { "###", "#..", "###", "..#", "###" } },
        { 'H', new[] { "#.#", "#.#", "###", "#.#", "#.#" } },
        { 'T', new[] { "###", ".#.", ".#.", ".#.", ".#." } },
    };

    private static readonly string[] SpeakerIcon =
    {
        "...#..#.",
        "..##...#",
        "####.#.#",
        "####.#.#",
        "####.#.#",
        "..##...#",
        "...#..#.",
    };

    private static readonly string[] MouseIcon =
    {
        "...#.",
        "..#..",
        ".###.",
        "#.#.#",
        "#.#.#",
        "#####",
        "#...#",
        "#...#",
        "#...#",
        ".###.",
    };

    private static readonly string[] ArrowIcon =
    {
        "#.....",
        "###...",
        "#####.",
        "######",
        "#####.",
        "###...",
        "#.....",
    };

    private static string[] TextToRows(string text)
    {
        var rows = new string[5];
        for (int r = 0; r < 5; r++)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                if (i > 0) sb.Append('.');
                if (text[i] == ' ') { sb.Append(".."); continue; }
                sb.Append(Font[text[i]][r]);
            }
            rows[r] = sb.ToString();
        }
        return rows;
    }

    /// <summary>Builds pixel art from '#' cells, one cube per horizontal run. Centred on the parent.</summary>
    private static Transform Pixels(string name, Transform parent, string[] rows, Vector3 centre, float px, float depth, Material mat)
    {
        Transform root = new GameObject(name).transform;
        root.SetParent(parent, false);
        root.localPosition = centre;
        int h = rows.Length, w = 0;
        foreach (string r in rows) w = Mathf.Max(w, r.Length);
        for (int y = 0; y < h; y++)
        {
            string row = rows[y];
            int x = 0;
            while (x < row.Length)
            {
                if (row[x] != '#') { x++; continue; }
                int start = x;
                while (x < row.Length && row[x] == '#') x++;
                int len = x - start;
                float cx = (start + len * 0.5f - w * 0.5f) * px;
                float cy = (h * 0.5f - y - 0.5f) * px;
                Box("Px", root, new Vector3(cx, cy, 0f), new Vector3(len * px, px, depth), mat);
            }
        }
        return root;
    }

    // ------------------------------------------------------------------ prefab

    private static GameObject BuildPrefab()
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder("Assets/Prefabs", "Menu");
        if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder("Assets/ProtMaterials", "PauseTablet");

        Material casing = Unlit("Tablet_Casing", new Color(0.20f, 0.21f, 0.19f));
        Material casingDark = Unlit("Tablet_CasingDark", new Color(0.11f, 0.11f, 0.10f));
        Material bumper = Unlit("Tablet_Bumper", new Color(0.88f, 0.46f, 0.08f));
        Material bezel = Unlit("Tablet_Bezel", new Color(0.04f, 0.04f, 0.04f));
        Material screen = Unlit("Tablet_Screen", new Color(0.03f, 0.11f, 0.06f));
        Material scanline = Unlit("Tablet_Scanline", new Color(0.02f, 0.075f, 0.04f));
        Material title = Unlit("Tablet_Title", new Color(1f, 0.64f, 0.12f));
        Material ink = Unlit("Tablet_Ink", new Color(0.35f, 1f, 0.5f));
        Material track = Unlit("Tablet_Track", new Color(0.06f, 0.24f, 0.13f));
        Material fill = Unlit("Tablet_Fill", new Color(0.30f, 0.88f, 0.44f));
        Material knob = Unlit("Tablet_Knob", new Color(0.78f, 0.95f, 0.82f));
        Material knobHot = Unlit("Tablet_KnobHover", new Color(1f, 0.92f, 0.55f));
        Material button = Unlit("Tablet_Button", new Color(0.92f, 0.56f, 0.10f));
        Material buttonHot = Unlit("Tablet_ButtonHover", new Color(1f, 0.80f, 0.35f));
        Material arrow = Unlit("Tablet_Arrow", new Color(0.08f, 0.05f, 0.02f));
        Material screw = Unlit("Tablet_Screw", new Color(0.45f, 0.45f, 0.42f));
        Material led = Unlit("Tablet_Led", new Color(1f, 0.15f, 0.1f));
        Material backdropMat = TransparentBlack("Tablet_Backdrop");

        GameObject root = new GameObject("PauseTablet");
        PauseTablet script = root.AddComponent<PauseTablet>();

        // Backdrop: darkens (or blacks out) the game behind the tablet. Placed and sized at runtime.
        GameObject back = GameObject.CreatePrimitive(PrimitiveType.Quad);
        back.name = "Backdrop";
        Object.DestroyImmediate(back.GetComponent<Collider>());
        back.transform.SetParent(root.transform, false);
        back.GetComponent<MeshRenderer>().sharedMaterial = backdropMat;
        NoShadows(back.GetComponent<MeshRenderer>());

        // The tablet. Its face points at the camera (-Z); +X right, +Y up. Body 1.6 x 1.05.
        Transform t = new GameObject("Tablet").transform;
        t.SetParent(root.transform, false);

        Box("Body", t, new Vector3(0f, 0f, 0f), new Vector3(1.6f, 1.05f, 0.08f), casing);
        Box("Back Plate", t, new Vector3(0f, 0f, 0.05f), new Vector3(1.3f, 0.8f, 0.03f), casingDark);
        Box("Grip Left", t, new Vector3(-0.82f, 0f, 0.01f), new Vector3(0.06f, 0.55f, 0.07f), casingDark);
        Box("Grip Right", t, new Vector3(0.82f, 0f, 0.01f), new Vector3(0.06f, 0.55f, 0.07f), casingDark);
        float bx = 0.76f, by = 0.48f;
        foreach (Vector2 c in new[] { new Vector2(-bx, by), new Vector2(bx, by), new Vector2(-bx, -by), new Vector2(bx, -by) })
        {
            Box("Bumper", t, new Vector3(c.x, c.y, 0f), new Vector3(0.17f, 0.17f, 0.11f), bumper);
        }
        Box("Bumper Stripe Top", t, new Vector3(0f, 0.515f, 0f), new Vector3(0.3f, 0.04f, 0.09f), bumper);
        Box("Bumper Stripe Bottom", t, new Vector3(0f, -0.515f, 0f), new Vector3(0.3f, 0.04f, 0.09f), bumper);

        Box("Bezel", t, new Vector3(0f, 0f, -0.045f), new Vector3(1.42f, 0.9f, 0.02f), bezel);
        Box("Screen", t, new Vector3(0f, 0f, -0.056f), new Vector3(1.34f, 0.82f, 0.004f), screen);
        // Faint scanlines (a few dark bands across the screen).
        for (int i = 0; i < 10; i++)
        {
            float y = -0.39f + i * 0.0866f;
            Box("Scanline", t, new Vector3(0f, y, -0.0585f), new Vector3(1.34f, 0.014f, 0.002f), scanline);
        }
        float sx = 0.665f, sy = 0.405f;
        foreach (Vector2 c in new[] { new Vector2(-sx, sy), new Vector2(sx, sy), new Vector2(-sx, -sy), new Vector2(sx, -sy) })
            Box("Screw", t, new Vector3(c.x * 1.035f, c.y * 1.06f, -0.058f), new Vector3(0.025f, 0.025f, 0.01f), screw);
        Box("Status Led", t, new Vector3(0.64f, -0.495f, -0.04f), new Vector3(0.035f, 0.02f, 0.02f), led);
        Box("Speaker Grille", t, new Vector3(-0.6f, -0.495f, -0.04f), new Vector3(0.12f, 0.012f, 0.012f), casingDark);

        // Title
        Pixels("Title", t, TextToRows("FINAL SHIFT"), new Vector3(0f, 0.25f, -0.064f), 0.028f, 0.008f, title);

        // Sliders
        const float trackLeft = -0.36f, trackRight = 0.56f, iconX = -0.5f;
        float trackW = trackRight - trackLeft, trackX = (trackLeft + trackRight) * 0.5f;
        PauseTablet.Slider volume = MakeSlider(t, "Volume", 0.04f, trackX, trackW, track, fill, knob);
        Pixels("Volume Icon", t, SpeakerIcon, new Vector3(iconX, 0.04f, -0.064f), 0.021f, 0.008f, ink);
        PauseTablet.Slider sens = MakeSlider(t, "Sensitivity", -0.13f, trackX, trackW, track, fill, knob);
        Pixels("Sensitivity Icon", t, MouseIcon, new Vector3(iconX, -0.13f, -0.064f), 0.016f, 0.008f, ink);

        // Start / resume button: amber plate with a dark arrow.
        Transform plate = Box("Start Button", t, new Vector3(0f, -0.305f, -0.075f), new Vector3(0.34f, 0.15f, 0.03f), button);
        Box("Start Button Rim", t, new Vector3(0f, -0.305f, -0.062f), new Vector3(0.37f, 0.18f, 0.012f), bezel);
        Transform arrowIcon = Pixels("Start Arrow", t, ArrowIcon, new Vector3(0.012f, -0.305f, -0.093f), 0.016f, 0.008f, arrow);

        SetLayer(root.transform, MenuLayer);

        SerializedObject so = new SerializedObject(script);
        so.FindProperty("tablet").objectReferenceValue = t;
        so.FindProperty("backdrop").objectReferenceValue = back.GetComponent<MeshRenderer>();
        WriteSlider(so.FindProperty("volumeSlider"), volume);
        WriteSlider(so.FindProperty("sensitivitySlider"), sens);
        so.FindProperty("startButton").objectReferenceValue = plate;
        so.FindProperty("startButtonRenderer").objectReferenceValue = plate.GetComponent<MeshRenderer>();
        so.FindProperty("startArrow").objectReferenceValue = arrowIcon;
        so.FindProperty("buttonNormal").objectReferenceValue = button;
        so.FindProperty("buttonHover").objectReferenceValue = buttonHot;
        so.FindProperty("knobNormal").objectReferenceValue = knob;
        so.FindProperty("knobHover").objectReferenceValue = knobHot;
        so.FindProperty("menuLayer").intValue = MenuLayer;
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        return prefab;
    }

    private static PauseTablet.Slider MakeSlider(Transform t, string name, float y, float x, float w, Material track, Material fill, Material knob)
    {
        var s = new PauseTablet.Slider();
        s.track = Box(name + " Track", t, new Vector3(x, y, -0.062f), new Vector3(w, 0.04f, 0.008f), track);
        // Fill and knob are placed for the current value at runtime; start at full.
        s.fill = Box(name + " Fill", t, new Vector3(x, y, -0.067f), new Vector3(w, 0.026f, 0.006f), fill);
        s.knob = Box(name + " Knob", t, new Vector3(x + w * 0.5f, y, -0.082f), new Vector3(0.045f, 0.1f, 0.03f), knob);
        s.knobRenderer = s.knob.GetComponent<MeshRenderer>();
        // Little tick marks under the track.
        for (int i = 0; i <= 4; i++)
            Box(name + " Tick", t, new Vector3(x - w * 0.5f + w * i / 4f, y - 0.045f, -0.062f), new Vector3(0.008f, 0.02f, 0.006f), track);
        return s;
    }

    private static void WriteSlider(SerializedProperty p, PauseTablet.Slider s)
    {
        p.FindPropertyRelative("track").objectReferenceValue = s.track;
        p.FindPropertyRelative("fill").objectReferenceValue = s.fill;
        p.FindPropertyRelative("knob").objectReferenceValue = s.knob;
        p.FindPropertyRelative("knobRenderer").objectReferenceValue = s.knobRenderer;
    }

    // ------------------------------------------------------------------ scenes

    private static void AddToScene(string path, GameObject prefab)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) return;

        Scene s = SceneManager.GetSceneByPath(path);
        bool wasLoaded = s.IsValid() && s.isLoaded;
        if (wasLoaded && s.isDirty)
        {
            Debug.LogWarning($"[FinalShift] Pause tablet: {path} is open with unsaved changes, skipped. Save it and run Final Shift > Pause Menu again.");
            return;
        }
        if (!wasLoaded) s = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

        bool has = false;
        foreach (GameObject go in s.GetRootGameObjects())
            if (go.GetComponentInChildren<PauseTablet>(true) != null) { has = true; break; }

        if (!has)
        {
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, s);
            inst.name = InstanceName;
            inst.transform.SetAsLastSibling();
            EditorSceneManager.MarkSceneDirty(s);
            EditorSceneManager.SaveScene(s);
            Debug.Log($"[FinalShift] Pause tablet added to {path}.");
        }
        else Debug.Log($"[FinalShift] Pause tablet already in {path} (prefab updated).");

        if (!wasLoaded) EditorSceneManager.CloseScene(s, true);
    }

    // ------------------------------------------------------------------ helpers

    private static Transform Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        MeshRenderer r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        NoShadows(r);
        return go.transform;
    }

    private static void NoShadows(MeshRenderer r)
    {
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform c in t) SetLayer(c, layer);
    }

    private static Material Unlit(string name, Color color)
    {
        string path = MatFolder + "/" + name + ".mat";
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        mat.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material TransparentBlack(string name)
    {
        string path = MatFolder + "/" + name + ".mat";
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, 1f));
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
