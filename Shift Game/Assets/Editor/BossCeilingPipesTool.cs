using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds medium-thick rusty pipes along the ceiling of the boss room. They come out of the side walls and the
/// back wall, run under the ceiling, bend down with curved elbows and plug into the sides of the boss's monitor head.
/// Works on the open scene only (must be BossFight) and does not save it. Running it again replaces the old pipes.
/// Positions are read from the scene's "Head Casing" (the monitor) and "Ceiling" (whose edges are the walls), so hand edits to
/// the room are respected.
/// Menu: Final Shift > Boss > Add Ceiling Pipes To Monitor
/// </summary>
[InitializeOnLoad]
public static class BossCeilingPipesTool
{
    private const string MaterialPath = "Assets/Art/Materials/rusty_metal_04.hand.mat";
    private const string MeshFolder = "Assets/Art/Generated/BossPipes";
    private const string RootName = "== Ceiling Pipes ==";
    private const string TriggerFile = "Temp/FinalShift_BossPipes";
    private const string ReportFile = "Temp/FinalShift_BossPipes_report.txt";
    private const string VersionKey = "FinalShift.BossPipes.LayoutVersion";
    private const int LayoutVersion = 2;

    private const float PipeRadius = 0.2f;   // 40 cm pipes
    private const float BendRadius = 1.0f;   // elbow radius
    private const int Sides = 16;
    private const int ArcSteps = 10;

    static BossCeilingPipesTool()
    {
        EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (File.Exists(TriggerFile)) { File.Delete(TriggerFile); Apply(); return; }
            // Rebuild once after this version of the layout first compiles (BossFight must be open).
            if (EditorPrefs.GetInt(VersionKey, 0) < LayoutVersion && SceneManager.GetActiveScene().name == "BossFight")
            {
                EditorPrefs.SetInt(VersionKey, LayoutVersion);
                Apply();
            }
        };
    }

    [MenuItem("Final Shift/Boss/Add Ceiling Pipes To Monitor")]
    public static void Apply()
    {
        string msg;
        try { msg = Build(); }
        catch (System.Exception e) { msg = "failed: " + e; Debug.LogException(e); }
        File.WriteAllText(ReportFile, msg);
        Debug.Log("[FinalShift] Boss ceiling pipes: " + msg);
    }

    private static string Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "BossFight") return "open scene is '" + scene.name + "', not BossFight; nothing added";

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null) return MaterialPath + " not found";

        Renderer head = FindLargest(scene, "Head Casing");
        if (head == null) return "no 'Head Casing' (the monitor) in the scene";
        Bounds hb = head.bounds;
        Vector3 H = hb.center;
        float hw = hb.extents.x;

        // The room's walls are the edges of the ceiling slab.
        Renderer ceiling = FindLargest(scene, "Ceiling");
        Bounds cb = ceiling != null ? ceiling.bounds : new Bounds(new Vector3(0f, 18.25f, -5f), new Vector3(40f, 0.5f, 38f));
        float ceilY = cb.min.y;
        float westX = cb.min.x, eastX = cb.max.x, backZ = cb.max.z;

        // Replace pipes from an earlier run.
        foreach (GameObject go in scene.GetRootGameObjects())
            if (go.name == RootName) Undo.DestroyObjectImmediate(go);

        EnsureFolder(MeshFolder);
        GameObject root = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        Undo.RegisterCreatedObjectUndo(root, "Add Ceiling Pipes");

        // The monitor is big and reaches almost to the ceiling, so the runs stay beside it and come in from the side.
        float yA = ceilY - 0.5f, yB = ceilY - 1.1f, yC = ceilY - 0.7f;
        float sink = 0.15f; // how far the pipe ends go into the wall / casing
        float front = hb.min.z, depthVisible = Mathf.Min(hb.max.z, backZ) - front;
        float zA = front + depthVisible * 0.3f, zB = front + depthVisible * 0.55f, zC = front + depthVisible * 0.8f;
        float hh = hb.extents.y;
        int count = 0;

        foreach (int s in new[] { -1, 1 })
        {
            string side = s < 0 ? "West" : "East";
            float wallX = (s < 0 ? westX : eastX) + s * 0.3f;
            float inX = H.x + s * (hw - sink);
            float gapX = Mathf.Abs((s < 0 ? westX : eastX) - H.x) - hw; // free space between the monitor and the side wall

            // A: out of the side wall in front of the monitor, along the ceiling, turns back toward the monitor,
            //    drops and plugs into the upper part of its side.
            float ax = H.x + s * (hw + gapX * 0.43f);
            Pipe(root.transform, mat, "Pipe " + side + " A (side wall)", new[]
            {
                new Vector3(wallX, yA, front - 5f),
                new Vector3(ax, yA, front - 5f),
                new Vector3(ax, yA, zA),
                new Vector3(ax, H.y + hh * 0.6f, zA),
                new Vector3(inX, H.y + hh * 0.6f, zA),
            }, ceilY);

            // B: out of the side wall a bit lower, crosses under A, turns back, drops and plugs into the middle.
            float bx = H.x + s * (hw + gapX * 0.28f);
            Pipe(root.transform, mat, "Pipe " + side + " B (side wall)", new[]
            {
                new Vector3(wallX, yB, front - 2f),
                new Vector3(bx, yB, front - 2f),
                new Vector3(bx, yB, zB),
                new Vector3(bx, H.y + hh * 0.25f, zB),
                new Vector3(inX, H.y + hh * 0.25f, zB),
            }, ceilY);

            // C: out of the back wall, a short run forward under the ceiling, a long drop, then into the lower part.
            float cx = H.x + s * (hw + gapX * 0.58f);
            Pipe(root.transform, mat, "Pipe " + side + " C (back wall)", new[]
            {
                new Vector3(cx, yC, backZ + 0.3f),
                new Vector3(cx, yC, zC),
                new Vector3(cx, H.y + hh * 0.0f, zC),
                new Vector3(inX, H.y + hh * 0.0f, zC),
            }, ceilY);
            count += 3;
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        return "v" + LayoutVersion + ": " + count + " pipes added to " + scene.name + " (scene not saved). Monitor at " + H + ", ceiling y " + ceilY;
    }

    // ------------------------------------------------------------------ one pipe

    private static void Pipe(Transform parent, Material mat, string name, Vector3[] corners, float ceilY)
    {
        List<Vector3> path = Smooth(corners);
        Vector3 origin = corners[0];

        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = origin;

        Mesh mesh = Tube(path, origin, name);
        string assetPath = MeshFolder + "/" + Sanitize(name) + ".asset";
        AssetDatabase.DeleteAsset(assetPath);
        AssetDatabase.CreateAsset(mesh, assetPath);

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.On;

        // Flanges where the pipe leaves the wall and where it enters the monitor.
        Flange(go.transform, mat, corners[0] + (corners[1] - corners[0]).normalized * 0.32f, corners[1] - corners[0]);
        int n = corners.Length;
        Flange(go.transform, mat, corners[n - 1] - (corners[n - 1] - corners[n - 2]).normalized * 0.2f, corners[n - 1] - corners[n - 2]);

        // Hangers holding the runs under the ceiling.
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 a = corners[i], b = corners[i + 1];
            if (Mathf.Abs(a.y - b.y) > 0.01f || a.y < ceilY - 1.6f) continue; // only horizontal ceiling runs
            float len = Vector3.Distance(a, b);
            Vector3 dir = (b - a) / len;
            for (float d = 2f; d < len - 1.4f; d += 3.5f)
                Hanger(go.transform, mat, a + dir * d, dir, ceilY);
        }
    }

    private static void Flange(Transform parent, Material mat, Vector3 pos, Vector3 dir)
    {
        GameObject f = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        f.name = "Flange";
        Object.DestroyImmediate(f.GetComponent<Collider>());
        f.transform.SetParent(parent, false);
        f.transform.position = pos;
        f.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
        f.transform.localScale = new Vector3(PipeRadius * 2.7f, 0.06f, PipeRadius * 2.7f);
        f.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    private static void Hanger(Transform parent, Material mat, Vector3 pos, Vector3 dir, float ceilY)
    {
        // Band around the pipe.
        GameObject band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        band.name = "Hanger Band";
        Object.DestroyImmediate(band.GetComponent<Collider>());
        band.transform.SetParent(parent, false);
        band.transform.position = pos;
        band.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir);
        band.transform.localScale = new Vector3(PipeRadius * 2.3f, 0.05f, PipeRadius * 2.3f);
        band.GetComponent<MeshRenderer>().sharedMaterial = mat;

        // Rod up to the ceiling.
        float top = pos.y + PipeRadius;
        float rod = ceilY - top;
        if (rod <= 0.02f) return;
        GameObject r = GameObject.CreatePrimitive(PrimitiveType.Cube);
        r.name = "Hanger Rod";
        Object.DestroyImmediate(r.GetComponent<Collider>());
        r.transform.SetParent(parent, false);
        r.transform.position = new Vector3(pos.x, top + rod * 0.5f, pos.z);
        r.transform.localScale = new Vector3(0.05f, rod, 0.05f);
        r.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    // ------------------------------------------------------------------ geometry

    /// <summary>Replaces each corner of the polyline with a circular elbow.</summary>
    private static List<Vector3> Smooth(Vector3[] pts)
    {
        var result = new List<Vector3> { pts[0] };
        int last = pts.Length - 1;
        for (int i = 1; i < last; i++)
        {
            Vector3 a = pts[i - 1], p = pts[i], b = pts[i + 1];
            float len0 = Vector3.Distance(a, p), len1 = Vector3.Distance(p, b);
            Vector3 d0 = (p - a) / len0, d1 = (b - p) / len1;
            float theta = Vector3.Angle(d0, d1) * Mathf.Deg2Rad;
            if (theta < 0.01f) { result.Add(p); continue; }

            float tanHalf = Mathf.Tan(theta * 0.5f);
            float avail0 = i - 1 == 0 ? len0 - 0.5f : len0 * 0.5f;
            float avail1 = i + 1 == last ? len1 - 0.5f : len1 * 0.5f;
            float t = Mathf.Max(0.05f, Mathf.Min(BendRadius * tanHalf, Mathf.Min(avail0, avail1)));
            float r = t / tanHalf;

            Vector3 start = p - d0 * t;
            Vector3 inward = (d1 - Vector3.Dot(d1, d0) * d0).normalized;
            Vector3 centre = start + inward * r;
            for (int k = 0; k <= ArcSteps; k++)
            {
                float phi = theta * k / ArcSteps;
                result.Add(centre - inward * (r * Mathf.Cos(phi)) + d0 * (r * Mathf.Sin(phi)));
            }
        }
        result.Add(pts[last]);

        // Drop points that touch (an elbow can end exactly where the next one starts).
        var clean = new List<Vector3> { result[0] };
        for (int i = 1; i < result.Count; i++)
            if ((result[i] - clean[clean.Count - 1]).sqrMagnitude > 1e-6f) clean.Add(result[i]);
        return clean;
    }

    /// <summary>Sweeps a circle along the path (parallel-transport frames). UV u goes round the pipe, v along it.</summary>
    private static Mesh Tube(List<Vector3> pts, Vector3 origin, string name)
    {
        int n = pts.Count;
        int ring = Sides + 1;
        var verts = new List<Vector3>(n * ring);
        var normals = new List<Vector3>(n * ring);
        var uvs = new List<Vector2>(n * ring);
        var tris = new List<int>((n - 1) * Sides * 6);

        Vector3 t0 = (pts[1] - pts[0]).normalized;
        Vector3 nrm = Vector3.Cross(t0, Mathf.Abs(t0.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 prevT = t0;
        float circumference = 2f * Mathf.PI * PipeRadius;
        float v = 0f;

        for (int i = 0; i < n; i++)
        {
            Vector3 t;
            if (i == 0) t = t0;
            else if (i == n - 1) t = (pts[i] - pts[i - 1]).normalized;
            else t = ((pts[i] - pts[i - 1]).normalized + (pts[i + 1] - pts[i]).normalized).normalized;

            if (i > 0)
            {
                v += Vector3.Distance(pts[i], pts[i - 1]) / circumference;
                nrm = Quaternion.FromToRotation(prevT, t) * nrm;
                nrm = Vector3.ProjectOnPlane(nrm, t).normalized;
            }
            prevT = t;
            Vector3 bin = Vector3.Cross(t, nrm);

            for (int j = 0; j <= Sides; j++)
            {
                float ang = j / (float)Sides * Mathf.PI * 2f;
                Vector3 dir = nrm * Mathf.Cos(ang) + bin * Mathf.Sin(ang);
                verts.Add(pts[i] - origin + dir * PipeRadius);
                normals.Add(dir);
                uvs.Add(new Vector2(j / (float)Sides, v));
            }
        }

        for (int i = 0; i < n - 1; i++)
            for (int j = 0; j < Sides; j++)
            {
                int a = i * ring + j, b = a + ring;
                tris.Add(a); tris.Add(a + 1); tris.Add(b);
                tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
            }

        var mesh = new Mesh { name = name };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ------------------------------------------------------------------ helpers

    private static Renderer FindLargest(Scene scene, string objectName)
    {
        Renderer best = null;
        float bestSize = -1f;
        foreach (GameObject rootGo in scene.GetRootGameObjects())
            foreach (Renderer r in rootGo.GetComponentsInChildren<Renderer>(true))
            {
                if (r.gameObject.name != objectName) continue;
                float size = r.bounds.size.sqrMagnitude;
                if (size > bestSize) { bestSize = size; best = r; }
            }
        return best;
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static string Sanitize(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace('(', '_').Replace(')', '_').Replace(' ', '_');
    }
}
