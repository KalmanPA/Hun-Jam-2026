using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Finds the dark spots of a level and mounts the Neon.fbx wall lamp on the nearest wall there,
/// with a point light of the same colour in front of it.
///
/// How it works: the level is sampled on a 2 m grid (raycasts down find every walkable surface, on every
/// storey). For each sample the light from the scene's point/spot lights is estimated (with a line-of-sight
/// check). The darkest samples get a lamp on the closest flat wall, then the estimate is updated, until no
/// dark spot is left or the lamp limit is hit.
///
/// Menu: Final Shift > Lighting > Add Neon Wall Lamps To Game Scene (and ... To Open Scene).
/// Running it again removes the old generated neon lamps first. Delete or move single lamps by hand freely.
/// </summary>
[InitializeOnLoad]
public static class NeonWallLampTool
{
    private const string GameScenePath = "Assets/Scenes/Game.unity";
    private const string ModelPath = "Assets/Art/Neon.fbx";
    private const string MaterialFolder = "Assets/ProtMaterials/Level";
    private const string RootName = "== Neon Wall Lamps (generated) ==";
    private const string TriggerFile = "Temp/FinalShift_NeonLamps";
    private const string InfoTriggerFile = "Temp/FinalShift_NeonInfo";

    // ---- Tuning ----
    private const float SampleStep = 2f;           // metres between light samples
    private const float DarkThreshold = 0.35f;     // estimated light below this = dark spot
    private const float MaxWallDistance = 7f;      // a dark spot needs a wall this close
    private const float MinLampSpacing = 7f;       // metres between neon lamps
    private const int MaxLamps = 40;
    private const int MaxFillLamps = 25;           // plain hanging lamps where no wall is near
    private const float MountHeight = 2.6f;        // above the floor
    private const float LampLength = 1.6f;         // the model's long side is scaled to this
    private const float NeonIntensity = 7f;
    private const float NeonRange = 10f;

    // Neon tube colours (the light uses the same colour).
    private static readonly Color[] NeonColours =
    {
        new Color(0.20f, 0.95f, 1.00f), // cyan
        new Color(1.00f, 0.22f, 0.55f), // hot pink
        new Color(1.00f, 0.30f, 0.12f), // red-orange
        new Color(0.45f, 1.00f, 0.35f), // acid green
        new Color(0.62f, 0.35f, 1.00f), // violet
    };

    static NeonWallLampTool()
    {
        EditorApplication.update += PollTriggers;
    }

    private static void PollTriggers()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        if (File.Exists(InfoTriggerFile))
        {
            File.Delete(InfoTriggerFile);
            LogModelInfo();
        }
        if (File.Exists("Temp/FinalShift_NeonPreview"))
        {
            File.Delete("Temp/FinalShift_NeonPreview");
            RenderPreviews();
        }
        if (File.Exists(TriggerFile))
        {
            File.Delete(TriggerFile);
            ApplyToScenePath(GameScenePath);
        }
    }

    [MenuItem("Final Shift/Lighting/Add Neon Wall Lamps To Game Scene")]
    public static void ApplyToGame() => ApplyToScenePath(GameScenePath);

    [MenuItem("Final Shift/Lighting/Add Neon Wall Lamps To Open Scene")]
    public static void ApplyToActive()
    {
        Scene s = SceneManager.GetActiveScene();
        int n = Apply(s);
        EditorSceneManager.MarkSceneDirty(s);
        EditorSceneManager.SaveScene(s);
        Debug.Log("[FinalShift] Neon wall lamps: placed " + n + " in " + s.path);
    }

    [MenuItem("Final Shift/Lighting/Remove Neon Wall Lamps From Open Scene")]
    public static void RemoveFromActive()
    {
        Scene s = SceneManager.GetActiveScene();
        foreach (GameObject root in s.GetRootGameObjects())
            if (root.name == RootName) Undo.DestroyObjectImmediate(root);
        EditorSceneManager.MarkSceneDirty(s);
    }

    /// <summary>Works on the scene whether or not it is open; opens it additively if needed and leaves the
    /// editor's scene setup the way it was.</summary>
    private static void ApplyToScenePath(string path)
    {
        Scene scene = SceneManager.GetSceneByPath(path);
        bool openedHere = false;
        if (!scene.IsValid() || !scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            openedHere = true;
        }
        int n = Apply(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        if (openedHere) EditorSceneManager.CloseScene(scene, true);
        Debug.Log("[FinalShift] Neon wall lamps: placed " + n + " in " + path);
    }

    // ------------------------------------------------------------------ core

    private struct LampLight { public Vector3 pos; public float intensity, range; }

    private static int Apply(Scene scene)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogWarning("[FinalShift] Neon wall lamps: " + ModelPath + " not found.");
            return 0;
        }

        foreach (GameObject old in scene.GetRootGameObjects())
            if (old.name == RootName) Undo.DestroyObjectImmediate(old);
        Physics.SyncTransforms();

        // Existing lights of this scene (enabled point/spot lights).
        var lights = new List<LampLight>();
        Bounds level = new Bounds();
        bool haveLevel = false;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Light l in root.GetComponentsInChildren<Light>(false))
            {
                if (!l.enabled || (l.type != LightType.Point && l.type != LightType.Spot)) continue;
                float i = l.type == LightType.Spot ? l.intensity * 0.5f : l.intensity;
                lights.Add(new LampLight { pos = l.transform.position, intensity = i, range = l.range });
            }
            foreach (Collider c in root.GetComponentsInChildren<Collider>(false))
            {
                if (c.isTrigger || !IsStaticLevel(c)) continue;
                if (!haveLevel) { level = c.bounds; haveLevel = true; }
                else level.Encapsulate(c.bounds);
            }
        }
        if (!haveLevel) return 0;

        // Walkable samples on every storey.
        var samples = new List<Vector3>();
        float top = level.max.y + 1f;
        for (float x = level.min.x + 1f; x < level.max.x; x += SampleStep)
        for (float z = level.min.z + 1f; z < level.max.z; z += SampleStep)
        {
            var origin = new Vector3(x, top, z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, level.size.y + 2f, ~0, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit h in hits)
            {
                if (h.collider.gameObject.scene != scene || h.normal.y < 0.7f || !IsStaticLevel(h.collider)) continue;
                Vector3 p = h.point + Vector3.up * 1.2f;
                // Needs headroom (not inside a block) to be a place the player can stand.
                if (Physics.CheckSphere(p, 0.4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (Physics.Raycast(h.point + Vector3.up * 0.1f, Vector3.up, 2.2f, ~0, QueryTriggerInteraction.Ignore)) continue;
                samples.Add(p);
            }
        }

        GameObject rootGo = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(rootGo, scene);
        Undo.RegisterCreatedObjectUndo(rootGo, "Neon Wall Lamps");

        var mats = new Material[NeonColours.Length];
        for (int i = 0; i < NeonColours.Length; i++) mats[i] = NeonMaterial(i);

        var placed = new List<Vector3>();
        var lit = new float[samples.Count];
        for (int i = 0; i < samples.Count; i++) lit[i] = Illumination(samples[i], lights);

        int darkAtStart = 0;
        foreach (float v in lit) if (v < DarkThreshold) darkAtStart++;

        var tried = new bool[samples.Count];
        int fills = 0;
        var fillSpots = new List<Vector3>();
        while (placed.Count < MaxLamps)
        {
            // Darkest untried sample.
            int best = -1;
            for (int i = 0; i < samples.Count; i++)
                if (!tried[i] && lit[i] < DarkThreshold && (best < 0 || lit[i] < lit[best])) best = i;
            if (best < 0) break;
            tried[best] = true;

            Vector3 floorPoint = samples[best] - Vector3.up * 1.2f;
            if (!FindWall(scene, floorPoint, out Vector3 wallPoint, out Vector3 normal))
            {
                // Open floor with no wall nearby: hang a plain factory lamp instead.
                if (fills >= MaxFillLamps) continue;
                Vector3 hang = floorPoint + Vector3.up * 4.2f;
                if (Physics.Raycast(floorPoint + Vector3.up * 0.2f, Vector3.up, out RaycastHit up, 4.6f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (up.distance < 2.6f) continue;
                    hang.y = Mathf.Min(hang.y, up.point.y - 0.35f);
                }
                bool crowded = false;
                foreach (Vector3 q in placed)
                    if ((q - hang).sqrMagnitude < MinLampSpacing * MinLampSpacing) { crowded = true; break; }
                foreach (Vector3 q in fillSpots)
                    if ((q - hang).sqrMagnitude < MinLampSpacing * MinLampSpacing) { crowded = true; break; }
                if (crowded) continue;

                DarkFactoryLightingTool.BuildLamp(rootGo.transform, fills, hang, false, false, false,
                    DarkFactoryLightingTool.LevelMaterial(DarkFactoryLightingTool.Kind.Ceiling),
                    DarkFactoryLightingTool.UnlitMaterial("Lamp_Warm", new Color(1f, 0.78f, 0.45f)));
                fills++;
                fillSpots.Add(hang);
                var fl = new LampLight { pos = hang + Vector3.down * 0.3f, intensity = 14f, range = 13f };
                lights.Add(fl);
                for (int i = 0; i < samples.Count; i++)
                    if (lit[i] < DarkThreshold) lit[i] += Illumination(samples[i], fl);
                continue;
            }

            bool near = false;
            foreach (Vector3 q in placed)
                if ((q - wallPoint).sqrMagnitude < MinLampSpacing * MinLampSpacing) { near = true; break; }
            if (near) continue;

            int colour = placed.Count % NeonColours.Length;
            Vector3 lightPos = BuildLamp(rootGo.transform, placed.Count, model, wallPoint, normal, colour, mats[colour]);
            placed.Add(wallPoint);

            var nl = new LampLight { pos = lightPos, intensity = NeonIntensity, range = NeonRange };
            lights.Add(nl);
            for (int i = 0; i < samples.Count; i++)
                if (lit[i] < DarkThreshold) lit[i] += Illumination(samples[i], nl);
        }

        int darkAtEnd = 0;
        foreach (float v in lit) if (v < DarkThreshold) darkAtEnd++;
        Debug.Log("[FinalShift] Neon wall lamps: " + samples.Count + " floor samples, " + darkAtStart +
                  " dark before, " + darkAtEnd + " dark after, " + fills + " hanging fill lamps.");
        return placed.Count;
    }

    private static float Illumination(Vector3 p, List<LampLight> lights)
    {
        float sum = 0f;
        foreach (LampLight l in lights) sum += Illumination(p, l);
        return sum;
    }

    private static float Illumination(Vector3 p, LampLight l)
    {
        Vector3 d = l.pos - p;
        float dist = d.magnitude;
        if (dist >= l.range) return 0f;
        float window = 1f - (dist / l.range) * (dist / l.range);
        float v = l.intensity / Mathf.Max(dist * dist, 1f) * window * window;
        if (v < 0.02f) return 0f;
        if (dist > 0.5f && Physics.Raycast(p, d / dist, dist - 0.5f, ~0, QueryTriggerInteraction.Ignore)) return 0f;
        return v;
    }

    /// <summary>Closest flat, vertical, static wall around a floor point, with room for the lamp.</summary>
    private static bool FindWall(Scene scene, Vector3 floorPoint, out Vector3 wallPoint, out Vector3 normal)
    {
        wallPoint = normal = Vector3.zero;
        Vector3 eye = floorPoint + Vector3.up * MountHeight;
        // Not above the ceiling: lower the mount under a low ceiling.
        if (Physics.Raycast(floorPoint + Vector3.up * 0.2f, Vector3.up, out RaycastHit ceil, MountHeight + 0.6f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (ceil.distance < 2.2f) return false;
            eye.y = Mathf.Min(eye.y, ceil.point.y - 0.6f);
        }

        float bestDist = float.MaxValue;
        for (int i = 0; i < 16; i++)
        {
            float a = i * Mathf.PI * 2f / 16f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            if (!Physics.Raycast(eye, dir, out RaycastHit h, MaxWallDistance, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (h.distance >= bestDist || Mathf.Abs(h.normal.y) > 0.15f) continue;
            if (h.collider.gameObject.scene != scene || !IsStaticLevel(h.collider)) continue;

            // Wall must be flat and wide enough for the lamp: check both ends and above/below.
            Vector3 n = new Vector3(h.normal.x, 0f, h.normal.z).normalized;
            Vector3 t = Vector3.Cross(Vector3.up, n);
            if (!SameWall(h, n, t * (LampLength * 0.6f)) || !SameWall(h, n, -t * (LampLength * 0.6f)) ||
                !SameWall(h, n, Vector3.up * 0.4f) || !SameWall(h, n, Vector3.down * 0.4f)) continue;

            bestDist = h.distance;
            wallPoint = h.point;
            normal = n;
        }
        return bestDist < float.MaxValue;
    }

    private static bool SameWall(RaycastHit h, Vector3 n, Vector3 offset)
    {
        Vector3 from = h.point + n * 0.5f + offset;
        if (Physics.CheckSphere(from, 0.1f, ~0, QueryTriggerInteraction.Ignore)) return false; // something in front
        if (!Physics.Raycast(from, -n, out RaycastHit h2, 0.7f, ~0, QueryTriggerInteraction.Ignore)) return false;
        return Vector3.Dot(h2.normal, n) > 0.97f && Mathf.Abs(h2.distance - 0.5f) < 0.06f;
    }

    /// <summary>Only plain level geometry counts (no robots, doors, belts, lifts, pickups or moving things).</summary>
    private static bool IsStaticLevel(Collider c)
    {
        if (c.attachedRigidbody != null) return false;
        for (Transform t = c.transform; t != null; t = t.parent)
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("robot") || n.Contains("enemy") || n.Contains("door") || n.Contains("conveyor") ||
                n.Contains("belt") || n.Contains("elevator") || n.Contains("lift") || n.Contains("spawner") ||
                n.Contains("player") || n.Contains("pickup") || n.Contains("crate") || n.Contains("barrel") ||
                n.Contains("cable") || n.Contains("button") || n.Contains("target") || n.Contains("intro") ||
                n.Contains("boss") || n.Contains("lamp"))
                return false;
            foreach (MonoBehaviour mb in t.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                string ns = mb.GetType().Namespace;
                if (ns == null || !ns.StartsWith("UnityEngine")) return false; // has game logic
            }
        }
        return true;
    }

    // ----------------------------------------------------------------- build

    private static Vector3 BuildLamp(Transform parent, int index, GameObject model, Vector3 wallPoint, Vector3 normal,
                                     int colour, Material neonMat)
    {
        GameObject lamp = new GameObject("Neon Lamp " + index);
        lamp.transform.SetParent(parent, false);

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        inst.transform.SetParent(lamp.transform, false);
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localRotation = Quaternion.identity;
        inst.transform.localScale = Vector3.one;

        // Measure the model in the lamp's space.
        Bounds b = LocalBounds(lamp.transform, inst);
        Vector3 s = b.size;
        int thin = s.x <= s.y && s.x <= s.z ? 0 : (s.y <= s.z ? 1 : 2);
        int longest = s.x >= s.y && s.x >= s.z ? 0 : (s.y >= s.z ? 1 : 2);
        if (longest == thin) longest = (thin + 1) % 3;
        Vector3 thinAxis = Axis(thin), longAxis = Axis(longest);
        // The glowing tube must face the room: flip if the tube sits on the negative side of the thin axis.
        Vector3 tubeCentre = TubeCentre(lamp.transform, inst);
        if (Vector3.Dot(tubeCentre - b.center, thinAxis) < 0f) thinAxis = -thinAxis;
        float scale = LampLength / Mathf.Max(0.01f, s[longest]);

        // Thin axis faces out of the wall, long axis runs along it.
        Quaternion modelToAxes = Quaternion.LookRotation(thinAxis, longAxis);
        Quaternion axesToWorld = Quaternion.LookRotation(normal, Vector3.Cross(Vector3.up, normal));
        Quaternion rot = axesToWorld * Quaternion.Inverse(modelToAxes);
        inst.transform.localRotation = rot;
        inst.transform.localScale = Vector3.one * scale;
        // Centre the model on the lamp origin.
        inst.transform.localPosition = -(rot * (b.center * scale));

        float depth = s[thin] * scale;
        lamp.transform.position = wallPoint + normal * (depth * 0.5f + 0.02f);

        foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
        {
            // Submeshes of Neon.fbx: "Neonfém" (metal housing), "Neonvisszeverő" (reflector), "Neon" (tube).
            Material[] src = r.sharedMaterials;
            var arr = new Material[src.Length];
            for (int i = 0; i < arr.Length; i++)
            {
                string n = src[i] != null ? src[i].name.ToLowerInvariant() : "";
                if (n == "neon" || n.Contains("tube") || (src.Length == 1)) arr[i] = neonMat;
                else if (n.Contains("vissz") || n.Contains("reflect")) arr[i] = ReflectorMaterial(colour);
                else arr[i] = HousingMaterial();
            }
            r.sharedMaterials = arr;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        foreach (Collider c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

        GameObject lightGo = new GameObject("Light");
        lightGo.transform.SetParent(lamp.transform, false);
        lightGo.transform.position = wallPoint + normal * 0.6f;
        Light l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.lightmapBakeType = LightmapBakeType.Realtime;
        l.color = NeonColours[colour];
        l.intensity = NeonIntensity;
        l.range = NeonRange;
        l.shadows = LightShadows.None;

        if (index % 6 == 4)
        {
            var f = lightGo.AddComponent<FinalShift.ShaderTest.ShaderTestFlicker>();
            f.baseIntensity = l.intensity;
            f.flickerAmount = 0.25f;
            f.speed = 9f;
        }
        return lightGo.transform.position;
    }

    private static Vector3 Axis(int i) => i == 0 ? Vector3.right : (i == 1 ? Vector3.up : Vector3.forward);

    private static Bounds LocalBounds(Transform space, GameObject go)
    {
        bool have = false;
        Bounds result = new Bounds();
        foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            Matrix4x4 m = space.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!have) { result = new Bounds(p, Vector3.zero); have = true; }
                else result.Encapsulate(p);
            }
        }
        return have ? result : new Bounds(Vector3.zero, Vector3.one);
    }

    /// <summary>Centre of the tube submesh (material named "Neon"), in the given space.</summary>
    private static Vector3 TubeCentre(Transform space, GameObject go)
    {
        foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length && i < mf.sharedMesh.subMeshCount; i++)
                if (mats[i] != null && mats[i].name.ToLowerInvariant() == "neon")
                {
                    Vector3 c = mf.sharedMesh.GetSubMesh(i).bounds.center;
                    return space.InverseTransformPoint(mf.transform.TransformPoint(c));
                }
        }
        return LocalBounds(space, go).center;
    }

    private static Material HousingMaterial()
    {
        return DarkFactoryLightingTool.LevelMaterial(DarkFactoryLightingTool.Kind.Ceiling);
    }

    private static Material ReflectorMaterial(int index)
    {
        Color c = NeonColours[index];
        return DarkFactoryLightingTool.UnlitMaterial("Neon_Reflector_" + index, Color.Lerp(Color.black, c, 0.45f));
    }

    private static Material NeonMaterial(int index)
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/ProtMaterials", "Level");
        string path = MaterialFolder + "/Neon_" + index + ".mat";
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        Color c = NeonColours[index];
        mat.SetColor("_BaseColor", Color.Lerp(c, Color.white, 0.35f));
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ------------------------------------------------------------- preview

    /// <summary>Renders a few of the lamps in the open Game scene to Temp/NeonPreview_N.png (check placement).</summary>
    private static void RenderPreviews()
    {
        Scene scene = SceneManager.GetSceneByPath(GameScenePath);
        bool openedHere = false;
        if (!scene.IsValid() || !scene.isLoaded) { scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Additive); openedHere = true; }
        GameObject root = null;
        foreach (GameObject g in scene.GetRootGameObjects()) if (g.name == RootName) root = g;
        if (root != null)
        {
            var camGo = new GameObject("NeonPreviewCam") { hideFlags = HideFlags.HideAndDontSave };
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.fieldOfView = 70f;
            cam.scene = scene;
            var rt = new RenderTexture(640, 400, 24);
            cam.targetTexture = rt;
            int shot = 0;
            foreach (Transform lamp in root.transform)
            {
                if (!lamp.name.StartsWith("Neon Lamp") || shot >= 4) continue;
                Transform l = lamp.Find("Light");
                Vector3 n = (l.position - lamp.position).normalized;
                camGo.transform.position = lamp.position + n * 3.5f + Vector3.down * 0.8f;
                camGo.transform.LookAt(lamp.position);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(640, 400, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 640, 400), 0, 0);
                RenderTexture.active = null;
                File.WriteAllBytes("Temp/NeonPreview_" + shot + ".png", tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                shot++;
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Debug.Log("[FinalShift] Neon previews written: " + shot);
        }
        if (openedHere) EditorSceneManager.CloseScene(scene, true);
    }

    // ------------------------------------------------------------------ info

    private static void LogModelInfo()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) { Debug.Log("[FinalShift] Neon info: model missing"); return; }
        var sb = new System.Text.StringBuilder("[FinalShift] Neon info:");
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
        {
            sb.Append("\n  " + t.name + " pos=" + t.localPosition + " rot=" + t.localEulerAngles + " scale=" + t.localScale);
            MeshFilter mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                sb.Append(" mesh=" + mf.sharedMesh.name + " bounds=" + mf.sharedMesh.bounds + " sub=" + mf.sharedMesh.subMeshCount);
            Renderer r = t.GetComponent<Renderer>();
            if (r != null)
                foreach (Material m in r.sharedMaterials)
                    sb.Append(" mat=" + (m ? m.name + (m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : "") : "null"));
        }
        Debug.Log(sb.ToString());
    }
}
