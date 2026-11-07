using UnityEngine;

/// <summary>
/// Decides where each robot shot goes, so the robots feel dangerous but fair.
///
/// Every shot first rolls a hit chance, then aims on purpose:
///  - Hit chance drops with distance, with the player's speed, while the player is in the air,
///    and a lot when the robot is off screen (shooting the player in the back feels cheap).
///  - Accuracy warms up: the first shots after spotting the player always miss (warning shots),
///    then accuracy climbs the longer the robot keeps line of sight. Losing sight resets it.
///  - A hit aims at where the player was a moment ago (aim lag), so moving players can still dodge.
///  - A miss aims at a ring just outside the player's body, so the bullet visibly whizzes past
///    instead of flying off at random. Some misses hit the floor in front of the player.
///  - A shared hit budget limits how often ANY robot may land a hit, so a group can't shred the player at once.
/// </summary>
public class EnemyAim : MonoBehaviour
{
    [Header("Hit chance")]
    [Tooltip("Chance to hit at close range, player standing still, robot fully warmed up.")]
    [SerializeField, Range(0f, 1f)] private float maxHitChance = 0.75f;
    [Tooltip("Up to this distance there is no distance penalty.")]
    [SerializeField] private float fullAccuracyDistance = 6f;
    [Tooltip("At this distance the distance penalty is strongest.")]
    [SerializeField] private float longDistance = 30f;
    [Tooltip("Distance multiplier at long distance.")]
    [SerializeField, Range(0f, 1f)] private float longDistanceMultiplier = 0.25f;
    [Tooltip("Player speed (m/s) where the movement penalty is strongest. Sprint speed is 7.")]
    [SerializeField] private float fastPlayerSpeed = 7f;
    [Tooltip("Multiplier when the player moves at the fast speed.")]
    [SerializeField, Range(0f, 1f)] private float fastPlayerMultiplier = 0.4f;
    [SerializeField, Range(0f, 1f)] private float airborneMultiplier = 0.6f;
    [Tooltip("Multiplier when this robot is not on the player's screen.")]
    [SerializeField, Range(0f, 1f)] private float offScreenMultiplier = 0.2f;

    [Header("Warm up")]
    [Tooltip("Shots after spotting the player that always miss (warning shots).")]
    [SerializeField] private int warningShots = 2;
    [Tooltip("Seconds of line of sight until full accuracy.")]
    [SerializeField] private float warmUpTime = 3f;
    [SerializeField, Range(0f, 1f)] private float coldAccuracy = 0.35f;
    [Tooltip("After this many seconds without line of sight the warm up resets.")]
    [SerializeField] private float forgetTime = 1.5f;

    [Header("Aim")]
    [Tooltip("Hits aim at where the player was this many seconds ago.")]
    [SerializeField] private float aimLag = 0.2f;
    [Tooltip("Random offset (m) for hits, so they don't all land in one spot.")]
    [SerializeField] private float hitJitter = 0.12f;
    [Tooltip("How far outside the player's body a near miss passes (m).")]
    [SerializeField] private Vector2 nearMissGap = new Vector2(0.35f, 1.1f);
    [Tooltip("Share of misses aimed at the floor in front of the player.")]
    [SerializeField, Range(0f, 1f)] private float floorMissShare = 0.2f;

    [Header("Shared hit budget (all robots)")]
    [Tooltip("Minimum seconds between hits landed by any robot.")]
    [SerializeField] private float minTimeBetweenAnyHits = 0.45f;

    [Header("Debug")]
    [SerializeField] private bool logShots;

    private static float nextAllowedHitTime;

    private Transform player;
    private CharacterController playerController;
    private Camera playerCamera;

    private float sightTime;
    private float timeWithoutSight = float.MaxValue;
    private int shotsSinceSpotted;

    /// <summary>The hit chance the last shot rolled against (for debugging/tuning).</summary>
    public float LastHitChance { get; private set; }

    public void SetTarget(Transform target)
    {
        player = target;
        playerController = target != null ? target.GetComponent<CharacterController>() : null;
        playerCamera = target != null ? target.GetComponentInChildren<Camera>() : null;
        if (playerCamera == null) playerCamera = Camera.main;
    }

    /// <summary>Call every frame with whether the robot can see the player.</summary>
    public void Tick(bool canSee, float deltaTime)
    {
        if (canSee)
        {
            if (timeWithoutSight > forgetTime)
            {
                // Spotted (again): start cold, warning shots first.
                sightTime = 0f;
                shotsSinceSpotted = 0;
            }
            timeWithoutSight = 0f;
            sightTime += deltaTime;
        }
        else
        {
            timeWithoutSight += deltaTime;
        }
    }

    /// <summary>The player's body centre in world space.</summary>
    public Vector3 TargetCenter
    {
        get
        {
            if (playerController != null)
                return player.TransformPoint(playerController.center);
            return player != null ? player.position + Vector3.up : transform.position;
        }
    }

    /// <summary>Rolls hit or miss for one shot fired from <paramref name="muzzle"/> and returns the point to shoot at.</summary>
    /// <remarks>
    /// When <paramref name="canSee"/> is false the player is in cover: the shot is always a near miss
    /// around the player (suppressive fire that hits the cover), and it doesn't use up the warning shots.
    /// </remarks>
    public Vector3 PickAimPoint(Vector3 muzzle, bool canSee, out bool aimedToHit)
    {
        aimedToHit = false;
        if (player == null) return muzzle + transform.forward * 10f;

        Vector3 velocity = playerController != null ? playerController.velocity : Vector3.zero;
        Vector3 lagged = TargetCenter - velocity * aimLag;

        if (!canSee)
        {
            LastHitChance = 0f;
            return MissPoint(muzzle, lagged);
        }

        LastHitChance = HitChance(muzzle, velocity);
        bool warning = shotsSinceSpotted < warningShots;
        shotsSinceSpotted++;

        if (!warning && Random.value < LastHitChance && Time.time >= nextAllowedHitTime)
        {
            aimedToHit = true;
            nextAllowedHitTime = Time.time + minTimeBetweenAnyHits;
        }

        Vector3 point = aimedToHit ? HitPoint(lagged) : MissPoint(muzzle, lagged);
        if (logShots)
            Debug.Log($"[EnemyAim] {name}: chance {LastHitChance:0.00}, warning {warning}, aimed to hit {aimedToHit}", this);
        return point;
    }

    private float HitChance(Vector3 muzzle, Vector3 playerVelocity)
    {
        float distance = Vector3.Distance(muzzle, TargetCenter);
        float d = Mathf.InverseLerp(fullAccuracyDistance, longDistance, distance);
        float distanceFactor = Mathf.Lerp(1f, longDistanceMultiplier, d * d * (3f - 2f * d));

        Vector3 flat = playerVelocity;
        flat.y = 0f;
        float moveFactor = Mathf.Lerp(1f, fastPlayerMultiplier, Mathf.Clamp01(flat.magnitude / Mathf.Max(0.01f, fastPlayerSpeed)));

        float airFactor = playerController != null && !playerController.isGrounded ? airborneMultiplier : 1f;

        float warmFactor = Mathf.Lerp(coldAccuracy, 1f, Mathf.Clamp01(sightTime / Mathf.Max(0.01f, warmUpTime)));

        float screenFactor = IsOnPlayerScreen() ? 1f : offScreenMultiplier;

        return maxHitChance * distanceFactor * moveFactor * airFactor * warmFactor * screenFactor;
    }

    private bool IsOnPlayerScreen()
    {
        if (playerCamera == null) return true;
        Vector3 v = playerCamera.WorldToViewportPoint(transform.position + Vector3.up);
        return v.z > 0f && v.x > -0.05f && v.x < 1.05f && v.y > -0.05f && v.y < 1.05f;
    }

    private Vector3 HitPoint(Vector3 center)
    {
        // Chest to head height, small random spread.
        Vector3 jitter = Random.insideUnitSphere * hitJitter;
        jitter.y = Random.Range(-0.1f, 0.45f);
        return center + jitter;
    }

    private Vector3 MissPoint(Vector3 muzzle, Vector3 center)
    {
        float radius = playerController != null ? playerController.radius : 0.4f;
        float halfHeight = playerController != null ? playerController.height * 0.5f : 0.9f;

        Vector3 toPlayer = center - muzzle;
        Vector3 dir = toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : transform.forward;

        if (Random.value < floorMissShare)
        {
            // Kick up the floor 1-2.5 m in front of the player, on the robot's side.
            Vector3 flatBack = -dir;
            flatBack.y = 0f;
            flatBack = flatBack.sqrMagnitude > 0.001f ? flatBack.normalized : -transform.forward;
            Vector3 across = Vector3.Cross(Vector3.up, flatBack);
            Vector3 floor = center + flatBack * Random.Range(1f, 2.5f) + across * Random.Range(-0.8f, 0.8f);
            floor.y = center.y - halfHeight;
            return floor;
        }

        // A ring around the body, at right angles to the shot. Mostly beside the player at
        // chest/head height or just over the shoulder, so the bullet flies past the camera.
        Vector3 right = Vector3.Cross(Vector3.up, dir);
        if (right.sqrMagnitude < 0.001f) right = transform.right;
        right.Normalize();
        Vector3 up = Vector3.Cross(dir, right).normalized;

        float side = Random.value < 0.5f ? -1f : 1f;
        float angle = Random.Range(-25f, 70f) * Mathf.Deg2Rad; // 0 = straight to the side, 90 = over the head
        float gap = radius + Random.Range(nearMissGap.x, nearMissGap.y);

        // Aim a bit higher than the body centre so the bullet passes near the camera.
        Vector3 ringCenter = center + Vector3.up * (halfHeight * 0.4f);
        return ringCenter + (right * (Mathf.Cos(angle) * side) + up * Mathf.Sin(angle)) * gap;
    }
}
