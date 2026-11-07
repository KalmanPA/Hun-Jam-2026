using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the placeholder shapes for each piece of the gear weapon. Every piece is built under its own
/// parent, so any of them can be swapped for a real model by assigning a prefab on GearShiftWeapon.
/// </summary>
public static class GearWeaponPlaceholders
{
    private static Material rust, steel, darkSteel, groove, reverseGroove, wood, knob, hazard, glow, healthMat, ammoMat;

    private static void EnsureMaterials()
    {
        if (rust != null) return;
        rust = MakeMaterial(new Color(0.36f, 0.2f, 0.12f), false);
        rust.name = "Weapon_Rust";
        steel = MakeMaterial(new Color(0.3f, 0.3f, 0.31f), false);
        steel.name = "Weapon_Steel";
        darkSteel = MakeMaterial(new Color(0.12f, 0.12f, 0.13f), false);
        darkSteel.name = "Weapon_DarkSteel";
        groove = MakeMaterial(new Color(0.02f, 0.02f, 0.02f), false);
        groove.name = "Weapon_Groove";
        reverseGroove = MakeMaterial(new Color(0.12f, 0.02f, 0.02f), false);
        reverseGroove.name = "Weapon_ReverseGroove";
        wood = MakeMaterial(new Color(0.28f, 0.17f, 0.09f), false);
        wood.name = "Weapon_Wood";
        knob = MakeMaterial(new Color(0.04f, 0.04f, 0.04f), false);
        knob.name = "Weapon_Knob";
        hazard = MakeMaterial(new Color(0.7f, 0.55f, 0.1f), false);
        hazard.name = "Weapon_Hazard";
        glow = MakeMaterial(new Color(1f, 0.45f, 0.1f), true);
        glow.name = "Weapon_Glow";
        healthMat = MakeMaterial(new Color(0.8f, 0.1f, 0.06f), true);
        healthMat.name = "Weapon_HealthBar";
        ammoMat = MakeMaterial(new Color(1f, 0.6f, 0.15f), true);
        ammoMat.name = "Weapon_AmmoBar";
    }

    // ------------------------------------------------------------ pieces

    /// <summary>Receiver, stock, grips, side tank. Origin = middle of the receiver, +Z = muzzle.</summary>
    public static void BuildBody(Transform parent)
    {
        EnsureMaterials();
        Box("Receiver", parent, new Vector3(0f, 0f, 0.02f), new Vector3(0.1f, 0.1f, 0.45f), rust);
        Cylinder("BarrelPort", parent, new Vector3(0f, 0f, 0.244f), 0.046f, 0.004f, groove);
        Box("Hazard", parent, new Vector3(0.051f, 0f, 0.19f), new Vector3(0.003f, 0.1f, 0.04f), hazard);
        Box("HazardL", parent, new Vector3(-0.051f, 0f, 0.19f), new Vector3(0.003f, 0.1f, 0.04f), hazard);

        Cylinder("Tank", parent, new Vector3(-0.072f, -0.015f, 0.05f), 0.028f, 0.22f, steel);
        Transform gauge = Part("Gauge", PrimitiveType.Cylinder, parent, new Vector3(-0.072f, 0.018f, -0.03f), new Vector3(0.035f, 0.006f, 0.035f), Vector3.zero, darkSteel);
        Part("GaugeFace", PrimitiveType.Cylinder, gauge, new Vector3(0f, 1.1f, 0f), new Vector3(0.8f, 0.3f, 0.8f), Vector3.zero, glow);

        Box("Stock", parent, new Vector3(0f, -0.025f, -0.32f), new Vector3(0.06f, 0.09f, 0.24f), wood);
        Box("ButtPlate", parent, new Vector3(0f, -0.025f, -0.445f), new Vector3(0.07f, 0.11f, 0.015f), darkSteel);
        Part("Grip", PrimitiveType.Cube, parent, new Vector3(0f, -0.1f, -0.1f), new Vector3(0.045f, 0.11f, 0.05f), new Vector3(15f, 0f, 0f), wood);
        Box("Foregrip", parent, new Vector3(0f, -0.085f, 0.19f), new Vector3(0.045f, 0.07f, 0.05f), wood);
    }

    /// <summary>
    /// Health and ammo bars on a tipped-up panel. The fills are named HealthFill and AmmoFill and fill
    /// along their local Z (scale Z = 0 empty, 1 full). Origin = back edge of the panel.
    /// </summary>
    public static void BuildIndicators(Transform parent)
    {
        EnsureMaterials();
        const float barLength = 0.11f;
        Box("PanelPlate", parent, new Vector3(0f, 0.006f, 0.065f), new Vector3(0.1f, 0.012f, 0.13f), darkSteel);
        Box("Riser", parent, new Vector3(0f, -0.022f, 0.085f), new Vector3(0.09f, 0.045f, 0.085f), darkSteel);
        Bar("Health", parent, new Vector3(-0.025f, 0.0145f, 0.01f), barLength, healthMat);
        Bar("Ammo", parent, new Vector3(0.025f, 0.0145f, 0.01f), barLength, ammoMat);
    }

    /// <summary>Gate plate with the rail and slot grooves. Gate space: X along the rail, Z into the slots.</summary>
    public static void BuildGate(Transform parent, List<GateSlot> slots, float spacing, float slotLength, float railMin, float railMax)
    {
        EnsureMaterials();
        // The plate sits just under the gate surface. In gate space the plate is 0.2 long (X) and 0.11 wide (Z).
        Box("Plate", parent, new Vector3(0f, -0.0065f, 0f), new Vector3(0.2f, 0.012f, 0.11f), steel);

        const float grooveWidth = 0.012f;
        Box("Rail", parent, new Vector3((railMin + railMax) * 0.5f, 0.0005f, 0f), new Vector3((railMax - railMin) + grooveWidth, 0.002f, grooveWidth), groove);
        foreach (GateSlot s in slots)
        {
            float dir = s.direction >= 0 ? 1f : -1f;
            Box("Slot_" + s.gear, parent, new Vector3(s.column * spacing, 0.0005f, dir * slotLength * 0.5f),
                new Vector3(grooveWidth, 0.002f, slotLength + grooveWidth), s.gear == Gear.Reverse ? reverseGroove : groove);
        }
    }

    /// <summary>Plain rod with a ball on top, no boot. Origin = where the stick meets the gate, +Y = up the stick.</summary>
    public static void BuildStick(Transform parent)
    {
        EnsureMaterials();
        Box("Collar", parent, new Vector3(0f, 0.004f, 0f), new Vector3(0.022f, 0.008f, 0.022f), darkSteel);
        Cylinder("Rod", parent, new Vector3(0f, 0.07f, 0f), 0.006f, 0.14f, steel, upright: true);
        Part("Knob", PrimitiveType.Sphere, parent, new Vector3(0f, 0.14f, 0f), Vector3.one * 0.04f, Vector3.zero, knob);
    }

    /// <summary>One barrel per gear. Origin = back end of the barrel, +Z = towards the muzzle, length = barrel length.</summary>
    public static void BuildBarrel(Transform parent, Gear gear, float length)
    {
        EnsureMaterials();
        switch (gear)
        {
            case Gear.First: // long thin laser tube with glowing coils
                Cylinder("Tube", parent, new Vector3(0f, 0f, length * 0.5f), 0.018f, length, steel);
                for (int i = 1; i <= 3; i++)
                    Cylinder("Coil" + i, parent, new Vector3(0f, 0f, length * (0.2f + 0.2f * i)), 0.03f, 0.012f, glow);
                Cylinder("Tip", parent, new Vector3(0f, 0f, length - 0.015f), 0.026f, 0.03f, darkSteel);
                break;

            case Gear.Second: // medium barrel with cooling ribs
                Cylinder("Tube", parent, new Vector3(0f, 0f, length * 0.5f), 0.024f, length, steel);
                for (int i = 0; i < 5; i++)
                    Cylinder("Rib" + i, parent, new Vector3(0f, 0f, length * (0.15f + 0.13f * i)), 0.032f, 0.01f, darkSteel);
                Cylinder("Clamp", parent, new Vector3(0f, 0f, length - 0.02f), 0.034f, 0.04f, darkSteel);
                break;

            case Gear.Third: // short wide tube with a flared mouth
                Cylinder("Tube", parent, new Vector3(0f, 0f, length * 0.5f), 0.03f, length, steel);
                Cylinder("Flare", parent, new Vector3(0f, 0f, length - 0.025f), 0.045f, 0.05f, darkSteel);
                Cylinder("Bore", parent, new Vector3(0f, 0f, length + 0.0005f), 0.035f, 0.002f, groove);
                break;

            case Gear.Fourth: // fat rocket tube
                Cylinder("Tube", parent, new Vector3(0f, 0f, length * 0.5f), 0.042f, length, darkSteel);
                Cylinder("HazardRing", parent, new Vector3(0f, 0f, length * 0.6f), 0.0435f, 0.03f, hazard);
                Cylinder("Bore", parent, new Vector3(0f, 0f, length + 0.0005f), 0.032f, 0.002f, groove);
                break;

            default: // reverse: stubby capped tube
                Cylinder("Cap", parent, new Vector3(0f, 0f, length * 0.5f), 0.035f, length, darkSteel);
                Cylinder("CapEnd", parent, new Vector3(0f, 0f, length + 0.0005f), 0.025f, 0.002f, reverseGroove);
                break;
        }
    }

    // ------------------------------------------------------------ helpers

    private static void Bar(string name, Transform parent, Vector3 start, float length, Material fillMat)
    {
        Box(name + "Back", parent, start + new Vector3(0f, -0.0015f, length * 0.5f), new Vector3(0.038f, 0.002f, length + 0.008f), groove);
        Transform fill = new GameObject(name + "Fill").transform;
        fill.SetParent(parent, false);
        fill.localPosition = start;
        Part("Fill", PrimitiveType.Cube, fill, new Vector3(0f, 0f, length * 0.5f), new Vector3(0.03f, 0.004f, length), Vector3.zero, fillMat);
    }

    private static Transform Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        return Part(name, PrimitiveType.Cube, parent, pos, size, Vector3.zero, mat);
    }

    /// <summary>Cylinder along Z (or upright along Y).</summary>
    private static Transform Cylinder(string name, Transform parent, Vector3 pos, float radius, float length, Material mat, bool upright = false)
    {
        return Part(name, PrimitiveType.Cylinder, parent, pos, new Vector3(radius * 2f, length * 0.5f, radius * 2f),
            upright ? Vector3.zero : new Vector3(90f, 0f, 0f), mat);
    }

    public static Transform Part(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Vector3 euler, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        Transform t = go.transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        t.localRotation = Quaternion.Euler(euler);
        t.localScale = scale;
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return t;
    }

    public static Material MakeMaterial(Color color, bool unlit)
    {
        Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material m = new Material(shader);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        return m;
    }
}
