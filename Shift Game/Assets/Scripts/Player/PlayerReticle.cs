using UnityEngine;

/// <summary>
/// Turns on the red marker X reticle that is part of the Player prefab (switched off there).
/// In a scene with the glasses cutscene (the Game scene) it stays off: the cutscene draws the X and gives it to the
/// player. In every other scene (BossFight, ElevatorRoom, test scenes) it is switched on right away.
/// Added by Final Shift > Player > Add Reticle To Player Prefab.
/// </summary>
public class PlayerReticle : MonoBehaviour
{
    [SerializeField] private AimReticle reticle;

    private void Start()
    {
        if (reticle == null) reticle = AimReticle.FindUnder(transform);
        if (reticle == null) return;
        if (FindFirstObjectByType<GlassesCutscene>(FindObjectsInactive.Include) != null) return; // the cutscene handles it
        reticle.Show();
    }
}
