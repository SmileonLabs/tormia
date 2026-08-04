using ithappy.Creative_Characters_FREE.Controller;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Tormia.Ontology.Core
{
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(CharacterController))]
    public sealed class OntologyInputSystemPlayerInput : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private OntologyObject actorObject;
        [SerializeField, Tooltip("Optional id override. Leave empty to use this object's OntologyObject id.")]
        private string actorIdOverride;
        [SerializeField] private PlayerCamera playerCamera;
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private Animator visualAnimator;

        [Header("Input Bindings")]
        [SerializeField] private string moveUpBinding = "<Keyboard>/w";
        [SerializeField] private string moveDownBinding = "<Keyboard>/s";
        [SerializeField] private string moveLeftBinding = "<Keyboard>/a";
        [SerializeField] private string moveRightBinding = "<Keyboard>/d";
        [SerializeField] private string moveUpAltBinding = "<Keyboard>/upArrow";
        [SerializeField] private string moveDownAltBinding = "<Keyboard>/downArrow";
        [SerializeField] private string moveLeftAltBinding = "<Keyboard>/leftArrow";
        [SerializeField] private string moveRightAltBinding = "<Keyboard>/rightArrow";
        [SerializeField] private string runBinding = "<Keyboard>/leftShift";
        [SerializeField] private string runAltBinding = "<Keyboard>/rightShift";
        [SerializeField] private string jumpBinding = "<Keyboard>/space";
        [SerializeField] private string lookBinding = "<Mouse>/delta";
        [SerializeField] private string lookHoldBinding = "<Mouse>/rightButton";
        [SerializeField] private string scrollBinding = "<Mouse>/scroll";
        [SerializeField] private string clickBinding = "<Mouse>/leftButton";
        [SerializeField] private string pointerPositionBinding = "<Pointer>/position";

        [Header("Movement")]
        [SerializeField, Tooltip(
            "Unity collision presentation runs only after the complete " +
            "Authority locomotion contract has been approved.")]
        private bool authorityApprovedCharacterControllerAdapter = true;
        [SerializeField] private float slowedSpeedMultiplier = 0.5f;
        [SerializeField] private bool clickToMove = true;
        [SerializeField] private float clickStopDistance = 0.1f;
        [SerializeField, Min(0f), Tooltip(
            "Additional planar tolerance used to finish click navigation before " +
            "sub-controller movement leaves the avatar walking in place.")]
        private float clickArrivalTolerance = 0.01f;
        [SerializeField] private float clickRaycastDistance = 500f;
        [SerializeField] private float moveTargetDistance = 10f;
        [SerializeField] private float fallbackTurnSpeed = 360f;
        [SerializeField] private float mouseLookScale = 0.02f;
        [SerializeField] private float mouseScrollScale = 0.01f;
        [SerializeField, Min(0f), Tooltip(
            "Navigation-only inset from the authored attack_range. This keeps " +
            "the avatar safely inside the Authority boundary without changing " +
            "the gameplay rule.")]
        private float combatApproachBuffer = 0.15f;
        [SerializeField, Min(0.05f), Tooltip(
            "Ephemeral retry interval while an accepted combat target is waiting " +
            "for an Authority cooldown or an in-flight route.")]
        private float combatIntentRetryInterval = 0.15f;
        [SerializeField, Range(0f, 45f), Tooltip(
            "Presentation-only angle that must be reached before publishing the " +
            "targeted attack intent. It does not change Authority eligibility.")]
        private float combatFacingTolerance = 3f;

        [Header("Animator Parameters")]
        [SerializeField] private string horizontalParameter = "Hor";
        [SerializeField] private string verticalParameter = "Vert";
        [SerializeField] private string stateParameter = "State";
        [SerializeField] private float animatorDampTime = 0.08f;

        [Header("Debug")]
        [SerializeField, Tooltip(
            "Opt-in development logs for jump input submission and Authority " +
            "approval. Disabled by default.")]
        private bool enableJumpDiagnostics;
        [SerializeField] private Vector2 lastMoveAxis;
        [SerializeField] private Vector3 lastMoveTarget;
        [SerializeField] private bool lastKeyboardDetected;
        [SerializeField] private bool lastHadInput;
        [SerializeField] private bool lastPresentationHadInput;
        [SerializeField] private bool lastRunIntent;
        [SerializeField] private bool animatorPresentationOwnedByResolver;
        [SerializeField] private bool lastGroundedObservation;
        [SerializeField] private CollisionFlags lastCollisionFlags;
        [SerializeField] private float lastRequestedVerticalDisplacement;
        [SerializeField] private float lastResolvedVerticalDisplacement;
        [SerializeField] private Vector3 externalImpulseVelocity;
        [SerializeField] private bool physicsReactionActive;
        [SerializeField] private bool hasClickTarget;
        [SerializeField] private Vector3 clickTarget;

        private CharacterController characterController;
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction lookHoldAction;
        private InputAction scrollAction;
        private InputAction runAction;
        private InputAction jumpAction;
        private InputAction clickAction;
        private InputAction pointerPositionAction;
        private bool ownsInputActions;
        private float verticalVelocity;
        private uint jumpOccurrence;
        private float externalImpulseDamping;
        private OntologyCharacterMotionCoordinator motionCoordinator;
        private OntologySwimmingMovementAdapter swimmingMovement;
        private OntologyDrowningRecoveryAdapter drowningRecovery;
        private OntologyWorldAuthorityPlayerIntentSender authorityIntentSender;
        private OntologyCombatController combatController;
        private OntologyObject selectedInteractionObject;
        private OntologyAuthorityEntityIdentity selectedCombatTarget;
        private Vector3 selectedCombatHitPoint;
        private Vector2 selectedCombatPointerPosition;
        private bool combatIntentRequestPending;
        private uint combatIntentRevision;
        private float nextCombatIntentRetryAt;
        private string publishedInteractionIntent;
        private OntologyRuntimeSelectionMarker interactionSelectionMarker;
        private string ResolvedActorId =>
            !string.IsNullOrWhiteSpace(actorIdOverride)
                ? actorIdOverride.Trim()
                : actorObject != null
                    ? actorObject.EntityId
                    : string.Empty;

        /// <summary>
        /// The latest player-controlled horizontal direction in world space.
        /// This is transport-neutral: networking may forward it as an intent,
        /// but it never becomes an ontology Fact by itself.
        /// </summary>
        public bool TryGetWorldMoveIntent(out Vector2 worldDirection)
        {
            return TryGetWorldMoveIntent(out worldDirection, out _);
        }

        /// <summary>
        /// Reports world-space direction plus the locally requested locomotion
        /// speed. The Authority treats this speed as transient input and clamps
        /// it to the avatar's authored movement_speed Fact.
        /// </summary>
        public bool TryGetWorldMoveIntent(
            out Vector2 worldDirection,
            out float requestedSpeed)
        {
            return TryGetWorldMoveIntent(
                out worldDirection,
                out requestedSpeed,
                out _,
                out _,
                out _);
        }

        public bool TryGetWorldMoveIntent(
            out Vector2 worldDirection,
            out float requestedSpeed,
            out bool hasDestination,
            out Vector2 destination,
            out float destinationStopDistance)
        {
            worldDirection = Vector2.zero;
            requestedSpeed = 0f;
            hasDestination = false;
            destination = Vector2.zero;
            destinationStopDistance = 0f;
            var retainGroundDestinationForAuthority =
                hasClickTarget &&
                selectedCombatTarget == null &&
                selectedInteractionObject == null &&
                HasReachedClickDestination(
                    GetActiveClickTargetPlanarDistance(),
                    GetActiveClickStopDistance(),
                    GetCanonicalArrivalTolerance());
            if (!lastHadInput &&
                !retainGroundDestinationForAuthority)
            {
                return false;
            }

            var move = hasClickTarget
                ? GetClickMoveDirection()
                : GetCameraRelativeMove(lastMoveAxis);
            if (move.sqrMagnitude <= 0.0001f &&
                !retainGroundDestinationForAuthority)
            {
                return false;
            }

            if (move.sqrMagnitude > 0.0001f)
            {
                move.Normalize();
            }
            worldDirection = new Vector2(move.x, move.z);
            if (authorityIntentSender == null ||
                !authorityIntentSender.TryResolveLocomotionSpeed(
                    lastRunIntent,
                    out requestedSpeed))
            {
                return false;
            }

            requestedSpeed *= GetOntologySpeedMultiplier();
            if (hasClickTarget)
            {
                var activeDestination = GetActiveClickTarget();
                hasDestination = true;
                destination = new Vector2(
                    activeDestination.x,
                    activeDestination.z);
                destinationStopDistance =
                    GetActiveClickStopDistance();
            }
            return requestedSpeed > 0f;
        }

        public bool IsMovingIntent => lastPresentationHadInput;
        public bool IsRunIntent =>
            lastRunIntent && lastPresentationHadInput;

        public bool TryCompleteAuthorityGroundDestination(
            Vector2 authorityPosition)
        {
            if (!hasClickTarget ||
                selectedCombatTarget != null ||
                selectedInteractionObject != null)
            {
                return false;
            }

            var target = GetActiveClickTarget();
            var remaining = Vector2.Distance(
                authorityPosition,
                new Vector2(target.x, target.z));
            if (!HasReachedClickDestination(
                    remaining,
                    GetActiveClickStopDistance(),
                    GetCanonicalArrivalTolerance()))
            {
                return false;
            }

            hasClickTarget = false;
            return true;
        }
        public bool IsGrounded =>
            characterController != null &&
            characterController.enabled &&
            !IsSwimmingPresentationActive &&
            verticalVelocity <= 0f &&
            (motionCoordinator != null
                ? motionCoordinator.HasGroundContact
                : lastGroundedObservation ||
                  characterController.isGrounded);
        public float VerticalVelocity => verticalVelocity;
        public uint JumpOccurrence => jumpOccurrence;
        public CollisionFlags LastCollisionFlags => lastCollisionFlags;
        public float LastRequestedVerticalDisplacement =>
            lastRequestedVerticalDisplacement;
        public float LastResolvedVerticalDisplacement =>
            lastResolvedVerticalDisplacement;
        public bool PhysicsReactionActive => physicsReactionActive;
        public Vector2 CurrentMoveAxis => lastMoveAxis;
        public string SelectedInteractionEntityId =>
            selectedInteractionObject == null
                ? string.Empty
                : selectedInteractionObject.EntityId;
        public OntologyObject SelectedInteractionTarget =>
            selectedInteractionObject;
        public bool IsSwimmingPresentationActive
        {
            get
            {
                swimmingMovement ??=
                    GetComponent<OntologySwimmingMovementAdapter>();
                return swimmingMovement != null &&
                       swimmingMovement.IsSwimmingPresentationActive;
            }
        }

        /// <summary>
        /// Called after the entry grounding adapter has resolved the restored
        /// capsule against Unity collision. This clears only ephemeral vertical
        /// motion; it does not alter the durable checkpoint or movement Facts.
        /// </summary>
        public void ResetVerticalMotionAfterGrounding()
        {
            verticalVelocity = 0f;
            motionCoordinator?.RefreshSupport();
            lastGroundedObservation =
                characterController != null &&
                characterController.enabled &&
                ((motionCoordinator != null &&
                  motionCoordinator.IsGrounded) ||
                 characterController.isGrounded);
        }

        /// <summary>
        /// Consumes an Authority-approved jump result. A delayed approval is
        /// discarded if the capsule no longer has support, preventing a stale
        /// network response from creating an air jump.
        /// </summary>
        public bool ApplyApprovedJump(float takeoffSpeed)
        {
            if (characterController == null ||
                !characterController.enabled ||
                !float.IsFinite(takeoffSpeed) ||
                takeoffSpeed <= 0f ||
                !IsGrounded)
            {
                return false;
            }

            verticalVelocity = takeoffSpeed;
            lastGroundedObservation = false;
            unchecked
            {
                jumpOccurrence++;
            }
            if (enableJumpDiagnostics)
            {
                Debug.LogWarning(
                    "[PlayerJumpTrace] stage=approved frame=" +
                    Time.frameCount +
                    " takeoffSpeed=" + takeoffSpeed.ToString("F4") +
                    " jumpOccurrence=" + jumpOccurrence,
                    this);
            }
            return true;
        }

        public bool ApplyApprovedExternalImpulse(
            Vector3 deltaVelocity,
            float damping)
        {
            if (physicsReactionActive ||
                !IsFinite(deltaVelocity) ||
                !float.IsFinite(damping) ||
                damping < 0f)
            {
                return false;
            }

            externalImpulseVelocity += deltaVelocity;
            externalImpulseDamping = damping;
            return true;
        }

        public void SetPhysicsReactionActive(bool value)
        {
            physicsReactionActive = value;
            externalImpulseVelocity = Vector3.zero;
            externalImpulseDamping = 0f;
            if (!value)
            {
                ResetVerticalMotionAfterGrounding();
            }
        }

        /// <summary>
        /// Clears all ephemeral observations that belong to the previous
        /// runtime activation. The entry coordinator invokes this while input
        /// is gated, after checkpoint collision preparation and before the
        /// session becomes InWorld.
        /// </summary>
        public void PrepareForRuntimeActivation()
        {
            verticalVelocity = 0f;
            lastGroundedObservation = false;
            lastCollisionFlags = CollisionFlags.None;
            lastRequestedVerticalDisplacement = 0f;
            lastResolvedVerticalDisplacement = 0f;
            externalImpulseVelocity = Vector3.zero;
            externalImpulseDamping = 0f;
            physicsReactionActive = false;
            lastMoveAxis = Vector2.zero;
            lastMoveTarget = transform.position;
            lastKeyboardDetected = false;
            lastHadInput = false;
            lastPresentationHadInput = false;
            lastRunIntent = false;
            hasClickTarget = false;
            clickTarget = transform.position;
            ClearCombatTargetIntent();
            ClearInteractionSelection(removeIntent: false);
            UpdateAnimator(Vector2.zero, false);
        }

        private void Awake()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            characterController = GetComponent<CharacterController>();
            motionCoordinator =
                GetComponent<OntologyCharacterMotionCoordinator>() ??
                gameObject.AddComponent<
                    OntologyCharacterMotionCoordinator>();
            lastGroundedObservation =
                motionCoordinator.IsGrounded;
            swimmingMovement = GetComponent<OntologySwimmingMovementAdapter>();
            drowningRecovery = GetComponent<OntologyDrowningRecoveryAdapter>();
            authorityIntentSender =
                GetComponent<OntologyWorldAuthorityPlayerIntentSender>() ??
                FindAnyObjectByType<
                    OntologyWorldAuthorityPlayerIntentSender>();
            combatController = GetComponent<OntologyCombatController>();
            if (visualAnimator == null)
            {
                visualAnimator = FindBestVisualAnimator();
            }

            if (playerCamera == null && Camera.main != null)
            {
                playerCamera = Camera.main.GetComponent<PlayerCamera>();
            }

            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }

            if (playerCamera != null)
            {
                playerCamera.SetPlayer(transform);
            }

            CreateInputActions();
        }

        private void OnEnable()
        {
            EnableInputActions();
        }

        private void OnDisable()
        {
            ClearInteractionSelection(removeIntent: true);
            ClearCombatTargetIntent();
            DisableInputActions();
        }

        private void OnDestroy()
        {
            if (!ownsInputActions) return;
            moveAction?.Dispose();
            lookAction?.Dispose();
            lookHoldAction?.Dispose();
            scrollAction?.Dispose();
            runAction?.Dispose();
            jumpAction?.Dispose();
            clickAction?.Dispose();
            pointerPositionAction?.Dispose();
        }

        private void Update()
        {
            EnsureCameraReferences();
            if (physicsReactionActive)
            {
                lastMoveAxis = Vector2.zero;
                lastHadInput = false;
                lastPresentationHadInput = false;
                UpdateAnimator(Vector2.zero, false);
                return;
            }
            if (TryUpdateDrowningRecovery())
            {
                return;
            }
            if (OntologyRuntimeObjectPlacementController.IsPlacementInputCaptured ||
                OntologyRuntimeWorldEditorController.IsEditInputCaptured ||
                OntologyUIPointerUtility.IsPointerOverBlockingUi())
            {
                StopPlayerForUi();
                return;
            }

            if (actorObject == null)
            {
                actorObject = GetComponent<OntologyObject>();
            }

            UpdateInteractionCompletion();
            var axis = ReadMoveAxis();
            UpdateClickTarget();
            CancelClickNavigationForDirectInput(axis);
            UpdateCombatTargetIntent();
            var useClickTarget = axis.sqrMagnitude <= 0.0001f && hasClickTarget;
            if (useClickTarget)
            {
                axis = GetClickMoveAxis();
            }

            var target = GetMoveTarget();
            var isRun = ReadRun();
            var jumpPressed = ReadJumpPressed();
            if (jumpPressed)
            {
                var groundedAtInput = IsGrounded;
                var submitted =
                    authorityIntentSender != null &&
                    authorityIntentSender.CanPresentPredictedLocomotion &&
                    authorityIntentSender.TryRequestJump(
                        groundedAtInput);
                if (enableJumpDiagnostics)
                {
                    Debug.LogWarning(
                        "[PlayerJumpTrace] stage=input frame=" +
                        Time.frameCount +
                        " grounded=" + groundedAtInput +
                        " submitted=" + submitted +
                        " binding=" + jumpBinding,
                        this);
                }
            }

            lastMoveAxis = axis;
            lastMoveTarget = target;
            lastKeyboardDetected = Keyboard.current != null;
            lastHadInput = axis.sqrMagnitude > 0.0001f;
            lastRunIntent = isRun && lastHadInput;
            var canPresentLocomotion =
                authorityIntentSender != null &&
                authorityIntentSender.CanPresentPredictedLocomotion;
            var presentationAxis = canPresentLocomotion
                ? axis
                : Vector2.zero;
            lastPresentationHadInput =
                presentationAxis.sqrMagnitude > 0.0001f;

            if (authorityApprovedCharacterControllerAdapter)
            {
                MoveCharacterController(
                    presentationAxis,
                    isRun && canPresentLocomotion,
                    useClickTarget && canPresentLocomotion);
            }

            if (!animatorPresentationOwnedByResolver)
                UpdateAnimator(
                    presentationAxis,
                    isRun && canPresentLocomotion);

            if (playerCamera != null)
            {
                playerCamera.SetInput(ReadMouseDelta(), ReadMouseScroll());
            }
        }

        private void StopPlayerForUi()
        {
            ClearCombatTargetIntent();
            hasClickTarget = false;
            lastMoveAxis = Vector2.zero;
            lastHadInput = false;
            lastPresentationHadInput = false;

            if (authorityApprovedCharacterControllerAdapter)
                MoveCharacterController(Vector2.zero, false, false);

            UpdateAnimator(Vector2.zero, false);
            if (playerCamera != null)
                playerCamera.SetInput(Vector2.zero, 0f);
        }

        private Animator FindBestVisualAnimator()
        {
            foreach (var candidate in GetComponentsInChildren<Animator>(true))
            {
                if (candidate != null && candidate.avatar != null && candidate.isHuman)
                {
                    return candidate;
                }
            }

            return null;
        }

        private void UpdateAnimator(Vector2 axis, bool isRun)
        {
            if (visualAnimator == null)
            {
                return;
            }

            var state = axis.sqrMagnitude > 0.0001f && isRun ? 1f : 0f;
            SetFloatIfExists(horizontalParameter, axis.x);
            SetFloatIfExists(verticalParameter, axis.y);
            SetFloatIfExists(stateParameter, state);
        }

        public void SetAnimatorPresentationOwnership(bool ownedByResolver)
        {
            animatorPresentationOwnedByResolver = ownedByResolver;
            if (!ownedByResolver) return;
            UpdateAnimator(Vector2.zero, false);
        }

        private void SetFloatIfExists(string parameter, float value)
        {
            if (string.IsNullOrWhiteSpace(parameter) || !HasAnimatorParameter(parameter, AnimatorControllerParameterType.Float))
            {
                return;
            }

            visualAnimator.SetFloat(parameter, value, Mathf.Max(0f, animatorDampTime), Time.deltaTime);
        }

        private bool HasAnimatorParameter(string parameter, AnimatorControllerParameterType type)
        {
            foreach (var animatorParameter in visualAnimator.parameters)
            {
                if (animatorParameter.name == parameter && animatorParameter.type == type)
                {
                    return true;
                }
            }

            return false;
        }

        private void CreateInputActions()
        {
            var projectActions = InputSystem.actions;
            moveAction = CloneProjectAction(projectActions, "Player/Move");
            lookAction = CloneProjectAction(projectActions, "Player/Look");
            scrollAction = CloneProjectAction(projectActions, "UI/ScrollWheel");
            runAction = CloneProjectAction(projectActions, "Player/Sprint");
            jumpAction = CloneProjectAction(projectActions, "Player/Jump");
            clickAction = CloneProjectAction(projectActions, "UI/Click");
            pointerPositionAction = CloneProjectAction(projectActions, "UI/Point");
            lookHoldAction = new InputAction(
                "OntologyMouseLookHold",
                InputActionType.Button,
                lookHoldBinding);
            ownsInputActions = true;

            if (moveAction == null || lookAction == null ||
                runAction == null || jumpAction == null ||
                clickAction == null || pointerPositionAction == null)
            {
                Debug.LogError(
                    "[OntologyInput] Project-wide gameplay actions are " +
                    "incomplete. Input fails closed.",
                    this);
            }
        }

        private static InputAction CloneProjectAction(
            InputActionAsset asset,
            string path)
        {
            return asset == null
                ? null
                : asset.FindAction(path, false)?.Clone();
        }

        private void EnableInputActions()
        {
            if (moveAction == null)
            {
                CreateInputActions();
            }

            moveAction?.Enable();
            lookAction?.Enable();
            lookHoldAction?.Enable();
            scrollAction?.Enable();
            runAction?.Enable();
            jumpAction?.Enable();
            clickAction?.Enable();
            pointerPositionAction?.Enable();
        }

        private void DisableInputActions()
        {
            if (moveAction == null)
            {
                return;
            }

            moveAction.Disable();
            lookAction?.Disable();
            lookHoldAction?.Disable();
            scrollAction?.Disable();
            runAction?.Disable();
            jumpAction?.Disable();
            clickAction?.Disable();
            pointerPositionAction?.Disable();
        }

        private void EnsureCameraReferences()
        {
            var mainCamera = Camera.main;
            if (mainCamera == null)
            {
                return;
            }

            if (cameraTransform == null)
            {
                cameraTransform = mainCamera.transform;
            }

            if (playerCamera == null)
            {
                playerCamera = mainCamera.GetComponent<PlayerCamera>();
                if (playerCamera != null)
                {
                    playerCamera.SetPlayer(transform);
                }
            }
        }

        private Vector3 GetMoveTarget()
        {
            var forward = transform.forward;
            if (cameraTransform != null)
            {
                forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
                if (forward.sqrMagnitude < 0.0001f)
                {
                    forward = transform.forward;
                }
            }

            return transform.position + forward.normalized * Mathf.Max(0.1f, moveTargetDistance);
        }

        private void MoveCharacterController(
            Vector2 axis,
            bool isRun,
            bool useClickTarget)
        {
            if (characterController == null ||
                !characterController.enabled ||
                !characterController.gameObject.activeInHierarchy)
            {
                verticalVelocity = 0f;
                return;
            }

            var move = axis.sqrMagnitude <= 0.0001f
                ? Vector3.zero
                : (useClickTarget ? GetClickMoveDirection() : GetCameraRelativeMove(axis));
            var speed = 0f;
            var authoredSpeed = 0f;
            if (move.sqrMagnitude > 0.0001f &&
                (authorityIntentSender == null ||
                 !authorityIntentSender.TryResolveLocomotionSpeed(
                     isRun,
                     out authoredSpeed)))
            {
                move = Vector3.zero;
            }
            else if (move.sqrMagnitude > 0.0001f)
            {
                speed =
                    authoredSpeed * GetOntologySpeedMultiplier();
            }
            if (swimmingMovement != null &&
                swimmingMovement.TryResolveDisplacement(
                    move,
                    speed,
                    Time.deltaTime,
                    out var swimmingDisplacement))
            {
                verticalVelocity = 0f;
                var swimmingResult = motionCoordinator.Move(
                    Vector3.ProjectOnPlane(
                        swimmingDisplacement,
                        Vector3.up),
                    swimmingDisplacement.y,
                    Vector3.zero);
                ApplyMotionResult(swimmingResult);
                RotateTowardsMove(move);
                return;
            }

            UpdateFallbackGravity();
            var horizontalDistance = speed * Time.deltaTime;
            if (useClickTarget)
            {
                horizontalDistance = ClampClickTravelDistance(
                    horizontalDistance,
                    GetActiveClickTargetPlanarDistance(),
                    GetActiveClickStopDistance());
            }

            lastRequestedVerticalDisplacement =
                (verticalVelocity + externalImpulseVelocity.y) *
                Time.deltaTime;
            var motionResult = motionCoordinator.Move(
                move * horizontalDistance,
                verticalVelocity * Time.deltaTime,
                externalImpulseVelocity * Time.deltaTime);
            ApplyMotionResult(motionResult);
            verticalVelocity = ResolveVerticalVelocityAfterMove(
                motionResult.collisionFlags,
                verticalVelocity,
                ResolveGroundStickVelocity());
            externalImpulseVelocity = DampExternalImpulseVelocity(
                externalImpulseVelocity,
                externalImpulseDamping,
                Time.deltaTime);
            RotateTowardsMove(move);
        }

        /// <summary>
        /// Advances the ontology-selected drowning presentation before normal
        /// input and CharacterController availability are evaluated. Recovery
        /// intentionally disables the controller while it owns the Transform,
        /// so placing this call inside controller locomotion would deadlock the
        /// state machine on the following frame.
        /// </summary>
        private bool TryUpdateDrowningRecovery()
        {
            if (drowningRecovery == null ||
                !drowningRecovery.TryRecover(Time.deltaTime))
            {
                return false;
            }

            verticalVelocity = 0f;
            externalImpulseVelocity = Vector3.zero;
            externalImpulseDamping = 0f;
            lastMoveAxis = Vector2.zero;
            lastHadInput = false;
            lastPresentationHadInput = false;
            lastRunIntent = false;
            UpdateAnimator(Vector2.zero, false);
            return true;
        }

        private void ApplyMotionResult(
            OntologyCharacterMotionResult result)
        {
            lastRequestedVerticalDisplacement =
                result.requestedVerticalDisplacement;
            lastResolvedVerticalDisplacement =
                result.resolvedVerticalDisplacement;
            lastCollisionFlags = result.collisionFlags;
            lastGroundedObservation =
                (result.collisionFlags & CollisionFlags.Below) != 0;
        }

        /// <summary>
        /// Keeps a click-navigation frame from crossing its stopping radius.
        /// This prevents low-frame-rate overshoot from alternating the movement
        /// direction and looking like the actor was thrown away from an NPC.
        /// </summary>
        public static float ClampClickTravelDistance(
            float requestedDistance,
            float planarDistanceToTarget,
            float stopDistance)
        {
            return Mathf.Min(
                Mathf.Max(0f, requestedDistance),
                Mathf.Max(0f, planarDistanceToTarget - Mathf.Max(0f, stopDistance)));
        }

        private void RotateTowardsMove(Vector3 move)
        {
            if (move.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    Quaternion.LookRotation(move, Vector3.up),
                    Mathf.Max(0f, fallbackTurnSpeed) * Time.deltaTime);
            }
        }

        private Vector3 GetCameraRelativeMove(Vector2 axis)
        {
            var forward = cameraTransform == null ? transform.forward : Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            var right = cameraTransform == null ? transform.right : Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = transform.forward;
            }
            if (right.sqrMagnitude < 0.0001f)
            {
                right = transform.right;
            }

            return Vector3.ClampMagnitude(right * axis.x + forward * axis.y, 1f);
        }

        private Vector3 GetClickMoveDirection()
        {
            var toTarget = GetActiveClickTarget() - transform.position;
            toTarget.y = 0f;
            return toTarget.sqrMagnitude <= 0.0001f ? Vector3.zero : toTarget.normalized;
        }

        private float GetOntologySpeedMultiplier()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            if (bootstrap == null || bootstrap.World == null)
            {
                return 1f;
            }

            return bootstrap.World.HasFact(ResolvedActorId, "movement_state", "Slowed") ? Mathf.Max(0f, slowedSpeedMultiplier) : 1f;
        }

        private void UpdateClickTarget()
        {
            if (OntologyRuntimeObjectPlacementController.IsPlacementInputCaptured || OntologyRuntimeWorldEditorController.IsEditInputCaptured)
            {
                return;
            }

            if (!clickToMove || clickAction == null || !clickAction.WasPressedThisFrame())
            {
                return;
            }

            var camera = Camera.main;
            if (camera == null || pointerPositionAction == null)
            {
                return;
            }

            var pointerPosition = pointerPositionAction.ReadValue<Vector2>();
            if (OntologyUIPointerUtility.IsPointerOverUi(pointerPosition))
            {
                return;
            }

            var ray = camera.ScreenPointToRay(pointerPosition);
            if (TryUnequipClickedAttachment(
                    ray,
                    Mathf.Max(1f, clickRaycastDistance)))
            {
                ClearCombatTargetIntent();
                ClearInteractionSelection(removeIntent: true);
                hasClickTarget = false;
                return;
            }

            if (combatController != null &&
                combatController.TryResolvePrimaryPointerTarget(
                    pointerPosition,
                    out var combatTarget,
                    out var combatHitPoint))
            {
                BeginCombatTargetIntent(
                    combatTarget,
                    combatHitPoint,
                    pointerPosition);
                return;
            }

            ResolveNonCombatPointerClick(pointerPosition);
        }

        private void BeginCombatTargetIntent(
            OntologyAuthorityEntityIdentity target,
            Vector3 hitPoint,
            Vector2 pointerPosition)
        {
            ClearInteractionSelection(removeIntent: true);
            unchecked
            {
                combatIntentRevision++;
            }
            selectedCombatTarget = target;
            selectedCombatHitPoint = hitPoint;
            selectedCombatPointerPosition = pointerPosition;
            combatIntentRequestPending = false;
            nextCombatIntentRetryAt = Time.time;
            hasClickTarget = false;
            TryRequestCombatTargetIntent();
        }

        private void UpdateCombatTargetIntent()
        {
            if (selectedCombatTarget == null)
            {
                return;
            }

            if (combatController == null ||
                !combatController.IsProjectedLivingCombatTarget(
                    selectedCombatTarget))
            {
                ClearCombatTargetIntent();
                return;
            }

            clickTarget = selectedCombatTarget.transform.position;
            if (hasClickTarget)
            {
                var toTarget =
                    selectedCombatTarget.transform.position -
                    transform.position;
                toTarget.y = 0f;
                if (!HasReachedClickDestination(
                        toTarget.magnitude,
                        GetActiveClickStopDistance(),
                        GetClickArrivalTolerance()))
                {
                    return;
                }

                hasClickTarget = false;
            }

            if (!combatIntentRequestPending &&
                Time.time >= nextCombatIntentRetryAt)
            {
                TryRequestCombatTargetIntent();
            }
        }

        private void TryRequestCombatTargetIntent()
        {
            if (selectedCombatTarget == null ||
                combatController == null ||
                combatIntentRequestPending)
            {
                return;
            }

            var target = selectedCombatTarget;
            var requestRevision = combatIntentRevision;
            var targetPresenter =
                target.GetComponentInParent<OntologyCombatTargetPresenter>();
            selectedCombatHitPoint = targetPresenter == null
                ? target.transform.position
                : targetPresenter.HitAnchor.position;
            if (!RotateTowardsCombatTarget(target.transform.position))
            {
                return;
            }
            combatIntentRequestPending = true;
            if (combatController.TryBeginPrimaryTargetIntent(
                    target,
                    selectedCombatHitPoint,
                    consumed =>
                    {
                        if (this == null || !isActiveAndEnabled)
                        {
                            return;
                        }

                        CompleteCombatTargetIntent(
                            requestRevision,
                            target,
                            consumed);
                    }))
            {
                return;
            }

            combatIntentRequestPending = false;
            HandleCombatIntentRejection(
                combatController.LastAttackDiagnostic);
        }

        private void CompleteCombatTargetIntent(
            uint requestRevision,
            OntologyAuthorityEntityIdentity requestTarget,
            bool consumed)
        {
            // A later ground click, direct movement input, or combat selection
            // owns navigation now. The completion of an older Authority route
            // must not clear that newer ephemeral intent.
            if (requestRevision != combatIntentRevision ||
                selectedCombatTarget != requestTarget)
            {
                return;
            }

            combatIntentRequestPending = false;
            if (consumed)
            {
                ClearCombatTargetIntent();
                return;
            }

            HandleCombatIntentRejection(
                combatController.LastAttackDiagnostic);
        }

        private bool RotateTowardsCombatTarget(Vector3 targetPosition)
        {
            var direction = targetPosition - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
                return true;

            var targetRotation =
                Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                Mathf.Max(0f, fallbackTurnSpeed) * Time.deltaTime);
            return IsWithinCombatFacingAngle(
                transform.forward,
                direction,
                combatFacingTolerance);
        }

        public static bool IsWithinCombatFacingAngle(
            Vector3 forward,
            Vector3 targetDirection,
            float toleranceDegrees)
        {
            forward.y = 0f;
            targetDirection.y = 0f;
            if (forward.sqrMagnitude <= Mathf.Epsilon ||
                targetDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                return true;
            }

            return Vector3.Angle(
                       forward.normalized,
                       targetDirection.normalized) <=
                   Mathf.Max(0f, toleranceDegrees);
        }

        private void HandleCombatIntentRejection(string diagnostic)
        {
            if (selectedCombatTarget == null)
            {
                return;
            }

            if (OntologyCombatController.RequiresApproach(diagnostic))
            {
                clickTarget = selectedCombatTarget.transform.position;
                hasClickTarget = true;
                nextCombatIntentRetryAt =
                    Time.time + Mathf.Max(
                        0.05f,
                        combatIntentRetryInterval);
                return;
            }

            if (OntologyCombatController.ShouldRetryWithoutMovement(diagnostic))
            {
                hasClickTarget = false;
                nextCombatIntentRetryAt =
                    Time.time + Mathf.Max(
                        0.05f,
                        combatIntentRetryInterval);
                return;
            }

            var fallbackPointer = selectedCombatPointerPosition;
            ClearCombatTargetIntent();
            ResolveNonCombatPointerClick(fallbackPointer);
        }

        private void ClearCombatTargetIntent()
        {
            unchecked
            {
                combatIntentRevision++;
            }
            selectedCombatTarget = null;
            selectedCombatHitPoint = default;
            selectedCombatPointerPosition = default;
            combatIntentRequestPending = false;
            nextCombatIntentRetryAt = 0f;
            hasClickTarget = false;
        }

        private void ResolveNonCombatPointerClick(Vector2 pointerPosition)
        {
            if (OntologyRuntimeObjectPlacementController.IsPlacementInputCaptured ||
                OntologyRuntimeWorldEditorController.IsEditInputCaptured ||
                OntologyUIPointerUtility.IsPointerOverUi(pointerPosition))
            {
                return;
            }

            ClearCombatTargetIntent();
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            var ray = camera.ScreenPointToRay(pointerPosition);
            if (TryConsumeCreatorNpcClick(
                    ray,
                    Mathf.Max(1f, clickRaycastDistance)))
            {
                ClearInteractionSelection(removeIntent: true);
                hasClickTarget = false;
                return;
            }

            if (TryGetSelectableInteractionTarget(
                    ray,
                    Mathf.Max(1f, clickRaycastDistance),
                    out var interactionTarget))
            {
                if (selectedInteractionObject != interactionTarget)
                {
                    SelectInteractionTarget(interactionTarget);
                    hasClickTarget = false;
                    return;
                }

                BeginInteractionApproach(interactionTarget);
                return;
            }

            ClearInteractionSelection(removeIntent: true);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, Mathf.Max(1f, clickRaycastDistance)))
            {
                clickTarget = hit.point;
                hasClickTarget = true;
            }
        }

        /// <summary>
        /// Direct movement owns navigation for the current frame. This prevents a
        /// stale click destination from resuming when the key is released.
        /// </summary>
        public bool CancelClickNavigationForDirectInput(Vector2 directAxis)
        {
            if (directAxis.sqrMagnitude <= 0.0001f ||
                (!hasClickTarget &&
                 selectedInteractionObject == null &&
                 selectedCombatTarget == null))
            {
                return false;
            }

            ClearCombatTargetIntent();
            ClearInteractionSelection(removeIntent: true);
            hasClickTarget = false;
            return true;
        }

        /// <summary>
        /// Creator assistants intentionally have no solid Collider. Their visible
        /// renderer bounds receive pointer input so a click cannot fall through to
        /// the terrain and become an unintended movement destination.
        /// </summary>
        public static bool TryConsumeCreatorNpcClick(
            Ray ray,
            float maximumDistance)
        {
            OntologyCreatorNpcAppearance selected = null;
            var selectedDistance = float.PositiveInfinity;
            foreach (var candidate in FindObjectsByType<OntologyCreatorNpcAppearance>(
                         FindObjectsInactive.Exclude))
            {
                if (candidate == null || !candidate.isActiveAndEnabled)
                {
                    continue;
                }

                foreach (var renderer in candidate.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null || !renderer.enabled ||
                        !renderer.gameObject.activeInHierarchy ||
                        !renderer.bounds.IntersectRay(ray, out var distance) ||
                        distance < 0f || distance > maximumDistance ||
                        distance >= selectedDistance)
                    {
                        continue;
                    }

                    selected = candidate;
                    selectedDistance = distance;
                }
            }

            if (selected == null)
            {
                return false;
            }

            if (Physics.Raycast(
                    ray,
                    out var blockingHit,
                    maximumDistance,
                    ~0,
                    QueryTriggerInteraction.Ignore) &&
                blockingHit.distance + 0.02f < selectedDistance)
            {
                return false;
            }

            selected.NotifyClicked();
            return true;
        }

        private static bool TryUnequipClickedAttachment(
            Ray ray,
            float maximumDistance)
        {
            OntologyAttachmentAdapter selected = null;
            var selectedDistance = float.PositiveInfinity;
            foreach (var attachment in FindObjectsByType<OntologyAttachmentAdapter>(
                         FindObjectsInactive.Exclude))
            {
                if (attachment != null &&
                    attachment.RaycastPresentation(
                        ray,
                        maximumDistance,
                        out var distance) &&
                    distance < selectedDistance)
                {
                    selected = attachment;
                    selectedDistance = distance;
                }
            }

            return selected != null && selected.TryUnequip();
        }

        private bool TryGetSelectableInteractionTarget(
            Ray ray,
            float maximumDistance,
            out OntologyObject target)
        {
            target = null;
            var hits = Physics.RaycastAll(
                ray,
                maximumDistance,
                ~0,
                QueryTriggerInteraction.Collide);
            System.Array.Sort(
                hits,
                (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                if (hit.collider == null)
                {
                    continue;
                }

                var candidate =
                    hit.collider.GetComponentInParent<OntologyObject>();
                if (candidate == null ||
                    !SupportsSelectThenEquip(candidate))
                {
                    continue;
                }

                target = candidate;
                return true;
            }

            return false;
        }

        private bool SupportsSelectThenEquip(
            OntologyObject candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            if (bootstrap != null && bootstrap.World != null)
            {
                return bootstrap.World.HasFact(
                    candidate.EntityId,
                    OntologyPredicates.PickupBehavior,
                    OntologyObjects.SelectThenEquip);
            }

            foreach (var fact in candidate.Facts)
            {
                if (fact == null ||
                    fact.predicate != OntologyPredicates.PickupBehavior)
                {
                    continue;
                }

                if (fact.obj == OntologyObjects.SelectThenEquip)
                {
                    return true;
                }
            }

            return false;
        }

        private void SelectInteractionTarget(OntologyObject target)
        {
            ClearInteractionSelection(removeIntent: true);
            selectedInteractionObject = target;
            if (interactionSelectionMarker == null)
            {
                interactionSelectionMarker =
                    OntologyRuntimeSelectionMarker.Create();
            }

            interactionSelectionMarker.SetTarget(target.transform);
        }

        private void BeginInteractionApproach(OntologyObject target)
        {
            if (target == null || bootstrap == null || bootstrap.World == null)
            {
                return;
            }

            selectedInteractionObject = target;
            if (OntologyRuntimeObservationFacts.SynchronizeSingleValue(
                    bootstrap.World,
                    ResolvedActorId,
                    OntologyPredicates.InteractionIntent,
                    target.EntityId,
                    ref publishedInteractionIntent))
            {
                bootstrap.RunSimulation();
            }

            clickTarget = target.transform.position;
            hasClickTarget = true;
        }

        private void UpdateInteractionCompletion()
        {
            if (selectedInteractionObject == null ||
                bootstrap == null || bootstrap.World == null)
            {
                return;
            }

            if (!bootstrap.World.HasFact(
                    selectedInteractionObject.EntityId,
                    OntologyPredicates.EquippedBy,
                    ResolvedActorId))
            {
                return;
            }

            ClearInteractionSelection(removeIntent: true);
            hasClickTarget = false;
        }

        private void ClearInteractionSelection(bool removeIntent)
        {
            if (removeIntent &&
                bootstrap != null && bootstrap.World != null)
            {
                if (OntologyRuntimeObservationFacts.RemovePublishedSingleValue(
                        bootstrap.World,
                        ResolvedActorId,
                        OntologyPredicates.InteractionIntent,
                        ref publishedInteractionIntent))
                {
                    bootstrap.RunSimulation();
                }
            }

            selectedInteractionObject = null;
            if (interactionSelectionMarker != null)
            {
                interactionSelectionMarker.SetTarget(null);
            }
        }

        private Vector2 GetClickMoveAxis()
        {
            if (!clickToMove || !hasClickTarget)
            {
                return Vector2.zero;
            }

            var activeTarget = GetActiveClickTarget();
            var toTarget = activeTarget - transform.position;
            toTarget.y = 0f;
            var stopDistance = GetActiveClickStopDistance();

            if (HasReachedClickDestination(
                    toTarget.magnitude,
                    stopDistance,
                    GetCanonicalArrivalTolerance()))
            {
                // A plain ground destination remains an active ephemeral
                // Authority navigation intent until another input replaces it.
                // Local presentation stops here, while the server continues
                // toward the exact same destination instead of freezing at an
                // earlier sampled position and pulling the avatar backwards.
                if (selectedCombatTarget != null ||
                    selectedInteractionObject != null)
                {
                    hasClickTarget = false;
                }
                return Vector2.zero;
            }

            var forward = cameraTransform == null ? transform.forward : Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            var right = cameraTransform == null ? transform.right : Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
            var direction = toTarget.normalized;
            return Vector2.ClampMagnitude(new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward)), 1f);
        }

        public static bool HasReachedClickDestination(
            float planarDistanceToTarget,
            float stopDistance,
            float arrivalTolerance)
        {
            return Mathf.Max(0f, planarDistanceToTarget) <=
                   Mathf.Max(0f, stopDistance) +
                   Mathf.Max(0f, arrivalTolerance);
        }

        private float GetClickArrivalTolerance()
        {
            var controllerResolution = characterController == null
                ? 0f
                : Mathf.Max(
                    characterController.minMoveDistance,
                    characterController.skinWidth * 0.1f);
            return Mathf.Max(
                Mathf.Max(0f, clickArrivalTolerance),
                controllerResolution);
        }

        private float GetCanonicalArrivalTolerance()
        {
            // Plain ground navigation shares the exact authored stop radius
            // with Authority. Interaction/combat may retain controller-sized
            // presentation tolerance because their canonical completion is a
            // separate Authority action rather than the navigation endpoint.
            return selectedCombatTarget == null &&
                   selectedInteractionObject == null
                ? 0f
                : GetClickArrivalTolerance();
        }

        private Vector3 GetActiveClickTarget()
        {
            if (selectedCombatTarget != null)
            {
                return selectedCombatTarget.transform.position;
            }

            return selectedInteractionObject != null &&
                   bootstrap != null &&
                   bootstrap.World != null &&
                   bootstrap.World.HasFact(
                       ResolvedActorId,
                       OntologyPredicates.InteractionIntent,
                       selectedInteractionObject.EntityId)
                ? selectedInteractionObject.transform.position
                : clickTarget;
        }

        private float GetActiveClickTargetPlanarDistance()
        {
            var toTarget = GetActiveClickTarget() - transform.position;
            toTarget.y = 0f;
            return toTarget.magnitude;
        }

        private float GetActiveClickStopDistance()
        {
            var stopDistance = Mathf.Max(0.01f, clickStopDistance);
            if (selectedCombatTarget != null &&
                combatController != null &&
                combatController.TryResolveEquippedAttackRange(
                    out var attackRange))
            {
                var minimumBodyClearance = 0f;
                if (combatController
                    .TryResolveEquippedContactApproachDistance(
                        selectedCombatTarget,
                        out var contactApproachDistance))
                {
                    attackRange = SelectCombatApproachDistance(
                        attackRange,
                        contactApproachDistance);
                }
                combatController.TryResolveCombatBodyClearanceDistance(
                    selectedCombatTarget,
                    characterController,
                    out minimumBodyClearance);
                return ComputeCombatApproachStopDistance(
                    stopDistance,
                    attackRange,
                    combatApproachBuffer,
                    minimumBodyClearance);
            }

            if (selectedInteractionObject == null)
            {
                return stopDistance;
            }

            var attachment =
                selectedInteractionObject.GetComponent<OntologyAttachmentAdapter>();
            if (attachment != null && attachment.AttachmentProfile != null)
            {
                stopDistance = Mathf.Max(
                    stopDistance,
                    attachment.AttachmentProfile.autoEquipDistance * 0.8f);
            }

            return stopDistance;
        }

        public static float ComputeCombatApproachStopDistance(
            float defaultStopDistance,
            float authoredAttackRange,
            float navigationBuffer,
            float minimumBodyClearance = 0f)
        {
            return Mathf.Max(
                Mathf.Max(0.01f, defaultStopDistance),
                Mathf.Max(
                    Mathf.Max(0f, minimumBodyClearance),
                    Mathf.Max(0f, authoredAttackRange) -
                    Mathf.Max(0f, navigationBuffer)));
        }

        public static float SelectCombatApproachDistance(
            float authoredAttackRange,
            float physicalContactDistance)
        {
            if (authoredAttackRange <= 0f)
                return Mathf.Max(0f, physicalContactDistance);
            if (physicalContactDistance <= 0f)
                return authoredAttackRange;
            return Mathf.Min(
                authoredAttackRange,
                physicalContactDistance);
        }

        private void UpdateFallbackGravity()
        {
            if (authorityIntentSender == null ||
                !authorityIntentSender.TryResolveGravityAcceleration(
                    out var gravityAcceleration))
            {
                verticalVelocity = 0f;
                return;
            }

            // Gravity is continuous Physical Meaning, not a new movement
            // intent. It must keep settling an already visible capsule while
            // an unrelated projection causes the transient locomotion lease
            // to be re-evaluated.
            verticalVelocity = IntegrateVerticalVelocity(
                IsGrounded,
                verticalVelocity,
                gravityAcceleration,
                Time.deltaTime,
                ResolveGroundStickVelocity());
        }

        public static float IntegrateVerticalVelocity(
            bool isGrounded,
            float currentVelocity,
            float gravityAcceleration,
            float deltaTime,
            float groundStickVelocity)
        {
            if (isGrounded && currentVelocity <= 0f)
            {
                return Mathf.Min(0f, groundStickVelocity);
            }

            return currentVelocity +
                   gravityAcceleration * Mathf.Max(0f, deltaTime);
        }

        public static float ResolveVerticalVelocityAfterMove(
            CollisionFlags collisionFlags,
            float currentVelocity,
            float groundStickVelocity)
        {
            if ((collisionFlags & CollisionFlags.Above) != 0 &&
                currentVelocity > 0f)
            {
                return 0f;
            }

            if ((collisionFlags & CollisionFlags.Below) != 0 &&
                currentVelocity <= 0f)
            {
                return Mathf.Min(0f, groundStickVelocity);
            }

            return currentVelocity;
        }

        private float ResolveGroundStickVelocity()
        {
            return authorityIntentSender != null &&
                   authorityIntentSender.TryResolveGroundStickVelocity(
                       out var value)
                ? value
                : 0f;
        }

        private Vector2 ReadMoveAxis()
        {
            if (moveAction == null)
            {
                return Vector2.zero;
            }

            return Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
        }

        private bool ReadRun()
        {
            return runAction != null && runAction.IsPressed();
        }

        private Vector2 ReadMouseDelta()
        {
            if (lookAction == null)
            {
                return Vector2.zero;
            }

            var value = lookAction.ReadValue<Vector2>();
            var isPointerDelta = lookAction.activeControl?.device is Mouse;
            if (isPointerDelta &&
                (lookHoldAction == null || !lookHoldAction.IsPressed()))
            {
                return Vector2.zero;
            }

            return value * mouseLookScale;
        }

        private bool ReadJumpPressed()
        {
            return jumpAction != null &&
                   jumpAction.WasPressedThisFrame();
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) &&
                   float.IsFinite(value.y) &&
                   float.IsFinite(value.z);
        }

        public static Vector3 DampExternalImpulseVelocity(
            Vector3 currentVelocity,
            float damping,
            float deltaTime)
        {
            return Vector3.MoveTowards(
                currentVelocity,
                Vector3.zero,
                Mathf.Max(0f, damping) *
                Mathf.Max(0f, deltaTime));
        }

        private float ReadMouseScroll()
        {
            return scrollAction == null ? 0f : scrollAction.ReadValue<Vector2>().y * mouseScrollScale;
        }
    }

}
