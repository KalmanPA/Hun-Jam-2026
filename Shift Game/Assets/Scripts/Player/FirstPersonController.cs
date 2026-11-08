using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simple first person controller driven by the project's InputSystem_Actions asset
/// (Player map: Move, Look, Jump, Sprint).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Input actions asset. Falls back to the project-wide actions if empty.")]
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private string actionMapName = "Player";

    [Header("References")]
    [SerializeField] private Transform cameraPivot;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 4f;
    [SerializeField] private float sprintSpeed = 7f;
    [SerializeField] private float jumpHeight = 1.1f;
    [SerializeField] private float gravity = -20f;
    [Tooltip("How fast pushes (like the gear 4 rocket jump) slow down on the ground and in the air.")]
    [SerializeField] private float groundPushDrag = 14f;
    [SerializeField] private float airPushDrag = 2.5f;

    [Header("Pushing props")]
    [Tooltip("Small loose props (like conveyor crates) lighter than this get shoved when the player walks into them.")]
    [SerializeField] private float maxPushMass = 30f;
    [Tooltip("Speed a shoved prop gets, as a fraction of the player's walk speed.")]
    [SerializeField] private float pushStrength = 0.9f;

    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 0.1f;
    [SerializeField] private float gamepadSensitivity = 150f;
    [SerializeField] private float maxPitch = 85f;

    private CharacterController controller;
    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction jumpAction;
    private InputAction sprintAction;

    private float pitch;
    private float verticalVelocity;

    /// <summary>When true the mouse no longer turns the camera (used while moving the gear stick).</summary>
    public bool LookLocked { get; set; }

    /// <summary>When true the player ignores move, look and jump input (used while dead). Gravity still applies.</summary>
    public bool Frozen { get; set; }

    public Transform CameraPivot => cameraPivot;

    private Vector3 pushVelocity;
    private Vector3 beltVelocity;   // set while standing on a conveyor belt
    private bool onBeltThisFrame;

    /// <summary>Adds a one-off push (m/s), e.g. the gear 4 rocket jump.</summary>
    public void AddImpulse(Vector3 velocityChange)
    {
        if (velocityChange.y > 0f && verticalVelocity < 0f) verticalVelocity = 0f;
        verticalVelocity += velocityChange.y;
        pushVelocity += new Vector3(velocityChange.x, 0f, velocityChange.z);
    }

    private void Awake()
    {
        if (GetComponent<PlayerFootsteps>() == null) gameObject.AddComponent<PlayerFootsteps>();
        if (GetComponent<PlayerGlowLight>() == null) gameObject.AddComponent<PlayerGlowLight>();
        controller = GetComponent<CharacterController>();

        if (inputActions == null)
            inputActions = InputSystem.actions;

        InputActionMap map = inputActions.FindActionMap(actionMapName, true);
        moveAction = map.FindAction("Move", true);
        lookAction = map.FindAction("Look", true);
        jumpAction = map.FindAction("Jump", true);
        sprintAction = map.FindAction("Sprint", true);

        if (cameraPivot == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) cameraPivot = cam.transform;
        }
    }

    private void OnEnable()
    {
        moveAction.Enable();
        lookAction.Enable();
        jumpAction.Enable();
        sprintAction.Enable();
        LockCursor(true);
    }

    private void OnDisable()
    {
        LockCursor(false);
    }

    private void Update()
    {
        if (PauseTablet.IsOpen) return; // paused: no look, and no jump queued up for when it resumes
        HandleCursor();
        if (Cursor.lockState == CursorLockMode.Locked && !LookLocked && !Frozen)
            HandleLook();
        HandleMovement();
    }

    private void HandleCursor()
    {
        if (PauseTablet.Exists) return; // the pause tablet owns Escape and the cursor lock
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            LockCursor(false);
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            LockCursor(true);
    }

    private void HandleLook()
    {
        Vector2 look = lookAction.ReadValue<Vector2>();

        // Mouse delta is already per-frame; stick input needs scaling by time.
        bool fromMouse = lookAction.activeControl != null && lookAction.activeControl.device is Pointer;
        Vector2 delta = fromMouse ? look * mouseSensitivity * GameSettings.MouseSensitivity : look * gamepadSensitivity * Time.deltaTime;

        transform.Rotate(Vector3.up, delta.x, Space.Self);

        pitch = Mathf.Clamp(pitch - delta.y, -maxPitch, maxPitch);
        if (cameraPivot != null)
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void HandleMovement()
    {
        Vector2 input = Frozen ? Vector2.zero : Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
        float speed = sprintAction.IsPressed() ? sprintSpeed : walkSpeed;
        Vector3 horizontal = (transform.right * input.x + transform.forward * input.y) * speed;

        bool grounded = controller.isGrounded;
        if (grounded)
        {
            if (verticalVelocity < 0f) verticalVelocity = -2f; // keep snapped to ground (but don't cancel an upward push)
            if (!Frozen && jumpAction.WasPressedThisFrame())
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = horizontal + pushVelocity + beltVelocity + Vector3.up * verticalVelocity;
        onBeltThisFrame = false;
        CollisionFlags flags = controller.Move(velocity * Time.deltaTime);
        if (!onBeltThisFrame) beltVelocity = Vector3.zero;

        if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f; // bonk the ceiling
        pushVelocity = Vector3.MoveTowards(pushVelocity, Vector3.zero, (grounded ? groundPushDrag : airPushDrag) * Time.deltaTime);
    }

    /// <summary>Moves the player to a spot (e.g. a checkpoint), facing the given yaw, with the camera level and all pushes cleared.</summary>
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        bool wasEnabled = controller.enabled;
        controller.enabled = false;
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, rotation.eulerAngles.y, 0f));
        controller.enabled = wasEnabled;
        Physics.SyncTransforms();

        pitch = 0f;
        if (cameraPivot != null) cameraPivot.localRotation = Quaternion.identity;
        verticalVelocity = 0f;
        pushVelocity = Vector3.zero;
        beltVelocity = Vector3.zero;
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // Standing on a conveyor belt carries the player along.
        if (hit.moveDirection.y < -0.3f && hit.collider.TryGetComponent(out ConveyorBelt belt))
        {
            beltVelocity = belt.SurfaceVelocity;
            onBeltThisFrame = true;
            return;
        }

        // Shove light loose props. Heavy or kinematic ones (big crates) don't move.
        Rigidbody body = hit.rigidbody;
        if (body == null || body.isKinematic || body.mass > maxPushMass) return;
        if (hit.moveDirection.y < -0.3f) return; // standing on top of it

        Vector3 dir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
        if (dir.sqrMagnitude < 0.0001f) return;
        Vector3 push = dir.normalized * walkSpeed * pushStrength;
        Vector3 v = body.linearVelocity;
        body.linearVelocity = new Vector3(push.x, v.y, push.z);
    }

    private static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
