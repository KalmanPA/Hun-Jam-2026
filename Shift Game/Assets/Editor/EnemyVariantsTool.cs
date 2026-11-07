using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.ProBuilder;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the robot prefab variants (all are variants of Assets/Prefabs/Robot.prefab):
///  - RobotGear3Tutorial: no gun, never shoots, slides wall to wall on one axis to block the player,
///    holds a big shield (with a roof) that only a full gear 3 burst knocks away.
///  - RobotShield: holds the same shield, shoots out of its middle, moves like a normal robot.
///  - RobotSniper: never moves, long range, accurate laser shots like the player's gear 1.
/// The shield is made of ProBuilder shapes sized by their faces (not by scaling), so tiling textures don't stretch.
/// Menu: Final Shift > Enemy Variants.
/// </summary>
[InitializeOnLoad]
public static class EnemyVariantsTool
{
    private const string BasePath = "Assets/Prefabs/Robot.prefab";
    private const string TutorialPath = "Assets/Prefabs/RobotGear3Tutorial.prefab";
    private const string ShieldPath = "Assets/Prefabs/RobotShield.prefab";
    private const string SniperPath = "Assets/Prefabs/RobotSniper.prefab";
    private const string ShieldMaterialPath = "Assets/ProtMaterials/EnemyShield.mat";
    // Creating this file (in the project's Temp folder) builds the variants once on the next editor reload.
    private const string TriggerFile = "Temp/FinalShift_BuildEnemyVariants";

    // Pieces of the robot model that make up its arm gun (hidden on the shield robots).
    private static readonly string[] GunPieces = { "Cube.016", "ShootMuzzle", "Cylinder.011" };

    // Shield layout, in the robot's local space. The pivot sits at the robot's hands.
    private static readonly Vector3 ShieldPivot = new Vector3(0.04f, 0.7f, 0.75f);
    private const float ShieldWidth = 2.0f;
    private const float ShieldHeight = 2.4f;
    private const float ShieldThickness = 0.08f;
    private const float ShieldPlateZ = 0.35f;       // plate centre, forward of the pivot
    private const float ShieldPlateCenterY = 0.95f; // plate centre, above the pivot
    private const float RoofDepth = 1.5f;           // how far the roof reaches back over the robot
    // Glass window in the plate, in front of the robot's face (face is about 2 m up).
    private const float WindowWidth = 0.9f;
    private const float WindowHeight = 0.5f;
    private const float WindowCenterY = 1.35f;      // above the pivot
    private const string GlassMaterialPath = "Assets/ProtMaterials/EnemyShieldGlass.mat";

    static EnemyVariantsTool()
    {
        if (!File.Exists(TriggerFile)) return;
        // "overwrite" in the trigger file rebuilds existing variants too.
        bool overwrite = File.ReadAllText(TriggerFile).Contains("overwrite");
        File.Delete(TriggerFile);
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            BuildAll(overwrite);
        };
    }

    [MenuItem("Final Shift/Enemy Variants/Build Missing Variants")]
    public static void BuildMissingMenu() => BuildAll(false);

    [MenuItem("Final Shift/Enemy Variants/Rebuild All Variants (overwrites them)")]
    public static void RebuildAllMenu()
    {
        if (EditorUtility.DisplayDialog("Rebuild enemy variants",
                "This overwrites RobotGear3Tutorial, RobotShield and RobotSniper, losing any changes made to them.", "Rebuild", "Cancel"))
            BuildAll(true);
    }

    private static void BuildAll(bool overwrite)
    {
        try
        {
            GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePath);
            if (basePrefab == null) { Debug.LogError("[FinalShift] Enemy variants: Robot.prefab not found."); return; }

            Build(basePrefab, TutorialPath, overwrite, SetUpTutorial);
            Build(basePrefab, ShieldPath, overwrite, SetUpShieldRobot);
            Build(basePrefab, SniperPath, overwrite, SetUpSniper);
            AssetDatabase.SaveAssets();
            Debug.Log("[FinalShift] Enemy variants done.");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[FinalShift] Enemy variants failed: " + e);
        }
    }

    private static void Build(GameObject basePrefab, string path, bool overwrite, System.Action<GameObject> setUp)
    {
        if (!overwrite && AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
        {
            Debug.Log($"[FinalShift] {path} already exists, skipped.");
            return;
        }

        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab, preview);
            instance.name = Path.GetFileNameWithoutExtension(path);
            setUp(instance);
            PrefabUtility.SaveAsPrefabAsset(instance, path);
            Debug.Log($"[FinalShift] Saved variant {path}.");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    // ---------------------------------------------------------------- variants

    private static void SetUpTutorial(GameObject robot)
    {
        HideGun(robot);
        EnemyShield shield = BuildShield(robot.transform, false);

        SerializedObject so = new SerializedObject(robot.GetComponent<RobotEnemy>());
        so.FindProperty("movementMode").enumValueIndex = (int)RobotEnemy.MovementMode.BlockAxis;
        so.FindProperty("canShoot").boolValue = false;
        so.FindProperty("shield").objectReferenceValue = shield;
        so.FindProperty("calmFaceWhileShielded").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Quick enough to keep up with the player and stay in front of them.
        NavMeshAgent agent = robot.GetComponent<NavMeshAgent>();
        agent.speed = 5f;
        agent.acceleration = 20f;
    }

    private static void SetUpShieldRobot(GameObject robot)
    {
        HideGun(robot);
        EnemyShield shield = BuildShield(robot.transform, true);

        // Shoots out of the middle of the shield. The muzzle belongs to the robot (not the shield),
        // so it stays put if the shield is knocked away.
        Transform muzzle = new GameObject("ShieldMuzzle").transform;
        muzzle.SetParent(robot.transform, false);
        muzzle.localPosition = ShieldPivot + new Vector3(0f, 0.85f, ShieldPlateZ + 0.2f);
        Light flash = new GameObject("MuzzleFlash").AddComponent<Light>();
        flash.transform.SetParent(muzzle, false);
        flash.type = LightType.Point;
        flash.color = new Color(1f, 0.35f, 0.2f);
        flash.range = 5f;
        flash.intensity = 4f;
        flash.shadows = LightShadows.None;
        flash.enabled = false;

        SerializedObject so = new SerializedObject(robot.GetComponent<RobotEnemy>());
        so.FindProperty("shield").objectReferenceValue = shield;
        SerializedProperty muzzles = so.FindProperty("muzzles");
        muzzles.arraySize = 1;
        muzzles.GetArrayElementAtIndex(0).objectReferenceValue = muzzle;
        so.FindProperty("muzzleFlash").objectReferenceValue = flash;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetUpSniper(GameObject robot)
    {
        SerializedObject so = new SerializedObject(robot.GetComponent<RobotEnemy>());
        so.FindProperty("movementMode").enumValueIndex = (int)RobotEnemy.MovementMode.Stationary;
        so.FindProperty("shotType").enumValueIndex = (int)RobotEnemy.ShotType.Laser;
        so.FindProperty("detectionRange").floatValue = 60f;
        so.FindProperty("attackRange").floatValue = 55f;
        so.FindProperty("bulletRange").floatValue = 70f;
        so.FindProperty("bulletDamage").floatValue = 15f;
        so.FindProperty("burstCount").intValue = 1;
        so.FindProperty("windUp").floatValue = 1f;
        so.FindProperty("burstCooldown").vector2Value = new Vector2(2.5f, 4f);
        so.FindProperty("fireAngle").floatValue = 8f;
        so.FindProperty("bulletColor").colorValue = new Color(1f, 0.25f, 0.1f); // gear 1 colour
        so.FindProperty("laserWidth").floatValue = 0.12f;
        so.FindProperty("laserDuration").floatValue = 0.25f;
        so.ApplyModifiedPropertiesWithoutUndo();

        // More accurate, and stays accurate at long range.
        SerializedObject aim = new SerializedObject(robot.GetComponent<EnemyAim>());
        aim.FindProperty("maxHitChance").floatValue = 0.85f;
        aim.FindProperty("fullAccuracyDistance").floatValue = 20f;
        aim.FindProperty("longDistance").floatValue = 60f;
        aim.FindProperty("longDistanceMultiplier").floatValue = 0.6f;
        aim.FindProperty("fastPlayerMultiplier").floatValue = 0.55f;
        aim.FindProperty("warningShots").intValue = 1;
        aim.FindProperty("warmUpTime").floatValue = 2f;
        aim.FindProperty("nearMissGap").vector2Value = new Vector2(0.2f, 0.6f);
        aim.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void HideGun(GameObject robot)
    {
        foreach (Transform t in robot.GetComponentsInChildren<Transform>(true))
            if (System.Array.IndexOf(GunPieces, t.name) >= 0)
                t.gameObject.SetActive(false);
    }

    // ---------------------------------------------------------------- shield

    private static EnemyShield BuildShield(Transform robot, bool withGunPort)
    {
        Material mat = GetShieldMaterial();

        GameObject root = new GameObject("Shield");
        root.transform.SetParent(robot, false);
        root.transform.localPosition = ShieldPivot;
        shatterRandom = new System.Random(1234); // same cracks on every rebuild

        // Front plate with a glass window at the robot's face, pre-shattered into random shards so it can
        // break apart: bottom, top, and the two sides of the window.
        float plateBottom = ShieldPlateCenterY - ShieldHeight * 0.5f;
        float plateTopY = ShieldPlateCenterY + ShieldHeight * 0.5f;
        float winBottom = WindowCenterY - WindowHeight * 0.5f;
        float winTop = WindowCenterY + WindowHeight * 0.5f;
        float halfW = ShieldWidth * 0.5f;
        float halfWin = WindowWidth * 0.5f;
        GridXY("PlateBottom", root.transform, -halfW, halfW, plateBottom, winBottom, ShieldPlateZ, 5, 3, mat);
        GridXY("PlateTop", root.transform, -halfW, halfW, winTop, plateTopY, ShieldPlateZ, 5, 1, mat);
        GridXY("PlateLeft", root.transform, -halfW, -halfWin, winBottom, winTop, ShieldPlateZ, 1, 2, mat);
        GridXY("PlateRight", root.transform, halfWin, halfW, winBottom, winTop, ShieldPlateZ, 1, 2, mat);
        // The glass still stops bullets (it's part of the shield). Shattered too.
        Material glass = GetGlassMaterial();
        GridXY("Glass", root.transform, -halfWin, halfWin, winBottom, winTop, ShieldPlateZ, 4, 2, glass, ShieldThickness * 0.4f);

        // Roof: from the plate's top edge back over the robot's head, also shattered.
        float plateFront = ShieldPlateZ + ShieldThickness * 0.5f;
        float plateTop = ShieldPlateCenterY + ShieldHeight * 0.5f;
        GridXZ("Roof", root.transform, -halfW, halfW, plateFront - RoofDepth, plateFront, plateTop + ShieldThickness * 0.5f, 4, 3, mat);

        // Handles the hands hold, bridging from the hands to the back of the plate.
        float handleLength = ShieldPlateZ - ShieldThickness * 0.5f + 0.05f;
        foreach (float x in new[] { -0.74f, 0.74f })
            Piece(x < 0f ? "HandleLeft" : "HandleRight", root.transform, new Vector3(0.14f, 0.14f, handleLength),
                  new Vector3(x, 0f, handleLength * 0.5f - 0.05f), mat);

        if (withGunPort)
            Piece("GunPort", root.transform, new Vector3(0.3f, 0.3f, 0.1f),
                  new Vector3(0f, 0.85f, plateFront + 0.05f), mat);

        return root.AddComponent<EnemyShield>();
    }

    // Fixed seed, so rebuilding gives the same cracks every time.
    private static System.Random shatterRandom = new System.Random(1234);

    /// <summary>
    /// Vertical plate from (x0,y0) to (x1,y1) at depth z, pre-shattered into random shards (Voronoi cells around
    /// cols x rows jittered seed points).
    /// </summary>
    private static void GridXY(string name, Transform parent, float x0, float x1, float y0, float y1, float z,
                               int cols, int rows, Material mat, float thickness = ShieldThickness)
    {
        int i = 0;
        foreach (System.Collections.Generic.List<Vector2> cell in Shatter(x0, x1, y0, y1, cols, rows))
            Shard($"{name}_{i++}", parent, cell, z, thickness, false, mat);
    }

    /// <summary>Horizontal plate from (x0,z0) to (x1,z1) at height y, pre-shattered like <see cref="GridXY"/>.</summary>
    private static void GridXZ(string name, Transform parent, float x0, float x1, float z0, float z1, float y,
                               int cols, int rows, Material mat)
    {
        int i = 0;
        foreach (System.Collections.Generic.List<Vector2> cell in Shatter(x0, x1, z0, z1, cols, rows))
            Shard($"{name}_{i++}", parent, cell, y, ShieldThickness, true, mat);
    }

    /// <summary>Voronoi cells of jittered seeds inside a rectangle. Every cell is a convex polygon (counter-clockwise).</summary>
    private static System.Collections.Generic.List<System.Collections.Generic.List<Vector2>> Shatter(
        float x0, float x1, float y0, float y1, int cols, int rows)
    {
        var seeds = new System.Collections.Generic.List<Vector2>();
        float w = (x1 - x0) / cols, h = (y1 - y0) / rows;
        for (int c = 0; c < cols; c++)
            for (int r = 0; r < rows; r++)
                seeds.Add(new Vector2(x0 + w * (c + 0.5f + Rand(-0.42f, 0.42f)), y0 + h * (r + 0.5f + Rand(-0.42f, 0.42f))));

        var cells = new System.Collections.Generic.List<System.Collections.Generic.List<Vector2>>();
        for (int i = 0; i < seeds.Count; i++)
        {
            var poly = new System.Collections.Generic.List<Vector2>
                { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };
            for (int j = 0; j < seeds.Count && poly.Count >= 3; j++)
            {
                if (i == j) continue;
                Vector2 mid = (seeds[i] + seeds[j]) * 0.5f;
                poly = ClipHalfPlane(poly, mid, seeds[j] - seeds[i]);
            }
            if (poly.Count >= 3) cells.Add(poly);
        }
        return cells;
    }

    /// <summary>Keeps the part of a convex polygon where (p - point)·normal &lt;= 0.</summary>
    private static System.Collections.Generic.List<Vector2> ClipHalfPlane(System.Collections.Generic.List<Vector2> poly, Vector2 point, Vector2 normal)
    {
        var result = new System.Collections.Generic.List<Vector2>();
        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
            float da = Vector2.Dot(a - point, normal), db = Vector2.Dot(b - point, normal);
            if (da <= 0f) result.Add(a);
            if ((da < 0f && db > 0f) || (da > 0f && db < 0f))
                result.Add(Vector2.Lerp(a, b, da / (da - db)));
        }
        return result;
    }

    private static float Rand(float min, float max) => min + (float)shatterRandom.NextDouble() * (max - min);

    /// <summary>
    /// One shard: the convex polygon extruded to the given thickness, built as a ProBuilder mesh at its real size
    /// (no scaling, so textures tile), with a convex mesh collider. Horizontal shards lie in the XZ plane.
    /// </summary>
    private static void Shard(string name, Transform parent, System.Collections.Generic.List<Vector2> poly2D, float depth,
                              float thickness, bool horizontal, Material mat)
    {
        Vector2 c2 = Vector2.zero;
        foreach (Vector2 p in poly2D) c2 += p;
        c2 /= poly2D.Count;

        // Local points around the shard centre; the polygon's plane axes are (u, v), thickness along n.
        Vector3 U = Vector3.right;
        Vector3 V = horizontal ? Vector3.forward : Vector3.up;
        Vector3 N = horizontal ? Vector3.up : Vector3.forward; // thickness axis
        float half = thickness * 0.5f;
        int n = poly2D.Count;
        Vector3 P(int i, float side) => U * (poly2D[i].x - c2.x) + V * (poly2D[i].y - c2.y) + N * (half * side);

        var positions = new System.Collections.Generic.List<Vector3>();
        var faces = new System.Collections.Generic.List<Face>();

        void AddFace(System.Collections.Generic.List<Vector3> verts, Vector3 outward)
        {
            int start = positions.Count;
            positions.AddRange(verts);
            var tris = new System.Collections.Generic.List<int>();
            for (int k = 1; k < verts.Count - 1; k++)
            {
                int a = start, b = start + k, c = start + k + 1;
                // Unity front faces: normal = Cross(b - a, c - a). Flip the triangle if it points inward.
                if (Vector3.Dot(Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]), outward) < 0f)
                    (b, c) = (c, b);
                tris.Add(a); tris.Add(b); tris.Add(c);
            }
            faces.Add(new Face(tris));
        }

        var front = new System.Collections.Generic.List<Vector3>();
        var back = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i < n; i++) { front.Add(P(i, 1f)); back.Add(P(i, -1f)); }
        AddFace(front, N);
        AddFace(back, -N);
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector3 edge = P(j, 0f) - P(i, 0f);
            Vector3 outward = Vector3.Cross(edge, N).normalized;
            Vector3 mid = (P(i, 0f) + P(j, 0f)) * 0.5f;
            if (Vector3.Dot(outward, mid) < 0f) outward = -outward; // shard is convex around its centre
            AddFace(new System.Collections.Generic.List<Vector3> { P(i, 1f), P(j, 1f), P(j, -1f), P(i, -1f) }, outward);
        }

        ProBuilderMesh pb = ProBuilderMesh.Create(positions, faces);
        pb.gameObject.name = name;
        pb.transform.SetParent(parent, false);
        pb.transform.localPosition = horizontal ? new Vector3(c2.x, depth, c2.y) : new Vector3(c2.x, c2.y, depth);
        pb.transform.localRotation = Quaternion.identity;
        pb.transform.localScale = Vector3.one;
        pb.ToMesh();
        pb.Refresh();
        pb.GetComponent<MeshRenderer>().sharedMaterial = mat;

        MeshCollider mc = pb.gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = pb.GetComponent<MeshFilter>().sharedMesh;
        mc.convex = true;
    }

    /// <summary>A ProBuilder box built at its real size (faces moved, not scaled), with a convex mesh collider.</summary>
    private static void Piece(string name, Transform parent, Vector3 size, Vector3 localCenter, Material mat)
    {
        ProBuilderMesh pb = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
        pb.gameObject.name = name;
        pb.transform.SetParent(parent, false);
        pb.transform.localPosition = localCenter;
        pb.transform.localRotation = Quaternion.identity;
        pb.transform.localScale = Vector3.one;
        foreach (Face f in pb.faces) f.submeshIndex = 0;
        pb.ToMesh();
        pb.Refresh();
        pb.GetComponent<MeshRenderer>().sharedMaterial = mat;

        MeshCollider mc = pb.gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = pb.GetComponent<MeshFilter>().sharedMesh;
        mc.convex = true;
    }

    private static Material GetGlassMaterial()
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(GlassMaterialPath);
        if (mat != null) return mat;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        mat = new Material(shader) { name = "EnemyShieldGlass" };
        mat.SetColor("_BaseColor", new Color(0.6f, 0.85f, 1f, 0.3f));
        mat.SetFloat("_Smoothness", 0.9f);
        mat.SetFloat("_Metallic", 0f);
        // URP transparent surface.
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.SetShaderPassEnabled("DepthOnly", false);
        mat.SetShaderPassEnabled("ShadowCaster", false);
        AssetDatabase.CreateAsset(mat, GlassMaterialPath);
        return mat;
    }

    private static Material GetShieldMaterial()
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(ShieldMaterialPath);
        if (mat != null) return mat;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        mat = new Material(shader) { name = "EnemyShield" };
        mat.SetColor("_BaseColor", new Color(0.5f, 0.52f, 0.55f));
        mat.SetFloat("_Smoothness", 0.15f);
        mat.SetFloat("_Metallic", 0.3f);
        AssetDatabase.CreateAsset(mat, ShieldMaterialPath);
        return mat;
    }
}




