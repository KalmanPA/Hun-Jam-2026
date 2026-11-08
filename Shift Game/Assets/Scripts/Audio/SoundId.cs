/// <summary>
/// Every sound event in the game. Gameplay code asks for a sound by id, and the SoundBank
/// (Assets/Audio/Resources/SoundBank.asset) says which SoundCue plays for it.
/// The numbers are saved in the bank, so only ever add new ids at the end; never renumber.
/// </summary>
public enum SoundId
{
    None = 0,

    // Player
    Footstep = 1,
    PlayerLand = 2,
    PlayerHurt = 3,

    // Gear-shift weapon
    WeaponGearEngage = 10,
    WeaponGearRelease = 11,
    WeaponEmpty = 12,
    WeaponLaser = 13,
    WeaponGear2Shot = 14,
    WeaponGear3Shot = 15,
    WeaponRocketJump = 16,
    BulletImpact = 17,
    LaserImpact = 18,
    Gear2Explosion = 19,

    // Robots
    RobotHurt = 30,
    RobotHitMetal = 31,
    RobotAlert = 32,
    RobotDeath = 33,
    RobotDeathVoice = 34,
    RobotExplode = 35,
    RobotLand = 36,
    EnemyBulletFire = 37,
    EnemyLaserFire = 38,
    EnemyBulletImpact = 39,
    ShieldHit = 40,
    ShieldBreak = 41,
    Debris = 42,
    RobotBerate = 43,   // long voice-acted lines the intro robots shout at the player

    // Doors, buttons, targets, spawners
    ButtonPress = 50,
    ButtonRelease = 51,
    TargetHit = 52,
    TargetBreak = 53,
    DoorOpen = 54,
    DoorClose = 55,
    DoorUnlock = 56,
    SpawnerHatch = 57,

    // Elevator, conveyors, machines
    ElevatorLoop = 60,
    ElevatorStart = 61,
    ElevatorStop = 62,
    ConveyorLoop = 63,
    CableBoxHum = 64,

    // Pickups
    PickupHealth = 70,
    PickupAmmo = 71,
    PickupLand = 72,

    // Music
    MusicEnd = 80,
}

/// <summary>Mixer bus a cue plays through. Each one maps to a group in Assets/Audio/GameMixer.mixer.</summary>
public enum SoundBus
{
    Weapon,
    Player,
    Enemies,
    World,
    Voice,
    Ambience,
    Music,
}
