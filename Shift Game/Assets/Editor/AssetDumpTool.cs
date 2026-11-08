using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Diagnostic helper: write an asset path into Temp/FinalShift_Dump.request and the hierarchy of that
/// prefab/model (local transforms, mesh bounds, materials, local file ids) is written to Temp/FinalShift_Dump.txt.
/// </summary>
[InitializeOnLoad]
public static class AssetDumpTool
{
    private const string RequestFile = "Temp/FinalShift_Dump.request";
    private const string OutFile = "Temp/FinalShift_Dump.txt";

    static AssetDumpTool()
    {
        EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            if (!File.Exists(RequestFile)) return;
            string[] paths = File.ReadAllLines(RequestFile);
            File.Delete(RequestFile);
            var sb = new StringBuilder();
            foreach (string p in paths) if (p.Trim().Length > 0) Dump(p.Trim(), sb);
            File.WriteAllText(OutFile, sb.ToString());
        };
    }

    private static void Dump(string path, StringBuilder sb)
    {
        sb.AppendLine("=== " + path);
        GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null) { sb.AppendLine("not found"); return; }
        Walk(go.transform, 0, sb);
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is Material || o is Mesh || o is Texture)
                sb.AppendLine("sub-asset " + o.GetType().Name + " '" + o.name + "'");
    }

    private static void Walk(Transform t, int depth, StringBuilder sb)
    {
        string ind = new string(' ', depth * 2);
        sb.Append(ind).Append(t.name)
          .Append(" pos ").Append(t.localPosition.ToString("F4"))
          .Append(" rot ").Append(t.localEulerAngles.ToString("F1"))
          .Append(" scl ").Append(t.localScale.ToString("F4"));
        if (!t.gameObject.activeSelf) sb.Append(" [inactive]");
        sb.AppendLine();
        foreach (Component c in t.GetComponents<Component>())
        {
            if (c == null) { sb.Append(ind).AppendLine("  - <missing script>"); continue; }
            if (c is Transform) continue;
            sb.Append(ind).Append("  - ").Append(c.GetType().Name);
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(c, out string g, out long id)) sb.Append(" id ").Append(id);
            if (c is MeshFilter mf && mf.sharedMesh != null)
                sb.Append(" mesh '").Append(mf.sharedMesh.name).Append("' bounds c").Append(mf.sharedMesh.bounds.center.ToString("F4"))
                  .Append(" s").Append(mf.sharedMesh.bounds.size.ToString("F4")).Append(" sub ").Append(mf.sharedMesh.subMeshCount);
            if (c is Renderer r)
            {
                sb.Append(" mats [");
                foreach (Material m in r.sharedMaterials) sb.Append(m != null ? m.name + "(" + AssetDatabase.GetAssetPath(m) + ")" : "null").Append(", ");
                sb.Append("]");
            }
            if (c is Behaviour b && !b.enabled) sb.Append(" [disabled]");
            sb.AppendLine();
        }
        foreach (Transform ch in t) Walk(ch, depth + 1, sb);
    }
}
