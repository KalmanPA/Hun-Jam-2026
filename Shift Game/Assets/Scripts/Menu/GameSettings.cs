using UnityEngine;

/// <summary>
/// Player options set on the pause tablet, saved in PlayerPrefs.
///  - Volume: master volume (AudioListener.volume), 0..1.
///  - MouseSensitivity: multiplier on the FirstPersonController's mouse look speed (1 = the speed set on the player).
/// </summary>
public static class GameSettings
{
    private const string VolumeKey = "FinalShift.Volume";
    private const string SensitivityKey = "FinalShift.MouseSensitivity";

    public const float MinSensitivity = 0.25f;
    public const float MaxSensitivity = 3f;

    private static bool loaded;
    private static float volume = 1f;
    private static float sensitivity = 1f;

    public static float Volume
    {
        get { Load(); return volume; }
        set
        {
            Load();
            volume = Mathf.Clamp01(value);
            AudioListener.volume = volume;
            PlayerPrefs.SetFloat(VolumeKey, volume);
        }
    }

    public static float MouseSensitivity
    {
        get { Load(); return sensitivity; }
        set
        {
            Load();
            sensitivity = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
            PlayerPrefs.SetFloat(SensitivityKey, sensitivity);
        }
    }

    /// <summary>Writes the prefs to disk (called when the pause menu closes, not on every slider move).</summary>
    public static void Save() => PlayerPrefs.Save();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { loaded = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyOnStart()
    {
        Load();
        AudioListener.volume = volume;
    }

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
        sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, 1f), MinSensitivity, MaxSensitivity);
    }
}
