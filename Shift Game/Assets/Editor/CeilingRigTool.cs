using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// For rooms where the roof structure has to hang UNDER the ceiling and the "Roof Truss" beams (ElevatorRoom):
///
///  1. A low metal structure, wall to wall:
///     - under every Roof Truss beam a V-webbed (Warren) truss hanging down, with its own bottom chord;
///     - under those, flat trusses running the other way (verticals + alternating V diagonals) every ~6 m.
///  2. The hanging lamps of the room are replaced by Neon.fbx fixtures in long rows: one row under each inner
///     flat truss, on cables, with blue emissive tubes, metal housing and a wide spot light pointing down
///     (URP has no real-time area lights).
///  3. A round hanging lamp with orange light in front of every "Checkpoint Deck", just under the structure.
///  4. Invisible fill lights (point lights with no mesh) a little above every floor, so the floors are not too dark.
/// Fixtures hang higher over the raised decks (at least NeonMinClearance above the floor under them).
///
/// Lengths come from raycasts (the nearest wall at least <see cref="MinWallWidth"/> wide), heights from the beams.
/// Meshes of the structure and the cables are combined and saved in Assets/Art/Generated/{scene}_CeilingRig.asset.
/// Menu: Final Shift > Rooms > Build Low Roof Structure + Neon Rows (Under Roof Trusses). Running it again rebuilds
/// both. The scene is marked dirty but NOT saved.
/// </summary>
[InitializeOnLoad]
public static class CeilingRigTool
{
    private const string StructureRoot = "== Roof Structure Under Trusses (generated) ==";
    private const string NeonRoot = "== Neon Lights (generated) ==";
    private static readonly string[] OldLampRoots = { "== Factory Lights ==", "== Factory Lights (generated) ==" };
    private const string BeamPrefix = "Roof Truss";
    private const string NeonModelPath = "Assets/Art/Neon.fbx";
    private const string MaterialFolder = "Assets/Art/Materials";
    private const string TextureFolder = "Assets/Art/Textures";
    private const string TriggerFile = "Temp/FinalShift_CeilingRig";
    private const string ReportFile = "Temp/FinalShift_CeilingRig_report.txt";

    // ---- Structure (metres) ----
    private const float LongTrussDepth = 0.7f;   // V truss hanging under each beam
    private const float CrossDepth = 0.55f;      // flat trusses under those
    private const float PanelLength = 2.5f;
    private const float CrossSpacing = 6f;
    private const float Chord = 0.18f;
    private const float Web = 0.09f;
    private const float MinWallWidth = 12f;

    // ---- Neons ----
    private const float NeonSpacing = 5f;        // fixture to fixture along a row
    private const float NeonEndGap = 2f;         // free space at the walls
    private const float NeonHeight = 8.5f;       // fixtures hang at least this high (cables up to the structure)
    private const float NeonMinClearance = 6f;   // ...and at least this far above the floor right under them (decks)
    private const float NeonTopGap = 0.5f;       // never closer than this to the structure above
    // URP only bakes area lights, and this level is lit in real time, so every fixture gets a wide spot light
    // pointing straight down instead: it reaches the floor and leaves the ceiling dark.
    private static readonly Color LightColour = new Color(0.55f, 0.78f, 1f);
    private const float LightIntensity = 14f;
    private const float LightRange = 20f;         // fixture at 8.5 m: reaches the lower floor and the pit
    private const float LightOuterAngle = 120f;
    private const float LightInnerAngle = 50f;

    // ---- Round hanging lamps in front of the checkpoint decks
    private const string CheckpointPrefix = "Checkpoint Deck";
    private const string CheckpointLampRoot = "== Checkpoint Lamps (generated) ==";
    private const float CheckpointLampTopGap = 0.6f;   // hangs this far under the structure (short cable)
    private const float CheckpointLampForward = 1.2f;  // towards where the player comes from
    private static readonly Color OrangeLight = new Color(1f, 0.55f, 0.18f);

    // ---- Invisible fill lights that brighten the floors (no mesh, only their light)
    private const string FillRoot = "== Floor Fill Lights (generated) ==";
    private const float FillSpacingAcross = 7.5f;
    private const float FillSpacingAlong = 8f;
    private const float FillHeight = 2.5f;        // above the floor under them
    private const float FillRange = 11f;
    private const float FillIntensity = 4f;
    private static readonly Color FillColour = new Color(0.85f, 0.9f, 1f);
    private const float MinFloorSize = 3f;        // a surface narrower than this (robot, crate) is not a floor
    private static readonly Color TubeEmission = new Color(0.3f, 0.62f, 1f);
    private const float TubeEmissionIntensity = 6f;

    static CeilingRigTool()
    {
        EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (File.Exists(TriggerFile)) { File.Delete(TriggerFile); BuildInActive(); }
        };
    }

    [MenuItem("Final Shift/Rooms/Build Low Roof Structure + Neon Rows (Under Roof Trusses)")]
    public static void BuildInActive()
    {
        var log = new System.Text.StringBuilder();
        Scene s = SceneManager.GetActiveScene();
        try { Build(s, log); }
        catch (System.Exception e) { log.AppendLine("ERROR " + e); }
        EditorSceneManager.MarkSceneDirty(s);
        File.WriteAllText(ReportFile, log.ToString());
        Debug.Log("[FinalShift] Ceiling rig in " + s.name + ":\n" + log);
    }

    private static bool alongX;
    private static Vector3 P(float along, float across, float y) => alongX ? new Vector3(along, y, across) : new Vector3(across, y, along);

    private static void Build(Scene scene, System.Text.StringBuilder log)
    {
        // ---- beams
        var beams = new List<Renderer>();
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.name.StartsWith(BeamPrefix) && r.gameObject.activeInHierarchy) beams.Add(r);
        if (beams.Count == 0) { log.AppendLine("no Roof Truss objects"); return; }
        Bounds all = beams[0].bounds;
        foreach (Renderer r in beams) all.Encapsulate(r.bounds);
        alongX = beams[0].bounds.size.x >= beams[0].bounds.size.z;   // one beam's long direction
        float beamBottom = float.MaxValue;
        foreach (Renderer r in beams) beamBottom = Mathf.Min(beamBottom, r.bounds.min.y);
        var lines = new List<float>();
        foreach (Renderer r in beams)
        {
            float c = alongX ? r.bounds.center.z : r.bounds.center.x;
            if (!lines.Exists(l => Mathf.Abs(l - c) < 0.5f)) lines.Add(c);
        }
        lines.Sort();
        Material steel = beams[0].sharedMaterial;
        log.AppendLine(beams.Count + " beams along " + (alongX ? "X" : "Z") + ", bottom at " + beamBottom.ToString("0.00") + ", lines " + string.Join(", ", lines.ConvertAll(l => l.ToString("0.0"))));

        // ---- clean up
        foreach (GameObject old in scene.GetRootGameObjects())
        {
            if (old.name == StructureRoot || old.name == NeonRoot || old.name == CheckpointLampRoot || old.name == FillRoot) Undo.DestroyObjectImmediate(old);
            else if (System.Array.IndexOf(OldLampRoots, old.name) >= 0) { log.AppendLine("removed old lamps: " + old.name); Undo.DestroyObjectImmediate(old); }
        }
        string meshPath = "Assets/Art/Generated/" + scene.name + "_CeilingRig.asset";
        Directory.CreateDirectory("Assets/Art/Generated");
        AssetDatabase.DeleteAsset(meshPath);
        Physics.SyncTransforms();

        GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh cube = tmp.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(tmp);
        var meshes = new List<Mesh>();

        // ---- heights
        float yLongBottom = beamBottom - LongTrussDepth;   // bottom chord of the hanging V trusses
        float yCrossTop = yLongBottom - Chord;             // flat trusses hang right under them
        float yCrossBottom = yCrossTop - CrossDepth;

        float midAlong = (alongX ? all.center.x : all.center.z);
        float midAcross = (lines[0] + lines[lines.Count - 1]) * 0.5f;
        Vector3 dAlong = alongX ? Vector3.right : Vector3.forward;
        Vector3 dAcross = alongX ? Vector3.forward : Vector3.right;
        float[] probes = { beamBottom - 0.3f, yLongBottom, yCrossBottom };
        int hits = 0, misses = 0;
        var wallsHit = new HashSet<string>();
        System.Func<float, float, Vector3, float, float> reach = (al, ac, dir, fallback) =>
        {
            foreach (float h in probes)
            {
                Vector3 o = P(al, ac, h);
                float best = float.MaxValue; string name = null;
                foreach (RaycastHit hit in Physics.RaycastAll(o, dir, 300f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.name.StartsWith(BeamPrefix)) continue;
                    Vector3 sz = hit.collider.bounds.size;
                    if ((Mathf.Abs(dir.x) > 0.5f ? sz.z : sz.x) < MinWallWidth) continue;
                    if (hit.distance < best) { best = hit.distance; name = RoofStructureTool.HierarchyPath(hit.collider.transform); }
                }
                if (name != null) { hits++; wallsHit.Add(name); return Vector3.Dot(o + dir * best, dir); }
            }
            misses++;
            return fallback;
        };

        GameObject structure = new GameObject(StructureRoot);
        SceneManager.MoveGameObjectToScene(structure, scene);

        // ---- hanging V trusses under the beams (wall to wall along the beams)
        GameObject longParent = RoofStructureTool.Child(structure, "Under-Beam Trusses");
        float gMin = float.MaxValue, gMax = float.MinValue;
        for (int li = 0; li < lines.Count; li++)
        {
            float c = lines[li];
            float aMin = -reach(midAlong, c, -dAlong, -(alongX ? all.min.x : all.min.z));
            float aMax = reach(midAlong, c, dAlong, alongX ? all.max.x : all.max.z);
            gMin = Mathf.Min(gMin, aMin); gMax = Mathf.Max(gMax, aMax);
            int n = Mathf.Max(1, Mathf.RoundToInt((aMax - aMin) / PanelLength));
            float p = (aMax - aMin) / n;
            var parts = new List<CombineInstance>();
            RoofStructureTool.Beam(parts, cube, P(aMin, c, yLongBottom), P(aMax, c, yLongBottom), Chord);
            for (int i = 0; i < n; i++)
            {
                float a0 = aMin + i * p, am = a0 + p * 0.5f, a1 = a0 + p;
                RoofStructureTool.Beam(parts, cube, P(a0, c, beamBottom), P(am, c, yLongBottom + Chord * 0.5f), Web);
                RoofStructureTool.Beam(parts, cube, P(am, c, yLongBottom + Chord * 0.5f), P(a1, c, beamBottom), Web);
            }
            RoofStructureTool.MeshObject(longParent, "Under-Beam Truss " + (li + 1), parts, steel, meshes);
        }

        // ---- flat trusses the other way (wall to wall across the beams)
        GameObject crossParent = RoofStructureTool.Child(structure, "Cross Trusses");
        int count = Mathf.Max(2, Mathf.RoundToInt((gMax - gMin) / CrossSpacing) + 1);
        var crossAt = new List<float>();
        var crossRange = new List<Vector2>();
        for (int k = 0; k < count; k++)
        {
            float a = Mathf.Lerp(gMin + Chord, gMax - Chord, k / (float)(count - 1));
            float cMin = -reach(a, midAcross, -dAcross, -(lines[0] - 1f));
            float cMax = reach(a, midAcross, dAcross, lines[lines.Count - 1] + 1f);
            crossAt.Add(a); crossRange.Add(new Vector2(cMin, cMax));
            int n = Mathf.Max(2, Mathf.RoundToInt((cMax - cMin) / PanelLength));
            float p = (cMax - cMin) / n;
            var parts = new List<CombineInstance>();
            RoofStructureTool.Beam(parts, cube, P(a, cMin, yCrossTop), P(a, cMax, yCrossTop), Chord);
            RoofStructureTool.Beam(parts, cube, P(a, cMin, yCrossBottom), P(a, cMax, yCrossBottom), Chord);
            for (int i = 0; i <= n; i++)
            {
                float c0 = cMin + i * p;
                RoofStructureTool.Beam(parts, cube, P(a, c0, yCrossTop), P(a, c0, yCrossBottom), Web);
                if (i == n) break;
                float c1 = c0 + p;
                if (i % 2 == 0) RoofStructureTool.Beam(parts, cube, P(a, c0, yCrossBottom), P(a, c1, yCrossTop), Web);
                else RoofStructureTool.Beam(parts, cube, P(a, c0, yCrossTop), P(a, c1, yCrossBottom), Web);
            }
            RoofStructureTool.MeshObject(crossParent, "Cross Truss " + (k + 1), parts, steel, meshes);
        }
        log.AppendLine("structure: " + lines.Count + " hanging V trusses, " + count + " flat cross trusses; y " +
                       yCrossBottom.ToString("0.00") + ".." + beamBottom.ToString("0.00") + " (" + (beamBottom - yCrossBottom).ToString("0.00") + " m tall)");
        log.AppendLine("walls: " + hits + " hits, " + misses + " misses; along " + gMin.ToString("0.0") + ".." + gMax.ToString("0.0") +
                       "; hit: " + string.Join(", ", wallsHit));

        // ---- neon rows: under every inner cross truss
        GameObject neonModel = AssetDatabase.LoadAssetAtPath<GameObject>(NeonModelPath);
        Material tube = TubeMaterial(), housing = HousingMaterial(), reflector = ReflectorMaterial();
        Material cableMat = DarkFactoryLightingTool.LevelMaterial(DarkFactoryLightingTool.Kind.Ceiling);
        GameObject neonRoot = new GameObject(NeonRoot);
        SceneManager.MoveGameObjectToScene(neonRoot, scene);
        var cables = new List<CombineInstance>();
        int fixtures = 0, lights = 0;

        // Between the first and the last checkpoint deck (incl. the pit between them) the fixtures hang as high as
        // over the decks themselves, measured from the highest checkpoint deck.
        float cpFrom = float.MaxValue, cpTo = float.MinValue, cpFloor = float.NegativeInfinity;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith(CheckpointPrefix)) continue;
                float c = Vector3.Dot(t.position, dAcross);
                cpFrom = Mathf.Min(cpFrom, c - CheckpointLampForward);
                cpTo = Mathf.Max(cpTo, c);
                cpFloor = Mathf.Max(cpFloor, t.position.y);
            }
        for (int k = 1; k < count - 1; k++)
        {
            float a = crossAt[k];
            Vector2 range = crossRange[k];
            float start = range.x + NeonEndGap, end = range.y - NeonEndGap;
            int n = Mathf.Max(1, Mathf.FloorToInt((end - start) / NeonSpacing) + 1);
            float used = (n - 1) * NeonSpacing;
            float first = (start + end) * 0.5f - used * 0.5f;   // centred in the row
            GameObject row = RoofStructureTool.Child(neonRoot, "Neon Row " + k);
            for (int i = 0; i < n; i++)
            {
                float c = first + i * NeonSpacing;
                float floor = FloorBelow(P(a, c, yCrossBottom - 0.2f));
                if (c >= cpFrom - NeonSpacing * 0.5f && c <= cpTo + NeonSpacing * 0.5f) floor = Mathf.Max(floor, cpFloor);
                float h = Mathf.Min(Mathf.Max(NeonHeight, floor + NeonMinClearance), yCrossBottom - NeonTopGap);
                var neon = (GameObject)PrefabUtility.InstantiatePrefab(neonModel, scene);
                neon.name = "Neon " + k + "." + (i + 1);
                neon.transform.SetParent(row.transform, false);
                neon.transform.position = P(a, c, h);
                // The model lies along its X with the tubes facing down: turn it to run along the row.
                // (yaw -90 turns the model's X onto world +Z).
                neon.transform.rotation = Quaternion.Euler(0f, alongX ? -90f : 0f, 0f);
                foreach (Renderer r in neon.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] src = r.sharedMaterials;
                    var mats = new Material[src.Length];
                    for (int m = 0; m < src.Length; m++)
                    {
                        string nm = src[m] != null ? src[m].name.ToLowerInvariant() : "";
                        mats[m] = nm.Contains("vissz") ? reflector : nm.Contains("fém") || nm.Contains("fem") ? housing : tube;
                    }
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
                foreach (Collider col in neon.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);

                // Two cables from the ends of the fixture up to the bottom chord of the truss above.
                foreach (float off in new[] { -0.75f, 0.75f })
                    RoofStructureTool.Beam(cables, cube, P(a, c + off, h + 0.08f), P(a, c + off, yCrossBottom), 0.025f);

                {
                    var lgo = new GameObject("Light (down)");
                    lgo.transform.SetParent(neon.transform, false);
                    lgo.transform.localPosition = new Vector3(0f, -0.12f, 0f);
                    lgo.transform.rotation = Quaternion.LookRotation(Vector3.down, P(0f, 1f, 0f) - P(0f, 0f, 0f));
                    Light l = lgo.AddComponent<Light>();
                    l.type = LightType.Spot;
                    l.lightmapBakeType = LightmapBakeType.Realtime;
                    l.color = LightColour;
                    l.intensity = LightIntensity;
                    l.range = LightRange;
                    l.spotAngle = LightOuterAngle;
                    l.innerSpotAngle = LightInnerAngle;
                    l.shadows = LightShadows.None;
                    lights++;
                }
                fixtures++;
            }
        }
        if (cables.Count > 0) RoofStructureTool.MeshObject(neonRoot, "Cables", cables, cableMat, meshes);
        log.AppendLine("neons: " + fixtures + " fixtures in " + Mathf.Max(0, count - 2) + " rows (at least " + NeonHeight + " m high and " + NeonMinClearance + " m above the floor under them), " + lights + " downward spot lights");

        // ---- round orange lamps in front of the checkpoint decks
        GameObject cpRoot = null;
        Material housingMat = DarkFactoryLightingTool.LevelMaterial(DarkFactoryLightingTool.Kind.Ceiling);
        Material orangeBulb = DarkFactoryLightingTool.UnlitMaterial("Lamp_Orange", new Color(1f, 0.62f, 0.3f));
        int cpIndex = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith(CheckpointPrefix)) continue;
                if (cpRoot == null) { cpRoot = new GameObject(CheckpointLampRoot); SceneManager.MoveGameObjectToScene(cpRoot, scene); }
                // "In front" = on the side the player arrives from (towards the start of the room).
                Vector3 front = P(0f, -1f, 0f) - P(0f, 0f, 0f);
                Vector3 pos = t.position + front * CheckpointLampForward;
                pos.y = yCrossBottom - CheckpointLampTopGap;
                DarkFactoryLightingTool.BuildLamp(cpRoot.transform, cpIndex, pos, false, false, false, housingMat, orangeBulb);
                Transform lamp = cpRoot.transform.GetChild(cpRoot.transform.childCount - 1);
                lamp.name = "Checkpoint Lamp (" + t.name + ")";
                Light l = lamp.GetComponentInChildren<Light>();
                if (l != null) l.color = OrangeLight;
                cpIndex++;
                log.AppendLine("orange lamp in front of " + t.name + " at " + pos.ToString("F1"));
            }
        if (cpRoot != null) Undo.RegisterCreatedObjectUndo(cpRoot, "Build Ceiling Rig");

        // ---- invisible fill lights over every floor (lower floor, decks, pit)
        GameObject fillRoot = new GameObject(FillRoot);
        SceneManager.MoveGameObjectToScene(fillRoot, scene);
        int fills = 0;
        float acMin = crossRange.Count > 0 ? crossRange[0].x : lines[0], acMax = crossRange.Count > 0 ? crossRange[0].y : lines[lines.Count - 1];
        foreach (Vector2 r in crossRange) { acMin = Mathf.Min(acMin, r.x); acMax = Mathf.Max(acMax, r.y); }
        int nA = Mathf.Max(1, Mathf.RoundToInt((gMax - gMin) / FillSpacingAcross));
        int nC = Mathf.Max(1, Mathf.RoundToInt((acMax - acMin) / FillSpacingAlong));
        for (int ia = 0; ia < nA; ia++)
            for (int ic = 0; ic < nC; ic++)
            {
                float a = gMin + (ia + 0.5f) * (gMax - gMin) / nA;
                float c = acMin + (ic + 0.5f) * (acMax - acMin) / nC;
                float floor = FloorBelow(P(a, c, yCrossBottom - 0.2f));
                if (float.IsNegativeInfinity(floor)) continue;
                var go = new GameObject("Fill " + (ia + 1) + "." + (ic + 1));
                go.transform.SetParent(fillRoot.transform, false);
                go.transform.position = P(a, c, floor + FillHeight);
                Light l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.lightmapBakeType = LightmapBakeType.Realtime;
                l.color = FillColour;
                l.intensity = FillIntensity;
                l.range = FillRange;
                l.shadows = LightShadows.None;
                fills++;
            }
        Undo.RegisterCreatedObjectUndo(fillRoot, "Build Ceiling Rig");
        log.AppendLine("fill lights: " + fills + " invisible point lights " + FillHeight + " m above the floors (" + nA + " x " + nC + " grid)");

        AssetDatabase.CreateAsset(meshes[0], meshPath);
        for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], meshPath);
        AssetDatabase.SaveAssets();
        Undo.RegisterCreatedObjectUndo(structure, "Build Ceiling Rig");
        Undo.RegisterCreatedObjectUndo(neonRoot, "Build Ceiling Rig");
    }

    /// <summary>Height of the floor straight under <paramref name="from"/> (the first wide, upward-facing surface),
    /// or -infinity if there is none.</summary>
    private static float FloorBelow(Vector3 from)
    {
        float best = float.MaxValue, y = float.NegativeInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.normal.y < 0.7f) continue;
            Vector3 sz = hit.collider.bounds.size;
            if (Mathf.Min(sz.x, sz.z) < MinFloorSize) continue;
            if (hit.distance < best) { best = hit.distance; y = hit.point.y; }
        }
        return y;
    }

    // ------------------------------------------------------------------ materials

    private static Material GetOrCreate(string name)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        return m;
    }

    /// <summary>Blue glowing tube: emissive with a soft bluish glow texture (brighter core, slight unevenness).</summary>
    private static Material TubeMaterial()
    {
        Material m = GetOrCreate("Neon_Tube_Blue");
        Texture2D glow = GlowTexture();
        m.SetColor("_BaseColor", new Color(0.78f, 0.9f, 1f));
        m.SetTexture("_BaseMap", glow);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Smoothness", 0.8f);
        m.EnableKeyword("_EMISSION");
        m.SetTexture("_EmissionMap", glow);
        m.SetColor("_EmissionColor", TubeEmission * TubeEmissionIntensity);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material HousingMaterial()
    {
        Material m = GetOrCreate("Neon_Housing_Metal");
        m.SetColor("_BaseColor", new Color(0.36f, 0.38f, 0.41f));
        m.SetFloat("_Metallic", 0.9f);
        m.SetFloat("_Smoothness", 0.5f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material ReflectorMaterial()
    {
        Material m = GetOrCreate("Neon_Reflector_Metal");
        m.SetColor("_BaseColor", new Color(0.82f, 0.85f, 0.9f));
        m.SetFloat("_Metallic", 1f);
        m.SetFloat("_Smoothness", 0.85f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Texture2D GlowTexture()
    {
        string path = TextureFolder + "/Neon_Tube_Glow.png";
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(TextureFolder);
            const int w = 256, h = 32;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var rnd = new System.Random(7);
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                float core = Mathf.Exp(-Mathf.Pow((v - 0.5f) / 0.22f, 2f)); // bright line along the middle of the tube
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    float uneven = 0.92f + 0.08f * Mathf.Sin(u * 37f) + (float)rnd.NextDouble() * 0.03f;
                    float b = Mathf.Lerp(0.55f, 1f, core) * uneven;
                    Color c = Color.Lerp(new Color(0.35f, 0.6f, 1f), new Color(0.85f, 0.95f, 1f), core) * b;
                    c.a = 1f;
                    t.SetPixel(x, y, c);
                }
            }
            t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
