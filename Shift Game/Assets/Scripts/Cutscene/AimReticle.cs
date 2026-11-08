using UnityEngine;

/// <summary>
/// The marker X the player draws on the right lens of their glasses (see GlassesCutscene). Once the glasses are
/// back on, the glass itself can't be seen, only this small X in the middle of the view: the guns shoot at
/// whatever is under the screen centre, so it shows where shots go.
///
/// It is two quads (one per marker stroke) so the cutscene can draw them stroke by stroke with <see cref="SetReveal"/>.
/// Once it is the reticle it is always visible.
/// </summary>
public class AimReticle : MonoBehaviour
{
    // Where each stroke starts and ends, in texture UV (0..1). Matches Reticle_StrokeA / B.
    public static readonly Vector2 StrokeAStart = new Vector2(0.144f, 0.781f);
    public static readonly Vector2 StrokeAEnd = new Vector2(0.853f, 0.102f);
    public static readonly Vector2 StrokeBStart = new Vector2(0.805f, 0.781f);
    public static readonly Vector2 StrokeBEnd = new Vector2(0.159f, 0.060f);

    private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    private Renderer strokeA;
    private Renderer strokeB;
    private float revealA = 1f;
    private float revealB = 1f;
    private MaterialPropertyBlock block;
    private Color colour = Color.black;


    /// <summary>False while a cutscene is drawing it. True once it's the aim reticle: then it hides for death, shifting and cutscenes.</summary>
    public bool Active { get; set; }

    /// <summary>Makes a reticle object with both strokes (1 x 1 m before scaling).</summary>
    public static AimReticle Create(Material strokeAMaterial, Material strokeBMaterial, Color tint, int layer)
    {
        GameObject root = new GameObject("AimReticle");
        root.layer = layer;
        AimReticle r = root.AddComponent<AimReticle>();
        r.colour = tint;
        r.strokeA = r.MakeStroke("StrokeA", strokeAMaterial, layer);
        r.strokeB = r.MakeStroke("StrokeB", strokeBMaterial, layer);
        r.SetReveal(1f, 1f);
        return r;
    }

    private Renderer MakeStroke(string name, Material mat, int layer)
    {
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = name;
        q.layer = layer;
        Destroy(q.GetComponent<Collider>());
        q.transform.SetParent(transform, false);
        Renderer rend = q.GetComponent<Renderer>();
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        return rend;
    }

    /// <summary>A point on the X (texture UV) in this object's local space (the X is 1 x 1 before scaling).</summary>
    public static Vector3 LocalPoint(Vector2 uv) => new Vector3(uv.x - 0.5f, uv.y - 0.5f, 0f);

    /// <summary>
    /// How much of each stroke is drawn, as a left-to-right wipe for stroke A and right-to-left for stroke B
    /// (the way the pen moves). 0 = not drawn, 1 = fully drawn.
    /// </summary>
    public void SetReveal(float a, float b)
    {
        revealA = Mathf.Clamp01(a);
        revealB = Mathf.Clamp01(b);
        Wipe(strokeA, revealA, false);
        Wipe(strokeB, revealB, true);
    }

    private void Wipe(Renderer r, float p, bool rightToLeft)
    {
        if (r == null) return;
        r.enabled = p > 0.001f;
        if (!r.enabled) return;
        Transform t = r.transform;
        t.localScale = new Vector3(p, 1f, 1f);
        t.localPosition = new Vector3(rightToLeft ? 0.5f - p * 0.5f : -0.5f + p * 0.5f, 0f, 0f);
        block ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetVector(BaseMapST, new Vector4(p, 1f, rightToLeft ? 1f - p : 0f, 0f));
        block.SetColor(BaseColor, colour);
        r.SetPropertyBlock(block);
    }

    private void LateUpdate()
    {
        // Constantly visible once it's the reticle (also while shifting and during later cutscenes).
        if (!Active) return;
        if (strokeA != null) strokeA.enabled = revealA > 0.001f;
        if (strokeB != null) strokeB.enabled = revealB > 0.001f;
    }
}
