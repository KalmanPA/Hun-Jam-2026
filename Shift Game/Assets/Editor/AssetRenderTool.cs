using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Diagnostic helper: renders orthographic views of a prefab/model into PNGs.
/// Write lines into Temp/FinalShift_Render.request:
///   asset=Assets/Art/Gun.fbx
///   view=name;dirX,dirY,dirZ;upX,upY,upZ;orthoSize;centerX,centerY,centerZ;pixels
/// (dir = direction the camera looks). Images go to Temp/FinalShift_Render_name.png.
/// </summary>
[InitializeOnLoad]
public static class AssetRenderTool
{
    private const string RequestFile = "Temp/FinalShift_Render.request";

    static AssetRenderTool()
    {
        EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            if (!File.Exists(RequestFile)) return;
            string[] lines = File.ReadAllLines(RequestFile);
            File.Delete(RequestFile);
            try { Run(lines); }
            catch (System.Exception e) { File.WriteAllText("Temp/FinalShift_Render_error.txt", e.ToString()); }
        };
    }

    private static Vector3 V(string s)
    {
        string[] p = s.Split(',');
        return new Vector3(float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
                           float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture),
                           float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void Run(string[] lines)
    {
        string asset = null;
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject inst = null;
            var cfg = System.Globalization.CultureInfo.InvariantCulture;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.StartsWith("asset="))
                {
                    asset = line.Substring(6);
                    if (inst != null) Object.DestroyImmediate(inst);
                    GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                    inst = (GameObject)Object.Instantiate(src);
                    SceneManager.MoveGameObjectToScene(inst, scene);
                    inst.transform.position = Vector3.zero;
                    inst.transform.rotation = Quaternion.identity;
                    // Runtime-only scripts would not run here; show everything.
                    foreach (Transform t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);
                    var lgo = new GameObject("Light");
                    SceneManager.MoveGameObjectToScene(lgo, scene);
                    Light l = lgo.AddComponent<Light>();
                    l.type = LightType.Directional;
                    l.intensity = 1.3f;
                    lgo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                }
                else if (line.StartsWith("settex=") && inst != null)
                {
                    // settex=RendererName;submeshIndex;texturePath  -> unlit point-filtered material
                    string[] p = line.Substring(7).Split(';');
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p[2]);
                    tex.filterMode = FilterMode.Point;
                    var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    m.SetTexture("_BaseMap", tex);
                    foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r.name != p[0]) continue;
                        Material[] mats = r.sharedMaterials;
                        mats[int.Parse(p[1])] = m;
                        r.sharedMaterials = mats;
                    }
                }
                else if (line.StartsWith("gunpreview=") && inst != null)
                {
                    // gunpreview=ammo,health  -> draws GunDisplayScreen with these values
                    string[] p = line.Substring(11).Split(',');
                    foreach (GunDisplayScreen g in inst.GetComponentsInChildren<GunDisplayScreen>(true))
                        g.BuildPreview(float.Parse(p[0], cfg), float.Parse(p[1], cfg));
                }
                else if (line.StartsWith("place=") && inst != null)
                {
                    // place=pos;euler  -> moves the instance (e.g. to undo the prefab root pose)
                    string[] p = line.Substring(6).Split(';');
                    inst.transform.position = V(p[0]);
                    inst.transform.rotation = Quaternion.Euler(V(p[1]));
                }
                else if (line.StartsWith("setrot=") && inst != null)
                {
                    // setrot=ObjectName;x,y,z  -> local euler of every object with that name
                    string[] p = line.Substring(7).Split(';');
                    foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
                        if (t.name == p[0]) t.localRotation = Quaternion.Euler(V(p[1]));
                }
                else if (line.StartsWith("hide=") && inst != null)
                {
                    string n = line.Substring(5);
                    foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
                        if (t.name == n) t.gameObject.SetActive(false);
                }
                else if (line.StartsWith("view=") && inst != null)
                {
                    string[] p = line.Substring(5).Split(';');
                    string name = p[0];
                    Vector3 dir = V(p[1]).normalized, up = V(p[2]);
                    float size = float.Parse(p[3], cfg);
                    Vector3 center = V(p[4]);
                    int px = int.Parse(p[5]);
                    var cgo = new GameObject("Cam");
                    SceneManager.MoveGameObjectToScene(cgo, scene);
                    Camera c = cgo.AddComponent<Camera>();
                    c.scene = scene;
                    c.orthographic = true;
                    c.orthographicSize = size;
                    c.nearClipPlane = 0.001f;
                    c.farClipPlane = 10f;
                    c.clearFlags = CameraClearFlags.SolidColor;
                    c.backgroundColor = new Color(0.35f, 0.38f, 0.42f);
                    cgo.transform.position = center - dir * 2f;
                    cgo.transform.rotation = Quaternion.LookRotation(dir, up);
                    var rt = new RenderTexture(px, px, 24, RenderTextureFormat.ARGB32);
                    c.targetTexture = rt;
                    c.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(px, px, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, px, px), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    File.WriteAllBytes("Temp/FinalShift_Render_" + name + ".png", tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    c.targetTexture = null;
                    rt.Release();
                    Object.DestroyImmediate(rt);
                    Object.DestroyImmediate(cgo);
                }
            }
            File.WriteAllText("Temp/FinalShift_Render_done.txt", "ok " + asset);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
