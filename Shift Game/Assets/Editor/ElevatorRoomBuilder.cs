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
/// Builds the elevator room scene (Assets/Scenes/ElevatorRoom.unity) from scratch.
///
/// Layout (the player rides up facing +Z; left = -X):
///  - Lower floor (y 0, z 0..30): the player comes up out of a shaft on the elevator, behind one big cover wall,
///    with smaller covers scattered around. A conveyor belt runs along the left wall in the bottom-left corner,
///    with button 1 on the wall behind it: crates on the belt keep passing in front of it.
///    Button 3 is on the right wall. The exit (a sliding door) is in the left wall, in the middle of the room.
///  - Half way (z 30): a tall see-through fence across the whole room, the front of a big raised deck (top 4 m).
///    4 m is out of reach of a normal jump (about 1.1 m): it takes the gear 4 rocket jump (about 6.4 m straight up).
///  - A hole in the ground across the whole room (z 42..50, 8 m wide). A sprint jump only covers about 4.6 m,
///    a gear 4 blast aimed down and back covers 10 m or more. Falling in kills (respawn at the last checkpoint).
///  - Far deck (top 4 m, z 50..66): high ground again. Button 2 is on the back wall, hidden behind a machine block
///    so it can't be shot from the first deck.
///  - All three buttons are wired (cables) to the exit door. They stay pressed once shot (they are too far apart to
///    hit within the usual few seconds), so the door opens when the last one is shot.
///  - Enemies: regular + shield robots on the lower floor, mostly snipers (and a few regular robots) up high.
///  - Checkpoints on both decks. The dark factory look (fog, hanging lamps, flat-colour materials).
///
/// Runs once by itself the first time Unity loads this script (if the scene doesn't exist yet), and any time from
/// Final Shift > Rooms > Rebuild Elevator Room Scene. A rebuild throws away hand edits to the scene, so tweak the
/// numbers here (or copy the scene) before rebuilding.
/// </summary>
[InitializeOnLoad]
public static class ElevatorRoomBuilder
{
    public const string ScenePath = "Assets/Scenes/ElevatorRoom.unity";
    private const string NavMeshFolder = "Assets/Scenes/ElevatorRoom";
    private const string MaterialFolder = "Assets/ProtMaterials/ElevatorRoom";
    private const string AutoRunPrefKey = "FinalShift.ElevatorRoom.AutoBuilt";

    private const string PlayerPrefab = "Assets/Prefabs/Player.prefab";
    private const string RobotPrefab = "Assets/Prefabs/Robot.prefab";
    private const string ShieldRobotPrefab = "Assets/Prefabs/RobotShield.prefab";
    private const string SniperPrefab = "Assets/Prefabs/RobotSniper.prefab";
    private const string ElevatorPrefab = "Assets/Prefabs/Interactables/ElevatorPlatform.prefab";
    private const string DoorPrefab = "Assets/Prefabs/Interactables/SlidingDoor.prefab";
    private const string ButtonPrefab = "Assets/Prefabs/Interactables/DoorButton.prefab";
    private const string CableBoxPrefab = "Assets/Prefabs/Interactables/CableBox.prefab";
    private const string OpeningPrefab = "Assets/Prefabs/Interactables/ConveyorOpening.prefab";
    private const string CrateBigPrefab = "Assets/Prefabs/Interactables/Crate_Big.prefab";
    private const string AmmoPrefab = "Assets/Prefabs/AmmoPickup.prefab";
    private const string HealthPrefab = "Assets/Prefabs/HealthPickup.prefab";

    // ---- Room (metres) ----
    private const float HalfWidth = 15f;       // x from -15 to 15
    private const float BackZ = 66f;
    private const float CeilingY = 13f;
    private const float PitBottomY = -8f;
    private const float FenceZ = 30f;          // half way: fence + front of the first deck
    private const float DeckY = 4f;            // top of both decks. Normal jump ~1.1 m, gear 4 straight down ~6.4 m.
    private const float GapStartZ = 42f;       // hole in the ground: 8 m. Sprint jump ~4.6 m, gear 4 ~10+ m.
    private const float GapEndZ = 50f;
    private const float ShaftY = -6f;          // elevator starts here (top surface) and rises to the floor
    private static readonly Vector3 ElevatorXZ = new Vector3(0f, 0f, 4f);
    private const float ShaftHalf = 1.8f;      // hole in the floor around the 3 x 3 platform
    private const float DoorZ = 24f;           // exit door in the left wall
    private const float DoorHalfOpening = 1.7f; // door width 2.6 + posts 0.4 on each side
    private const float DoorOpeningHeight = 3.4f;
    private const float WallT = 0.5f;

    private const float ButtonStayPressed = 600f; // seconds; effectively "stays pressed"

    static ElevatorRoomBuilder()
    {
        if (File.Exists(ScenePath) || EditorPrefs.GetBool(AutoRunPrefKey, false)) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || File.Exists(ScenePath)) return;
            EditorPrefs.SetBool(AutoRunPrefKey, true); // only try once by itself, even if it fails
            Build(false);
        };
    }

    [MenuItem("Final Shift/Rooms/Rebuild Elevator Room Scene")]
    public static void RebuildMenu()
    {
        if (File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog("Rebuild elevator room scene?",
                ScenePath + " will be built again from scratch. Hand edits to that scene are lost.", "Rebuild", "Cancel"))
            return;
        Build(true);
    }

    [MenuItem("Final Shift/Rooms/Open Elevator Room Scene")]
    public static void OpenMenu()
    {
        if (!File.Exists(ScenePath)) { Build(true); return; }
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
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
            // Build next to whatever is open, so the open scene isn't touched. If this scene itself is open,
            // or an untitled scene is open (additive needs saved scenes), replace everything instead.
            bool selfOpen = SceneManager.GetSceneByPath(ScenePath).isLoaded;
            bool untitledOpen = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path)) untitledOpen = true;

            if (selfOpen || untitledOpen)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
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

            Transform level = BuildRoom(scene);
            BuildCovers(scene, level);
            BuildElevator(scene);
            SlidingDoor door = BuildDoor(scene);
            DoorButton[] buttons = BuildButtons(scene, door);
            BuildConveyor(scene);
            BuildEnemies(scene);
            BuildPickups(scene);
            BuildCheckpoints(scene);
            BuildPlayer(scene);
            BuildLights(scene);
            LinkDoor(door, buttons);
            BuildCables(scene);

            DarkFactoryLightingTool.ApplyEnvironment(scene);
            RenderSettings.fogStartDistance = 12f; // long room: snipers up high should still show through the haze
            RenderSettings.fogEndDistance = 55f;

            EditorSceneManager.SaveScene(scene, ScenePath);

            // NavMesh (normal + the wide "Shielded" agent type) needs the saved scene path for its data assets.
            NavMeshSurface surface = level.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            int shieldedId = ShieldNavMeshTool.EnsureAgentType();
            ShieldNavMeshTool.BakeScene(scene, shieldedId, true);
            EditorSceneManager.SaveScene(scene);

            AddToBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("[FinalShift] Elevator room built: " + ScenePath +
                      ". Menu Final Shift > Rooms > Rebuild Elevator Room Scene builds it again.");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[FinalShift] Elevator room build failed: " + e);
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
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    // ------------------------------------------------------------------ room shell

    private static Transform BuildRoom(Scene scene)
    {
        Material floor = DarkFactoryLightingTool.LevelMaterial(Kind.Floor);
        Material wall = DarkFactoryLightingTool.LevelMaterial(Kind.Wall);
        Material ceiling = DarkFactoryLightingTool.LevelMaterial(Kind.Ceiling);
        Material pillar = DarkFactoryLightingTool.LevelMaterial(Kind.Pillar);
        Material steel = DarkFactoryLightingTool.LevelMaterial(Kind.Stairs);
        Material block = DarkFactoryLightingTool.LevelMaterial(Kind.Block);
        Material stripe = DarkFactoryLightingTool.LevelMaterial(Kind.Door);
        Material fence = Flat("Room_Fence", new Color(0.30f, 0.31f, 0.30f), 0.5f, true);
        Material dark = Flat("Room_DarkSteel", new Color(0.12f, 0.12f, 0.13f), 0.5f);

        GameObject root = new GameObject("== Level ==");
        Transform r = root.transform;

        // ---- Lower floor (z 0..42, runs under the first deck), with the elevator hole.
        float floorEnd = GapStartZ - WallT;
        Transform floorRoot = Group("Lower Floor", r);
        float hx0 = ElevatorXZ.x - ShaftHalf, hx1 = ElevatorXZ.x + ShaftHalf;
        float hz0 = ElevatorXZ.z - ShaftHalf, hz1 = ElevatorXZ.z + ShaftHalf;
        BoxSpan("Floor West", floorRoot, -HalfWidth, hx0, -0.5f, 0f, 0f, floorEnd, floor);
        BoxSpan("Floor East", floorRoot, hx1, HalfWidth, -0.5f, 0f, 0f, floorEnd, floor);
        BoxSpan("Floor Front", floorRoot, hx0, hx1, -0.5f, 0f, 0f, hz0, floor);
        BoxSpan("Floor Back", floorRoot, hx0, hx1, -0.5f, 0f, hz1, floorEnd, floor);
        // Hazard edging around the hole.
        BoxSpan("Shaft Edge Front", floorRoot, hx0, hx1, -0.02f, 0.02f, hz0 - 0.2f, hz0, stripe, false);
        BoxSpan("Shaft Edge Back", floorRoot, hx0, hx1, -0.02f, 0.02f, hz1, hz1 + 0.2f, stripe, false);
        BoxSpan("Shaft Edge West", floorRoot, hx0 - 0.2f, hx0, -0.02f, 0.02f, hz0 - 0.2f, hz1 + 0.2f, stripe, false);
        BoxSpan("Shaft Edge East", floorRoot, hx1, hx1 + 0.2f, -0.02f, 0.02f, hz0 - 0.2f, hz1 + 0.2f, stripe, false);

        // ---- Elevator shaft below the hole.
        Transform shaft = Group("Elevator Shaft", r);
        float shaftBottom = ShaftY - 0.9f;
        BoxSpan("Shaft Floor", shaft, hx0, hx1, shaftBottom - 0.3f, shaftBottom, hz0, hz1, dark);
        BoxSpan("Shaft Wall West", shaft, hx0 - WallT, hx0, shaftBottom - 0.3f, -0.5f, hz0 - WallT, hz1 + WallT, wall);
        BoxSpan("Shaft Wall East", shaft, hx1, hx1 + WallT, shaftBottom - 0.3f, -0.5f, hz0 - WallT, hz1 + WallT, wall);
        BoxSpan("Shaft Wall Front", shaft, hx0, hx1, shaftBottom - 0.3f, -0.5f, hz0 - WallT, hz0, wall);
        BoxSpan("Shaft Wall Back", shaft, hx0, hx1, shaftBottom - 0.3f, -0.5f, hz1, hz1 + WallT, wall);
        for (int i = 0; i < 3; i++) // guide rails
            BoxSpan("Shaft Rail " + i, shaft, hx0 + 0.6f + i * 1.2f, hx0 + 0.75f + i * 1.2f, shaftBottom, -0.5f, hz1 - 0.12f, hz1, steel, false);

        // ---- Outer walls, floor of the pit, ceiling.
        BoxSpan("Ceiling", r, -HalfWidth - WallT, HalfWidth + WallT, CeilingY, CeilingY + 0.5f, -WallT, BackZ + WallT, ceiling);
        BoxSpan("Wall Front", r, -HalfWidth - WallT, HalfWidth + WallT, -0.5f, CeilingY, -WallT, 0f, wall);
        BoxSpan("Wall Back", r, -HalfWidth - WallT, HalfWidth + WallT, PitBottomY, CeilingY, BackZ, BackZ + WallT, wall);
        BoxSpan("Wall East", r, HalfWidth, HalfWidth + WallT, PitBottomY, CeilingY, 0f, BackZ, wall);
        // West wall with the exit doorway cut out.
        float d0 = DoorZ - DoorHalfOpening, d1 = DoorZ + DoorHalfOpening;
        BoxSpan("Wall West Front", r, -HalfWidth - WallT, -HalfWidth, PitBottomY, CeilingY, 0f, d0, wall);
        BoxSpan("Wall West Back", r, -HalfWidth - WallT, -HalfWidth, PitBottomY, CeilingY, d1, BackZ, wall);
        BoxSpan("Wall West Over Door", r, -HalfWidth - WallT, -HalfWidth, DoorOpeningHeight, CeilingY, d0, d1, wall);
        BoxSpan("Wall West Under Door", r, -HalfWidth - WallT, -HalfWidth, PitBottomY, -0.5f, d0, d1, wall);
        BoxSpan("Pit Floor", r, -HalfWidth, HalfWidth, PitBottomY - 0.5f, PitBottomY, GapStartZ, GapEndZ, dark);

        // Wall pillars along the long walls up on the decks (rusted girders). None on the lower floor, where the cables run.
        foreach (float z in new[] { 36f, 56f })
        {
            BoxSpan("Wall Pillar W", r, -HalfWidth, -HalfWidth + 0.5f, PitBottomY, CeilingY, z - 0.3f, z + 0.3f, pillar);
            BoxSpan("Wall Pillar E", r, HalfWidth - 0.5f, HalfWidth, PitBottomY, CeilingY, z - 0.3f, z + 0.3f, pillar);
        }
        // Roof trusses.
        for (float z = 6f; z < BackZ; z += 10f)
            BoxSpan("Roof Truss", r, -HalfWidth, HalfWidth, CeilingY - 0.6f, CeilingY, z - 0.2f, z + 0.2f, pillar);

        // ---- First deck (top DeckY, z 30..42): a steel slab on pillars, fenced in front, open storage space under it.
        Transform deckB = Group("Deck 1 (rocket jump up)", r);
        BoxSpan("Deck 1 Slab", deckB, -HalfWidth, HalfWidth, DeckY - 0.5f, DeckY, FenceZ, GapStartZ, steel);
        BoxSpan("Deck 1 Pit Wall", deckB, -HalfWidth, HalfWidth, PitBottomY, DeckY - 0.5f, GapStartZ - WallT, GapStartZ, wall);
        BoxSpan("Deck 1 Front Stripe", deckB, -HalfWidth, HalfWidth, DeckY - 0.3f, DeckY, FenceZ - 0.04f, FenceZ, stripe, false);
        BoxSpan("Deck 1 Gap Stripe", deckB, -HalfWidth, HalfWidth, DeckY - 0.02f, DeckY + 0.01f, GapStartZ - 0.35f, GapStartZ, stripe, false);
        foreach (float x in new[] { -10f, 0f, 10f })
            foreach (float z in new[] { 33f, 39f })
                BoxSpan("Deck 1 Pillar", deckB, x - 0.25f, x + 0.25f, 0f, DeckY - 0.5f, z - 0.25f, z + 0.25f, pillar);
        // Old stock stored under the deck (seen through the fence).
        GameObject crate = Load(CrateBigPrefab);
        if (crate != null)
            foreach (Vector3 p in new[] { new Vector3(-12f, 0f, 35f), new Vector3(-5f, 0f, 38.5f), new Vector3(6f, 0f, 34f), new Vector3(12.5f, 0f, 39f) })
                Place(scene, crate, deckB, p + Vector3.up * 0.95f, Random.Range(-15f, 15f));

        // ---- The fence across the room, the front of the first deck.
        BuildFence(Group("Fence", r), fence, stripe);

        // ---- Far deck (top DeckY, z 50..66): solid block, its front is the far wall of the pit.
        Transform deckC = Group("Deck 2 (past the gap)", r);
        BoxSpan("Deck 2 Block", deckC, -HalfWidth, HalfWidth, PitBottomY, DeckY, GapEndZ, BackZ, block);
        BoxSpan("Deck 2 Top Plate", deckC, -HalfWidth, HalfWidth, DeckY - 0.01f, DeckY + 0.02f, GapEndZ, BackZ, steel, false);
        BoxSpan("Deck 2 Gap Stripe", deckC, -HalfWidth, HalfWidth, DeckY - 0.02f, DeckY + 0.03f, GapEndZ, GapEndZ + 0.35f, stripe, false);
        // Machine block: hides button 2 from the first deck, cover from the snipers, and a perch for one of them.
        BoxSpan("Deck 2 Machine", deckC, -2f, HalfWidth, DeckY, DeckY + 3.2f, 56f, 60f, block);
        BoxSpan("Deck 2 Machine Stripe", deckC, -2.02f, HalfWidth, DeckY + 2.9f, DeckY + 3.2f, 55.98f, 60.02f, stripe, false);
        for (int i = 0; i < 4; i++)
            BoxSpan("Deck 2 Machine Vent", deckC, 0f + i * 3.5f, 2.2f + i * 3.5f, DeckY + 1f, DeckY + 2.2f, 55.95f, 56f, dark, false);
        BoxSpan("Deck 2 Cover", deckC, -9f, -6f, DeckY, DeckY + 1.3f, 53f, 54f, block);
        BoxSpan("Deck 2 Cover 2", deckC, -13.5f, -12f, DeckY, DeckY + 1.9f, 60f, 61.5f, block);

        // ---- Hole: kill zone well below the floor level.
        GameObject kill = new GameObject("Pit Kill Zone");
        kill.transform.SetParent(r, false);
        kill.transform.localPosition = new Vector3(0f, (PitBottomY + -2.5f) * 0.5f + 0.5f, (GapStartZ + GapEndZ) * 0.5f);
        FallKillZone kz = kill.AddComponent<FallKillZone>();
        SerializedObject kso = new SerializedObject(kz);
        kso.FindProperty("size").vector3Value = new Vector3(HalfWidth * 2f, -2.5f - PitBottomY + 1f, GapEndZ - GapStartZ);
        kso.ApplyModifiedPropertiesWithoutUndo();

        // ---- Exit corridor behind the door.
        Transform exit = Group("Exit Corridor", r);
        float ex0 = -HalfWidth - WallT - 8f, ex1 = -HalfWidth - WallT;
        BoxSpan("Exit Floor", exit, ex0, ex1, -0.5f, 0f, d0 - 0.5f, d1 + 0.5f, floor);
        BoxSpan("Exit Ceiling", exit, ex0, ex1, 4f, 4.5f, d0 - 0.5f, d1 + 0.5f, ceiling);
        BoxSpan("Exit Wall South", exit, ex0, ex1, 0f, 4f, d0 - 1f, d0 - 0.5f, wall);
        BoxSpan("Exit Wall North", exit, ex0, ex1, 0f, 4f, d1 + 0.5f, d1 + 1f, wall);
        BoxSpan("Exit End", exit, ex0 - 0.5f, ex0, -0.5f, 4.5f, d0 - 1f, d1 + 1f, wall);
        BoxSpan("Exit Arrow", exit, ex0 + 1f, ex0 + 4f, 0f, 0.02f, DoorZ - 0.15f, DoorZ + 0.15f, stripe, false);

        return r;
    }

    /// <summary>Chain-link style fence: posts, rails and thin bars (bars close enough that nobody walks through).</summary>
    private static void BuildFence(Transform parent, Material fence, Material stripe)
    {
        float top = DeckY - 0.5f;
        float z = FenceZ - 0.1f;
        for (float x = -HalfWidth; x <= HalfWidth + 0.01f; x += 3f)
            BoxSpan("Post", parent, x - 0.09f, x + 0.09f, 0f, top, z - 0.09f, z + 0.09f, fence);
        foreach (float y in new[] { 0.15f, top * 0.5f, top - 0.1f })
            BoxSpan("Rail", parent, -HalfWidth, HalfWidth, y - 0.04f, y + 0.04f, z - 0.05f, z + 0.05f, fence);
        for (float x = -HalfWidth + 0.25f; x < HalfWidth; x += 0.25f)
            BoxSpan("Bar", parent, x - 0.02f, x + 0.02f, 0f, top, z - 0.02f, z + 0.02f, fence);
        // Warning sign plates.
        foreach (float x in new[] { -7.5f, 7.5f })
            BoxSpan("Sign", parent, x - 0.6f, x + 0.6f, 1.5f, 2.1f, z - 0.12f, z - 0.08f, stripe, false);
    }

    // ------------------------------------------------------------------ covers around the elevator

    private static void BuildCovers(Scene scene, Transform level)
    {
        Material block = DarkFactoryLightingTool.LevelMaterial(Kind.Block);
        Material pillar = DarkFactoryLightingTool.LevelMaterial(Kind.Pillar);
        Material stripe = DarkFactoryLightingTool.LevelMaterial(Kind.Door);
        Transform r = Group("Covers", level);

        // Big cover right in front of the elevator.
        BoxSpan("Big Cover", r, -3.5f, 3.5f, 0f, 3f, 9f, 10.6f, block);
        BoxSpan("Big Cover Stripe", r, -3.52f, 3.52f, 2.7f, 3f, 8.98f, 10.62f, stripe, false);
        BoxSpan("Big Cover Wing W", r, -3.5f, -2.3f, 0f, 3f, 7.4f, 9f, block);
        BoxSpan("Big Cover Wing E", r, 2.3f, 3.5f, 0f, 3f, 7.4f, 9f, block);

        // Small covers around the sides (kept out of the line between the room and button 1 on the left wall).
        BoxSpan("Low Cover 1", r, 6f, 8f, 0f, 1.2f, 7f, 8f, block);
        BoxSpan("Low Cover 2", r, -8.5f, -7f, 0f, 1.2f, 17f, 19f, block);
        BoxSpan("Low Cover 3", r, 3f, 5f, 0f, 1.2f, 20f, 21f, block);
        BoxSpan("Low Cover 4", r, 9.5f, 11f, 0f, 1.2f, 25f, 26.5f, block);
        BoxSpan("Column 1", r, -4.6f, -3.6f, 0f, CeilingY, 15.5f, 16.5f, pillar);
        BoxSpan("Column 2", r, 8.5f, 9.5f, 0f, CeilingY, 14.5f, 15.5f, pillar);

        GameObject crate = Load(CrateBigPrefab);
        if (crate != null)
            foreach (Vector3 p in new[] { new Vector3(11f, 0f, 4f), new Vector3(-2f, 0f, 24.5f), new Vector3(12.5f, 0f, 18f), new Vector3(-9f, 0f, 27.5f) })
                Place(scene, crate, r, p + Vector3.up * 0.95f, Random.Range(-20f, 20f));
    }

    // ------------------------------------------------------------------ elevator

    private static void BuildElevator(Scene scene)
    {
        GameObject prefab = Load(ElevatorPrefab);
        if (prefab == null) return;
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        go.name = "Start Elevator";
        go.transform.SetPositionAndRotation(new Vector3(ElevatorXZ.x, ShaftY, ElevatorXZ.z), Quaternion.identity);
        ElevatorPlatform e = go.GetComponent<ElevatorPlatform>();
        SerializedObject so = new SerializedObject(e);
        so.FindProperty("endOffset").vector3Value = new Vector3(0f, -ShaftY, 0f);
        so.FindProperty("riseSpeed").floatValue = 1.4f;
        so.FindProperty("returnWindow").floatValue = 0f; // the player starts on it: always finish the ride
        so.ApplyModifiedPropertiesWithoutUndo();
        go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
    }

    // ------------------------------------------------------------------ exit door, buttons, cables

    private static SlidingDoor BuildDoor(Scene scene)
    {
        GameObject prefab = Load(DoorPrefab);
        if (prefab == null) return null;
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        go.name = "Exit Door";
        // Door plane along the west wall (panels slide along Z).
        go.transform.SetPositionAndRotation(new Vector3(-HalfWidth - WallT * 0.5f, 0f, DoorZ), Quaternion.Euler(0f, 90f, 0f));
        return go.GetComponent<SlidingDoor>();
    }

    private static readonly List<(CableNode from, CableNode to)> cableLinks = new List<(CableNode, CableNode)>();

    private static DoorButton[] BuildButtons(Scene scene, SlidingDoor door)
    {
        cableLinks.Clear();
        GameObject prefab = Load(ButtonPrefab);
        GameObject boxPrefab = Load(CableBoxPrefab);
        GameObject root = new GameObject("== Door Buttons ==");
        if (prefab == null) return new DoorButton[0];

        // 1: bottom-left corner, on the west wall behind the conveyor belt.
        DoorButton b1 = Button(scene, prefab, root.transform, "Button 1 (behind conveyor)", new Vector3(-HalfWidth + 0.4f, 1.25f, 7f), 90f);
        // 2: far deck, back wall, behind the machine block.
        DoorButton b2 = Button(scene, prefab, root.transform, "Button 2 (far deck)", new Vector3(10f, DeckY + 1.4f, BackZ - 0.4f), 180f);
        // 3: lower floor, east wall.
        DoorButton b3 = Button(scene, prefab, root.transform, "Button 3 (lower right)", new Vector3(HalfWidth - 0.4f, 1.5f, 21f), -90f);

        CableNode doorNode = door != null ? door.GetComponentInChildren<CableNode>() : null;
        if (boxPrefab != null && doorNode != null)
        {
            Transform cables = new GameObject("Cable Boxes").transform;
            cables.SetParent(root.transform, false);
            float wx = -HalfWidth + 0.35f, ex = HalfWidth - 0.35f;
            float deckX = -HalfWidth + 0.8f; // clear of the wall pillars up on the decks
            // Button 1: straight up the wall to junction boxes high above the conveyor machine, then to the door.
            Route(scene, boxPrefab, cables, b1, doorNode,
                new Vector3(wx, 4.3f, 7f), new Vector3(wx, 4.3f, 19f));
            Route(scene, boxPrefab, cables, b3, doorNode,
                new Vector3(ex, 0f, 26f), new Vector3(ex, 0f, FenceZ - 0.5f), new Vector3(4f, 0f, FenceZ - 0.5f),
                new Vector3(-6f, 0f, FenceZ - 0.5f), new Vector3(wx, 0f, FenceZ - 0.5f));
            Route(scene, boxPrefab, cables, b2, doorNode,
                new Vector3(ex, DeckY, BackZ - 0.4f), new Vector3(ex - 0.2f, DeckY, 62f), new Vector3(-HalfWidth + 1.2f, DeckY, 62f),
                new Vector3(deckX, DeckY, GapEndZ + 0.4f), new Vector3(deckX, DeckY, GapStartZ - 0.4f), new Vector3(deckX, DeckY, FenceZ + 0.4f));
        }
        else Debug.LogWarning("[FinalShift] Elevator room: cable boxes skipped (CableBox prefab or door cable node missing).");

        return new[] { b1, b2, b3 };
    }

    private static DoorButton Button(Scene scene, GameObject prefab, Transform parent, string name, Vector3 pos, float yaw)
    {
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        go.name = name;
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        DoorButton b = go.GetComponent<DoorButton>();
        SerializedObject so = new SerializedObject(b);
        so.FindProperty("pressedTime").floatValue = ButtonStayPressed;
        so.ApplyModifiedPropertiesWithoutUndo();
        return b;
    }

    /// <summary>A chain of cable boxes from a button to the door. Links are set by hand (no auto-connect), so chains don't cross-wire.</summary>
    private static void Route(Scene scene, GameObject boxPrefab, Transform parent, DoorButton button, CableNode doorNode, params Vector3[] points)
    {
        CableNode prev = button != null ? button.GetComponentInChildren<CableNode>() : null;
        if (prev == null) return;
        foreach (Vector3 p in points)
        {
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(boxPrefab, scene);
            go.name = "Cable Box (" + button.name + ")";
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(p, Quaternion.identity);
            CableNode node = go.GetComponent<CableNode>();
            if (node == null) continue;
            SerializedObject so = new SerializedObject(node);
            so.FindProperty("autoConnect").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            cableLinks.Add((node, prev));
            prev = node;
        }
        cableLinks.Add((doorNode, prev));
    }

    private static void BuildCables(Scene scene)
    {
        // Each node lists the node before it as an extra link.
        var extra = new Dictionary<CableNode, List<CableNode>>();
        foreach (var (from, to) in cableLinks)
        {
            if (from == null || to == null) continue;
            if (!extra.TryGetValue(from, out List<CableNode> list)) extra[from] = list = new List<CableNode>();
            list.Add(to);
        }
        foreach (var kv in extra)
        {
            SerializedObject so = new SerializedObject(kv.Key);
            SetList(so.FindProperty("extraLinks"), kv.Value.ToArray());
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        // Build the cable meshes now (they would otherwise only appear once the scene is opened).
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (CableNode n in root.GetComponentsInChildren<CableNode>(true))
                n.RebuildCables();
    }

    private static void LinkDoor(SlidingDoor door, DoorButton[] buttons)
    {
        if (door == null) return;
        SerializedObject so = new SerializedObject(door);
        so.FindProperty("openCondition").enumValueIndex = (int)SlidingDoor.OpenCondition.ShootAllButtons;
        SetList(so.FindProperty("buttons"), buttons);
        so.FindProperty("stayOpen").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------ conveyor in the bottom-left corner

    private static void BuildConveyor(Scene scene)
    {
        GameObject prefab = Load(OpeningPrefab);
        if (prefab == null) return;
        GameObject root = new GameObject("== Conveyor ==");
        const float x = -HalfWidth + 1.8f; // belt frame reaches about 1.06 m to each side; button sits between it and the wall
        ConveyorOpening outOpening = Opening(scene, prefab, root.transform, "Conveyor Out", new Vector3(x, 0f, 1.8f), 0f, ConveyorOpening.Flow.ItemsComeOut);
        Opening(scene, prefab, root.transform, "Conveyor In", new Vector3(x, 0f, 13f), 180f, ConveyorOpening.Flow.ItemsGoIn);

        // A housing behind the "in" hatch, so it reads as a machine, not a floating frame.
        Material block = DarkFactoryLightingTool.LevelMaterial(Kind.Block);
        BoxSpan("Conveyor Machine", root.transform, x - 1.7f, x + 1.7f, 0f, 4.2f, 15f, 16.6f, block);

        if (outOpening != null) outOpening.RebuildBelt();
    }

    private static ConveyorOpening Opening(Scene scene, GameObject prefab, Transform parent, string name, Vector3 pos, float yaw, ConveyorOpening.Flow flow)
    {
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        go.name = name;
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        ConveyorOpening o = go.GetComponent<ConveyorOpening>();
        SerializedObject so = new SerializedObject(o);
        so.FindProperty("beltId").intValue = 1;
        so.FindProperty("flow").enumValueIndex = (int)flow;
        so.FindProperty("spawnInterval").floatValue = 2f;
        so.FindProperty("spawnIntervalRandom").floatValue = 1.2f;
        so.ApplyModifiedPropertiesWithoutUndo();
        return o;
    }

    // ------------------------------------------------------------------ enemies, pickups, checkpoints, player

    private static void BuildEnemies(Scene scene)
    {
        GameObject robot = Load(RobotPrefab), shield = Load(ShieldRobotPrefab), sniper = Load(SniperPrefab);

        Transform low = new GameObject("== Enemies Lower Floor ==").transform;
        Enemy(scene, robot, low, new Vector3(-7f, 0f, 14f), 180f);
        Enemy(scene, robot, low, new Vector3(7f, 0f, 12f), 180f);
        Enemy(scene, robot, low, new Vector3(1f, 0f, 22f), 180f);
        Enemy(scene, shield, low, new Vector3(-5f, 0f, 26f), 180f);
        Enemy(scene, shield, low, new Vector3(6f, 0f, 25f), 180f);

        Transform deck1 = new GameObject("== Enemies Deck 1 ==").transform;
        Enemy(scene, sniper, deck1, new Vector3(-9f, DeckY, 36f), 180f);
        Enemy(scene, sniper, deck1, new Vector3(9f, DeckY, 38f), 180f);
        Enemy(scene, sniper, deck1, new Vector3(0f, DeckY, 40f), 180f);
        Enemy(scene, robot, deck1, new Vector3(3f, DeckY, 34f), 180f);

        Transform deck2 = new GameObject("== Enemies Deck 2 ==").transform;
        Enemy(scene, sniper, deck2, new Vector3(-10f, DeckY, 63f), 180f);
        Enemy(scene, sniper, deck2, new Vector3(8f, DeckY + 3.2f, 58f), 180f); // on top of the machine block
        Enemy(scene, sniper, deck2, new Vector3(-4f, DeckY, 57f), 180f);
        Enemy(scene, robot, deck2, new Vector3(4f, DeckY, 63f), 180f);
    }

    private static void Enemy(Scene scene, GameObject prefab, Transform parent, Vector3 pos, float yaw)
    {
        if (prefab == null) return;
        Place(scene, prefab, parent, pos, yaw);
    }

    private static void BuildPickups(Scene scene)
    {
        Transform root = new GameObject("== Pickups ==").transform;
        GameObject ammo = Load(AmmoPrefab), health = Load(HealthPrefab);
        // Gear 4 costs ammo: a few refills on the way up.
        if (ammo != null)
            foreach (Vector3 p in new[] { new Vector3(-1.5f, 0f, 7.5f), new Vector3(1.5f, 0f, 7.5f), new Vector3(13f, DeckY, 40f), new Vector3(-13f, DeckY, 52f) })
                Place(scene, ammo, root, p + Vector3.up * 0.4f, 0f);
        if (health != null)
            foreach (Vector3 p in new[] { new Vector3(-13f, DeckY, 32f), new Vector3(12f, DeckY, 64f) })
                Place(scene, health, root, p + Vector3.up * 0.4f, 0f);
    }

    private static void BuildCheckpoints(Scene scene)
    {
        Transform root = new GameObject("== Checkpoints ==").transform;
        AddCheckpoint(root, "Checkpoint Deck 1", new Vector3(0f, DeckY, FenceZ + 1.2f), new Vector3(HalfWidth * 2f, 3f, 1.5f), new Vector3(0f, DeckY, FenceZ + 3f));
        AddCheckpoint(root, "Checkpoint Deck 2", new Vector3(0f, DeckY, GapEndZ + 1.2f), new Vector3(HalfWidth * 2f, 3f, 1.5f), new Vector3(-6f, DeckY, GapEndZ + 2.5f));
    }

    private static void AddCheckpoint(Transform parent, string name, Vector3 pos, Vector3 size, Vector3 spawn)
    {
        const int ignoreRaycast = 2; // bullets fly straight through
        GameObject go = new GameObject(name);
        go.layer = ignoreRaycast;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = size;
        box.center = new Vector3(0f, size.y * 0.5f, 0f);
        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

        GameObject sp = new GameObject("SpawnPoint");
        sp.layer = ignoreRaycast;
        sp.transform.SetParent(go.transform, false);
        sp.transform.SetPositionAndRotation(spawn + Vector3.up * 0.1f, Quaternion.identity); // facing on into the room

        Checkpoint cp = go.AddComponent<Checkpoint>();
        SerializedObject so = new SerializedObject(cp);
        so.FindProperty("spawnPoint").objectReferenceValue = sp.transform;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BuildPlayer(Scene scene)
    {
        GameObject prefab = Load(PlayerPrefab);
        if (prefab == null) return;
        GameObject p = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        // Standing on the elevator at the bottom of the shaft, facing into the room.
        p.transform.SetPositionAndRotation(new Vector3(ElevatorXZ.x, ShaftY + 0.05f, ElevatorXZ.z - 0.3f), Quaternion.identity);
        p.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
    }

    // ------------------------------------------------------------------ lights

    private static void BuildLights(Scene scene)
    {
        GameObject root = new GameObject("== Factory Lights ==");
        Material housing = DarkFactoryLightingTool.LevelMaterial(Kind.Ceiling);
        Material warm = DarkFactoryLightingTool.UnlitMaterial("Lamp_Warm", new Color(1f, 0.78f, 0.45f));
        Material cold = DarkFactoryLightingTool.UnlitMaterial("Lamp_Cold", new Color(0.82f, 0.92f, 1f));

        var lamps = new List<(Vector3 pos, bool cold, bool flicker, bool shadows)>
        {
            // Lower floor.
            (new Vector3(-8f, 7f, 5f), false, false, false), (new Vector3(0f, 7f, 12f), false, false, true),
            (new Vector3(9f, 7f, 5f), true, false, false), (new Vector3(-9f, 7f, 20f), false, true, true),
            (new Vector3(9f, 7f, 21f), false, false, false), (new Vector3(0f, 7f, 27f), true, false, false),
            // First deck.
            (new Vector3(-8f, 9.5f, 36f), false, false, true), (new Vector3(8f, 9.5f, 36f), false, true, false),
            // Far deck.
            (new Vector3(-8f, 9.5f, 55f), true, false, false), (new Vector3(8f, 10f, 63f), false, false, true),
            (new Vector3(-9f, 9.5f, 63f), false, true, false),
        };
        for (int i = 0; i < lamps.Count; i++)
        {
            var l = lamps[i];
            DarkFactoryLightingTool.BuildLamp(root.transform, i, l.pos, l.cold, l.flicker, l.shadows, housing, l.cold ? cold : warm);
            Transform lamp = root.transform.GetChild(root.transform.childCount - 1);
            float cable = CeilingY - l.pos.y;
            Prim(PrimitiveType.Cylinder, "Long Cable", lamp, new Vector3(0f, cable * 0.5f, 0f), new Vector3(0.03f, cable * 0.5f, 0.03f), housing, false);
        }

        // Dim lights where there are no lamps: the shaft, under the deck, and a red glow down in the hole.
        PointLight("Shaft Light", root.transform, new Vector3(ElevatorXZ.x, -2f, ElevatorXZ.z), new Color(1f, 0.7f, 0.4f), 3f, 7f);
        PointLight("Under Deck Glow", root.transform, new Vector3(0f, 2.5f, 36f), new Color(0.5f, 0.6f, 0.7f), 1.5f, 10f);
        PointLight("Pit Glow W", root.transform, new Vector3(-8f, PitBottomY + 1.5f, 46f), new Color(1f, 0.25f, 0.08f), 4f, 12f);
        PointLight("Pit Glow E", root.transform, new Vector3(8f, PitBottomY + 1.5f, 46f), new Color(1f, 0.25f, 0.08f), 4f, 12f);
        PointLight("Exit Light", root.transform, new Vector3(-HalfWidth - 4f, 3.4f, DoorZ), new Color(0.7f, 0.85f, 1f), 3f, 8f);
    }

    // ------------------------------------------------------------------ helpers

    private static Transform Group(string name, Transform parent)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    /// <summary>A box spanning the given world ranges (the parents here sit at the origin).</summary>
    private static Transform BoxSpan(string name, Transform parent, float x0, float x1, float y0, float y1, float z0, float z1, Material mat, bool collider = true)
        => Prim(PrimitiveType.Cube, name, parent, new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f),
                new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0)), mat, collider);

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
        if (!collider) mr.shadowCastingMode = ShadowCastingMode.Off;
        return go.transform;
    }

    private static GameObject Place(Scene scene, GameObject prefab, Transform parent, Vector3 pos, float yaw)
    {
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        return go;
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
        string path = MaterialFolder + "/" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
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

    private static GameObject Load(string path)
    {
        GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null) Debug.LogWarning("[FinalShift] Elevator room: prefab not found at " + path);
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
