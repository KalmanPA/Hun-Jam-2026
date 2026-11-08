using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// BossFight: a low truss (top + bottom chord, verticals, V diagonals) under the ceiling from each full-height
/// "Column" both ways along the room: towards the boss screen and back to the opposite wall, each ending where it
/// would run into something (the monitor casing or a wall).
/// Neon.fbx fixtures sit high, right under the bottom chord, with faintly glowing red tubes and a weak red light each.
/// The existing lamps are left alone.
///
/// Menu: Final Shift > Rooms > Build Column Beams To Screen (+ Red Neons). Running it again rebuilds them.
/// The scene is marked dirty but NOT saved.
/// </summary>
[InitializeOnLoad]
public static class ColumnBeamTool
{
    private const string Root = "== Column Beams (generated) ==";
    private const string ColumnPrefix = "Column";
    private const string ScreenName = "Screen";
    private const string NeonModelPath = "Assets/Art/Neon.fbx";
    private const string TriggerFile = "Temp/FinalShift_ColumnBeams";
    private const string ReportFile = "Temp/FinalShift_ColumnBeams_report.txt";

    // ---- truss (same low profile as the ElevatorRoom one)
    private const float Depth = 1.25f;       // chord centre to chord centre (about 1.43 m overall)
    private const float Chord = 0.18f;
    private const float Web = 0.09f;
    private const float Panel = 2.5f;
    private const float EndGap = 0.3f;       // stops this short of whatever it runs into
    private const float MinWallWidth = 12f;

    // ---- red neons
    private const float NeonSpacing = 5f;
    private const float NeonUnderChord = 0.2f;
    private static readonly Color TubeRed = new Color(1f, 0.12f, 0.08f);
    private const float TubeEmissionIntensity = 1.6f;   // faint
    private static readonly Color RedLight = new Color(1f, 0.15f, 0.1f);
    private const float RedLightIntensity = 1.5f;
    private const float RedLightRange = 6f;

    static ColumnBeamTool()
    {
        EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (File.Exists(TriggerFile)) { File.Delete(TriggerFile); BuildInActive(); }
        };
    }

    [MenuItem("Final Shift/Rooms/Build Column Beams To Screen (+ Red Neons)")]
    public static void BuildInActive()
    {
        var log = new System.Text.StringBuilder();
        Scene s = SceneManager.GetActiveScene();
        try { Build(s, log); }
        catch (System.Exception e) { log.AppendLine("ERROR " + e); }
        EditorSceneManager.MarkSceneDirty(s);
        File.WriteAllText(ReportFile, log.ToString());
        Debug.Log("[FinalShift] Column beams in " + s.name + ":\n" + log);
    }

    private static void Build(Scene scene, System.Text.StringBuilder log)
    {
        foreach (GameObject old in scene.GetRootGameObjects())
            if (old.name == Root) Undo.DestroyObjectImmediate(old);
        Physics.SyncTransforms();

        // Columns, ceiling, screen.
        var columns = new List<Renderer>();
        Renderer screen = null;
        float ceiling = float.MaxValue;
        var all = new List<Renderer>();
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.gameObject.activeInHierarchy) continue;
                all.Add(r);
                if (r.name.StartsWith(ColumnPrefix)) columns.Add(r);
                if (r.name == ScreenName) screen = r;
                if (r.name == "Ceiling") ceiling = Mathf.Min(ceiling, r.bounds.min.y);
            }
        if (columns.Count == 0 || screen == null) { log.AppendLine("need objects named Column* and Screen"); return; }
        if (ceiling == float.MaxValue) foreach (Renderer c in columns) ceiling = Mathf.Min(ceiling, c.bounds.max.y);

        float yTop = ceiling - Chord * 0.5f;
        float yBottom = yTop - Depth;
        log.AppendLine("ceiling " + ceiling.ToString("0.00") + ", truss " + (yBottom - Chord * 0.5f).ToString("0.00") + ".." + ceiling.ToString("0.00"));

        GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh cube = tmp.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(tmp);
        var meshes = new List<Mesh>();
        GameObject rootGo = new GameObject(Root);
        SceneManager.MoveGameObjectToScene(rootGo, scene);

        GameObject neonModel = AssetDatabase.LoadAssetAtPath<GameObject>(NeonModelPath);
        Material tube = RedTube(), housing = CeilingRigTool.HousingMaterial(), reflector = CeilingRigTool.ReflectorMaterial();
        int neons = 0;

        foreach (Renderer col in columns)
        {
            Bounds cb = col.bounds;
            // Run along the room axis that points most towards the screen.
            Vector3 to = screen.bounds.center - cb.center;
            to.y = 0f;
            Vector3 toScreen = Mathf.Abs(to.z) >= Mathf.Abs(to.x) ? new Vector3(0f, 0f, Mathf.Sign(to.z)) : new Vector3(Mathf.Sign(to.x), 0f, 0f);
            // Both ways from the column: towards the screen and back to the opposite wall.
            foreach (Vector3 dir in new[] { toScreen, -toScreen })
            {
            string label = col.name + (dir == toScreen ? ", to screen" : ", back");
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Vector3 c = cb.center; c.y = 0f;
            float start = Vector3.Dot(c, dir) + Vector3.Dot(cb.extents, new Vector3(Mathf.Abs(dir.x), 0f, Mathf.Abs(dir.z)));
            float lateral = Vector3.Dot(c, side);

            // End: the nearest thing the truss would run into (renderers in its path), or the wall.
            float end = WallAlong(c, dir, yTop, yBottom, col);
            string endName = "wall";
            const float halfWidth = 0.3f;
            foreach (Renderer r in all)
            {
                if (r == col || r.name == "Ceiling" || IsUnder(r.transform, Root)) continue;
                Bounds b = r.bounds;
                Vector3 sz = b.size;
                if (Mathf.Min(sz.x, sz.z) < 0.2f) continue;                // cables, thin rods
                if (b.max.y < yBottom - Chord || b.min.y > yTop + Chord) continue;
                float bl0 = Mathf.Min(Vector3.Dot(b.min, side), Vector3.Dot(b.max, side));
                float bl1 = Mathf.Max(Vector3.Dot(b.min, side), Vector3.Dot(b.max, side));
                if (bl1 < lateral - halfWidth || bl0 > lateral + halfWidth) continue;
                float near = Mathf.Min(Vector3.Dot(b.min, dir), Vector3.Dot(b.max, dir));
                if (near > start + 0.05f && near - EndGap < end) { end = near - EndGap; endName = r.name; }
            }
            log.AppendLine(label + ": from " + start.ToString("0.0") + " to " + end.ToString("0.0") + " along " + dir + " (ends at " + endName + ")");

            System.Func<float, float, Vector3> P = (along, y) => side * lateral + dir * along + Vector3.up * y;
            var parts = new List<CombineInstance>();
            RoofStructureTool.Beam(parts, cube, P(start, yTop), P(end, yTop), Chord);
            RoofStructureTool.Beam(parts, cube, P(start, yBottom), P(end, yBottom), Chord);
            int n = Mathf.Max(2, Mathf.RoundToInt((end - start) / Panel));
            float p = (end - start) / n;
            for (int i = 0; i <= n; i++)
            {
                float a0 = start + i * p;
                RoofStructureTool.Beam(parts, cube, P(a0, yTop), P(a0, yBottom), Web);
                if (i == n) break;
                float a1 = a0 + p;
                if (i % 2 == 0) RoofStructureTool.Beam(parts, cube, P(a0, yBottom), P(a1, yTop), Web);
                else RoofStructureTool.Beam(parts, cube, P(a0, yTop), P(a1, yBottom), Web);
            }
            RoofStructureTool.MeshObject(rootGo, "Beam (" + label + ")", parts, col.sharedMaterial, meshes);

            // Red neons right under the bottom chord.
            GameObject row = RoofStructureTool.Child(rootGo, "Red Neons (" + label + ")");
            int count = Mathf.Max(1, Mathf.FloorToInt((end - start - 2f) / NeonSpacing) + 1);
            float used = (count - 1) * NeonSpacing, first = (start + end) * 0.5f - used * 0.5f;
            float yNeon = yBottom - Chord * 0.5f - NeonUnderChord;
            for (int i = 0; i < count; i++)
            {
                var neon = (GameObject)PrefabUtility.InstantiatePrefab(neonModel, scene);
                neon.name = "Red Neon " + (i + 1);
                neon.transform.SetParent(row.transform, false);
                neon.transform.position = P(first + i * NeonSpacing, yNeon);
                // The model lies along its X, tubes down: turn X onto the beam direction.
                neon.transform.rotation = Quaternion.FromToRotation(Vector3.right, dir);
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
                foreach (Collider cl in neon.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(cl);
                var lgo = new GameObject("Red Glow");
                lgo.transform.SetParent(neon.transform, false);
                lgo.transform.localPosition = new Vector3(0f, -0.3f, 0f);
                Light l = lgo.AddComponent<Light>();
                l.type = LightType.Point;
                l.lightmapBakeType = LightmapBakeType.Realtime;
                l.color = RedLight;
                l.intensity = RedLightIntensity;
                l.range = RedLightRange;
                l.shadows = LightShadows.None;
                neons++;
            }
            }
        }
        log.AppendLine(neons + " red neons at " + (yBottom - Chord * 0.5f - NeonUnderChord).ToString("0.00") + " m");

        string meshPath = "Assets/Art/Generated/" + scene.name + "_ColumnBeams.asset";
        Directory.CreateDirectory("Assets/Art/Generated");
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(meshes[0], meshPath);
        for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], meshPath);
        AssetDatabase.SaveAssets();
        Undo.RegisterCreatedObjectUndo(rootGo, "Build Column Beams");
    }

    private static bool IsUnder(Transform t, string rootName)
    {
        while (t != null) { if (t.name == rootName) return true; t = t.parent; }
        return false;
    }

    private static float WallAlong(Vector3 from, Vector3 dir, float yTop, float yBottom, Renderer ignore)
    {
        float best = float.MaxValue;
        foreach (float y in new[] { yTop, (yTop + yBottom) * 0.5f, yBottom })
            foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(from.x, y, from.z), dir, 300f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform == ignore.transform) continue;
                Vector3 sz = hit.collider.bounds.size;
                if ((Mathf.Abs(dir.x) > 0.5f ? sz.z : sz.x) < MinWallWidth) continue;
                best = Mathf.Min(best, Vector3.Dot(hit.point, dir));
            }
        return best == float.MaxValue ? Vector3.Dot(from, dir) + 20f : best;
    }

    /// <summary>Faint red neon: dim red emission.</summary>
    private static Material RedTube()
    {
        Material m = CeilingRigTool.GetOrCreate("Neon_Tube_Red");
        m.SetColor("_BaseColor", new Color(0.55f, 0.08f, 0.06f));
        m.SetTexture("_BaseMap", null);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Smoothness", 0.8f);
        m.EnableKeyword("_EMISSION");
        m.SetTexture("_EmissionMap", null);
        m.SetColor("_EmissionColor", TubeRed * TubeEmissionIntensity);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(m);
        return m;
    }
}
