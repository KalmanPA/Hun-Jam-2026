using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Kind = DarkFactoryLightingTool.Kind;

/// <summary>
/// Builds the final boss fight scene (Assets/Scenes/BossFight.unity) from scratch:
///  - a big dark factory hall with a high catwalk along the back wall and two high sniper ledges on the sides,
///  - the boss machine in the middle: treads, a body with a front ledge, a torso with two cannons, and an old
///    monitor head with the robot face on its screen and a health bar under it,
///  - four wire hatches in a trench on the boss's back, high up, each with a red/green beacon and a shield bubble,
///  - six shield targets around the room (some on the high catwalk and ledges) with beams to the boss,
///  - four endless spawners: two over the sniper ledges (snipers), two over the floor (regular and shield robots),
///  - the player at the front, the dark factory look (fog, hanging lamps, flat-colour materials), baked NavMesh,
///  - the ending cutscene object.
///
/// Runs once by itself the first time Unity loads this script (if the scene doesn't exist yet), and any time from
/// Final Shift > Boss > Rebuild Boss Fight Scene. A rebuild throws away hand edits to the scene, so tweak the
/// numbers here (or copy the scene) before rebuilding.
/// </summary>
[InitializeOnLoad]
public static class BossLevelBuilder
{
    public const string ScenePath = "Assets/Scenes/BossFight.unity";
    private const string NavMeshFolder = "Assets/Scenes/BossFight";
    private const string MaterialFolder = "Assets/ProtMaterials/Boss";
    private const string AutoRunPrefKey = "FinalShift.BossLevel.AutoBuilt";

    private const string PlayerPrefab = "Assets/Prefabs/Player.prefab";
    private const string RobotPrefab = "Assets/Prefabs/Robot.prefab";
    private const string ShieldRobotPrefab = "Assets/Prefabs/RobotShield.prefab";
    private const string SniperPrefab = "Assets/Prefabs/RobotSniper.prefab";
    private const string SpawnerPrefab = "Assets/Prefabs/Interactables/EnemySpawner.prefab";
    private const string TargetPrefab = "Assets/Prefabs/Interactables/DoorTarget.prefab";
    private const string CratePrefab = "Assets/Prefabs/Interactables/Crate_Big.prefab";
    private const string FaceTexture = "Assets/Art/ROBOMAT/Robot.fbm/Deanall.png";
    private const string ShieldGlass = "Assets/ProtMaterials/EnemyShieldGlass.mat";
    private const string BulletMat = "Assets/ProtMaterials/EnemyBullet.mat";
    private const string ExplosionMat = "Assets/ProtMaterials/RobotExplosion.mat";

    // ---- Room (metres). The boss stands at the origin and faces -Z, toward the player's start.
    private const float HalfWidth = 20f;     // x from -20 to 20
    private const float FrontZ = -24f;
    private const float BackZ = 14f;
    private const float CeilingY = 18f;
    private const float CatwalkY = 4.5f;     // back catwalk top: needs the gear 4 rocket jump
    private const float CatwalkDepth = 4f;
    private const float LedgeY = 7f;         // sniper ledges: rocket jump + jump
    private const float DeckY = 5f;          // top of the boss's body, where the hatches sit

    static BossLevelBuilder()
    {
        if (File.Exists(ScenePath) || EditorPrefs.GetBool(AutoRunPrefKey, false)) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || File.Exists(ScenePath)) return;
            EditorPrefs.SetBool(AutoRunPrefKey, true); // only try once by itself, even if it fails
            Build(false);
        };
    }

    [MenuItem("Final Shift/Boss/Rebuild Boss Fight Scene")]
    public static void RebuildMenu()
    {
        if (File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog("Rebuild boss fight scene?",
                ScenePath + " will be built again from scratch. Hand edits to that scene are lost.", "Rebuild", "Cancel"))
            return;
        Build(true);
    }

    [MenuItem("Final Shift/Boss/Open Boss Fight Scene")]
    public static void OpenMenu()
    {
        if (!File.Exists(ScenePath)) { Build(true); return; }
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsToSave())
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    // ------------------------------------------------------------------ build

    private static void Build(bool openWhenDone)
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = default;
        bool additive = false;
        try
        {
            // Build next to whatever is open, so the open scene isn't touched. If the boss scene itself is open,
            // or an untitled scene is open (additive needs saved scenes), replace everything instead.
            bool bossOpen = SceneManager.GetSceneByPath(ScenePath).isLoaded;
            bool untitledOpen = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path)) untitledOpen = true;

            if (bossOpen || untitledOpen)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsToSave()) return;
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                additive = true;
            }
            SceneManager.SetActiveScene(scene);

            if (AssetDatabase.IsValidFolder(NavMeshFolder)) AssetDatabase.DeleteAsset(NavMeshFolder);
            EnsureFolder(MaterialFolder);

            BuildRoom(scene, out Transform levelRoot);
            BossMachine boss = BuildBoss(scene);
            DoorTarget[] targets = BuildTargets(scene, boss, out LineRenderer[] beams);
            EnemySpawner[] spawners = BuildSpawners(scene);
            BuildPlayer(scene);
            BossEndingCutscene ending = BuildEnding(scene, boss);
            LinkBoss(boss, targets, beams, spawners, ending);
            BuildLights(scene);

            DarkFactoryLightingTool.ApplyEnvironment(scene);
            RenderSettings.fogStartDistance = 14f; // bigger hall than the corridors: see the boss from the start
            RenderSettings.fogEndDistance = 62f;

            EditorSceneManager.SaveScene(scene, ScenePath);

            // NavMesh (normal + the wide "Shielded" agent type) needs the saved scene path for its data assets.
            NavMeshSurface surface = levelRoot.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            int shieldedId = ShieldNavMeshTool.EnsureAgentType();
            ShieldNavMeshTool.BakeScene(scene, shieldedId, true);
            EditorSceneManager.SaveScene(scene);

            AddToBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("[FinalShift] Boss fight scene built: " + ScenePath +
                      ". Menu Final Shift > Boss > Rebuild Boss Fight Scene builds it again.");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[FinalShift] Boss fight scene build failed: " + e);
        }
        finally
        {
            if (additive && scene.IsValid() && scene.isLoaded)
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        if (openWhenDone && File.Exists(ScenePath) && SceneManager.GetActiveScene().path != ScenePath &&
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsToSave())
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    // ------------------------------------------------------------------ room

    private static void BuildRoom(Scene scene, out Transform levelRoot)
    {
        Material floor = DarkFactoryLightingTool.LevelMaterial(Kind.Floor);
        Material wall = DarkFactoryLightingTool.LevelMaterial(Kind.Wall);
        Material ceiling = DarkFactoryLightingTool.LevelMaterial(Kind.Ceiling);
        Material pillar = DarkFactoryLightingTool.LevelMaterial(Kind.Pillar);
        Material block = DarkFactoryLightingTool.LevelMaterial(Kind.Block);
        Material steel = DarkFactoryLightingTool.LevelMaterial(Kind.Stairs);
        Material stripe = DarkFactoryLightingTool.LevelMaterial(Kind.Door);

        GameObject root = new GameObject("== Level ==");
        levelRoot = root.transform;
        Transform r = root.transform;

        float width = HalfWidth * 2f, depth = BackZ - FrontZ, midZ = (BackZ + FrontZ) * 0.5f;
        Box("Floor", r, new Vector3(0f, -0.25f, midZ), new Vector3(width, 0.5f, depth), floor);
        Box("Ceiling", r, new Vector3(0f, CeilingY + 0.25f, midZ), new Vector3(width, 0.5f, depth), ceiling);
        Box("Wall Back", r, new Vector3(0f, CeilingY * 0.5f, BackZ + 0.25f), new Vector3(width + 1f, CeilingY, 0.5f), wall);
        Box("Wall Front", r, new Vector3(0f, CeilingY * 0.5f, FrontZ - 0.25f), new Vector3(width + 1f, CeilingY, 0.5f), wall);
        Box("Wall West", r, new Vector3(-HalfWidth - 0.25f, CeilingY * 0.5f, midZ), new Vector3(0.5f, CeilingY, depth), wall);
        Box("Wall East", r, new Vector3(HalfWidth + 0.25f, CeilingY * 0.5f, midZ), new Vector3(0.5f, CeilingY, depth), wall);

        // Back catwalk, high above the floor: the only good view into the hatches on the boss's back.
        float cz = BackZ - CatwalkDepth * 0.5f;
        Box("Catwalk Back", r, new Vector3(0f, CatwalkY - 0.2f, cz), new Vector3(width, 0.4f, CatwalkDepth), steel);
        Box("Catwalk Edge Stripe", r, new Vector3(0f, CatwalkY - 0.2f, BackZ - CatwalkDepth - 0.02f), new Vector3(width, 0.4f, 0.05f), stripe, false);
        foreach (float x in new[] { -15f, -5f, 5f, 15f })
            Box("Catwalk Pillar", r, new Vector3(x, (CatwalkY - 0.4f) * 0.5f, BackZ - CatwalkDepth + 0.3f), new Vector3(0.5f, CatwalkY - 0.4f, 0.5f), pillar);

        // Sniper ledges high on both side walls, with a low parapet the snipers shoot over.
        foreach (int side in new[] { -1, 1 })
        {
            string n = side < 0 ? "West" : "East";
            float x = side * (HalfWidth - 2f);
            Box("Sniper Ledge " + n, r, new Vector3(x, LedgeY - 0.2f, -3f), new Vector3(4f, 0.4f, 14f), steel);
            Box("Sniper Ledge Parapet " + n, r, new Vector3(side * (HalfWidth - 4.1f), LedgeY + 0.45f, -3f), new Vector3(0.2f, 0.9f, 14f), block);
            Box("Ledge Edge Stripe " + n, r, new Vector3(side * (HalfWidth - 4.02f), LedgeY - 0.2f, -3f), new Vector3(0.05f, 0.4f, 14f), stripe, false);
            foreach (float z in new[] { -9.5f, 3.5f })
                Box("Ledge Pillar " + n, r, new Vector3(side * (HalfWidth - 4.3f), (LedgeY - 0.4f) * 0.5f, z), new Vector3(0.4f, LedgeY - 0.4f, 0.4f), pillar);

            // Floor-to-ceiling columns as cover in the front half.
            Box("Column " + n, r, new Vector3(side * 10f, CeilingY * 0.5f, -14f), new Vector3(1.2f, CeilingY, 1.2f), pillar);
            // Old machines along the walls.
            Box("Machine " + n, r, new Vector3(side * (HalfWidth - 1.5f), 1.5f, -18f), new Vector3(3f, 3f, 4f), block);
        }

        // Crates as low cover.
        GameObject crate = Load(CratePrefab);
        if (crate != null)
        {
            Vector3[] spots = { new Vector3(-6f, 0f, -12f), new Vector3(6.5f, 0f, -11f), new Vector3(-13f, 0f, 6f), new Vector3(13f, 0f, 5f), new Vector3(-3f, 0f, -17f) };
            foreach (Vector3 p in spots)
            {
                GameObject c = (GameObject)PrefabUtility.InstantiatePrefab(crate, scene);
                c.transform.SetParent(r, true);
                c.transform.SetPositionAndRotation(p + Vector3.up * 0.95f, Quaternion.Euler(0f, Random.Range(-20f, 20f), 0f));
            }
        }
    }

    // ------------------------------------------------------------------ boss

    private static BossMachine BuildBoss(Scene scene)
    {
        Material body = Flat("Boss_Body", new Color(0.25f, 0.29f, 0.25f), 0.6f);
        Material dark = Flat("Boss_Dark", new Color(0.11f, 0.11f, 0.12f), 0.5f);
        Material casing = Flat("Boss_MonitorCasing", new Color(0.60f, 0.56f, 0.46f), 0.55f); // old yellowed monitor plastic
        Material rust = Flat("Boss_Rust", new Color(0.48f, 0.25f, 0.13f), 0.6f);
        Material yellow = Flat("Boss_Yellow", new Color(0.78f, 0.60f, 0.12f), 0.4f);
        Material moving = Flat("Boss_HatchLid", new Color(0.78f, 0.60f, 0.12f), 0.4f, true);
        Material inside = Unlit("Boss_Inside", new Color(0.02f, 0.02f, 0.025f));
        Material barBack = Unlit("Boss_BarBack", new Color(0.03f, 0.03f, 0.03f));
        Material barTrail = Unlit("Boss_BarTrail", new Color(0.95f, 0.85f, 0.6f));
        Material barFill = Unlit("Boss_BarFill", new Color(0.9f, 0.08f, 0.04f));
        Material emitter = Unlit("Boss_Emitter", new Color(0.45f, 0.85f, 1f));
        Material lampRed = Unlit("Boss_LampClosed", new Color(1f, 0.12f, 0.06f));
        Material lampGreen = Unlit("Boss_LampOpen", new Color(0.2f, 1f, 0.25f));
        Material glass = AssetDatabase.LoadAssetAtPath<Material>(ShieldGlass);
        Material spark = AssetDatabase.LoadAssetAtPath<Material>(ExplosionMat);
        Material[] wireColours =
        {
            Flat("Boss_WireRed", new Color(0.7f, 0.08f, 0.05f), 0.2f, true),
            Flat("Boss_WireYellow", new Color(0.85f, 0.7f, 0.1f), 0.2f, true),
            Flat("Boss_WireBlue", new Color(0.1f, 0.25f, 0.7f), 0.2f, true),
            Flat("Boss_WireBlack", new Color(0.06f, 0.06f, 0.06f), 0.2f, true),
        };

        GameObject root = new GameObject("== Boss ==");
        Transform b = root.transform;

        // Treads and body. The body's top is the deck at DeckY; the front part is a ledge the ending robots stand on.
        Box("Tread West", b, new Vector3(-4.6f, 0.85f, -0.25f), new Vector3(1.4f, 1.7f, 12.5f), dark);
        Box("Tread East", b, new Vector3(4.6f, 0.85f, -0.25f), new Vector3(1.4f, 1.7f, 12.5f), dark);
        Box("Body", b, new Vector3(0f, DeckY * 0.5f, -0.25f), new Vector3(8f, DeckY, 10.5f), body);
        Box("Body Stripe", b, new Vector3(0f, DeckY - 0.35f, -0.25f), new Vector3(8.04f, 0.25f, 10.54f), yellow, false);
        Box("Ledge Edge Stripe", b, new Vector3(0f, DeckY - 0.1f, -5.52f), new Vector3(8.02f, 0.2f, 0.04f), yellow, false);
        for (int i = 0; i < 3; i++)
            Box("Vent", b, new Vector3(-2.5f + i * 2.5f, 2f, -5.52f), new Vector3(1.6f, 1.4f, 0.06f), dark, false);

        // Shoulders: walls on both sides of the hatch trench, so the hatches can't be seen from the floor.
        Box("Shoulder West", b, new Vector3(-3.5f, DeckY + 0.8f, 2.5f), new Vector3(1f, 1.6f, 5f), body);
        Box("Shoulder East", b, new Vector3(3.5f, DeckY + 0.8f, 2.5f), new Vector3(1f, 1.6f, 5f), body);

        // Torso with a cannon on each side.
        Box("Torso", b, new Vector3(0f, DeckY + 2f, -1.5f), new Vector3(5f, 4f, 3f), body);
        Box("Torso Plate", b, new Vector3(0f, DeckY + 2.2f, -3.02f), new Vector3(3.6f, 2.4f, 0.05f), rust, false);
        var cannons = new List<Transform>();
        foreach (int side in new[] { -1, 1 })
        {
            Box("Cannon Housing", b, new Vector3(side * 3.1f, 7.7f, -1.8f), new Vector3(1.2f, 1.4f, 2.6f), rust);
            Transform barrel = Prim(PrimitiveType.Cylinder, "Cannon Barrel", b, new Vector3(side * 3.1f, 7.7f, -3.6f), new Vector3(0.55f, 0.6f, 0.55f), dark, true);
            barrel.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Transform muzzle = new GameObject("Cannon Muzzle").transform;
            muzzle.SetParent(b, false);
            muzzle.localPosition = new Vector3(side * 3.1f, 7.7f, -4.5f);
            cannons.Add(muzzle);
        }

        // Neck and the monitor head: screen with the face, health bar in the bezel under it.
        Box("Neck", b, new Vector3(0f, 9.2f, -2f), new Vector3(2f, 0.4f, 1f), dark);
        Box("Head Casing", b, new Vector3(0f, 12.2f, -2f), new Vector3(8.4f, 5.6f, 2.4f), casing);
        Box("Head Back", b, new Vector3(0f, 12.2f, 0.4f), new Vector3(6f, 4.2f, 2.4f), casing);
        Box("Screen Border", b, new Vector3(0f, 12.5f, -3.21f), new Vector3(7.7f, 4.7f, 0.02f), dark, false);

        Material screenMat = ScreenMaterial();
        Transform screen = Prim(PrimitiveType.Quad, "Screen", b, new Vector3(0f, 12.5f, -3.23f), new Vector3(7.4f, 4.4f, 1f), screenMat, false);

        Prim(PrimitiveType.Quad, "Health Bar Back", b, new Vector3(0f, 9.85f, -3.22f), new Vector3(6.6f, 0.5f, 1f), barBack, false);
        Transform trailPivot = new GameObject("Health Bar Trail").transform;
        trailPivot.SetParent(b, false);
        trailPivot.localPosition = new Vector3(-3.2f, 9.85f, -3.23f);
        Prim(PrimitiveType.Quad, "Trail", trailPivot, new Vector3(3.2f, 0f, 0f), new Vector3(6.4f, 0.36f, 1f), barTrail, false);
        Transform fillPivot = new GameObject("Health Bar Fill").transform;
        fillPivot.SetParent(b, false);
        fillPivot.localPosition = new Vector3(-3.2f, 9.85f, -3.24f);
        Prim(PrimitiveType.Quad, "Fill", fillPivot, new Vector3(3.2f, 0f, 0f), new Vector3(6.4f, 0.36f, 1f), barFill, false);

        // Antennas and the shield emitter the target beams run to.
        foreach (int side in new[] { -1, 1 })
            Prim(PrimitiveType.Cylinder, "Antenna", b, new Vector3(side * 3.2f, 15.8f, -1.2f), new Vector3(0.1f, 0.8f, 0.1f), dark, false);
        Transform emitterT = Prim(PrimitiveType.Sphere, "Shield Emitter", b, new Vector3(0f, 15.4f, -1.6f), new Vector3(0.8f, 0.8f, 0.8f), emitter, false);
        Prim(PrimitiveType.Cylinder, "Emitter Stalk", b, new Vector3(0f, 15.1f, -1.6f), new Vector3(0.25f, 0.2f, 0.25f), dark, false);

        GameObject focus = new GameObject("Face Focus");
        focus.transform.SetParent(b, false);
        focus.transform.localPosition = new Vector3(0f, 12.5f, -3.2f);

        Light screenLight = PointLight("Screen Glow", b, new Vector3(0f, 12.3f, -5f), new Color(1f, 0.6f, 0.35f), 3f, 14f);
        PointLight("Emitter Glow", b, new Vector3(0f, 15.4f, -2.4f), new Color(0.45f, 0.85f, 1f), 1.5f, 5f);
        PointLight("Belly Glow", b, new Vector3(0f, 1f, -6.5f), new Color(1f, 0.25f, 0.1f), 2f, 7f);

        // Wire hatches in the trench on the back deck.
        var weakPoints = new List<BossWeakPoint>();
        for (int i = 0; i < 4; i++)
        {
            Transform hatch = new GameObject("Wire Hatch " + (i + 1)).transform;
            hatch.SetParent(b, false);
            hatch.localPosition = new Vector3(-2.1f + i * 1.4f, DeckY, 2.4f);

            Box("Wall Front", hatch, new Vector3(0f, 0.25f, -0.51f), new Vector3(1.1f, 0.5f, 0.08f), dark);
            Box("Wall Back", hatch, new Vector3(0f, 0.25f, 0.51f), new Vector3(1.1f, 0.5f, 0.08f), dark);
            Box("Wall Left", hatch, new Vector3(-0.51f, 0.25f, 0f), new Vector3(0.08f, 0.5f, 0.94f), dark);
            Box("Wall Right", hatch, new Vector3(0.51f, 0.25f, 0f), new Vector3(0.08f, 0.5f, 0.94f), dark);
            Box("Inside", hatch, new Vector3(0f, 0.02f, 0f), new Vector3(0.94f, 0.02f, 0.94f), inside, false);

            // Lid hinged on the front edge (toward the head), so it swings up away from the catwalk's view.
            Transform hinge = new GameObject("Lid Hinge").transform;
            hinge.SetParent(hatch, false);
            hinge.localPosition = new Vector3(0f, 0.5f, -0.55f);
            Box("Lid", hinge, new Vector3(0f, 0.04f, 0.55f), new Vector3(1.16f, 0.08f, 1.16f), moving);
            Box("Lid Stripe", hinge, new Vector3(0f, 0.085f, 0.55f), new Vector3(0.2f, 0.01f, 1.0f), dark, false);

            // Wires: sunk inside while closed, rise out when open.
            Transform wires = new GameObject("Wires").transform;
            wires.SetParent(hatch, false);
            wires.localPosition = new Vector3(0f, -0.55f, 0f);
            Vector2[] offsets = { new Vector2(-0.22f, -0.15f), new Vector2(0.05f, -0.22f), new Vector2(0.24f, 0.05f), new Vector2(-0.1f, 0.18f), new Vector2(0.12f, 0.2f), new Vector2(-0.25f, 0.1f) };
            for (int w = 0; w < offsets.Length; w++)
            {
                Transform cable = Prim(PrimitiveType.Cylinder, "Wire", wires, new Vector3(offsets[w].x, 0.45f, offsets[w].y),
                                       new Vector3(0.09f, 0.45f, 0.09f), wireColours[w % wireColours.Length], false);
                cable.localRotation = Quaternion.Euler(Random.Range(-8f, 8f), 0f, Random.Range(-8f, 8f));
            }
            Transform loop = Prim(PrimitiveType.Cylinder, "Wire Loop", wires, new Vector3(0f, 0.8f, 0f), new Vector3(0.07f, 0.28f, 0.07f), wireColours[0], false);
            loop.localRotation = Quaternion.Euler(0f, 0f, 90f);
            BoxCollider wireCol = wires.gameObject.AddComponent<BoxCollider>();
            wireCol.center = new Vector3(0f, 0.5f, 0f);
            wireCol.size = new Vector3(0.75f, 1f, 0.75f);
            wires.gameObject.AddComponent<BossWires>();

            // Beacon: red closed, green open. Tall enough to peek over the shoulders.
            Prim(PrimitiveType.Cylinder, "Beacon Stalk", hatch, new Vector3(0f, 1f, 1.2f), new Vector3(0.08f, 1f, 0.08f), dark, false);
            Transform lamp = Box("Beacon", hatch, new Vector3(0f, 2.15f, 1.2f), new Vector3(0.34f, 0.3f, 0.34f), lampRed, false);
            Light lampLight = PointLight("Beacon Light", hatch, new Vector3(0f, 2.15f, 1.2f), new Color(1f, 0.12f, 0.06f), 4f, 5f);

            // Shield bubble (hidden until the shield phase).
            Transform bubble = Prim(PrimitiveType.Sphere, "Shield Bubble", hatch, new Vector3(0f, 0.55f, 0f), new Vector3(1.5f, 1.5f, 1.5f), glass, true);
            bubble.gameObject.AddComponent<BossShieldBubble>();
            bubble.gameObject.SetActive(false);

            BossWeakPoint wp = hatch.gameObject.AddComponent<BossWeakPoint>();
            SerializedObject so = new SerializedObject(wp);
            so.FindProperty("lid").objectReferenceValue = hinge;
            so.FindProperty("wires").objectReferenceValue = wires;
            SetList(so.FindProperty("lamps"), lamp.GetComponent<Renderer>());
            so.FindProperty("lampLight").objectReferenceValue = lampLight;
            so.FindProperty("lampClosedMaterial").objectReferenceValue = lampRed;
            so.FindProperty("lampOpenMaterial").objectReferenceValue = lampGreen;
            so.FindProperty("shieldBubble").objectReferenceValue = bubble.gameObject;
            so.FindProperty("sparkMaterial").objectReferenceValue = spark;
            so.ApplyModifiedPropertiesWithoutUndo();
            weakPoints.Add(wp);
        }

        BossMachine boss = root.AddComponent<BossMachine>();
        SerializedObject bso = new SerializedObject(boss);
        SetList(bso.FindProperty("weakPoints"), weakPoints.ToArray());
        SetList(bso.FindProperty("cannons"), cannons.ToArray());
        bso.FindProperty("screen").objectReferenceValue = screen.GetComponent<Renderer>();
        bso.FindProperty("screenLight").objectReferenceValue = screenLight;
        bso.FindProperty("healthFill").objectReferenceValue = fillPivot;
        bso.FindProperty("healthTrail").objectReferenceValue = trailPivot;
        bso.FindProperty("bulletMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(BulletMat);
        bso.ApplyModifiedPropertiesWithoutUndo();

        // Remember these for the targets and the ending.
        emitterPoint = emitterT;
        faceFocus = focus.transform;
        return boss;
    }

    private static Transform emitterPoint;
    private static Transform faceFocus;

    private static Material ScreenMaterial()
    {
        Material m = Unlit("Boss_Screen", Color.white);
        Texture2D face = AssetDatabase.LoadAssetAtPath<Texture2D>(FaceTexture);
        if (face == null) Debug.LogWarning("[FinalShift] Boss screen: face texture not found at " + FaceTexture);
        m.SetTexture("_BaseMap", face);
        m.SetTextureScale("_BaseMap", new Vector2(0.33f, 1f));
        m.SetTextureOffset("_BaseMap", new Vector2(0.34f, 0f));
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------ shield targets

    private static DoorTarget[] BuildTargets(Scene scene, BossMachine boss, out LineRenderer[] beams)
    {
        GameObject prefab = Load(TargetPrefab);
        Material beamMat = Unlit("Boss_Beam", new Color(0.45f, 0.85f, 1f));
        Vector3[] spots =
        {
            new Vector3(-15f, 0f, -16f), new Vector3(15f, 0f, -16f),                         // floor, front corners
            new Vector3(-12f, CatwalkY, 12.5f), new Vector3(12f, CatwalkY, 12.5f),             // back catwalk
            new Vector3(-HalfWidth + 2f, LedgeY, 2.5f), new Vector3(HalfWidth - 2f, LedgeY, 2.5f), // sniper ledges
        };

        GameObject root = new GameObject("== Shield Targets ==");
        var targets = new List<DoorTarget>();
        var beamList = new List<LineRenderer>();
        if (prefab == null) { beams = new LineRenderer[0]; return targets.ToArray(); }

        for (int i = 0; i < spots.Length; i++)
        {
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.name = "Shield Target " + (i + 1);
            go.transform.SetParent(root.transform, true);
            Vector3 look = new Vector3(0f, spots[i].y, -6f) - spots[i];
            look.y = 0f;
            go.transform.SetPositionAndRotation(spots[i], Quaternion.LookRotation(look));
            DoorTarget t = go.GetComponent<DoorTarget>();
            if (t == null) continue;
            SerializedObject so = new SerializedObject(t);
            so.FindProperty("health").floatValue = 30f;
            so.ApplyModifiedPropertiesWithoutUndo();
            targets.Add(t);

            GameObject beamGo = new GameObject("Beam " + (i + 1));
            beamGo.transform.SetParent(root.transform, false);
            LineRenderer lr = beamGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.SetPosition(0, spots[i] + Vector3.up * 1.5f);
            lr.SetPosition(1, emitterPoint != null ? emitterPoint.position : boss.transform.position + Vector3.up * 15f);
            lr.sharedMaterial = beamMat;
            lr.startWidth = 0.07f;
            lr.endWidth = 0.11f;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            beamList.Add(lr);
        }
        beams = beamList.ToArray();
        return targets.ToArray();
    }

    // ------------------------------------------------------------------ spawners

    private static EnemySpawner[] BuildSpawners(Scene scene)
    {
        GameObject prefab = Load(SpawnerPrefab);
        GameObject robot = Load(RobotPrefab), shield = Load(ShieldRobotPrefab), sniper = Load(SniperPrefab);
        GameObject root = new GameObject("== Spawners ==");
        var list = new List<EnemySpawner>();
        if (prefab == null) return list.ToArray();

        // Snipers drop onto the high ledges, regular + shield robots onto the floor.
        // East ones are turned around so the trapdoor housing points into the room, not into the wall.
        list.Add(Spawner(scene, prefab, root.transform, "Spawner Sniper West", new Vector3(-HalfWidth + 2f, LedgeY + 3.6f, -6f), 0f,
                         2, 7f, 4f, (sniper, 1)));
        list.Add(Spawner(scene, prefab, root.transform, "Spawner Sniper East", new Vector3(HalfWidth - 2f, LedgeY + 3.6f, -6f), 180f,
                         2, 7f, 5f, (sniper, 1)));
        list.Add(Spawner(scene, prefab, root.transform, "Spawner Floor West", new Vector3(-11f, 6.5f, -5f), 0f,
                         3, 4.5f, 2f, (robot, 2), (shield, 1)));
        list.Add(Spawner(scene, prefab, root.transform, "Spawner Floor East", new Vector3(11f, 6.5f, -5f), 180f,
                         3, 4.5f, 3f, (robot, 2), (shield, 1)));
        return list.ToArray();
    }

    private static EnemySpawner Spawner(Scene scene, GameObject prefab, Transform parent, string name, Vector3 pos, float yaw,
                                        int maxAlive, float delay, float firstDelay, params (GameObject prefab, int weight)[] enemies)
    {
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        go.name = name;
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        EnemySpawner s = go.GetComponent<EnemySpawner>();
        SerializedObject so = new SerializedObject(s);
        so.FindProperty("spawnOnStart").boolValue = true;
        so.FindProperty("endless").boolValue = true;
        so.FindProperty("maxAlive").intValue = maxAlive;
        so.FindProperty("delayBetweenSpawns").floatValue = delay;
        so.FindProperty("firstSpawnDelay").floatValue = firstDelay;
        so.FindProperty("shuffleOrder").boolValue = true;
        SerializedProperty list = so.FindProperty("enemies");
        list.arraySize = enemies.Length;
        for (int i = 0; i < enemies.Length; i++)
        {
            SerializedProperty e = list.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("label").stringValue = enemies[i].prefab != null ? enemies[i].prefab.name : "missing";
            e.FindPropertyRelative("prefab").objectReferenceValue = enemies[i].prefab;
            e.FindPropertyRelative("count").intValue = enemies[i].weight;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        return s;
    }

    // ------------------------------------------------------------------ player + ending

    private static void BuildPlayer(Scene scene)
    {
        GameObject prefab = Load(PlayerPrefab);
        if (prefab == null) return;
        GameObject p = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        p.transform.SetPositionAndRotation(new Vector3(0f, 0.1f, FrontZ + 4f), Quaternion.identity); // looking at the boss
        p.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
    }

    private static BossEndingCutscene BuildEnding(Scene scene, BossMachine boss)
    {
        GameObject go = new GameObject("== Ending Cutscene ==");
        BossEndingCutscene ending = go.AddComponent<BossEndingCutscene>();
        SerializedObject so = new SerializedObject(ending);
        so.FindProperty("robotPrefab").objectReferenceValue = Load(RobotPrefab);
        so.FindProperty("focus").objectReferenceValue = faceFocus;
        so.FindProperty("explosionMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(ExplosionMat);
        so.ApplyModifiedPropertiesWithoutUndo();
        return ending;
    }

    private static void LinkBoss(BossMachine boss, DoorTarget[] targets, LineRenderer[] beams, EnemySpawner[] spawners, BossEndingCutscene ending)
    {
        SerializedObject so = new SerializedObject(boss);
        SetList(so.FindProperty("shieldTargets"), targets);
        SetList(so.FindProperty("targetBeams"), beams);
        SetList(so.FindProperty("spawners"), spawners);
        so.FindProperty("ending").objectReferenceValue = ending;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------ lights

    private static void BuildLights(Scene scene)
    {
        GameObject root = new GameObject("== Factory Lights ==");
        Material housing = DarkFactoryLightingTool.LevelMaterial(Kind.Ceiling);
        Material warm = DarkFactoryLightingTool.UnlitMaterial("Lamp_Warm", new Color(1f, 0.78f, 0.45f));
        Material cold = DarkFactoryLightingTool.UnlitMaterial("Lamp_Cold", new Color(0.82f, 0.92f, 1f));

        Vector3[] spots =
        {
            new Vector3(-14f, 10f, -19f), new Vector3(-5f, 10f, -19f), new Vector3(5f, 10f, -19f), new Vector3(14f, 10f, -19f),
            new Vector3(-14f, 10f, -9f), new Vector3(-6f, 10f, -9f), new Vector3(6f, 10f, -9f), new Vector3(14f, 10f, -9f),
            new Vector3(-12f, 10f, 1f), new Vector3(12f, 10f, 1f), new Vector3(-6.5f, 11f, 6f), new Vector3(6.5f, 11f, 6f),
            new Vector3(-14f, 9.5f, 12f), new Vector3(-4f, 9.5f, 12f), new Vector3(4f, 9.5f, 12f), new Vector3(14f, 9.5f, 12f),
        };
        for (int i = 0; i < spots.Length; i++)
        {
            bool isCold = i % 4 == 2;
            bool flicker = i == 5 || i == 14;
            bool shadows = i == 1 || i == 6 || i == 9 || i == 13;
            DarkFactoryLightingTool.BuildLamp(root.transform, i, spots[i], isCold, flicker, shadows, housing, isCold ? cold : warm);
            Transform lamp = root.transform.GetChild(root.transform.childCount - 1);

            // High hall: brighter, longer-reaching lamps, hung from the ceiling on a long cable.
            Light l = lamp.GetComponentInChildren<Light>();
            if (l != null)
            {
                l.range = 17f;
                l.intensity *= 1.7f;
                var f = l.GetComponent<FinalShift.ShaderTest.ShaderTestFlicker>();
                if (f != null) f.baseIntensity = l.intensity;
            }
            float cable = CeilingY - spots[i].y;
            Prim(PrimitiveType.Cylinder, "Long Cable", lamp, new Vector3(0f, cable * 0.5f, 0f), new Vector3(0.03f, cable * 0.5f, 0.03f), housing, false);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static Transform Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat, bool collider = true)
        => Prim(PrimitiveType.Cube, name, parent, localPos, size, mat, collider);

    private static Transform Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat, bool collider)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mat != null) mr.sharedMaterial = mat;
        if (type == PrimitiveType.Quad) { mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; }
        return go.transform;
    }

    private static Light PointLight(string name, Transform parent, Vector3 localPos, Color colour, float intensity, float range)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.lightmapBakeType = LightmapBakeType.Realtime;
        l.color = colour;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        return l;
    }

    /// <summary>Flat colour + grime material (same shader as the rest of the level).</summary>
    private static Material Flat(string name, Color colour, float grime, bool objectSpace = false)
    {
        Shader shader = Shader.Find(DarkFactoryLightingTool.ShaderName);
        bool custom = shader != null;
        if (!custom) shader = Shader.Find("Universal Render Pipeline/Lit");
        Material mat = LoadOrCreate(name, shader);
        mat.SetColor("_BaseColor", colour);
        if (custom)
        {
            mat.SetTexture("_GrimeTex", AssetDatabase.LoadAssetAtPath<Texture2D>(DarkFactoryLightingTool.GrimePath));
            mat.SetFloat("_GrimeStrength", grime);
            mat.SetFloat("_GrimeScale", 0.5f);
            mat.SetColor("_Ambient", DarkFactoryLightingTool.AmbientColor);
            mat.SetFloat("_ObjectSpace", objectSpace ? 1f : 0f);
            if (objectSpace) mat.EnableKeyword("_OBJECT_SPACE"); else mat.DisableKeyword("_OBJECT_SPACE");
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material Unlit(string name, Color colour)
    {
        Material mat = LoadOrCreate(name, Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetColor("_BaseColor", colour);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material LoadOrCreate(string name, Shader shader)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        return mat;
    }

    private static GameObject Load(string path)
    {
        GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null) Debug.LogWarning("[FinalShift] Boss level: prefab not found at " + path);
        return go;
    }

    private static void SetList(SerializedProperty list, params Object[] items)
    {
        list.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static void AddToBuildSettings()
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (EditorBuildSettingsScene s in scenes) if (s.path == ScenePath) return;
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
