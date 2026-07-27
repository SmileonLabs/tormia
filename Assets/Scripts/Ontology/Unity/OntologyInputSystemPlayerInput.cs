using UnityEngine;
using UnityEngine.InputSystem;
using ithappy.Creative_Characters_FREE.Controller;

namespace Tormia.Ontology.Core
{
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
        [SerializeField] private bool directCharacterControllerFallback = true;
        [SerializeField] private bool disableLegacyInputComponents = true;
        [SerializeField] private float fallbackWalkSpeed = 1.5f;
        [SerializeField] private float fallbackRunSpeed = 4.0f;
        [SerializeField, Min(0f)] private float fallbackJumpHeight = 1.5f;
        [SerializeField] private float slowedSpeedMultiplier = 0.5f;
        [SerializeField] private float fallbackGravity = -20f;
        [SerializeField] private bool clickToMove = true;
        [SerializeField] private float clickStopDistance = 0.1f;
        [SerializeField] private float clickRaycastDistance = 500f;
        [SerializeField] private float moveTargetDistance = 10f;
        [SerializeField] private float fallbackTurnSpeed = 360f;
        [SerializeField] private float mouseLookScale = 0.02f;
        [SerializeField] private float mouseScrollScale = 0.01f;

        [Header("Animator Parameters")]
        [SerializeField] private string horizontalParameter = "Hor";
        [SerializeField] private string verticalParameter = "Vert";
        [SerializeField] private string stateParameter = "State";
        [SerializeField] private string jumpParameter = "IsJump";
        [SerializeField] private float animatorDampTime = 0.08f;

        [Header("Debug")]
        [SerializeField] private Vector2 lastMoveAxis;
        [SerializeField] private Vector3 lastMoveTarget;
        [SerializeField] private bool lastKeyboardDetected;
        [SerializeField] private bool lastHadInput;
        [SerializeField] private bool hasClickTarget;
        [SerializeField] private Vector3 clickTarget;

        private CharacterController characterController;
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction lookHoldAction;
        private InputAction scrollAction;
        private InputAction jumpAction;
        private InputAction runAction;
        private InputAction clickAction;
        private InputAction pointerPositionAction;
        private float verticalVelocity;
        private OntologySwimmingMovementAdapter swimmingMovement;
        private OntologyDrowningRecoveryAdapter drowningRecovery;
        private OntologyObject selectedInteractionObject;
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
            worldDirection = Vector2.zero;
            if (!lastHadInput)
            {
                return false;
            }

            var move = hasClickTarget
                ? GetClickMoveDirection()
                : GetCameraRelativeMove(lastMoveAxis);
            if (move.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            move.Normalize();
            worldDirection = new Vector2(move.x, move.z);
            return true;
        }

        public bool IsJumpIntentHeld => ReadJumpHeld();
        public bool IsMovingIntent => lastHadInput;
        public Vector2 CurrentMoveAxis => lastMoveAxis;
        public string SelectedInteractionEntityId =>
            selectedInteractionObject == null
                ? string.Empty
                : selectedInteractionObject.EntityId;

        private void Awake()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            characterController = GetComponent<CharacterController>();
            if (disableLegacyInputComponents)
            {
                // The project owns movement/input through this component. Leaving the
                // third-party input pair enabled would move the controller twice and
                // overwrite the ontology-driven animator parameters every frame.
                var legacyInput = GetComponent<MovePlayerInput>();
                if (legacyInput != null) legacyInput.enabled = false;
                var legacyMover = GetComponent<CharacterMover>();
                if (legacyMover != null) legacyMover.enabled = false;
            }
            swimmingMovement = GetComponent<OntologySwimmingMovementAdapter>();
            drowningRecovery = GetComponent<OntologyDrowningRecoveryAdapter>();
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
            DisableInputActions();
        }

        private void Update()
        {
            EnsureCameraReferences();

            if (OntologyRuntimeObjectPlacementController.IsPlacementInputCaptured ||
                OntologyRuntimeWorldEditorController.IsEditInputCaptured ||
                OntologyUIPointerUtility.IsPointerOverUi())
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
            var useClickTarget = axis.sqrMagnitude <= 0.0001f && hasClickTarget;
            if (useClickTarget)
            {
                axis = GetClickMoveAxis();
            }

            var target = GetMoveTarget();
            var isRun = ReadRun();
            var isJump = ReadJumpHeld();

            lastMoveAxis = axis;
            lastMoveTarget = target;
            lastKeyboardDetected = Keyboard.current != null;
            lastHadInput = axis.sqrMagnitude > 0.0001f;

            if (directCharacterControllerFallback)
            {
                MoveCharacterController(axis, isRun, useClickTarget, ReadJumpPressed());
            }

            var airborne = characterController != null &&
                           (!characterController.isGrounded || verticalVelocity > 0.01f);
            UpdateAnimator(axis, isRun, isJump || airborne);

            if (playerCamera != null)
            {
                playerCamera.SetInput(ReadMouseDelta(), ReadMouseScroll());
            }
        }

        private void StopPlayerForUi()
        {
            hasClickTarget = false;
            lastMoveAxis = Vector2.zero;
            lastHadInput = false;

            if (directCharacterControllerFallback)
                MoveCharacterController(Vector2.zero, false, false, false);

            UpdateAnimator(Vector2.zero, false, false);
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

        private void UpdateAnimator(Vector2 axis, bool isRun, bool isJump)
        {
            if (visualAnimator == null)
            {
                return;
            }

            var state = axis.sqrMagnitude > 0.0001f && isRun ? 1f : 0f;
            SetFloatIfExists(horizontalParameter, axis.x);
            SetFloatIfExists(verticalParameter, axis.y);
            SetFloatIfExists(stateParameter, state);
            SetBoolIfExists(jumpParameter, isJump);
        }

        private void SetFloatIfExists(string parameter, float value)
        {
            if (string.IsNullOrWhiteSpace(parameter) || !HasAnimatorParameter(parameter, AnimatorControllerParameterType.Float))
            {
                return;
            }

            visualAnimator.SetFloat(parameter, value, Mathf.Max(0f, animatorDampTime), Time.deltaTime);
        }

        private void SetBoolIfExists(string parameter, bool value)
        {
            if (string.IsNullOrWhiteSpace(parameter) || !HasAnimatorParameter(parameter, AnimatorControllerParameterType.Bool))
            {
                return;
            }

            visualAnimator.SetBool(parameter, value);
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
            moveAction = new InputAction("OntologyMove", InputActionType.Value, expectedControlType: "Vector2");
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", moveUpBinding)
                .With("Up", moveUpAltBinding)
                .With("Down", moveDownBinding)
                .With("Down", moveDownAltBinding)
                .With("Left", moveLeftBinding)
                .With("Left", moveLeftAltBinding)
                .With("Right", moveRightBinding)
                .With("Right", moveRightAltBinding);

            lookAction = new InputAction("OntologyLook", InputActionType.Value, lookBinding);
            lookHoldAction = new InputAction("OntologyLookHold", InputActionType.Button, lookHoldBinding);
            scrollAction = new InputAction("OntologyScroll", InputActionType.Value, scrollBinding);
            jumpAction = new InputAction("OntologyJump", InputActionType.Button, jumpBinding);
            runAction = new InputAction("OntologyRun", InputActionType.Button);
            runAction.AddBinding(runBinding);
            runAction.AddBinding(runAltBinding);
            clickAction = new InputAction("OntologyClick", InputActionType.Button, clickBinding);
            pointerPositionAction = new InputAction("OntologyPointerPosition", InputActionType.Value, pointerPositionBinding);
        }

        private void EnableInputActions()
        {
            if (moveAction == null)
            {
                CreateInputActions();
            }

            moveAction.Enable();
            lookAction.Enable();
            lookHoldAction.Enable();
            scrollAction.Enable();
            jumpAction.Enable();
            runAction.Enable();
            clickAction.Enable();
            pointerPositionAction.Enable();
        }

        private void DisableInputActions()
        {
            if (moveAction == null)
            {
                return;
            }

            moveAction.Disable();
            lookAction.Disable();
            lookHoldAction.Disable();
            scrollAction.Disable();
            jumpAction.Disable();
            runAction.Disable();
            clickAction.Disable();
            pointerPositionAction.Disable();
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
            bool useClickTarget,
            bool jumpPressed)
        {
            if (characterController == null)
            {
                return;
            }

            if (drowningRecovery != null && drowningRecovery.TryRecover(Time.deltaTime))
            {
                verticalVelocity = 0f;
                return;
            }

            var move = axis.sqrMagnitude <= 0.0001f
                ? Vector3.zero
                : (useClickTarget ? GetClickMoveDirection() : GetCameraRelativeMove(axis));
            var speed = (isRun ? fallbackRunSpeed : fallbackWalkSpeed) * GetOntologySpeedMultiplier();

            if (swimmingMovement != null && swimmingMovement.TryMove(move, speed, Time.deltaTime))
            {
                verticalVelocity = 0f;
                RotateTowardsMove(move);
                return;
            }

            if (jumpPressed && characterController.isGrounded)
            {
                var jumpHeight = Mathf.Max(0f, fallbackJumpHeight);
                verticalVelocity = Mathf.Sqrt(
                    Mathf.Max(0f, 2f * Mathf.Abs(fallbackGravity) * jumpHeight));
            }

            if (axis.sqrMagnitude <= 0.0001f)
            {
                ApplyFallbackGravityOnly();
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

            characterController.Move(
                move * horizontalDistance +
                Vector3.up * verticalVelocity * Time.deltaTime);
            RotateTowardsMove(move);
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
            var toTarget = clickTarget - transform.position;
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
                ClearInteractionSelection(removeIntent: true);
                hasClickTarget = false;
                return;
            }

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
                (!hasClickTarget && selectedInteractionObject == null))
            {
                return false;
            }

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
                if (candidate == null || !SupportsSelectThenEquip(candidate))
                {
                    continue;
                }

                target = candidate;
                return true;
            }

            return false;
        }

        private bool SupportsSelectThenEquip(OntologyObject candidate)
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

            if (toTarget.magnitude <= stopDistance)
            {
                hasClickTarget = false;
                return Vector2.zero;
            }

            var forward = cameraTransform == null ? transform.forward : Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            var right = cameraTransform == null ? transform.right : Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
            var direction = toTarget.normalized;
            return Vector2.ClampMagnitude(new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward)), 1f);
        }

        private Vector3 GetActiveClickTarget()
        {
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

        private void ApplyFallbackGravityOnly()
        {
            if (characterController == null)
            {
                return;
            }

            UpdateFallbackGravity();
            characterController.Move(
                Vector3.up * verticalVelocity * Time.deltaTime);
        }

        private void UpdateFallbackGravity()
        {
            if (characterController != null && characterController.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -1f;
                return;
            }

            verticalVelocity += fallbackGravity * Time.deltaTime;
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

        private bool ReadJumpHeld()
        {
            return jumpAction != null && jumpAction.IsPressed();
        }

        private bool ReadJumpPressed()
        {
            return jumpAction != null && jumpAction.WasPressedThisFrame();
        }

        private Vector2 ReadMouseDelta()
        {
            if (lookAction == null || lookHoldAction == null || !lookHoldAction.IsPressed())
            {
                return Vector2.zero;
            }

            return lookAction.ReadValue<Vector2>() * mouseLookScale;
        }

        private float ReadMouseScroll()
        {
            return scrollAction == null ? 0f : scrollAction.ReadValue<Vector2>().y * mouseScrollScale;
        }
    }
}
