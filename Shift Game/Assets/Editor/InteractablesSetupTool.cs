using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the placeholder art and prefabs for the environment interactables (conveyor belts, sliding doors,
/// door buttons, door targets, door trigger zones, conveyor crates) and puts a demo of each in the Kalman test scene.
/// Textures are tiny and point-filtered for the crisp PS1 look.
/// Menu: Final Shift > Interactables > ...
/// </summary>
[InitializeOnLoad]
public static class InteractablesSetupTool
{
    private const string ScenePath = "Assets/Scenes/Kalman test scene.unity";
    private const string PrefabFolder = "Assets/Prefabs/Interactables";
    private const string MaterialFolder = "Assets/ProtMaterials/Interactables";
    private const string TextureFolder = "Assets/Art/Interactables";
    private const string DemoRootName = "Interactables Demo";
    // Creating this file (in the project's Temp folder) builds everything once on the next editor reload.
    private const string TriggerFile = "Temp/FinalShift_BuildInteractables";

    // Door size (must match SlidingDoor's slide distance = DoorWidth / 2).
    private const float DoorWidth = 2.6f, DoorHeight = 3f, DoorPost = 0.4f, DoorDepth = 0.5f;
    private const float WallHeight = DoorHeight + DoorPost, WallThickness = 0.5f;

    private static Material beltMat, frameMat, openingMat, voidMat, hazardMat, markingMat, crateMat, cardboardMat, containerMat,
        doorMat, wallMat, darkMat, redMat, greenMat, targetMat, explosionMat;

    static InteractablesSetupTool()
    {
        if (!File.Exists(TriggerFile)) return;
        File.Delete(TriggerFile);
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[FinalShift] Interactables setup skipped: editor is in play mode.");
                return;
            }
            BuildAll();
            AddDemo();
        };
    }

    private static bool overwritePrefabs;

    [MenuItem("Final Shift/Interactables/Build Missing Prefabs")]
    public static void BuildAll() => BuildAll(false);

    [MenuItem("Final Shift/Interactables/Rebuild All Prefabs (overwrites them)")]
    public static void RebuildAllPrefabs() => BuildAll(true);

    [MenuItem("Final Shift/Interactables/Rebuild Textures And Materials")]
    public static void RebuildTexturesAndMaterials()
    {
        EnsureFolder(MaterialFolder);
        EnsureFolder(TextureFolder);
        BuildMaterials();
        AssetDatabase.SaveAssets();
        Debug.Log("[FinalShift] Interactables textures and materials rebuilt.");
    }

    private static void BuildAll(bool overwrite)
    {
        overwritePrefabs = overwrite;
        try
        {
            EnsureFolder(PrefabFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(TextureFolder);
            BuildMaterials();
            GameObject small = BuildCrate("Crate_Small", new Vector3(0.55f, 0.55f, 0.55f), crateMat, 4f, false);
            GameObject box = BuildCrate("Crate_Box", new Vector3(0.7f, 0.4f, 0.45f), cardboardMat, 2.5f, false);
            GameObject big = BuildCrate("Crate_Big", new Vector3(1.4f, 1.9f, 1.4f), containerMat, 500f, true);
            BuildOpening(new[] { small, box, big });
            BuildDoor();
            BuildButton();
            BuildTarget();
            BuildTriggerZone();
            CableSetupTool.UpgradePrefabs();
            AssetDatabase.SaveAssets();
            Debug.Log("[FinalShift] Interactables prefabs built in " + PrefabFolder);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[FinalShift] Interactables build failed: " + e);
        }
    }

    // ---------------------------------------------------------------- textures and materials

    private static void BuildMaterials()
    {
        Texture2D belt = MakeTexture("Tex_Belt", 32, BeltPixel);
        Texture2D metal = MakeTexture("Tex_MetalPanel", 32, MetalPixel);
        Texture2D hazard = MakeTexture("Tex_Hazard", 32, HazardPixel);
        Texture2D crate = MakeTexture("Tex_Crate", 32, CratePixel);
        Texture2D container = MakeTexture("Tex_Container", 32, ContainerPixel);
        Texture2D target = MakeTexture("Tex_Target", 32, TargetPixel);

        beltMat = Mat("Conveyor_Belt", Color.white, belt);
        frameMat = Mat("Conveyor_Frame", new Color(0.55f, 0.57f, 0.6f), metal);
        openingMat = Mat("Opening_Frame", new Color(0.5f, 0.46f, 0.4f), metal);
        voidMat = Mat("Opening_Void", new Color(0.02f, 0.02f, 0.025f), null, true);
        hazardMat = Mat("Hazard_Stripes", Color.white, hazard);
        markingMat = Mat("Floor_Marking", new Color(0.55f, 0.55f, 0.55f), hazard);
        crateMat = Mat("Crate_Wood", Color.white, crate);
        cardboardMat = Mat("Crate_Cardboard", new Color(1f, 0.85f, 0.62f), crate);
        containerMat = Mat("Crate_Container", new Color(0.35f, 0.55f, 0.32f), container);
        doorMat = Mat("Door_Panel", new Color(0.5f, 0.56f, 0.64f), metal);
        wallMat = Mat("Wall_Panel", new Color(0.62f, 0.6f, 0.56f), metal);
        darkMat = Mat("Prop_DarkSteel", new Color(0.18f, 0.18f, 0.2f), metal);
        redMat = Mat("Glow_Red", new Color(1f, 0.12f, 0.08f), null, true);
        greenMat = Mat("Glow_Green", new Color(0.15f, 1f, 0.25f), null, true);
        targetMat = Mat("Target_Face", Color.white, target);
        explosionMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ProtMaterials/RobotExplosion.mat");
        if (explosionMat == null) explosionMat = Mat("Target_Explosion", new Color(1f, 0.6f, 0.15f), null, true);
    }

    private static float Noise(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 73856093 ^ y * 19349663 ^ seed * 83492791;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xffff) / 65535f;
        }
    }

    private static Color Grey(float v) => new Color(v, v, v);

    private static Color BeltPixel(int x, int y)
    {
        float n = (Noise(x, y, 1) - 0.5f) * 0.05f;
        if (x < 2 || x > 29) return Grey(0.3f + n);                // worn edges
        if (y % 8 == 0) return Grey(0.06f + n);                     // slat gaps
        float dx = Mathf.Abs(x - 15.5f);
        int d = ((y + Mathf.FloorToInt(dx)) % 16 + 16) % 16;         // tip at the highest V, so chevrons point along +V (belt direction)
        if (dx < 11f && d < 4) return new Color(0.78f + n, 0.6f + n, 0.1f);
        return Grey(0.13f + n);
    }

    private static Color MetalPixel(int x, int y)
    {
        float n = (Noise(x, y, 2) - 0.5f) * 0.08f;
        if (x == 0 || y == 0 || y == 16) return Grey(0.22f + n);    // panel seams
        if (x == 31 || y == 31 || y == 17) return Grey(0.62f + n);  // bevel highlights
        bool rivetX = x == 3 || x == 28, rivetY = y == 3 || y == 13 || y == 19 || y == 28;
        if (rivetX && rivetY) return Grey(0.75f);
        if ((x == 4 || x == 29) && (y == 2 || y == 12 || y == 18 || y == 27)) return Grey(0.25f);
        return Grey(0.45f + n);
    }

    private static Color HazardPixel(int x, int y)
    {
        float n = (Noise(x, y, 3) - 0.5f) * 0.08f;
        bool yellow = ((x + y) / 8) % 2 == 0;
        Color c = yellow ? new Color(0.88f + n, 0.66f + n, 0.08f) : Grey(0.08f + n);
        if (Noise(x, y, 33) > 0.94f) c = Color.Lerp(c, Grey(0.4f), 0.6f); // scuffs
        return c;
    }

    private static Color CratePixel(int x, int y)
    {
        float grain = (Noise(x / 3, y, 4) - 0.5f) * 0.1f;
        Color wood = new Color(0.56f + grain, 0.37f + grain, 0.18f + grain * 0.5f);
        Color frame = new Color(0.36f + grain, 0.22f + grain, 0.1f);
        if (x < 3 || x > 28 || y < 3 || y > 28) return ((x == 1 || x == 30) && (y == 1 || y == 30)) ? Grey(0.7f) : frame;
        if (Mathf.Abs(x - y) <= 1) return frame;                      // diagonal brace
        if (y % 7 == 0) return wood * 0.6f;                           // plank gaps
        return wood;
    }

    private static Color ContainerPixel(int x, int y)
    {
        float n = (Noise(x, y, 5) - 0.5f) * 0.08f;
        if (x < 2 || x > 29 || y < 2 || y > 29) return Grey(0.35f + n);
        float rib = (x / 3) % 2 == 0 ? 0.95f : 0.72f;                 // corrugated ribs
        if (y > 12 && y < 18) return (((x + y) / 4) % 2 == 0) ? new Color(0.9f, 0.7f, 0.15f) : Grey(0.1f); // hazard band
        return Grey(rib + n);
    }

    private static Color TargetPixel(int x, int y)
    {
        float r = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f));
        float n = (Noise(x, y, 6) - 0.5f) * 0.06f;
        if (r < 2.6f) return new Color(1f, 0.85f, 0.1f);
        if (r > 15f) return Grey(0.12f + n);
        return (Mathf.FloorToInt(r / 3.2f) % 2 == 0) ? new Color(0.85f + n, 0.1f, 0.08f) : new Color(0.92f + n, 0.9f + n, 0.82f + n);
    }

    private static Texture2D MakeTexture(string name, int size, System.Func<int, int, Color> pixel)
    {
        string path = $"{TextureFolder}/{name}.png";
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, pixel(x, y));
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.filterMode = FilterMode.Point;   // crisp PS1 pixels
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material Mat(string name, Color color, Texture2D tex, bool unlit = false)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        bool create = mat == null;
        if (create) mat = new Material(shader) { name = name };
        else mat.shader = shader;

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);

        if (create) AssetDatabase.CreateAsset(mat, path);
        else EditorUtility.SetDirty(mat);
        return mat;
    }

    // ---------------------------------------------------------------- prefabs

    private static GameObject BuildCrate(string name, Vector3 size, Material mat, float mass, bool immovable)
    {
        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = name;
        root.transform.localScale = size;
        root.GetComponent<Renderer>().sharedMaterial = mat;
        Rigidbody rb = root.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.isKinematic = immovable;
        ConveyorItem item = root.AddComponent<ConveyorItem>();
        SerializedObject so = new SerializedObject(item);
        so.FindProperty("immovable").boolValue = immovable;
        so.ApplyModifiedPropertiesWithoutUndo();
        return Save(root, name);
    }

    private static void BuildOpening(GameObject[] items)
    {
        const float w = ConveyorOpening.InnerWidth, h = ConveyorOpening.InnerHeight, d = ConveyorOpening.TunnelDepth, t = 0.3f;
        GameObject root = new GameObject("ConveyorOpening");
        Transform r = root.transform;
        Part("PostLeft", PrimitiveType.Cube, r, new Vector3(-(w + t) * 0.5f, (h + t) * 0.5f, -d * 0.5f), new Vector3(t, h + t, d), openingMat, true);
        Part("PostRight", PrimitiveType.Cube, r, new Vector3((w + t) * 0.5f, (h + t) * 0.5f, -d * 0.5f), new Vector3(t, h + t, d), openingMat, true);
        Part("Roof", PrimitiveType.Cube, r, new Vector3(0f, h + t * 0.5f, -d * 0.5f), new Vector3(w + 2f * t, t, d), openingMat, true);
        Part("Back", PrimitiveType.Cube, r, new Vector3(0f, (h + t) * 0.5f, -d - t * 0.5f), new Vector3(w + 2f * t, h + t, t), voidMat, true);
        Part("TrimLeft", PrimitiveType.Cube, r, new Vector3(-(w + t) * 0.5f, (h + t) * 0.5f, 0.02f), new Vector3(t + 0.02f, h + t, 0.04f), hazardMat, false);
        Part("TrimRight", PrimitiveType.Cube, r, new Vector3((w + t) * 0.5f, (h + t) * 0.5f, 0.02f), new Vector3(t + 0.02f, h + t, 0.04f), hazardMat, false);
        Part("TrimTop", PrimitiveType.Cube, r, new Vector3(0f, h + t * 0.5f, 0.02f), new Vector3(w + 2f * t + 0.02f, t, 0.04f), hazardMat, false);
        // Strip curtain stubs hanging from the roof (above the tallest item).
        for (int i = 0; i < 6; i++)
            Part("Strip" + i, PrimitiveType.Cube, r, new Vector3(-w * 0.5f + 0.2f + i * (w - 0.4f) / 5f, h - 0.25f, -0.15f), new Vector3(0.3f, 0.5f, 0.02f), darkMat, false);

        ConveyorOpening opening = root.AddComponent<ConveyorOpening>();
        SerializedObject so = new SerializedObject(opening);
        SerializedProperty list = so.FindProperty("itemPrefabs");
        list.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        so.FindProperty("beltSurfaceMaterial").objectReferenceValue = beltMat;
        so.FindProperty("beltFrameMaterial").objectReferenceValue = frameMat;
        so.ApplyModifiedPropertiesWithoutUndo();
        Save(root, "ConveyorOpening");
    }

    private static void BuildDoor()
    {
        const float w = DoorWidth, h = DoorHeight, t = DoorPost, d = DoorDepth;
        GameObject root = new GameObject("SlidingDoor");
        Transform r = root.transform;
        Part("PostLeft", PrimitiveType.Cube, r, new Vector3(-(w + t) * 0.5f, (h + t) * 0.5f, 0f), new Vector3(t, h + t, d), openingMat, true);
        Part("PostRight", PrimitiveType.Cube, r, new Vector3((w + t) * 0.5f, (h + t) * 0.5f, 0f), new Vector3(t, h + t, d), openingMat, true);
        Part("Lintel", PrimitiveType.Cube, r, new Vector3(0f, h + t * 0.5f, 0f), new Vector3(w + 2f * t, t, d), openingMat, true);
        Part("TrimLeft", PrimitiveType.Cube, r, new Vector3(-w * 0.5f - 0.05f, h * 0.5f, 0f), new Vector3(0.1f, h, d + 0.04f), hazardMat, false);
        Part("TrimRight", PrimitiveType.Cube, r, new Vector3(w * 0.5f + 0.05f, h * 0.5f, 0f), new Vector3(0.1f, h, d + 0.04f), hazardMat, false);
        Part("TrimTop", PrimitiveType.Cube, r, new Vector3(0f, h + 0.06f, 0f), new Vector3(w, 0.12f, d + 0.04f), hazardMat, false);
        Renderer lampFront = Part("LampFront", PrimitiveType.Cube, r, new Vector3(0f, h + t * 0.5f, d * 0.5f + 0.02f), new Vector3(0.6f, 0.14f, 0.04f), redMat, false).GetComponent<Renderer>();
        Renderer lampBack = Part("LampBack", PrimitiveType.Cube, r, new Vector3(0f, h + t * 0.5f, -d * 0.5f - 0.02f), new Vector3(0.6f, 0.14f, 0.04f), redMat, false).GetComponent<Renderer>();

        Transform left = Panel("LeftPanel", r, -w * 0.25f, +1f);
        Transform right = Panel("RightPanel", r, w * 0.25f, -1f);

        SlidingDoor door = root.AddComponent<SlidingDoor>();
        SerializedObject so = new SerializedObject(door);
        so.FindProperty("leftPanel").objectReferenceValue = left;
        so.FindProperty("rightPanel").objectReferenceValue = right;
        so.FindProperty("slideDistance").floatValue = w * 0.5f;
        SerializedProperty lamps = so.FindProperty("statusLamps");
        lamps.arraySize = 2;
        lamps.GetArrayElementAtIndex(0).objectReferenceValue = lampFront;
        lamps.GetArrayElementAtIndex(1).objectReferenceValue = lampBack;
        so.FindProperty("lampClosedMaterial").objectReferenceValue = redMat;
        so.FindProperty("lampOpenMaterial").objectReferenceValue = greenMat;
        so.ApplyModifiedPropertiesWithoutUndo();
        Save(root, "SlidingDoor");
    }

    /// <param name="innerSide">+1 if the panel's meeting edge is on its +X side.</param>
    private static Transform Panel(string name, Transform parent, float x, float innerSide)
    {
        const float w = DoorWidth * 0.5f, h = DoorHeight;
        Transform pivot = new GameObject(name).transform;
        pivot.SetParent(parent, false);
        pivot.localPosition = new Vector3(x, 0f, 0f);
        Part("Slab", PrimitiveType.Cube, pivot, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, 0.18f), doorMat, true);
        Part("Stripe", PrimitiveType.Cube, pivot, new Vector3(0f, 1.1f, 0f), new Vector3(w, 0.24f, 0.2f), hazardMat, false);
        Part("Edge", PrimitiveType.Cube, pivot, new Vector3(innerSide * (w * 0.5f - 0.03f), h * 0.5f, 0f), new Vector3(0.06f, h, 0.2f), darkMat, false);

        // Moving colliders want a kinematic body. Robots path through the doorway only while it's open.
        Rigidbody rb = pivot.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        NavMeshObstacle obstacle = pivot.gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = new Vector3(0f, h * 0.5f, 0f);
        obstacle.size = new Vector3(w, h, 0.2f);
        obstacle.carving = true;
        pivot.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        return pivot;
    }

    private static void BuildButton()
    {
        GameObject root = new GameObject("DoorButton");
        Transform r = root.transform;
        Part("Backer", PrimitiveType.Cube, r, new Vector3(0f, 0f, 0.02f), new Vector3(0.62f, 0.62f, 0.04f), hazardMat, false);
        Part("Plate", PrimitiveType.Cube, r, new Vector3(0f, 0f, 0.06f), new Vector3(0.44f, 0.44f, 0.05f), darkMat, false);
        Transform cap = Part("Cap", PrimitiveType.Cylinder, r, new Vector3(0f, 0f, 0.12f), new Vector3(0.26f, 0.05f, 0.26f), redMat, false);
        cap.localRotation = Quaternion.Euler(90f, 0f, 0f);

        GameObject lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(r, false);
        lightGo.transform.localPosition = new Vector3(0f, 0f, 0.4f);
        Light glow = lightGo.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.range = 2.5f;
        glow.intensity = 2f;
        glow.color = new Color(1f, 0.1f, 0.05f);
        glow.shadows = LightShadows.None;

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0f, 0.09f);
        box.size = new Vector3(0.62f, 0.62f, 0.18f);

        DoorButton button = root.AddComponent<DoorButton>();
        SerializedObject so = new SerializedObject(button);
        so.FindProperty("cap").objectReferenceValue = cap;
        SerializedProperty glows = so.FindProperty("glowRenderers");
        glows.arraySize = 1;
        glows.GetArrayElementAtIndex(0).objectReferenceValue = cap.GetComponent<Renderer>();
        so.FindProperty("unpressedMaterial").objectReferenceValue = redMat;
        so.FindProperty("pressedMaterial").objectReferenceValue = greenMat;
        so.FindProperty("glowLight").objectReferenceValue = glow;
        so.ApplyModifiedPropertiesWithoutUndo();
        Save(root, "DoorButton");
    }

    private static void BuildTarget()
    {
        GameObject root = new GameObject("DoorTarget");
        Transform r = root.transform;
        Part("Base", PrimitiveType.Cube, r, new Vector3(0f, 0.04f, 0f), new Vector3(0.6f, 0.08f, 0.6f), darkMat, true);
        Part("Post", PrimitiveType.Cube, r, new Vector3(0f, 0.8f, 0f), new Vector3(0.12f, 1.5f, 0.12f), darkMat, true);
        Transform board = Part("Board", PrimitiveType.Cylinder, r, new Vector3(0f, 1.6f, 0f), new Vector3(0.9f, 0.03f, 0.9f), targetMat, false);
        board.localRotation = Quaternion.Euler(90f, 0f, 0f);
        board.gameObject.AddComponent<BoxCollider>(); // flat disc hitbox (mesh bounds 1 x 2 x 1)

        DoorTarget target = root.AddComponent<DoorTarget>();
        SerializedObject so = new SerializedObject(target);
        SerializedProperty hide = so.FindProperty("hideWhenDestroyed");
        hide.arraySize = 1;
        hide.GetArrayElementAtIndex(0).objectReferenceValue = board.gameObject;
        so.FindProperty("explosionMaterial").objectReferenceValue = explosionMat;
        so.ApplyModifiedPropertiesWithoutUndo();
        Save(root, "DoorTarget");
    }

    private static void BuildTriggerZone()
    {
        GameObject root = new GameObject("DoorTriggerZone");
        root.transform.localScale = new Vector3(4f, 3f, 4f);
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.isTrigger = true;
        root.AddComponent<DoorTriggerZone>();
        root.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        Transform marking = Part("FloorMarking", PrimitiveType.Quad, root.transform, new Vector3(0f, -0.5f + 0.004f, 0f), Vector3.one, markingMat, false);
        marking.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Save(root, "DoorTriggerZone");
    }

    // ---------------------------------------------------------------- demo scene

    [MenuItem("Final Shift/Interactables/Add Demo To Kalman Test Scene")]
    public static void AddDemo()
    {
        try
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                openedHere = true;
            }
            bool wasDirty = scene.isDirty;

            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == DemoRootName) Object.DestroyImmediate(go);

            GameObject root = new GameObject(DemoRootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform r = root.transform;

            BuildConveyorDemo(r);
            BuildDoorDemo(r);

            bool active = SceneManager.GetActiveScene() == scene;
            if (active) RobotSetupTool.BakeNavMesh();
            else Debug.LogWarning("[FinalShift] Demo added, but the NavMesh was not rebaked (scene not active). Use Final Shift > Bake NavMesh.");

            EditorSceneManager.MarkSceneDirty(scene);
            if (!wasDirty || openedHere)
            {
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[FinalShift] Interactables demo added to the Kalman test scene and saved.");
            }
            else
            {
                Debug.LogWarning("[FinalShift] Interactables demo added. The scene already had unsaved changes, so it was left unsaved: save it yourself.");
            }
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[FinalShift] Interactables demo failed: " + e);
        }
    }

    private static void BuildConveyorDemo(Transform parent)
    {
        GameObject prefab = Load("ConveyorOpening");
        Transform group = Group("Conveyor Belt", parent);

        ConveyorOpening a = Place(prefab, group, new Vector3(-20f, 0f, -12f), Quaternion.LookRotation(Vector3.forward)).GetComponent<ConveyorOpening>();
        a.name = "ConveyorOpening 1 (out)";
        ConveyorOpening b = Place(prefab, group, new Vector3(-20f, 0f, 10f), Quaternion.LookRotation(Vector3.back)).GetComponent<ConveyorOpening>();
        b.name = "ConveyorOpening 1 (in)";
        SerializedObject so = new SerializedObject(b);
        so.FindProperty("flow").enumValueIndex = (int)ConveyorOpening.Flow.ItemsGoIn;
        so.ApplyModifiedPropertiesWithoutUndo();

        b.RebuildBelt();
        a.RebuildBelt();
    }

    private static void BuildDoorDemo(Transform parent)
    {
        Transform group = Group("Doors", parent);
        const float z = -20f, backZ = -24.75f;
        float half = DoorWidth * 0.5f + DoorPost;

        // Front wall with three doorways, closed rooms behind.
        float[] doorX = { -11f, -1f, 9f };
        float wallStart = -16f, wallEnd = 14f;
        float x0 = wallStart;
        foreach (float dx in doorX)
        {
            Wall(group, new Vector3(x0, 0f, z), new Vector3(dx - half, 0f, z));
            x0 = dx + half;
        }
        Wall(group, new Vector3(x0, 0f, z), new Vector3(wallEnd, 0f, z));
        Wall(group, new Vector3(wallStart, 0f, backZ), new Vector3(wallEnd, 0f, backZ));
        foreach (float wx in new[] { wallStart, -6f, 4f, wallEnd })
            Wall(group, new Vector3(wx, 0f, z - WallThickness * 0.5f), new Vector3(wx, 0f, backZ + WallThickness * 0.5f));

        GameObject doorPrefab = Load("SlidingDoor");
        GameObject zonePrefab = Load("DoorTriggerZone");
        GameObject buttonPrefab = Load("DoorButton");
        GameObject targetPrefab = Load("DoorTarget");

        // 1) Starts open and shuts behind the player once they're inside (Final Shift > Cables adds the lockdown room).
        SlidingDoor door1 = Place(doorPrefab, group, new Vector3(doorX[0], 0f, z), Quaternion.identity).GetComponent<SlidingDoor>();
        door1.name = "SlidingDoor (trigger)";
        GameObject zoneBack = Place(zonePrefab, group, new Vector3(doorX[0], 1.5f, z - 2.4f), Quaternion.identity);
        zoneBack.name = "DoorTriggerZone (inside)";
        zoneBack.transform.localScale = new Vector3(4f, 3f, 3.5f);
        SetDoor(door1, SlidingDoor.OpenCondition.None, "closeTriggers", true, zoneBack.GetComponent<DoorTriggerZone>());
        SerializedObject door1So = new SerializedObject(door1);
        door1So.FindProperty("startOpen").boolValue = true;
        door1So.ApplyModifiedPropertiesWithoutUndo();

        // 2) Button door: shoot both buttons within 4 seconds of each other.
        SlidingDoor door2 = Place(doorPrefab, group, new Vector3(doorX[1], 0f, z), Quaternion.identity).GetComponent<SlidingDoor>();
        door2.name = "SlidingDoor (buttons)";
        GameObject button1 = Place(buttonPrefab, group, new Vector3(doorX[1] - 3.5f, 1.7f, z + WallThickness * 0.5f), Quaternion.identity);
        button1.name = "DoorButton (wall)";
        Transform pillar = Part("ButtonPillar", PrimitiveType.Cube, group, new Vector3(3f, 1.25f, -13f), new Vector3(0.7f, 2.5f, 0.7f), wallMat, true);
        GameObject button2 = Place(buttonPrefab, group, pillar.position + new Vector3(0f, 0.45f, 0.35f), Quaternion.identity);
        button2.name = "DoorButton (pillar)";
        SetDoor(door2, SlidingDoor.OpenCondition.ShootAllButtons, "buttons", true,
            button1.GetComponent<DoorButton>(), button2.GetComponent<DoorButton>());

        // 3) Target door: destroy all three targets.
        SlidingDoor door3 = Place(doorPrefab, group, new Vector3(doorX[2], 0f, z), Quaternion.identity).GetComponent<SlidingDoor>();
        door3.name = "SlidingDoor (targets)";
        Vector3[] targetSpots = { new Vector3(6.5f, 0f, -15f), new Vector3(9.5f, 0f, -12.5f), new Vector3(12f, 0f, -16f) };
        Object[] targets = new Object[targetSpots.Length];
        for (int i = 0; i < targetSpots.Length; i++)
        {
            GameObject t = Place(targetPrefab, group, targetSpots[i], Quaternion.identity);
            t.name = "DoorTarget " + (i + 1);
            targets[i] = t.GetComponent<DoorTarget>();
        }
        SetDoor(door3, SlidingDoor.OpenCondition.DestroyAllTargets, "targets", true, targets);

        // A little reward behind each door.
        string[] rewards = { "Assets/Prefabs/HealthPickup.prefab", "Assets/Prefabs/AmmoPickup.prefab", "Assets/Prefabs/HealthPickup.prefab" };
        for (int i = 0; i < doorX.Length; i++)
        {
            GameObject reward = AssetDatabase.LoadAssetAtPath<GameObject>(rewards[i]);
            if (reward != null) Place(reward, group, new Vector3(doorX[i], 0.5f, -22.8f), Quaternion.identity);
        }
    }

    private static void SetDoor(SlidingDoor door, SlidingDoor.OpenCondition condition, string listName, bool stayOpen, params Object[] links)
    {
        SerializedObject so = new SerializedObject(door);
        so.FindProperty("openCondition").enumValueIndex = (int)condition;
        so.FindProperty("stayOpen").boolValue = stayOpen;
        SerializedProperty list = so.FindProperty(listName);
        list.arraySize = links.Length;
        for (int i = 0; i < links.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = links[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Straight wall from a to b (on the floor), built from 2 m panels so the texture keeps its scale.</summary>
    private static void Wall(Transform parent, Vector3 a, Vector3 b)
    {
        Vector3 dir = b - a;
        float length = dir.magnitude;
        if (length < 0.05f) return;
        dir /= length;
        Quaternion rot = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up)); // panel faces sideways, X along the wall
        int count = Mathf.Max(1, Mathf.RoundToInt(length / 2f));
        float panel = length / count;
        for (int i = 0; i < count; i++)
        {
            Vector3 center = a + dir * (panel * (i + 0.5f)) + Vector3.up * (WallHeight * 0.5f);
            Transform t = Part("Wall", PrimitiveType.Cube, parent, Vector3.zero, new Vector3(panel, WallHeight, WallThickness), wallMat, true);
            t.SetPositionAndRotation(center, rot);
        }
    }

    // ---------------------------------------------------------------- helpers

    private static Transform Group(string name, Transform parent)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    private static GameObject Load(string name)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{name}.prefab");
        if (prefab == null) throw new System.Exception($"Missing prefab {PrefabFolder}/{name}.prefab. Run Final Shift > Interactables > Build Prefabs first.");
        return prefab;
    }

    private static GameObject Place(GameObject prefab, Transform parent, Vector3 position, Quaternion rotation)
    {
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(position, rotation);
        return go;
    }

    private static Transform Part(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool keepCollider)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        Transform t = go.transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        t.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return t;
    }

    private static GameObject Save(GameObject root, string name)
    {
        try
        {
            string path = $"{PrefabFolder}/{name}.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && !overwritePrefabs) return existing; // keeps your edits and the scene's links
            return PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
