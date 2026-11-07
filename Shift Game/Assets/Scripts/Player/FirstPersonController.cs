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

    private Vector3 pushVelocity;

    /// <summary>Adds a one-off push (m/s), e.g. the gear 4 rocket jump.</summary>
    public void AddImpulse(Vector3 velocityChange)
    {
        if (velocityChange.y > 0f && verticalVelocity < 0f) verticalVelocity = 0f;
        verticalVelocity += velocityChange.y;
        pushVelocity += new Vector3(velocityChange.x, 0f, velocityChange.z);
    }

    private void Awake()
    {
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
        HandleCursor();
        if (Cursor.lockState == CursorLockMode.Locked && !LookLocked)
            HandleLook();
        HandleMovement();
    }

    private void HandleCursor()
    {
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
        Vector2 delta = fromMouse ? look * mouseSensitivity : look * gamepadSensitivity * Time.deltaTime;

        transform.Rotate(Vector3.up, delta.x, Space.Self);

        pitch = Mathf.Clamp(pitch - delta.y, -maxPitch, maxPitch);
        if (cameraPivot != null)
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void HandleMovement()
    {
        Vector2 input = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
        float speed = sprintAction.IsPressed() ? sprintSpeed : walkSpeed;
        Vector3 horizontal = (transform.right * input.x + transform.forward * input.y) * speed;

        bool grounded = controller.isGrounded;
        if (grounded)
        {
            if (verticalVelocity < 0f) verticalVelocity = -2f; // keep snapped to ground (but don't cancel an upward push)
            if (jumpAction.WasPressedThisFrame())
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = horizontal + pushVelocity + Vector3.up * verticalVelocity;
        CollisionFlags flags = controller.Move(velocity * Time.deltaTime);

        if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f; // bonk the ceiling
        pushVelocity = Vector3.MoveTowards(pushVelocity, Vector3.zero, (grounded ? groundPushDrag : airPushDrag) * Time.deltaTime);
    }

    private static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
