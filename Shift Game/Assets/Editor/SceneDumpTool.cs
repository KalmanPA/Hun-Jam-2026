using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Diagnostic helper: write a regex into Temp/FinalShift_SceneDump.request; every object of the open scenes whose
/// hierarchy path matches is listed with world bounds, transform, components and lights in Temp/FinalShift_SceneDump.txt.
/// </summary>
[InitializeOnLoad]
public static class SceneDumpTool
{
    private const string RequestFile = "Temp/FinalShift_SceneDump.request";
    private const string OutFile = "Temp/FinalShift_SceneDump.txt";

    static SceneDumpTool()
    {
        EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!File.Exists(RequestFile)) return;
            string pattern = File.ReadAllText(RequestFile).Trim();
            File.Delete(RequestFile);
            var rx = new Regex(pattern.Length > 0 ? pattern : ".*", RegexOptions.IgnoreCase);
            var sb = new StringBuilder();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                sb.AppendLine("=== scene " + s.path + (s == SceneManager.GetActiveScene() ? " (active)" : "") + " dirty=" + s.isDirty);
                foreach (GameObject root in s.GetRootGameObjects())
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        string path = Path(t);
                        if (!rx.IsMatch(path)) continue;
                        sb.Append(path).Append(t.gameObject.activeInHierarchy ? "" : " [inactive]")
                          .Append(" | pos ").Append(t.position.ToString("F2")).Append(" rot ").Append(t.eulerAngles.ToString("F0"))
                          .Append(" lscl ").Append(t.localScale.ToString("F2"));
                        Renderer r = t.GetComponent<Renderer>();
                        if (r != null) sb.Append(" | bounds ").Append(r.bounds.min.ToString("F2")).Append("..").Append(r.bounds.max.ToString("F2"))
                                         .Append(" mat ").Append(r.sharedMaterial != null ? r.sharedMaterial.name : "-");
                        Light l = t.GetComponent<Light>();
                        if (l != null) sb.Append(" | LIGHT ").Append(l.type).Append(" int ").Append(l.intensity.ToString("F1")).Append(" range ").Append(l.range.ToString("F1"))
                                         .Append(" col ").Append(ColorUtility.ToHtmlStringRGB(l.color)).Append(" shadows ").Append(l.shadows);
                        var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                        if (src != null) sb.Append(" | prefab ").Append(AssetDatabase.GetAssetPath(src));
                        sb.Append(" | comps");
                        foreach (Component c in t.GetComponents<Component>()) if (c != null && !(c is Transform)) sb.Append(' ').Append(c.GetType().Name);
                        sb.AppendLine();
                    }
            }
            File.WriteAllText(OutFile, sb.ToString());
        };
    }

    private static string Path(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
