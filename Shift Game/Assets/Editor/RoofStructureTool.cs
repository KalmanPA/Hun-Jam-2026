using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a metal roof structure above the "Roof Truss" beams of the open scene.
///
///  - Longitudinal trusses: on top of every Roof Truss beam a V-webbed (Warren) truss with its own top chord.
///  - Cross trusses: at regular spacing, spanning wall to wall across the hall, a pitched (gable) truss with
///    verticals and alternating V diagonals.
///  - Purlins: long beams on top of the cross trusses, at every cross-truss panel node (incl. the ridge).
///
/// Heights come from the beams; lengths come from raycasts: every member runs until it hits the nearest wall
/// (any collider that is not a Roof Truss), so it follows the beams and walls if they move.
/// Each truss is one combined mesh (saved in Assets/Art/Generated/RoofStructure_Meshes.asset), no colliders.
///
/// Menu: Final Shift > Rooms > Build Roof Structure Over Roof Trusses (running it again rebuilds it).
/// The scene is marked dirty but NOT saved: save it yourself (Cmd+S) if you like the result.
/// </summary>
[InitializeOnLoad]
public static class RoofStructureTool
{
    private const string RootName = "== Roof Structure (generated) ==";
    private const string BeamPrefix = "Roof Truss";
    private const string MeshAssetPath = "Assets/Art/Generated/RoofStructure_Meshes.asset";
    private const string TriggerFile = "Temp/FinalShift_RoofStructure";
    private const string RemoveTriggerFile = "Temp/FinalShift_RoofStructureRemove";

    // ---- Tuning (metres) ----
    private const float LongTrussHeight = 1.2f;  // V truss height on top of each Roof Truss beam
    private const float PanelLength = 2.5f;      // target length of one V panel
    private const float CrossSpacing = 6f;       // target distance between cross trusses
    private const float EdgeDepth = 0.7f;        // cross truss depth at its two ends
    private const float RidgeRise = 2.2f;        // extra height at the middle (gable). 0 = flat
    private const float Overhang = 0.6f;         // cross trusses / purlins run past the outer beams
    private const float ChordThickness = 0.2f;
    private const float WebThickness = 0.1f;
    private const float PurlinThickness = 0.16f;
    private const float MinWallWidth = 12f;       // a collider narrower than this across the ray is not a wall

    static RoofStructureTool()
    {
        EditorApplication.update += PollTriggers;
    }

    private static void PollTriggers()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        if (File.Exists(TriggerFile)) { File.Delete(TriggerFile); BuildInActive(); }
        if (File.Exists(RemoveTriggerFile)) { File.Delete(RemoveTriggerFile); RemoveFromActive(); }
    }

    [MenuItem("Final Shift/Rooms/Build Roof Structure Over Roof Trusses")]
    public static void BuildInActive()
    {
        Scene s = SceneManager.GetActiveScene();
        string msg = Build(s);
        EditorSceneManager.MarkSceneDirty(s);
        Debug.Log("[FinalShift] Roof structure: " + msg);
    }

    [MenuItem("Final Shift/Rooms/Remove Roof Structure")]
    public static void RemoveFromActive()
    {
        Scene s = SceneManager.GetActiveScene();
        foreach (GameObject root in s.GetRootGameObjects())
            if (root.name == RootName) Undo.DestroyObjectImmediate(root);
        EditorSceneManager.MarkSceneDirty(s);
    }

    // ------------------------------------------------------------------ core

    private static bool alongX;
    private static Vector3 P(float along, float across, float y) =>
        alongX ? new Vector3(along, y, across) : new Vector3(across, y, along);

    private static string Build(Scene scene)
    {
        // 1. Find the beams.
        var beams = new List<Renderer>();
        foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            foreach (Renderer r in sceneRoot.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.name.StartsWith(BeamPrefix) && r.gameObject.activeInHierarchy)
                    beams.Add(r);
        if (beams.Count == 0) return "no '" + BeamPrefix + "' objects found in " + scene.name;

        Bounds all = beams[0].bounds;
        foreach (Renderer r in beams) all.Encapsulate(r.bounds);
        alongX = all.size.x >= all.size.z;

        float alongMin = alongX ? all.min.x : all.min.z;
        float alongMax = alongX ? all.max.x : all.max.z;
        float topY = all.max.y;
        var lines = new List<float>();
        foreach (Renderer r in beams)
        {
            float c = alongX ? r.bounds.center.z : r.bounds.center.x;
            bool dup = false;
            foreach (float l in lines) if (Mathf.Abs(l - c) < 0.5f) dup = true;
            if (!dup) lines.Add(c);
        }
        lines.Sort();
        Material mat = beams[0].sharedMaterial;

        // 2. Clean up the previous build.
        foreach (GameObject old in scene.GetRootGameObjects())
            if (old.name == RootName) Undo.DestroyObjectImmediate(old);
        Directory.CreateDirectory(Path.GetDirectoryName(MeshAssetPath));
        AssetDatabase.DeleteAsset(MeshAssetPath);

        GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh cube = tmp.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(tmp);

        var root = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        var meshes = new List<Mesh>();
        float y1 = topY + LongTrussHeight;                 // top chord of the longitudinal trusses
        float halfC = ChordThickness * 0.5f;

        // 3. Measure the walls: every member runs until it hits the nearest wall.
        Physics.SyncTransforms();
        float centerAlong = (alongMin + alongMax) * 0.5f;
        float centerAcross = (lines[0] + lines[lines.Count - 1]) * 0.5f;
        Vector3 dirAlong = alongX ? Vector3.right : Vector3.forward;
        Vector3 dirAcross = alongX ? Vector3.forward : Vector3.right;
        float[] probeHeights = { y1, topY + 0.3f, topY - 1f, topY - 3f };
        int wallHits = 0, wallMisses = 0;
        var debug = new System.Text.StringBuilder();
        foreach (Renderer r in beams) debug.AppendLine("beam " + HierarchyPath(r.transform) + " bounds " + r.bounds.min.ToString("F1") + ".." + r.bounds.max.ToString("F1"));
        System.Func<float, float, Vector3, float, float> reach = (al, ac, dir, fallback) =>
        {
            foreach (float h in probeHeights)
            {
                Vector3 o = P(al, ac, h);
                float best = float.MaxValue;
                string bestName = "";
                foreach (RaycastHit hit in Physics.RaycastAll(o, dir, 300f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.gameObject.name.StartsWith(BeamPrefix)) continue;
                    // Only real walls count: the collider has to be wide across the ray (skips small props,
                    // spawner booths etc. that reach up into the roof zone).
                    Vector3 sz = hit.collider.bounds.size;
                    float width = Mathf.Abs(dir.x) > 0.5f ? sz.z : sz.x;
                    if (width < MinWallWidth) continue;
                    if (hit.distance < best) { best = hit.distance; bestName = HierarchyPath(hit.collider.transform); }
                }
                if (best < float.MaxValue)
                {
                    wallHits++;
                    debug.AppendLine("ray from " + o.ToString("F1") + " dir " + dir + " -> " + bestName + " at " + (o + dir * best).ToString("F1"));
                    return Vector3.Dot(o + dir * best, dir);
                }
            }
            wallMisses++;
            return fallback;
        };

        // 4. Longitudinal V trusses on every beam, wall to wall.
        GameObject longParent = Child(root, "Longitudinal Trusses");
        float gAlongMin = float.MaxValue, gAlongMax = float.MinValue;
        for (int li = 0; li < lines.Count; li++)
        {
            float c = lines[li];
            float aMin = -reach(centerAlong, c, -dirAlong, -alongMin);
            float aMax = reach(centerAlong, c, dirAlong, alongMax);
            gAlongMin = Mathf.Min(gAlongMin, aMin); gAlongMax = Mathf.Max(gAlongMax, aMax);
            int nLong = Mathf.Max(1, Mathf.RoundToInt((aMax - aMin) / PanelLength));
            float pLong = (aMax - aMin) / nLong;
            var parts = new List<CombineInstance>();
            Beam(parts, cube, P(aMin, c, y1), P(aMax, c, y1), ChordThickness);
            for (int i = 0; i < nLong; i++)
            {
                float a0 = aMin + i * pLong, am = a0 + pLong * 0.5f, a1 = a0 + pLong;
                Beam(parts, cube, P(a0, c, topY), P(am, c, y1 - halfC), WebThickness);
                Beam(parts, cube, P(am, c, y1 - halfC), P(a1, c, topY), WebThickness);
            }
            MeshObject(longParent, "Longitudinal Truss " + (li + 1), parts, mat, meshes);
        }

        // 5. Cross trusses (gable, verticals + alternating V diagonals), wall to wall.
        float y2 = y1 + ChordThickness;                   // bottom chord sits on the long trusses
        int count = Mathf.Max(2, Mathf.RoundToInt((gAlongMax - gAlongMin) / CrossSpacing) + 1);
        float inset = ChordThickness;
        var crossAt = new float[count];
        var cMins = new float[count];
        var cMaxs = new float[count];
        float gMin = float.MaxValue, gMax = float.MinValue;
        for (int k = 0; k < count; k++)
        {
            crossAt[k] = Mathf.Lerp(gAlongMin + inset, gAlongMax - inset, k / (float)(count - 1));
            cMins[k] = -reach(crossAt[k], centerAcross, -dirAcross, -(lines[0] - Overhang));
            cMaxs[k] = reach(crossAt[k], centerAcross, dirAcross, lines[lines.Count - 1] + Overhang);
            gMin = Mathf.Min(gMin, cMins[k]); gMax = Mathf.Max(gMax, cMaxs[k]);
        }
        float span = gMax - gMin;
        int nCross = Mathf.Max(2, Mathf.RoundToInt(span / PanelLength));
        if (nCross % 2 == 1) nCross++;                     // even, so the ridge sits on a node
        float pCross = span / nCross;
        System.Func<float, float> topAt = a =>
        {
            float u = Mathf.Clamp01((a - gMin) / span);
            return y2 + EdgeDepth + RidgeRise * (1f - Mathf.Abs(2f * u - 1f));
        };

        GameObject crossParent = Child(root, "Cross Trusses");
        for (int k = 0; k < count; k++)
        {
            float a = crossAt[k], cMin = cMins[k], cMax = cMaxs[k];
            var nodes = new List<float> { cMin };
            for (int i = 0; i <= nCross; i++)
            {
                float g = gMin + i * pCross;
                if (g > cMin + 0.3f && g < cMax - 0.3f) nodes.Add(g);
            }
            nodes.Add(cMax);
            var parts = new List<CombineInstance>();
            Beam(parts, cube, P(a, cMin, y2), P(a, cMax, y2), ChordThickness);
            for (int j = 0; j < nodes.Count; j++)
            {
                float c0 = nodes[j];
                Beam(parts, cube, P(a, c0, y2), P(a, c0, topAt(c0)), WebThickness);   // vertical
                if (j == nodes.Count - 1) break;
                float c1 = nodes[j + 1];
                Beam(parts, cube, P(a, c0, topAt(c0)), P(a, c1, topAt(c1)), ChordThickness); // top chord
                int gi = Mathf.Clamp(Mathf.FloorToInt(((c0 + c1) * 0.5f - gMin) / pCross), 0, nCross - 1);
                bool leftHalf = gi < nCross / 2;
                // V pattern, mirrored about the ridge.
                if ((gi % 2 == 0) == leftHalf) Beam(parts, cube, P(a, c0, y2), P(a, c1, topAt(c1)), WebThickness);
                else Beam(parts, cube, P(a, c0, topAt(c0)), P(a, c1, y2), WebThickness);
            }
            MeshObject(crossParent, "Cross Truss " + (k + 1), parts, mat, meshes);
        }

        // 6. Purlins on top of the cross trusses, wall to wall.
        var purl = new List<CombineInstance>();
        float pOff = ChordThickness * 0.5f + PurlinThickness * 0.5f;
        for (int i = 0; i <= nCross; i++)
        {
            float c = gMin + i * pCross;
            if (i == 0) c += PurlinThickness;
            if (i == nCross) c -= PurlinThickness;
            float y = topAt(c) + pOff;
            float aMin = -reach(centerAlong, c, -dirAlong, -gAlongMin);
            float aMax = reach(centerAlong, c, dirAlong, gAlongMax);
            Beam(purl, cube, P(aMin, c, y), P(aMax, c, y), PurlinThickness);
        }
        MeshObject(root, "Purlins", purl, mat, meshes);

        File.WriteAllText("Temp/FinalShift_RoofStructure_debug.txt", debug.ToString());

        // 7. Save meshes as one asset (keeps the scene file small).
        AssetDatabase.CreateAsset(meshes[0], MeshAssetPath);
        for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], MeshAssetPath);
        AssetDatabase.SaveAssets();

        Undo.RegisterCreatedObjectUndo(root, "Build Roof Structure");
        Selection.activeGameObject = root;
        return lines.Count + " longitudinal trusses, " + count + " cross trusses, " + (nCross + 1) +
               " purlins over " + beams.Count + " beams (" + (alongX ? "along X" : "along Z") + ", top Y " +
               topY.ToString("0.00") + "). Walls: " + wallHits + " hits, " + wallMisses + " misses; extent along " +
               gAlongMin.ToString("0.0") + ".." + gAlongMax.ToString("0.0") + ", across " + gMin.ToString("0.0") + ".." + gMax.ToString("0.0") + ".";
    }

    private static string HierarchyPath(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }

    private static GameObject Child(GameObject parent, string name)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent.transform, false);
        return g;
    }

    private static void Beam(List<CombineInstance> list, Mesh cube, Vector3 a, Vector3 b, float t)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 0.001f) return;
        Vector3 up = Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
        Matrix4x4 m = Matrix4x4.TRS((a + b) * 0.5f, Quaternion.LookRotation(d, up), new Vector3(t, t, len + t * 0.5f));
        list.Add(new CombineInstance { mesh = cube, transform = m });
    }

    private static void MeshObject(GameObject parent, string name, List<CombineInstance> parts, Material mat, List<Mesh> store)
    {
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.CombineMeshes(parts.ToArray(), true, true);
        mesh.RecalculateBounds();
        store.Add(mesh);
        var g = new GameObject(name);
        g.transform.SetParent(parent.transform, false);   // root is at the origin, mesh is in world space
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = g.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;     // lamps hang below the roof; saves shadow-map budget
    }
}
