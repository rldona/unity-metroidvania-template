using Metroidvania.Controls;
using UnityEngine;
using UnityEngine.Events;

namespace Metroidvania.Player
{
    /// <summary>
    /// Kinematic-feeling platformer controller on top of a dynamic Rigidbody2D.
    /// Gravity is applied manually (gravityScale = 0). Input is read in Update and
    /// consumed in FixedUpdate. All distances are in Unity units (1 u = 16 px).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    [DisallowMultipleComponent]
    public class PlayerController : MonoBehaviour
    {
        public enum State { Idle, Run, Jump, Fall, Attack, AttackUp, AttackDown, Slash, WallSlide }

        public enum AttackDirection { Forward, Up, Down }

        /// <summary>Hitbox placement relative to the feet, in units. Forward is authored facing right.</summary>
        [System.Serializable]
        public struct HitboxShape
        {
            public Vector2 offset;
            public Vector2 size;

            public HitboxShape(Vector2 offset, Vector2 size)
            {
                this.offset = offset;
                this.size = size;
            }
        }

        // Movement within this speed of zero counts as standing still.
        private const float VelocityEpsilon = 0.01f;
        // Upward speed above which a jump still counts as rising. Solver push-out on landing
        // stays well below it, so it can never hide the ground.
        private const float RisingSpeed = 0.5f;

        [Header("Movement")]
        [Tooltip("Maximum horizontal speed (u/s). 6.25 u/s = 100 px/s.")]
        [SerializeField, Min(0f)] private float moveSpeed = 6.25f;
        [Tooltip("Acceleration towards target speed while grounded (u/s²).")]
        [SerializeField, Min(0f)] private float groundAcceleration = 56f;
        [Tooltip("Deceleration when stopping or turning while grounded (u/s²).")]
        [SerializeField, Min(0f)] private float groundDeceleration = 78f;
        [Tooltip("Fraction of ground acceleration/deceleration available in the air.")]
        [SerializeField, Range(0f, 1f)] private float airControl = 0.6f;
        [Tooltip("Stick deflection below this value is ignored. Movement is digital above it.")]
        [SerializeField, Range(0f, 1f)] private float moveDeadZone = 0.35f;

        [Header("Jump")]
        [Tooltip("Apex height of a full jump (u). Initial velocity is derived from gravity.")]
        [SerializeField, Min(0f)] private float jumpHeight = 3.5f;
        [Tooltip("Downward acceleration (u/s²).")]
        [SerializeField, Min(0f)] private float gravity = 56f;
        [Tooltip("Terminal falling speed (u/s).")]
        [SerializeField, Min(0f)] private float maxFallSpeed = 16f;
        [Tooltip("Time after leaving a ledge during which a jump is still allowed (s).")]
        [SerializeField, Min(0f)] private float coyoteTime = 0.10f;
        [Tooltip("Time a jump press is remembered before landing (s).")]
        [SerializeField, Min(0f)] private float jumpBuffer = 0.12f;
        [Tooltip("Vertical velocity multiplier applied once when jump is released while rising.")]
        [SerializeField, Range(0f, 1f)] private float jumpCutMultiplier = 0.4f;

        [Header("Collision")]
        [Tooltip("Layers treated as solid ground and walls.")]
        [SerializeField] private LayerMask groundMask;
        [Tooltip("How far below the feet the ground probe reaches (u).")]
        [SerializeField, Min(0.001f)] private float groundProbeDistance = 0.06f;
        [Tooltip("How far beside the body the wall probe reaches (u).")]
        [SerializeField, Min(0.001f)] private float wallProbeDistance = 0.04f;
        [Tooltip("Horizontal inset of the ground probe (u). Keep it tiny: the body is a box, so any wider\n" +
                 "inset leaves a strip at ledge edges where the body is supported but not 'grounded'.\n" +
                 "Walls are rejected by the normal test, not by the inset.")]
        [SerializeField, Min(0f)] private float groundProbeInset = 0.01f;
        [Tooltip("Surfaces whose normal.y is above this are ground; below, they are walls.")]
        [SerializeField, Range(0f, 1f)] private float minGroundNormalY = 0.7f;

        [Header("Attack")]
        [Tooltip("Total duration of the attack state (s).")]
        [SerializeField, Min(0f)] private float attackDuration = 0.28f;
        [Tooltip("Time into the attack when the hitbox turns on (s).")]
        [SerializeField, Min(0f)] private float hitboxStart = 0.08f;
        [Tooltip("Time into the attack when the hitbox turns off (s).")]
        [SerializeField, Min(0f)] private float hitboxEnd = 0.18f;
        [Tooltip("Trigger collider on a child object at the feet. Its offset and size are set per attack direction.")]
        [SerializeField] private BoxCollider2D attackHitbox;
        [Tooltip("Side slash: 14x12 px in front of the body. Mirrored when facing left.")]
        [SerializeField] private HitboxShape forwardHitbox = new HitboxShape(new Vector2(13f, 18f) / 16f, new Vector2(14f, 12f) / 16f);
        [Tooltip("Up slash (hold up + attack, on the ground or in the air): 16x14 px above the head.")]
        [SerializeField] private HitboxShape upHitbox = new HitboxShape(new Vector2(0f, 39f) / 16f, new Vector2(16f, 14f) / 16f);
        [Tooltip("Down slash (hold down + attack, only in the air): 16x14 px below the feet.")]
        [SerializeField] private HitboxShape downHitbox = new HitboxShape(new Vector2(0f, -7f) / 16f, new Vector2(16f, 14f) / 16f);

        [Header("Abilities")]
        [Tooltip("Master switch for double jump, forward slash and wall cling (F3 toggles it in play).")]
        [SerializeField] private bool abilitiesEnabled = true;
        [Tooltip("Extra jumps available in the air; refilled on the ground or a wall.")]
        [SerializeField, Min(0)] private int airJumps = 1;
        [Tooltip("Apex height of the double jump (u).")]
        [SerializeField, Min(0f)] private float doubleJumpHeight = 3f;
        [Tooltip("Distance covered by the forward slash (u).")]
        [SerializeField, Min(0f)] private float slashDistance = 3.5f;
        [Tooltip("Duration of the forward slash (s). Gravity is suspended meanwhile.")]
        [SerializeField, Min(0.01f)] private float slashDuration = 0.2f;
        [Tooltip("Time after a slash before the next one (s). One slash per airtime.")]
        [SerializeField, Min(0f)] private float slashCooldown = 0.45f;
        [Tooltip("Slash hitbox: 20x24 px in front of the body, active for the whole slash. Mirrored when facing left.")]
        [SerializeField] private HitboxShape slashHitbox = new HitboxShape(new Vector2(16f, 16f) / 16f, new Vector2(20f, 24f) / 16f);
        [Tooltip("Steady sliding speed while clinging to a wall (u/s).")]
        [SerializeField, Min(0f)] private float wallSlideSpeed = 3.5f;
        [Tooltip("Apex height of a jump off a wall (u).")]
        [SerializeField, Min(0f)] private float wallJumpHeight = 3f;
        [Tooltip("Time after a wall jump during which the kick away from the wall overrides horizontal input (s).\n" +
                 "Short, so holding toward the wall brings the body back to re-grab it higher (climbing).")]
        [SerializeField, Min(0f)] private float wallJumpLockTime = 0.1f;

        [Header("Visuals")]
        [Tooltip("Sprite flipped with flipX. Art faces right.")]
        [SerializeField] private SpriteRenderer spriteRenderer;

        [Header("Events")]
        [Tooltip("Raised whenever the state changes. Hook animations here.")]
        [SerializeField] private UnityEvent<State> onStateChanged = new UnityEvent<State>();
        [SerializeField] private UnityEvent onJumped = new UnityEvent();
        [SerializeField] private UnityEvent onLanded = new UnityEvent();
        [Tooltip("Raised with the direction of each new attack.")]
        [SerializeField] private UnityEvent<AttackDirection> onAttackStarted = new UnityEvent<AttackDirection>();
        [Tooltip("Raised with true when the hitbox turns on and false when it turns off.")]
        [SerializeField] private UnityEvent<bool> onAttackHitboxChanged = new UnityEvent<bool>();
        [SerializeField] private UnityEvent onAttackEnded = new UnityEvent();
        [SerializeField] private UnityEvent onDoubleJumped = new UnityEvent();
        [SerializeField] private UnityEvent onWallJumped = new UnityEvent();
        [SerializeField] private UnityEvent onSlashStarted = new UnityEvent();

        private Rigidbody2D body;
        private BoxCollider2D bodyCollider;
        private PlayerInputActions input;
        private ContactFilter2D groundFilter;
        private readonly RaycastHit2D[] hits = new RaycastHit2D[4];
        private readonly ContactPoint2D[] contacts = new ContactPoint2D[8];

        // Input sampled in Update, consumed in FixedUpdate.
        private float moveInput;
        private float verticalInput;
        private bool jumpHeld;
        private bool attackRequested;
        private bool slashRequested;

        // While locked (e.g. during a room transition) input is ignored and the body walks on its own.
        private bool controlLocked;
        private float lockedMove;

        private float coyoteTimer;
        private float jumpBufferTimer;
        private bool isJumping;
        private bool jumpCutApplied;
        private bool isAttacking;
        private float attackTimer;
        private float groundGap;
        private int facing = 1;
        private AttackDirection attackDirection;

        private int airJumpsLeft;
        private bool airSlashAvailable = true;
        private bool isSlashing;
        private int slashDirection;
        private float slashTimer;
        private float slashCooldownTimer;
        private bool isWallClinging;
        private int wallDirection;
        private int wallJumpDirection;
        private float wallJumpLockTimer;

        public State CurrentState { get; private set; } = State.Idle;
        public Vector2 Velocity => body != null ? body.linearVelocity : Vector2.zero;
        public bool IsGrounded { get; private set; }
        /// <summary>What detected the ground on the last physics step: probe, contact, both or none (debug).</summary>
        public string GroundSource { get; private set; } = "none";
        public float CoyoteTimeRemaining => coyoteTimer;
        public float JumpBufferRemaining => jumpBufferTimer;
        public int Facing => facing;
        public bool IsHitboxActive => attackHitbox != null && attackHitbox.enabled;
        public bool IsAttacking => isAttacking;
        public bool IsControlLocked => controlLocked;
        public AttackDirection CurrentAttackDirection => attackDirection;
        public bool IsSlashing => isSlashing;
        public bool IsWallClinging => isWallClinging;
        public int AirJumpsLeft => airJumpsLeft;

        public bool AbilitiesEnabled
        {
            get => abilitiesEnabled;
            set
            {
                abilitiesEnabled = value;
                if (!value)
                    StopWallCling();
            }
        }
        public BoxCollider2D AttackHitbox => attackHitbox;
        public BoxCollider2D BodyCollider => bodyCollider;
        public float GroundProbeDistance => groundProbeDistance;
        public UnityEvent<State> OnStateChanged => onStateChanged;
        public UnityEvent OnJumped => onJumped;
        public UnityEvent OnLanded => onLanded;
        public UnityEvent<AttackDirection> OnAttackStarted => onAttackStarted;
        public UnityEvent<bool> OnAttackHitboxChanged => onAttackHitboxChanged;
        public UnityEvent OnAttackEnded => onAttackEnded;
        public UnityEvent OnDoubleJumped => onDoubleJumped;
        public UnityEvent OnWallJumped => onWallJumped;
        public UnityEvent OnSlashStarted => onSlashStarted;

        /// <summary>
        /// Initial jump speed so that the apex reaches jumpHeight. The continuous answer is
        /// sqrt(2 g h); with a fixed step of dt the discrete apex is v²/2g + v·dt/2, so we
        /// solve that instead to land exactly on jumpHeight.
        /// </summary>
        public float JumpVelocity => VelocityForHeight(jumpHeight);

        private float VelocityForHeight(float height)
        {
            var halfStep = gravity * Time.fixedDeltaTime * 0.5f;
            return Mathf.Sqrt(halfStep * halfStep + 2f * gravity * height) - halfStep;
        }

        private float SlashSpeed => slashDistance / slashDuration;

        private void Reset()
        {
            // Sensible defaults when the component is first added in the editor.
            groundMask = LayerMask.GetMask("Ground");
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

            var rb = GetComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            // 12x32 px box anchored to the feet (transform origin = feet).
            var box = GetComponent<BoxCollider2D>();
            box.size = new Vector2(0.75f, 2f);
            box.offset = new Vector2(0f, 1f);
        }

        private void OnValidate()
        {
            hitboxEnd = Mathf.Max(hitboxEnd, hitboxStart);
            attackDuration = Mathf.Max(attackDuration, hitboxEnd);
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            bodyCollider = GetComponent<BoxCollider2D>();
            body.gravityScale = 0f;

            // Zero friction keeps the body from sticking to walls while pushing into them.
            if (bodyCollider.sharedMaterial == null)
                bodyCollider.sharedMaterial = new PhysicsMaterial2D("PlayerNoFriction") { friction = 0f, bounciness = 0f };

            groundFilter = new ContactFilter2D { useTriggers = false };
            groundFilter.SetLayerMask(groundMask);

            if (attackHitbox != null)
            {
                attackHitbox.isTrigger = true;
                attackHitbox.enabled = false;
            }

            input = new PlayerInputActions();
        }

        private void OnEnable() => input.Gameplay.Enable();

        private void OnDisable() => input.Gameplay.Disable();

        private void OnDestroy() => input?.Dispose();

        private void Update()
        {
            if (controlLocked)
            {
                moveInput = lockedMove;
                verticalInput = 0f;
                jumpHeld = false;
                return;
            }

            var move = input.Gameplay.Move.ReadValue<Vector2>();
            moveInput = Mathf.Abs(move.x) >= moveDeadZone ? Mathf.Sign(move.x) : 0f;
            verticalInput = Mathf.Abs(move.y) >= moveDeadZone ? Mathf.Sign(move.y) : 0f;

            if (input.Gameplay.Jump.WasPressedThisFrame())
                jumpBufferTimer = jumpBuffer;
            jumpHeld = input.Gameplay.Jump.IsPressed();

            if (input.Gameplay.Attack.WasPressedThisFrame())
                attackRequested = true;
            if (input.Gameplay.Slash.WasPressedThisFrame())
                slashRequested = true;
        }

        private void FixedUpdate()
        {
            var dt = Time.fixedDeltaTime;
            var verticalVelocity = body.linearVelocity.y;

            UpdateGrounded(verticalVelocity);
            UpdateWallCling(verticalVelocity);
            UpdateSlash(dt);
            UpdateAttack(dt);
            // Read after the updates above: a slash that just ended sets its exit speed on the body.
            var velocity = body.linearVelocity;
            var jumped = TryJump(ref velocity, dt);
            velocity.x = ComputeHorizontalVelocity(velocity.x, dt);
            if (!jumped)
                velocity.y = ComputeVerticalVelocity(velocity.y, dt);
            body.linearVelocity = velocity;

            UpdateFacing();
            SetState(EvaluateState(velocity));
        }

        /// <summary>
        /// Takes input away from the player. The character keeps walking in autoMove
        /// (-1 left, 0 stand, 1 right); jumps and attacks are ignored until UnlockControl.
        /// </summary>
        public void LockControl(float autoMove)
        {
            controlLocked = true;
            lockedMove = Mathf.Clamp(Mathf.Round(autoMove), -1f, 1f);
            jumpBufferTimer = 0f;
            attackRequested = false;
            slashRequested = false;
        }

        public void UnlockControl() => controlLocked = false;

        /// <summary>Turns the character to face left (-1) or right (1) without moving it.</summary>
        public void Face(int direction)
        {
            if (direction == 0)
                return;
            facing = direction > 0 ? 1 : -1;
            if (spriteRenderer != null)
                spriteRenderer.flipX = facing < 0;
        }

        /// <summary>Teleports the player and clears all motion and timers.</summary>
        public void Respawn(Vector2 position)
        {
            EndAttack();
            EndSlash();
            StopWallCling();
            wallJumpLockTimer = 0f;
            slashCooldownTimer = 0f;
            body.position = position;
            transform.position = position;
            body.linearVelocity = Vector2.zero;
            coyoteTimer = 0f;
            jumpBufferTimer = 0f;
            isJumping = false;
            attackRequested = false;
            slashRequested = false;
            IsGrounded = false;
            SetState(State.Fall);
        }

        private void UpdateGrounded(float verticalVelocity)
        {
            var wasGrounded = IsGrounded;

            // Past the apex the jump is over, wherever the body is. Without this, a landing
            // that left a tiny upward speed could keep 'rising' true and ignore the floor forever.
            if (isJumping && verticalVelocity <= 0f)
                isJumping = false;

            // While rising from a jump the probe may still touch the floor; ignore it.
            var risingFromJump = isJumping && verticalVelocity > RisingSpeed;

            // Two independent sources: the probe below the feet, and the body's actual physics
            // contacts with an upward normal. Resting on the ground always yields a contact,
            // so a probe miss (ledge corner, sub-pixel gap) can no longer leave the body 'airborne'.
            var byProbe = ProbeGround(out groundGap);
            var byContact = TouchingGround();
            if (!byProbe)
                groundGap = 0f;
            GroundSource = byProbe ? (byContact ? "probe+contact" : "probe") : (byContact ? "contact" : "none");
            IsGrounded = !risingFromJump && (byProbe || byContact);

            if (IsGrounded)
            {
                coyoteTimer = coyoteTime;
                isJumping = false;
                RefillAirMoves();
                if (!wasGrounded)
                    onLanded.Invoke();
            }
            else
            {
                coyoteTimer = Mathf.Max(0f, coyoteTimer - Time.fixedDeltaTime);
            }
        }

        private float ComputeHorizontalVelocity(float vx, float dt)
        {
            if (isSlashing)
                return slashDirection * SlashSpeed;

            // A slight push into the wall keeps the contact alive while sliding.
            if (isWallClinging)
                return wallDirection * 0.5f;

            // Right after a wall jump the kick away from the wall wins over the stick. When it
            // ends the sideways speed is dropped, so the stick takes over from rest.
            if (wallJumpLockTimer > 0f)
            {
                wallJumpLockTimer -= dt;
                return wallJumpLockTimer > 0f ? wallJumpDirection * moveSpeed : 0f;
            }

            // Grounded attacks root the character in place.
            if (isAttacking && IsGrounded)
                return 0f;

            var target = moveInput * moveSpeed;
            if (target != 0f && IsTouchingWall((int)Mathf.Sign(target)))
                target = 0f;

            var speedingUp = target != 0f && (Mathf.Abs(vx) < VelocityEpsilon || Mathf.Sign(target) == Mathf.Sign(vx));
            var rate = speedingUp ? groundAcceleration : groundDeceleration;
            if (!IsGrounded)
                rate *= airControl;

            return Mathf.MoveTowards(vx, target, rate * dt);
        }

        /// <summary>
        /// Consumes a buffered jump press, in priority order: ground (or coyote) jump,
        /// wall jump while clinging, then double jump in the air. Returns true if one fired.
        /// </summary>
        private bool TryJump(ref Vector2 velocity, float dt)
        {
            if (jumpBufferTimer <= 0f || isSlashing)
            {
                jumpBufferTimer = Mathf.Max(0f, jumpBufferTimer - dt);
                return false;
            }

            if (coyoteTimer > 0f && !(isAttacking && IsGrounded))
            {
                velocity.y = StartJump(JumpVelocity);
                coyoteTimer = 0f;
                IsGrounded = false;
                onJumped.Invoke();
                return true;
            }

            if (isWallClinging)
            {
                // Kick off away from the wall, facing away from it.
                wallJumpDirection = -wallDirection;
                StopWallCling();
                wallJumpLockTimer = wallJumpLockTime;
                velocity.x = wallJumpDirection * moveSpeed;
                velocity.y = StartJump(VelocityForHeight(wallJumpHeight));
                Face(wallJumpDirection);
                onWallJumped.Invoke();
                return true;
            }

            if (abilitiesEnabled && !IsGrounded && airJumpsLeft > 0)
            {
                airJumpsLeft--;
                velocity.y = StartJump(VelocityForHeight(doubleJumpHeight));
                onDoubleJumped.Invoke();
                return true;
            }

            // Nothing available yet: keep the press buffered (e.g. for the landing).
            jumpBufferTimer = Mathf.Max(0f, jumpBufferTimer - dt);
            return false;
        }

        private float StartJump(float speed)
        {
            jumpBufferTimer = 0f;
            isJumping = true;
            jumpCutApplied = false;
            return speed;
        }

        private float ComputeVerticalVelocity(float vy, float dt)
        {
            // The slash holds its height.
            if (isSlashing)
                return 0f;

            if (IsGrounded)
            {
                // Close the probe gap exactly so the body rests on the floor instead of hovering.
                return -Mathf.Max(0f, groundGap) / dt;
            }

            if (isWallClinging)
                return -wallSlideSpeed;

            if (isJumping && !jumpCutApplied && !jumpHeld && vy > 0f)
            {
                vy *= jumpCutMultiplier;
                jumpCutApplied = true;
            }

            vy -= gravity * dt;
            return Mathf.Max(vy, -maxFallSpeed);
        }

        private void RefillAirMoves()
        {
            airJumpsLeft = airJumps;
            airSlashAvailable = true;
        }

        /// <summary>
        /// Wall cling: in the air and falling, pushing into a wall starts a steady slide.
        /// It holds until the player pushes away, the wall ends, or the body lands.
        /// </summary>
        private void UpdateWallCling(float verticalVelocity)
        {
            if (!abilitiesEnabled || IsGrounded || isSlashing)
            {
                StopWallCling();
                return;
            }

            if (isWallClinging)
            {
                if (moveInput == -wallDirection || !IsTouchingWall(wallDirection))
                    StopWallCling();
                return;
            }

            if (verticalVelocity <= 0f && moveInput != 0f && IsTouchingWall((int)moveInput))
            {
                isWallClinging = true;
                wallDirection = (int)moveInput;
                isJumping = false;
                wallJumpLockTimer = 0f;
                RefillAirMoves();
                if (isAttacking)
                    EndAttack();
            }
        }

        private void StopWallCling() => isWallClinging = false;

        /// <summary>
        /// Forward slash: a short burst at constant speed with gravity suspended and the
        /// hitbox active throughout. From a wall it goes away from the wall.
        /// </summary>
        private void UpdateSlash(float dt)
        {
            slashCooldownTimer = Mathf.Max(0f, slashCooldownTimer - dt);

            var canSlash = abilitiesEnabled && !isSlashing && !isAttacking && slashCooldownTimer <= 0f
                && (IsGrounded || isWallClinging || airSlashAvailable);
            if (slashRequested && canSlash)
            {
                slashDirection = isWallClinging ? -wallDirection : facing;
                if (!IsGrounded && !isWallClinging)
                    airSlashAvailable = false;
                StopWallCling();
                wallJumpLockTimer = 0f;
                isJumping = false;
                Face(slashDirection);

                isSlashing = true;
                slashTimer = 0f;
                if (attackHitbox != null)
                {
                    attackHitbox.offset = new Vector2(slashHitbox.offset.x * slashDirection, slashHitbox.offset.y);
                    attackHitbox.size = slashHitbox.size;
                }
                SetHitboxActive(true);
                onSlashStarted.Invoke();
            }
            slashRequested = false;

            if (!isSlashing)
                return;
            slashTimer += dt;
            if (slashTimer >= slashDuration)
                EndSlash();
        }

        private void EndSlash()
        {
            if (!isSlashing)
                return;
            isSlashing = false;
            SetHitboxActive(false);
            slashCooldownTimer = slashCooldown;
            // Leave the burst at running speed instead of drifting at slash speed.
            body.linearVelocity = new Vector2(slashDirection * moveSpeed, 0f);
        }

        private void UpdateAttack(float dt)
        {
            // No regular attacks during a slash or while clinging.
            if (isSlashing || isWallClinging)
                attackRequested = false;

            if (attackRequested && !isAttacking)
            {
                isAttacking = true;
                attackTimer = 0f;
                attackDirection = ChooseAttackDirection();
                PlaceHitbox();
                onAttackStarted.Invoke(attackDirection);
            }
            attackRequested = false;

            if (!isAttacking)
                return;

            attackTimer += dt;
            SetHitboxActive(attackTimer >= hitboxStart && attackTimer < hitboxEnd);
            if (attackTimer >= attackDuration)
                EndAttack();
        }

        /// <summary>Up wins over sideways input; down only counts in the air.</summary>
        private AttackDirection ChooseAttackDirection()
        {
            if (verticalInput > 0f)
                return AttackDirection.Up;
            if (verticalInput < 0f && !IsGrounded)
                return AttackDirection.Down;
            return AttackDirection.Forward;
        }

        private void PlaceHitbox()
        {
            if (attackHitbox == null)
                return;

            var shape = attackDirection switch
            {
                AttackDirection.Up => upHitbox,
                AttackDirection.Down => downHitbox,
                _ => forwardHitbox,
            };
            var offset = shape.offset;
            if (attackDirection == AttackDirection.Forward)
                offset.x *= facing;
            attackHitbox.offset = offset;
            attackHitbox.size = shape.size;
        }

        private void EndAttack()
        {
            if (!isAttacking)
                return;
            isAttacking = false;
            SetHitboxActive(false);
            onAttackEnded.Invoke();
        }

        private void SetHitboxActive(bool active)
        {
            if (attackHitbox == null || attackHitbox.enabled == active)
                return;
            attackHitbox.enabled = active;
            onAttackHitboxChanged.Invoke(active);
        }

        private void UpdateFacing()
        {
            // While clinging the character faces away from the wall.
            if (isWallClinging)
            {
                Face(-wallDirection);
                return;
            }

            // Facing is locked for the whole attack, slash, or wall-jump kick.
            if (isAttacking || isSlashing || wallJumpLockTimer > 0f || moveInput == 0f)
                return;

            facing = moveInput > 0f ? 1 : -1;
            if (spriteRenderer != null)
                spriteRenderer.flipX = facing < 0;
        }

        private State EvaluateState(Vector2 velocity)
        {
            if (isSlashing)
                return State.Slash;
            if (isWallClinging)
                return State.WallSlide;
            if (isAttacking)
            {
                return attackDirection switch
                {
                    AttackDirection.Up => State.AttackUp,
                    AttackDirection.Down => State.AttackDown,
                    _ => State.Attack,
                };
            }
            if (IsGrounded)
                return Mathf.Abs(velocity.x) > VelocityEpsilon ? State.Run : State.Idle;
            return velocity.y > 0f ? State.Jump : State.Fall;
        }

        private void SetState(State next)
        {
            if (next == CurrentState)
                return;
            CurrentState = next;
            onStateChanged.Invoke(next);
        }

        /// <summary>
        /// Casts a box as wide as the body (minus a tiny inset) down from just above the feet.
        /// Wall faces touched by its sides are rejected by the normal test.
        /// </summary>
        private bool ProbeGround(out float gap)
        {
            GetGroundProbe(out var origin, out var size, out var skin);
            var count = Physics2D.BoxCast(origin, size, 0f, Vector2.down, groundFilter, hits, groundProbeDistance + skin);
            gap = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                if (hits[i].normal.y < minGroundNormalY)
                    continue;
                gap = Mathf.Min(gap, hits[i].distance - skin);
            }
            return gap != float.MaxValue;
        }

        /// <summary>True when the body has a physics contact with ground whose normal points up.</summary>
        private bool TouchingGround()
        {
            if (bodyCollider == null)
                return false;
            var count = bodyCollider.GetContacts(groundFilter, contacts);
            for (var i = 0; i < count; i++)
            {
                if (contacts[i].normal.y >= minGroundNormalY)
                    return true;
            }
            return false;
        }

        /// <summary>Casts a thin box sideways from the body's flank; only near-vertical faces count.</summary>
        private bool IsTouchingWall(int direction)
        {
            var bounds = GetBodyBounds();
            var size = new Vector2(0.02f, bounds.size.y * 0.6f);
            var origin = new Vector2(bounds.center.x + direction * (bounds.extents.x - size.x), bounds.center.y);
            var count = Physics2D.BoxCast(origin, size, 0f, Vector2.right * direction, groundFilter, hits, wallProbeDistance + size.x);
            for (var i = 0; i < count; i++)
            {
                if (Mathf.Abs(hits[i].normal.x) > minGroundNormalY)
                    return true;
            }
            return false;
        }

        /// <summary>Ground probe geometry, shared with debug gizmos.</summary>
        public void GetGroundProbe(out Vector2 origin, out Vector2 size, out float skin)
        {
            var bounds = GetBodyBounds();
            skin = 0.05f;
            size = new Vector2(Mathf.Max(0.01f, bounds.size.x - 2f * groundProbeInset), 0.02f);
            origin = new Vector2(bounds.center.x, bounds.min.y + skin + size.y * 0.5f);
        }

        /// <summary>Body bounds from collider settings, independent of the physics broadphase.</summary>
        private Bounds GetBodyBounds()
        {
            var col = bodyCollider != null ? bodyCollider : GetComponent<BoxCollider2D>();
            var position = body != null ? body.position : (Vector2)transform.position;
            return new Bounds(position + col.offset, col.size);
        }
    }
}
