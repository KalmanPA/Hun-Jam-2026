using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Builds the game's sound setup from the clips in Assets/Audio:
///  - a SoundCue per sound event in Assets/Audio/Cues (clips matched to events by file name),
///  - the SoundBank at Assets/Audio/Resources/SoundBank.asset that GameAudio loads,
///  - the GameMixer (Master > SFX > Weapon / Player / Enemies / World, Voice, Ambience, Music),
///  - import settings for every clip (mono for 3D, decompressed short sounds, streamed music).
/// Runs once by itself when the bank doesn't exist yet, and again from Final Shift > Audio > Rebuild Sound Bank.
/// A rebuild keeps hand-tuned cue settings; it only refreshes the clip list of cues that have Auto Assign Clips on.
/// To add sounds later: drop clips into Assets/Audio named like the patterns below (e.g. "Enemy shot 2.mp3",
/// "Gear 2 shot 1.mp3", "Player hurt 1.mp3") and rebuild.
/// </summary>
[InitializeOnLoad]
public static class AudioSetupTool
{
    private const string AudioRoot = "Assets/Audio";
    private const string CueFolder = "Assets/Audio/Cues";
    private const string BankFolder = "Assets/Audio/Resources";
    private const string BankPath = "Assets/Audio/Resources/SoundBank.asset";
    private const string MixerPath = "Assets/Audio/GameMixer.mixer";
    private const string ReportPath = "Assets/Audio/Cues/_AudioReport.txt";

    /// <summary>Clips left out on purpose (none right now; the Gazember clip is used by RobotGazember).</summary>
    private static readonly Regex Skipped = new Regex(@"^$a", RegexOptions.IgnoreCase); // matches nothing

    /// <summary>The long voice-acted lines. Only the intro robots use them (RobotBerate).</summary>
    private const string BerateLines = @"^11s break$|^blinkrate$|^denied lunch$|^terminate$|^your output is down$|^berate";

    private class Rule
    {
        public SoundId id;
        public SoundBus bus;
        public Regex match;      // proper clips for this event
        public Regex standIn;    // used only while no proper clip exists
        public Action<SoundCue> setup;
    }

    // Short helpers so the table below stays readable.
    private static Regex R(string pattern) => pattern == null ? null : new Regex(pattern, RegexOptions.IgnoreCase);

    private static Action<SoundCue> S(float volume, float pitch, float pitchVar, float spatial, float min, float max,
        int voices, float cooldown, int priority, SoundCue.Limit limit = SoundCue.Limit.StealOldest, bool loop = false, float volVar = 0.1f, bool occlusion = true)
    {
        return c =>
        {
            c.volume = volume; c.pitch = pitch; c.pitchVariance = pitchVar; c.volumeVariance = volVar;
            c.spatialBlend = spatial; c.minDistance = min; c.maxDistance = max;
            c.maxVoices = voices; c.cooldown = cooldown; c.priority = priority; c.limit = limit; c.loop = loop;
            c.occlusion = occlusion && spatial > 0.5f;
        };
    }

    private const string MetalLight = @"^metal hit \d*$";
    private const string MetalHard = @"^metal hit hard";
    private const string Bumm = @"big.?bumm";

    private static readonly Rule[] Rules =
    {
        // ---- Player (first person: flat 2D)
        new Rule { id = SoundId.Footstep, bus = SoundBus.Player, match = R(@"^footstep"), setup = S(0.45f, 1f, 0.08f, 0f, 1, 10, 3, 0.12f, 140, volVar: 0.2f) },
        new Rule { id = SoundId.PlayerLand, bus = SoundBus.Player, match = R(@"^land|player land"), standIn = R(@"^footstep"), setup = S(0.8f, 0.8f, 0.05f, 0f, 1, 10, 1, 0.2f, 120) },
        new Rule { id = SoundId.PlayerHurt, bus = SoundBus.Player, match = R(@"player hurt|player pain"), setup = S(0.8f, 1f, 0.06f, 0f, 1, 10, 1, 0.25f, 60, SoundCue.Limit.Reject) },

        // ---- Gear-shift weapon (first person: almost flat)
        new Rule { id = SoundId.WeaponGearEngage, bus = SoundBus.Weapon, match = R(@"gear ?shift|shift engage|^lock clack"), setup = S(0.9f, 1f, 0.04f, 0f, 1, 10, 2, 0.05f, 40) },
        new Rule { id = SoundId.WeaponGearRelease, bus = SoundBus.Weapon, match = R(@"shift release|^lock clack"), setup = S(0.45f, 1.3f, 0.05f, 0f, 1, 10, 2, 0.05f, 60) },
        new Rule { id = SoundId.WeaponEmpty, bus = SoundBus.Weapon, match = R(@"empty|dry ?fire"), standIn = R(@"^button"), setup = S(0.5f, 1.5f, 0.05f, 0f, 1, 10, 1, 0.25f, 80) },
        new Rule { id = SoundId.WeaponLaser, bus = SoundBus.Weapon, match = R(@"^fire.?laser"), setup = S(1f, 1f, 0.04f, 0f, 1, 10, 2, 0.05f, 10) },
        new Rule { id = SoundId.WeaponGear2Shot, bus = SoundBus.Weapon, match = R(@"gear ?2|auto ?shot|^shot"), standIn = R(MetalHard), setup = S(0.7f, 1.6f, 0.06f, 0f, 1, 10, 3, 0.05f, 20) },
        new Rule { id = SoundId.WeaponGear3Shot, bus = SoundBus.Weapon, match = R(@"gear ?3|burst"), standIn = R(MetalLight), setup = S(0.55f, 1.8f, 0.1f, 0f, 1, 10, 4, 0.025f, 20) },
        new Rule { id = SoundId.WeaponRocketJump, bus = SoundBus.Weapon, match = R(@"gear ?4|rocket"), standIn = R(Bumm), setup = S(1f, 1.1f, 0.05f, 0.2f, 2, 20, 1, 0.1f, 10) },
        new Rule { id = SoundId.BulletImpact, bus = SoundBus.World, match = R(MetalLight), setup = S(0.5f, 1.2f, 0.12f, 1f, 1.5f, 25, 6, 0.03f, 160, volVar: 0.25f) },
        new Rule { id = SoundId.LaserImpact, bus = SoundBus.World, match = R(MetalHard), setup = S(0.8f, 1.1f, 0.08f, 1f, 2, 40, 2, 0.05f, 100) },
        new Rule { id = SoundId.Gear2Explosion, bus = SoundBus.Weapon, match = R(@"small explosion|explosion small|^pop"), standIn = R(Bumm), setup = S(0.5f, 1.55f, 0.1f, 1f, 2.5f, 40, 4, 0.06f, 90) },
        new Rule { id = SoundId.WeaponTrumpetShot, bus = SoundBus.Weapon, match = R(@"^trumpet shot"), standIn = R(Bumm), setup = S(1f, 1f, 0f, 0f, 1, 10, 2, 0f, 0, volVar: 0f) },
        new Rule { id = SoundId.WeaponMinigunSpin, bus = SoundBus.Weapon, match = R(@"^minigun spin"), setup = S(0.8f, 1f, 0.04f, 0f, 1, 10, 2, 0.1f, 30) },
        new Rule { id = SoundId.WeaponSelfKill, bus = SoundBus.Voice, match = R(@"^self ?kill"), setup = S(1f, 1f, 0f, 0f, 1, 10, 2, 0f, 0, volVar: 0f) },

        // ---- Robots
        new Rule { id = SoundId.RobotHurt, bus = SoundBus.Voice, match = R(@"^ouch|grunt"), setup = S(0.9f, 1f, 0.08f, 1f, 3, 35, 4, 0.08f, 50, SoundCue.Limit.StealOldest, volVar: 0.15f) },
        new Rule { id = SoundId.RobotHitMetal, bus = SoundBus.Enemies, match = R(MetalLight), setup = S(0.75f, 1f, 0.1f, 1f, 2, 35, 4, 0.04f, 90, volVar: 0.2f) },
        new Rule { id = SoundId.RobotAlert, bus = SoundBus.Voice, match = R(@"stop you criminal|^alert"), setup = S(1f, 1f, 0.04f, 1f, 4, 45, 1, 3f, 30, SoundCue.Limit.Reject, occlusion: true) },
        new Rule { id = SoundId.RobotDeath, bus = SoundBus.Enemies, match = R(@"robo.?death"), setup = S(0.85f, 1f, 0.06f, 1f, 3, 45, 3, 0.1f, 40) },
        new Rule { id = SoundId.RobotDeathVoice, bus = SoundBus.Voice, match = R(@"^goodbye"), setup = S(0.9f, 1f, 0.05f, 1f, 3, 40, 1, 2f, 50, SoundCue.Limit.Reject) },
        new Rule { id = SoundId.RobotExplode, bus = SoundBus.Enemies, match = R(Bumm + @"|robot explo"), setup = S(1f, 1f, 0.08f, 1f, 4, 70, 3, 0.05f, 20) },
        new Rule { id = SoundId.RobotLand, bus = SoundBus.Enemies, match = R(@"robot land"), standIn = R(MetalHard), setup = S(0.8f, 0.6f, 0.06f, 1f, 3, 35, 2, 0.1f, 100) },
        new Rule { id = SoundId.EnemyBulletFire, bus = SoundBus.Enemies, match = R(@"enemy shot|robot shot|enemy fire"), standIn = R(@"^fire.?laser"), setup = S(0.55f, 1.7f, 0.08f, 1f, 3, 45, 4, 0.05f, 70) },
        new Rule { id = SoundId.EnemyLaserFire, bus = SoundBus.Enemies, match = R(@"enemy laser|^fire.?laser"), setup = S(0.9f, 0.75f, 0.05f, 1f, 4, 60, 2, 0.1f, 40) },
        new Rule { id = SoundId.EnemyBulletImpact, bus = SoundBus.World, match = R(MetalHard), setup = S(0.65f, 1.15f, 0.1f, 1f, 2, 30, 4, 0.04f, 110) },
        new Rule { id = SoundId.ShieldHit, bus = SoundBus.Enemies, match = R(@"shield hit|" + MetalHard), setup = S(0.8f, 0.9f, 0.08f, 1f, 2.5f, 35, 3, 0.05f, 90) },
        new Rule { id = SoundId.ShieldBreak, bus = SoundBus.Enemies, match = R(@"shield break|^metal hit [345]$"), setup = S(1f, 0.75f, 0.05f, 1f, 3, 45, 2, 0.1f, 40) },
        new Rule { id = SoundId.RobotBerate, bus = SoundBus.Voice, match = R(BerateLines), setup = S(1f, 1f, 0f, 1f, 5, 45, 2, 0f, 20, volVar: 0f, occlusion: false) },
        new Rule { id = SoundId.Debris, bus = SoundBus.World, match = R(MetalLight), setup = S(0.35f, 1.3f, 0.15f, 1f, 1.5f, 20, 5, 0.04f, 200, volVar: 0.3f) },

        // ---- Doors, buttons, targets, spawners
        new Rule { id = SoundId.ButtonPress, bus = SoundBus.World, match = R(@"^button"), setup = S(0.8f, 1f, 0.04f, 1f, 2, 30, 2, 0.05f, 60) },
        new Rule { id = SoundId.ButtonRelease, bus = SoundBus.World, match = R(@"button release|^button"), setup = S(0.45f, 0.75f, 0.04f, 1f, 2, 25, 2, 0.05f, 90) },
        new Rule { id = SoundId.TargetHit, bus = SoundBus.World, match = R(MetalLight), setup = S(0.7f, 1.1f, 0.1f, 1f, 2, 35, 3, 0.04f, 100) },
        new Rule { id = SoundId.TargetBreak, bus = SoundBus.World, match = R(@"target break"), standIn = R(Bumm), setup = S(0.7f, 1.3f, 0.06f, 1f, 3, 50, 2, 0.05f, 40) },
        new Rule { id = SoundId.DoorOpen, bus = SoundBus.World, match = R(@"^door \d+$|door open"), setup = S(0.9f, 1f, 0.04f, 1f, 3, 35, 4, 0.1f, 50) },
        new Rule { id = SoundId.DoorClose, bus = SoundBus.World, match = R(@"^door close"), setup = S(0.8f, 1f, 0.04f, 1f, 3, 35, 4, 0.1f, 50) },
        new Rule { id = SoundId.DoorUnlock, bus = SoundBus.World, match = R(@"^approval|unlock"), setup = S(0.9f, 1f, 0.02f, 0.7f, 4, 50, 2, 0.3f, 30, occlusion: false) },
        new Rule { id = SoundId.SpawnerHatch, bus = SoundBus.World, match = R(@"^door small|hatch|trapdoor"), setup = S(0.9f, 0.9f, 0.04f, 1f, 3, 40, 2, 0.2f, 60) },

        // ---- Elevator, conveyors, machines (loops)
        new Rule { id = SoundId.ElevatorLoop, bus = SoundBus.Ambience, match = R(@"^elevator"), setup = S(0.8f, 1f, 0.03f, 1f, 3, 30, 4, 0f, 60, loop: true, volVar: 0f) },
        new Rule { id = SoundId.ElevatorStart, bus = SoundBus.World, match = R(@"elevator start"), standIn = R(@"^lock clack"), setup = S(0.9f, 0.6f, 0.04f, 1f, 3, 30, 1, 0.3f, 60) },
        new Rule { id = SoundId.ElevatorStop, bus = SoundBus.World, match = R(@"elevator stop"), standIn = R(MetalHard), setup = S(0.9f, 0.55f, 0.04f, 1f, 3, 30, 1, 0.3f, 60) },
        new Rule { id = SoundId.ConveyorLoop, bus = SoundBus.Ambience, match = R(@"^machine drone|conveyor"), setup = S(0.55f, 1f, 0.06f, 1f, 2.5f, 18, 16, 0f, 150, loop: true, volVar: 0f) },
        new Rule { id = SoundId.CableBoxHum, bus = SoundBus.Ambience, match = R(@"^server drone|hum"), setup = S(0.3f, 1f, 0.08f, 1f, 1.5f, 8, 16, 0f, 200, loop: true, volVar: 0f) },

        // ---- Pickups
        new Rule { id = SoundId.PickupHealth, bus = SoundBus.Player, match = R(@"pickup health|^health ?get|^heal"), standIn = R(@"^approval"), setup = S(0.8f, 1.15f, 0.03f, 0f, 1, 10, 2, 0.05f, 40) },
        new Rule { id = SoundId.PickupAmmo, bus = SoundBus.Player, match = R(@"pickup ammo|^ammo"), standIn = R(@"^lock clack"), setup = S(0.8f, 0.85f, 0.04f, 0f, 1, 10, 2, 0.05f, 40) },
        new Rule { id = SoundId.PickupLand, bus = SoundBus.World, match = R(@"^metal hit [12]$"), setup = S(0.3f, 1.5f, 0.1f, 1f, 1.5f, 15, 3, 0.05f, 200) },

        // ---- Music
        new Rule { id = SoundId.MusicEnd, bus = SoundBus.Music, match = R(@"^end tunes|music"), setup = S(0.8f, 1f, 0f, 0f, 1, 10, 1, 0f, 0, volVar: 0f) },

        // ---- Special enemies
        new Rule { id = SoundId.RobotGazember, bus = SoundBus.Voice, match = R(@"gazember"), setup = S(1f, 1f, 0f, 1f, 5, 45, 1, 0f, 20, volVar: 0f, occlusion: false) },
    };

    // ---------------------------------------------------------------- entry points

    static AudioSetupTool()
    {
        // First time the project opens with this script: build everything once.
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (AssetDatabase.LoadAssetAtPath<SoundBank>(BankPath) != null) return;
            if (!AssetDatabase.IsValidFolder(AudioRoot)) return;
            Build();
        };
    }

    /// <summary>True when the bank exists but has no cue for <paramref name="id"/> yet (it was made before the id was added).</summary>
    public static bool BankIsMissing(SoundId id)
    {
        SoundBank bank = AssetDatabase.LoadAssetAtPath<SoundBank>(BankPath);
        return bank != null && !bank.entries.Any(e => e != null && e.id == id && e.cue != null);
    }

    [MenuItem("Final Shift/Audio/Rebuild Sound Bank")]
    public static void Build()
    {
        EnsureFolder(CueFolder);
        EnsureFolder(BankFolder);

        // Every clip in the Audio folder, by its normalised name.
        var clips = new List<(AudioClip clip, string name, string path)>();
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null) clips.Add((clip, Normalise(Path.GetFileNameWithoutExtension(path)), path));
        }
        clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        AudioMixer mixer = EnsureMixer();
        SoundBank bank = AssetDatabase.LoadAssetAtPath<SoundBank>(BankPath);
        if (bank == null)
        {
            bank = ScriptableObject.CreateInstance<SoundBank>();
            AssetDatabase.CreateAsset(bank, BankPath);
        }
        bank.mixer = mixer;
        bank.buses.RemoveAll(b => b == null);
        foreach (SoundBus bus in Enum.GetValues(typeof(SoundBus)))
        {
            SoundBank.BusGroup b = bank.Bus(bus);
            if (b == null) { b = new SoundBank.BusGroup { bus = bus }; bank.buses.Add(b); }
            if (mixer != null) b.group = mixer.FindMatchingGroups(bus.ToString()).FirstOrDefault(g => g.name == bus.ToString());
        }

        var used = new HashSet<string>();
        var report = new StringBuilder();
        var standIns = new List<string>();
        var empty = new List<string>();

        foreach (Rule rule in Rules)
        {
            string cuePath = $"{CueFolder}/{rule.id}.asset";
            SoundCue cue = AssetDatabase.LoadAssetAtPath<SoundCue>(cuePath);
            bool created = cue == null;
            if (created)
            {
                cue = ScriptableObject.CreateInstance<SoundCue>();
                cue.bus = rule.bus;
                rule.setup?.Invoke(cue);
                AssetDatabase.CreateAsset(cue, cuePath);
            }

            var matched = clips.Where(c => rule.match != null && rule.match.IsMatch(c.name) && !Skipped.IsMatch(c.name)).ToList();
            bool usingStandIn = false;
            if (matched.Count == 0 && rule.standIn != null)
            {
                matched = clips.Where(c => rule.standIn.IsMatch(c.name) && !Skipped.IsMatch(c.name)).ToList();
                usingStandIn = matched.Count > 0;
            }

            if (cue.autoAssignClips)
                cue.clips = matched.Select(c => c.clip).ToArray();
            foreach (AudioClip c in cue.clips) if (c != null) used.Add(AssetDatabase.GetAssetPath(c));
            EditorUtility.SetDirty(cue);

            string clipNames = cue.clips.Length > 0 ? string.Join(", ", cue.clips.Where(c => c != null).Select(c => c.name)) : "(none)";
            report.AppendLine($"{rule.id,-20} {(usingStandIn ? "[stand-in] " : "")}{clipNames}");
            if (usingStandIn) standIns.Add(rule.id.ToString());
            if (!cue.HasClips) empty.Add(rule.id.ToString());

            SoundBank.Entry entry = bank.entries.FirstOrDefault(e => e != null && e.id == rule.id);
            if (entry == null) bank.entries.Add(entry = new SoundBank.Entry { id = rule.id });
            entry.cue = cue;
        }
        EditorUtility.SetDirty(bank);

        // Import settings for the clips in use.
        foreach (var c in clips)
            if (used.Contains(c.path)) ApplyImportSettings(c.path, c.clip, IsMusic(c.name));

        var skipped = clips.Where(c => Skipped.IsMatch(c.name)).Select(c => Path.GetFileName(c.path)).ToList();
        var unplaced = clips.Where(c => !used.Contains(c.path) && !Skipped.IsMatch(c.name)).Select(c => Path.GetFileName(c.path)).ToList();

        report.AppendLine();
        report.AppendLine("Stand-ins (placeholder clips until proper ones exist): " + (standIns.Count > 0 ? string.Join(", ", standIns) : "none"));
        report.AppendLine("Events with no clip yet: " + (empty.Count > 0 ? string.Join(", ", empty) : "none"));
        report.AppendLine("Skipped on purpose: " + (skipped.Count > 0 ? string.Join(", ", skipped) : "none"));
        report.AppendLine("Clips not used by anything: " + (unplaced.Count > 0 ? string.Join(", ", unplaced) : "none"));
        report.AppendLine("Mixer: " + (mixer != null ? MixerPath : "not created (make one by hand at " + MixerPath + " with groups named after the buses, then rebuild)"));
        File.WriteAllText(ReportPath, report.ToString());

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Sound bank rebuilt (" + Rules.Length + " sound events). Details in " + ReportPath + "\n" + report);
    }

    // ---------------------------------------------------------------- import settings

    private static bool IsMusic(string name) => Regex.IsMatch(name, @"^end tunes|music", RegexOptions.IgnoreCase);

    private static void ApplyImportSettings(string path, AudioClip clip, bool music)
    {
        AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null) return;

        AudioImporterSampleSettings s = importer.defaultSampleSettings;
        AudioClipLoadType load = music ? AudioClipLoadType.Streaming
            : clip.length < 3f ? AudioClipLoadType.DecompressOnLoad
            : AudioClipLoadType.CompressedInMemory;
        bool mono = !music; // 3D sources fold to mono anyway; this halves memory
        bool preload = !music;

        bool changed = s.loadType != load || importer.forceToMono != mono || s.preloadAudioData != preload
                       || s.compressionFormat != AudioCompressionFormat.Vorbis;
        if (!changed) return;

        s.loadType = load;
        s.preloadAudioData = preload;
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = music ? 0.7f : 0.6f;
        importer.defaultSampleSettings = s;
        importer.forceToMono = mono;
        importer.loadInBackground = music;
        importer.SaveAndReimport();
    }

    // ---------------------------------------------------------------- mixer

    /// <summary>
    /// Loads or creates Assets/Audio/GameMixer.mixer with the bus groups. Unity has no public API to make mixers
    /// from code, so this uses the editor's own (internal) one; if that ever fails the game still works without a mixer.
    /// </summary>
    private static AudioMixer EnsureMixer()
    {
        AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        try
        {
            Type controllerType = typeof(Editor).Assembly.GetType("UnityEditor.Audio.AudioMixerController");
            if (controllerType == null) return mixer;
            const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

            if (mixer == null)
            {
                MethodInfo create = controllerType.GetMethod("CreateMixerControllerAtPath", Any);
                if (create == null) return null;
                mixer = create.Invoke(null, new object[] { MixerPath }) as AudioMixer;
                if (mixer == null) return null;
            }
            if (!controllerType.IsInstanceOfType(mixer)) return mixer;

            MethodInfo newGroup = controllerType.GetMethods(Any).FirstOrDefault(m => m.Name == "CreateNewGroup" && m.GetParameters().Length == 2);
            MethodInfo addChild = controllerType.GetMethod("AddChildToParent", Any);
            PropertyInfo masterProp = controllerType.GetProperty("masterGroup", Any);
            MethodInfo addToView = controllerType.GetMethod("AddGroupToCurrentView", Any);
            if (newGroup == null || addChild == null || masterProp == null) return mixer;

            AudioMixerGroup master = masterProp.GetValue(mixer) as AudioMixerGroup;
            AudioMixerGroup sfx = Group(mixer, "SFX", master, newGroup, addChild, addToView);
            foreach (string name in new[] { "Weapon", "Player", "Enemies", "World" })
                Group(mixer, name, sfx, newGroup, addChild, addToView);
            foreach (string name in new[] { "Voice", "Ambience", "Music" })
                Group(mixer, name, master, newGroup, addChild, addToView);

            EditorUtility.SetDirty(mixer);
            AssetDatabase.SaveAssets();
        }
        catch (Exception e)
        {
            Debug.LogWarning("AudioSetupTool: couldn't build the mixer automatically (" + e.Message + "). Sounds still play, just without mixer groups.");
        }
        return mixer;
    }

    private static AudioMixerGroup Group(AudioMixer mixer, string name, AudioMixerGroup parent,
        MethodInfo newGroup, MethodInfo addChild, MethodInfo addToView)
    {
        AudioMixerGroup existing = mixer.FindMatchingGroups(name).FirstOrDefault(g => g.name == name);
        if (existing != null) return existing;
        AudioMixerGroup g = newGroup.Invoke(mixer, new object[] { name, false }) as AudioMixerGroup;
        if (g == null) return parent;
        addChild.Invoke(mixer, new object[] { g, parent });
        addToView?.Invoke(mixer, new object[] { g });
        return g;
    }

    // ---------------------------------------------------------------- helpers

    private static string Normalise(string fileName)
    {
        string s = fileName.ToLowerInvariant().Replace('_', ' ');
        s = Regex.Replace(s, @"[^a-z0-9 ]", "");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
