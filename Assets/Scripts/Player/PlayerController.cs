using UnityEngine;

// Current way of moving. Used by footsteps (loudness) and later by other systems.
public enum MoveMode
{
    Idle,       // standing still or in the air
    Crouch,     // crouched, any direction
    Walk,       // forward / sideways
    Run,        // Shift + forward / sideways
    BackWalk,   // S
    BackStep    // S + Shift (careful retreat)
}

public class PlayerController : MonoBehaviour
{
    public CharacterController controller;

    // ---------------------------------------------------------------
    // Movement speeds.
    // walkSpeed - normal movement. runSpeed - while holding Left Shift.
    // New field names on purpose: Unity keeps old Inspector values for
    // the old "speed" field, so new names make these defaults apply.
    // ---------------------------------------------------------------
    [Header("Movement")]
    public float walkSpeed = 4f;
    public float runSpeed = 8f;

    // Backward movement. S alone - normal step back.
    // S + Shift - careful retreat (watching the bear, D-01).
    public float backWalkSpeed = 3f;
    public float backStepSpeed = 2f;

    public KeyCode runKey = KeyCode.LeftShift;

    // ---------------------------------------------------------------
    // Crouch (hold the key). Slow sneaking in any direction.
    // crouchHeight       - CharacterController height while crouched.
    // crouchTransitionSpeed - how fast the body goes down / up (m/s).
    // While crouched: no running, no jumping.
    // ---------------------------------------------------------------
    [Header("Crouch")]
    public KeyCode crouchKey = KeyCode.LeftControl;
    public float crouchSpeed = 1.8f;
    public float crouchHeight = 1.0f;
    public float crouchTransitionSpeed = 6f;

    [Header("Look / Physics")]
    public float mouseSensitivity = 2f;
    public float gravity = -20f;
    public float jumpHeight = 1.5f;

    public Transform cameraHolder;

    // State flags for other systems (dominance, footstep sounds).
    // IsRunning     - Shift + moving forward or sideways (not crouched).
    // IsBackingAway - Shift + moving backward (careful retreat, not crouched).
    // IsCrouching   - crouch key held, or forced to stay down (no room above).
    // IsMoving      - player is trying to move this frame.
    public bool IsRunning { get; private set; }
    public bool IsBackingAway { get; private set; }
    public bool IsCrouching { get; private set; }
    public bool IsMoving { get; private set; }
    public MoveMode CurrentMode { get; private set; }

    float xRotation = 0f;
    Vector3 velocity;

    // Standing values, remembered at Start, so crouch works with
    // whatever height / camera position is set in the Inspector.
    float standHeight;
    float standCenterY;
    float standCameraY;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;

        standHeight = controller.height;
        standCenterY = controller.center.y;
        standCameraY = cameraHolder.localPosition.y;
    }

    void Update()
    {
        Look();
        Crouch();
        Move();
    }

    // ---------------------------------------------------------------
    // Mouse look: vertical rotation on the camera holder,
    // horizontal rotation on the whole player body.
    // ---------------------------------------------------------------
    void Look()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        cameraHolder.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX);
    }

    // ---------------------------------------------------------------
    // Crouch: decides IsCrouching and smoothly changes the body height
    // (CharacterController) and the camera height.
    // Releasing the key under a low ceiling keeps the player crouched,
    // otherwise the capsule would grow into the ceiling and get stuck.
    // ---------------------------------------------------------------
    void Crouch()
    {
        bool wantsCrouch = Input.GetKey(crouchKey);

        if (wantsCrouch)
            IsCrouching = true;
        else if (IsCrouching && CanStandUp())
            IsCrouching = false;

        float targetHeight = IsCrouching ? crouchHeight : standHeight;
        float newHeight = Mathf.MoveTowards(
            controller.height,
            targetHeight,
            crouchTransitionSpeed * Time.deltaTime
        );

        // Keep the feet on the ground: the capsule shrinks from the top.
        float heightLost = standHeight - newHeight;
        controller.height = newHeight;
        controller.center = new Vector3(
            controller.center.x,
            standCenterY - heightLost * 0.5f,
            controller.center.z
        );

        // Camera goes down by the same amount as the top of the capsule.
        Vector3 camPos = cameraHolder.localPosition;
        camPos.y = standCameraY - heightLost;
        cameraHolder.localPosition = camPos;
    }

    // Is there free space above the head to stand up?
    // Casts a sphere from the top of the current (crouched) capsule
    // up to the standing height. The player's own collider is not hit,
    // because casts ignore colliders they start inside.
    bool CanStandUp()
    {
        float radius = controller.radius * 0.95f;   // a bit smaller - no false hits on walls
        Vector3 worldCenter = transform.position + controller.center;
        Vector3 topSphere = worldCenter + Vector3.up * (controller.height * 0.5f - radius);
        float distance = standHeight - controller.height;

        if (distance <= 0.01f)
            return true;

        return !Physics.SphereCast(
            topSphere,
            radius,
            Vector3.up,
            out _,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore
        );
    }

    // ---------------------------------------------------------------
    // Movement: WASD direction, speed by mode (crouch / walk / run /
    // back / careful retreat), gravity and jump.
    // ---------------------------------------------------------------
    void Move()
    {
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        Vector3 move = transform.right * x + transform.forward * z;

        // Without this, diagonal movement (W + D) is ~1.4x faster.
        move = Vector3.ClampMagnitude(move, 1f);

        bool isTryingToMove = move.sqrMagnitude > 0.01f;
        bool isShiftHeld = Input.GetKey(runKey);
        bool isMovingBackward = z < -0.1f;   // S pressed (also S+A / S+D)

        IsMoving = isTryingToMove;

        // Shift means "run" only forward/sideways.
        // Backward, Shift means "retreat carefully" - nobody sprints backward.
        // Crouched, Shift does nothing - crouch has one slow speed.
        IsRunning = isTryingToMove && isShiftHeld && !isMovingBackward && !IsCrouching;
        IsBackingAway = isTryingToMove && isShiftHeld && isMovingBackward && !IsCrouching;

        float currentSpeed;

        if (IsCrouching)
            currentSpeed = crouchSpeed;
        else if (isMovingBackward)
            currentSpeed = isShiftHeld ? backStepSpeed : backWalkSpeed;
        else
            currentSpeed = isShiftHeld ? runSpeed : walkSpeed;

        // One summary value for other systems (footsteps etc.).
        if (!isTryingToMove)
            CurrentMode = MoveMode.Idle;
        else if (IsCrouching)
            CurrentMode = MoveMode.Crouch;
        else if (isMovingBackward)
            CurrentMode = isShiftHeld ? MoveMode.BackStep : MoveMode.BackWalk;
        else
            CurrentMode = isShiftHeld ? MoveMode.Run : MoveMode.Walk;

        controller.Move(move * currentSpeed * Time.deltaTime);

        if (controller.isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        // No jumping from a crouch.
        if (Input.GetButtonDown("Jump") && controller.isGrounded && !IsCrouching)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        velocity.y += gravity * Time.deltaTime;

        controller.Move(velocity * Time.deltaTime);
    }
}
