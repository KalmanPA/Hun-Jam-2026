using UnityEngine;

/// <summary>
/// Footsteps for the first-person player: one step per stride while walking on the ground
/// (longer strides when sprinting), a step on jumping, and a heavier one on landing.
/// Added automatically by FirstPersonController.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerFootsteps : MonoBehaviour
{
    [Tooltip("Metres per step at walking speed.")]
    [SerializeField] private float strideLength = 1.9f;
    [Tooltip("Extra stride length per m/s above walking speed.")]
    [SerializeField] private float strideGrowth = 0.12f;
    [SerializeField] private float walkSpeed = 4f;
    [Tooltip("Minimum seconds in the air before landing plays a sound.")]
    [SerializeField] private float minAirTime = 0.25f;

    private CharacterController controller;
    private Vector3 lastPosition;
    private float distance;
    private bool wasGrounded = true;
    private float airTime;
    private float fallSpeed;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        lastPosition = transform.position;
    }

    private void Update()
    {
        Vector3 pos = transform.position;
        Vector3 moved = pos - lastPosition;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        lastPosition = pos;

        bool grounded = controller.isGrounded;
        if (!grounded)
        {
            airTime += Time.deltaTime;
            fallSpeed = Mathf.Max(fallSpeed, -moved.y / dt);
            if (wasGrounded && moved.y > 0f) Step(0.7f, 1f); // jumped
        }
        else
        {
            if (!wasGrounded && airTime >= minAirTime)
            {
                float hard = Mathf.InverseLerp(3f, 14f, fallSpeed);
                GameAudio.Play2D(SoundId.PlayerLand, Mathf.Lerp(0.6f, 1f, hard), Mathf.Lerp(1f, 0.85f, hard));
                distance = 0f;
            }
            airTime = 0f;
            fallSpeed = 0f;

            moved.y = 0f;
            float step = moved.magnitude;
            // Ignore teleports and being carried (elevator, belts move the player too, but only a little sideways).
            if (step < 1f)
            {
                distance += step;
                float speed = step / dt;
                float stride = strideLength + Mathf.Max(0f, speed - walkSpeed) * strideGrowth;
                if (distance >= stride && speed > 0.8f)
                {
                    distance = 0f;
                    Step(Mathf.Clamp(speed / walkSpeed, 0.6f, 1.1f), 1f);
                }
            }
        }
        wasGrounded = grounded;
    }

    private static void Step(float volume, float pitch) => GameAudio.Play2D(SoundId.Footstep, volume, pitch);
}
