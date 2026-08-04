using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Sends a narrow attack intent to World Authority. It may present the swing
    /// immediately, but hit, damage, and death presentation happen only after an
    /// accepted Authority command and refreshed projection.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyCombatController : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private OntologyWorldAuthorityBridge authorityBridge;
        [SerializeField] private OntologyAuthorityEntityIdentity actorIdentity;
        [SerializeField] private OntologyAuthorityEntityIdentity equippedToolIdentity;
        [SerializeField] private OntologyCombatWeaponPresenter equippedWeapon;
        [SerializeField] private OntologyAnimationAdapter actorAnimationAdapter;
        [SerializeField] private OntologyAuthorityTargetingAdapter targetingAdapter;
        [SerializeField] private OntologyInputSystemPlayerInput playerInput;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private LayerMask equipmentCandidateLayers = ~0;
        [SerializeField, Min(0f), Tooltip(
            "Keeps a recent equip input alive while an Authority-first placement " +
            "is being projected into the Unity scene.")]
        private float equipReadinessGracePeriod = 2f;
        [SerializeField] private InputActionReference attackActionReference;
        [SerializeField] private InputActionReference equipActionReference;
        [SerializeField, Min(0.02f)] private float equipPromptRefreshInterval = 0.1f;
        [SerializeField] private string lastAttackDiagnostic;

        private InputAction attackAction;
        private InputAction equipAction;
        private OntologyWorldAuthorityClient subscribedAuthorityClient;
        private bool actionPending;
        private bool primaryPointerRoutePending;
        private Guid projectedEquippedItemId;
        private Guid[] projectedEquippedItemIds = Array.Empty<Guid>();
        private Vector2 pendingEquipScreenPosition;
        private float pendingEquipUntil;
        private float nextPendingEquipProbeAt;
        private float nextEquipPromptRefreshAt;
        private float nextAuthorityInteractionProbeAt;
        private bool authorityInteractionProbePending;
        private float authorityInteractionProbeStartedAt;
        private int authorityInteractionProbeGeneration;
        private bool hasAuthorityInteractionPosition;
        private Vector3 authorityInteractionPosition;
        private OntologyAuthorityEntityIdentity promptedEquipCandidate;
        private OntologyRuntimeSelectionMarker equipAvailabilityMarker;
        private OntologyWorldInteractionPrompt equipInteractionPrompt;
        public string LastAttackDiagnostic => lastAttackDiagnostic;
        public bool IsPrimaryAttackBusy =>
            actionPending || primaryPointerRoutePending;

        private enum CombatStatusSeverity
        {
            Info,
            Positive,
            Warning,
            Negative
        }

        private void Awake()
        {
            ResolveRuntimeDependencies();
            attackAction = attackActionReference?.action?.Clone();
            equipAction = equipActionReference?.action?.Clone();
        }

        private void ResolveRuntimeDependencies()
        {
            if (authorityClient == null)
                authorityClient = FindAnyObjectByType<OntologyWorldAuthorityClient>();
            if (authorityBridge == null)
                authorityBridge = FindAnyObjectByType<OntologyWorldAuthorityBridge>();
            if (actorIdentity == null)
                actorIdentity = GetComponent<OntologyAuthorityEntityIdentity>();
            if (actorAnimationAdapter == null)
                actorAnimationAdapter =
                    GetComponentInChildren<OntologyAnimationAdapter>(true);
            if (targetingAdapter == null)
                targetingAdapter = GetComponent<OntologyAuthorityTargetingAdapter>();
            if (playerInput == null)
                playerInput = GetComponent<OntologyInputSystemPlayerInput>();
            if (worldCamera == null) worldCamera = Camera.main;

            if (subscribedAuthorityClient != authorityClient)
            {
                if (subscribedAuthorityClient != null)
                {
                    subscribedAuthorityClient.ProjectionReceived -=
                        ApplyEquipmentProjection;
                }
                subscribedAuthorityClient = null;
            }
        }

        private void OnEnable()
        {
            ResolveRuntimeDependencies();
            if (authorityClient != null &&
                subscribedAuthorityClient == null)
            {
                subscribedAuthorityClient = authorityClient;
                subscribedAuthorityClient.ProjectionReceived +=
                    ApplyEquipmentProjection;
                ApplyEquipmentProjection(
                    subscribedAuthorityClient.CurrentProjection);
            }
            if (attackAction != null && playerInput == null)
            {
                attackAction.performed += HandleAttackPerformed;
                attackAction.Enable();
            }
            if (equipAction != null)
            {
                equipAction.performed += HandleEquipPerformed;
                equipAction.Enable();
            }
        }

        private void OnDisable()
        {
            if (attackAction != null)
            {
                attackAction.performed -= HandleAttackPerformed;
                attackAction.Disable();
            }
            if (equipAction != null)
            {
                equipAction.performed -= HandleEquipPerformed;
                equipAction.Disable();
            }
            ClearPendingEquip();
            primaryPointerRoutePending = false;
            actionPending = false;
            authorityInteractionProbePending = false;
            authorityInteractionProbeGeneration++;
            hasAuthorityInteractionPosition = false;
            HideEquipAvailabilityPresentation();
            StopAllCoroutines();
            if (subscribedAuthorityClient != null)
            {
                subscribedAuthorityClient.ProjectionReceived -=
                    ApplyEquipmentProjection;
                subscribedAuthorityClient = null;
            }
        }

        private void OnDestroy()
        {
            if (equipAvailabilityMarker != null)
                Destroy(equipAvailabilityMarker.gameObject);
            if (equipInteractionPrompt != null)
                Destroy(equipInteractionPrompt.gameObject);
            attackAction?.Dispose();
            equipAction?.Dispose();
            if (subscribedAuthorityClient != null)
            {
                subscribedAuthorityClient.ProjectionReceived -=
                    ApplyEquipmentProjection;
                subscribedAuthorityClient = null;
            }
        }

        private void Update()
        {
            RefreshAuthorityInteractionPosition();
            RetryPendingEquip();
            RefreshEquipAvailabilityPresentation();
        }

        private void RefreshAuthorityInteractionPosition()
        {
            if (authorityClient == null ||
                !authorityClient.IsWorldRuntimeReady ||
                actorIdentity == null ||
                !actorIdentity.TryGetGuid(out var actorId))
            {
                return;
            }

            if (authorityClient.TryGetLatestPlayerMotion(
                    actorId,
                    Mathf.Max(0.25f, equipPromptRefreshInterval * 3f),
                    out var cachedMotion) &&
                IsRuntimePositionReady(cachedMotion))
            {
                ApplyAuthorityInteractionPosition(cachedMotion);
                return;
            }

            if (authorityInteractionProbePending)
            {
                if (!IsInteractionProbeExpired(
                        authorityInteractionProbeStartedAt,
                        Time.unscaledTime,
                        1f))
                {
                    return;
                }

                authorityInteractionProbePending = false;
                authorityInteractionProbeGeneration++;
                hasAuthorityInteractionPosition = false;
            }

            if (Time.unscaledTime < nextAuthorityInteractionProbeAt)
                return;

            authorityInteractionProbePending = true;
            authorityInteractionProbeStartedAt = Time.unscaledTime;
            var probeGeneration = ++authorityInteractionProbeGeneration;
            nextAuthorityInteractionProbeAt =
                Time.unscaledTime + Mathf.Max(0.1f, equipPromptRefreshInterval);
            StartCoroutine(
                authorityClient.LoadPlayerMotionRoutine(
                    actorId,
                    motion =>
                    {
                        if (probeGeneration !=
                            authorityInteractionProbeGeneration)
                        {
                            return;
                        }
                        authorityInteractionProbePending = false;
                        hasAuthorityInteractionPosition =
                            IsRuntimePositionReady(motion);
                        if (hasAuthorityInteractionPosition)
                        {
                            ApplyAuthorityInteractionPosition(motion);
                        }
                    }));
        }

        private void ApplyAuthorityInteractionPosition(
            OntologyAuthorityPlayerMotionState motion)
        {
            hasAuthorityInteractionPosition =
                IsRuntimePositionReady(motion);
            if (!hasAuthorityInteractionPosition)
                return;

            authorityInteractionPosition = new Vector3(
                (float)motion.positionX,
                (float)motion.positionY,
                (float)motion.positionZ);
        }

        public static bool IsInteractionProbeExpired(
            float startedAt,
            float now,
            float timeoutSeconds) =>
            float.IsFinite(startedAt) &&
            float.IsFinite(now) &&
            float.IsFinite(timeoutSeconds) &&
            timeoutSeconds > 0f &&
            now - startedAt >= timeoutSeconds;

        private void HandleAttackPerformed(InputAction.CallbackContext context)
        {
            if (actionPending || Mouse.current == null) return;
            TryBeginPrimaryPointerIntent(
                Mouse.current.position.ReadValue(),
                null);
        }

        private void HandleEquipPerformed(InputAction.CallbackContext context)
        {
            if (actionPending) return;
            var pointerPosition = Mouse.current == null
                ? new Vector2(
                    Screen.width * 0.5f,
                    Screen.height * 0.5f)
                : Mouse.current.position.ReadValue();
            TryToggleEquipment(pointerPosition);
        }

        private void TryToggleEquipment(Vector2 screenPosition)
        {
            var lootCandidate = FindAvailableLootCandidate(screenPosition);
            if (lootCandidate != null &&
                lootCandidate.TryGetGuid(out var lootEntityId))
            {
                ClearPendingEquip();
                TryCollectLoot(lootEntityId);
                return;
            }

            // Consume the exact semantic opportunity that the prompt exposed.
            // Re-running an asynchronous Authority-position probe between the
            // displayed F prompt and the key event can otherwise turn one
            // visible interaction into a false "no equipment" result. The
            // Authority command remains the final range/permission boundary.
            var candidate = IsCurrentPromptEquipCandidate(
                    promptedEquipCandidate)
                ? promptedEquipCandidate
                : FindEquipCandidate(screenPosition);
            if (candidate != null &&
                candidate.TryGetGuid(out var candidateId))
            {
                ClearPendingEquip();
                if (projectedEquippedItemIds.Contains(candidateId))
                {
                    TryUnequip(candidateId);
                }
                else
                {
                    TryEquip(screenPosition, candidate);
                }
                return;
            }

            var equippedPresentation =
                FindAimedEquippedPresentation(screenPosition);
            if (equippedPresentation != null &&
                equippedPresentation.TryGetGuid(out var equippedId))
            {
                ClearPendingEquip();
                TryUnequip(equippedId);
                return;
            }

            if (projectedEquippedItemIds.Length == 1)
            {
                ClearPendingEquip();
                TryUnequip(projectedEquippedItemIds[0]);
                return;
            }

            if (projectedEquippedItemIds.Length > 1)
            {
                ShowCombatStatus(
                    "equipment_selection_required",
                    CombatStatusSeverity.Info);
                return;
            }

            TryEquip(screenPosition);
        }

        private bool IsCurrentPromptEquipCandidate(
            OntologyAuthorityEntityIdentity candidate)
        {
            return candidate != null &&
                   candidate.gameObject.activeInHierarchy &&
                   IsEquipmentActionTarget(candidate) &&
                   candidate.TryGetGuid(out var candidateId) &&
                   !projectedEquippedItemIds.Contains(candidateId);
        }

        private void TryCollectLoot(Guid lootEntityId)
        {
            if (authorityClient == null ||
                !authorityClient.IsWorldRuntimeReady ||
                actorIdentity == null ||
                !actorIdentity.TryGetGuid(out var actorId) ||
                lootEntityId == Guid.Empty ||
                !TryResolveCanonicalFact(
                    authorityClient.CurrentProjection,
                    lootEntityId,
                    OntologyPredicates.LootAction,
                    out var collectActionId) ||
                !authorityClient.TryResolveEnabledAction(
                    collectActionId,
                    out var definition))
            {
                ShowCombatStatus(
                    "loot_unavailable",
                    CombatStatusSeverity.Info);
                return;
            }

            StartCoroutine(
                ExecuteCollectLootRoutine(
                    actorId,
                    lootEntityId,
                    definition));
        }

        private IEnumerator ExecuteCollectLootRoutine(
            Guid actorId,
            Guid lootEntityId,
            OntologyAuthorityActionDefinitionProjection definition)
        {
            actionPending = true;
            var payload =
                OntologyWorldAuthorityClient.CreateExecuteActionPayload(
                    actorId,
                    lootEntityId,
                    null,
                    definition.packageId,
                    definition.packageVersion,
                    definition.actionId,
                    definition.definitionVersion);
            OntologyAuthorityCommandResult result = null;
            yield return authorityClient.SendCommandWithRevisionRetryRoutine(
                () => OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.ExecuteAction,
                    payload),
                value => result = value);
            if (result != null && result.accepted)
            {
                yield return authorityClient.LoadWorldRoutine();
                ShowCombatStatus(
                    "loot_collected",
                    CombatStatusSeverity.Positive);
            }
            else
            {
                LogTechnicalFailure("loot_collection_failed", authorityClient.LastStatus);
                ShowCombatStatus(
                    "loot_collection_failed",
                    CombatStatusSeverity.Warning);
            }
            actionPending = false;
        }

        private void TryUnequip(Guid equippedItemId)
        {
            if (authorityClient == null ||
                !authorityClient.IsWorldRuntimeReady ||
                actorIdentity == null ||
                !actorIdentity.TryGetGuid(out var actorId) ||
                equippedItemId == Guid.Empty)
            {
                return;
            }

            if (!TryResolveCanonicalFact(
                    authorityClient.CurrentProjection,
                    equippedItemId,
                    OntologyPredicates.UnequipAction,
                    out var unequipActionId) ||
                !authorityClient.TryResolveEnabledAction(
                    unequipActionId,
                    out var definition))
            {
                StartCoroutine(
                    RefreshCombatPackageAndUnequipRoutine(equippedItemId));
                return;
            }

            StartCoroutine(
                ExecuteUnequipRoutine(
                    actorId,
                    equippedItemId,
                    definition));
        }

        private void TryEquip(
            Vector2 screenPosition,
            OntologyAuthorityEntityIdentity resolvedCandidate = null,
            bool queueWhilePlacementCompletes = true)
        {
            if (authorityClient == null || !authorityClient.IsWorldRuntimeReady)
            {
                ShowCombatStatus(
                    "equip_world_not_ready",
                    CombatStatusSeverity.Warning);
                return;
            }

            if (actorIdentity == null ||
                !actorIdentity.TryGetGuid(out var actorId))
            {
                ShowCombatStatus(
                    "equip_player_identity_not_ready",
                    CombatStatusSeverity.Warning);
                return;
            }

            var equipmentIdentity =
                resolvedCandidate ?? FindEquipCandidate(screenPosition);
            if (equipmentIdentity == null ||
                !equipmentIdentity.TryGetGuid(out var equipmentId) ||
                !TryResolveEquipActionId(
                    equipmentIdentity,
                    out var equipActionId))
            {
                QueuePendingEquip(
                    screenPosition,
                    queueWhilePlacementCompletes);
                ShowCombatStatus(
                    "no_equipment_nearby",
                    CombatStatusSeverity.Info);
                return;
            }

            if (!authorityClient.TryResolveEnabledAction(
                    equipActionId,
                    out var definition))
            {
                ShowCombatStatus(
                    "equip_rule_missing",
                    CombatStatusSeverity.Warning);
                StartCoroutine(RefreshCombatPackageAndEquipRoutine(screenPosition));
                return;
            }

            var weaponIdentity = equipmentIdentity;
            var weaponId = equipmentId;
            if (weaponIdentity == null ||
                weaponId == Guid.Empty)
            {
                if (queueWhilePlacementCompletes &&
                    equipReadinessGracePeriod > 0f)
                {
                    pendingEquipScreenPosition = screenPosition;
                    pendingEquipUntil =
                        Time.unscaledTime + equipReadinessGracePeriod;
                    nextPendingEquipProbeAt = Time.unscaledTime;
                }
                ShowCombatStatus(
                    "no_weapon_nearby",
                    CombatStatusSeverity.Info);
                return;
            }
            ClearPendingEquip();

            if (authorityBridge != null &&
                !authorityBridge.IsEntityPublished(weaponIdentity))
            {
                var placedInstance =
                    weaponIdentity.GetComponent<OntologyPlaceableInstance>();
                if (placedInstance == null)
                {
                    ShowCombatStatus(
                        "weapon_instance_missing",
                        CombatStatusSeverity.Warning);
                    return;
                }

                StartCoroutine(PublishWeaponAndEquipRoutine(
                    actorId,
                    weaponId,
                    weaponIdentity,
                    placedInstance,
                    definition));
                return;
            }

            StartCoroutine(ExecuteEquipRoutine(
                actorId,
                weaponId,
                definition));
        }

        private void RetryPendingEquip()
        {
            if (pendingEquipUntil <= 0f || actionPending)
            {
                return;
            }

            if (!ShouldRetryPendingEquip(
                    pendingEquipUntil,
                    Time.unscaledTime,
                    actionPending,
                    false))
            {
                ClearPendingEquip();
                return;
            }

            if (Time.unscaledTime < nextPendingEquipProbeAt)
            {
                return;
            }

            nextPendingEquipProbeAt = Time.unscaledTime + 0.1f;
            var candidate = FindEquipCandidate(pendingEquipScreenPosition);
            if (candidate == null || !candidate.TryGetGuid(out _))
            {
                return;
            }

            var screenPosition = pendingEquipScreenPosition;
            ClearPendingEquip();
            TryEquip(screenPosition, candidate, false);
        }

        public static bool ShouldRetryPendingEquip(
            float pendingUntil,
            float currentTime,
            bool commandPending,
            bool alreadyEquipped)
        {
            return pendingUntil > 0f &&
                   currentTime <= pendingUntil &&
                   !commandPending &&
                   !alreadyEquipped;
        }

        private void ClearPendingEquip()
        {
            pendingEquipUntil = 0f;
            nextPendingEquipProbeAt = 0f;
            pendingEquipScreenPosition = default;
        }

        private void QueuePendingEquip(
            Vector2 screenPosition,
            bool queueWhilePlacementCompletes)
        {
            if (!queueWhilePlacementCompletes ||
                equipReadinessGracePeriod <= 0f)
            {
                return;
            }

            pendingEquipScreenPosition = screenPosition;
            pendingEquipUntil =
                Time.unscaledTime + equipReadinessGracePeriod;
            nextPendingEquipProbeAt = Time.unscaledTime;
        }

        private IEnumerator PublishWeaponAndEquipRoutine(
            Guid actorId,
            Guid weaponId,
            OntologyAuthorityEntityIdentity weaponIdentity,
            OntologyPlaceableInstance placedInstance,
            OntologyAuthorityActionDefinitionProjection definition)
        {
            actionPending = true;
            ShowCombatStatus(
                "weapon_publishing",
                CombatStatusSeverity.Info);
            var published = false;
            yield return authorityBridge.EnsureEntityPublishedRoutine(
                placedInstance,
                value => published = value);
            if (!published)
            {
                LogTechnicalFailure(
                    "weapon_publish_failed",
                    authorityBridge.LastPublishStatus);
                ShowCombatStatus(
                    "weapon_publish_failed",
                    CombatStatusSeverity.Negative);
                actionPending = false;
                yield break;
            }

            if (weaponIdentity == null ||
                !weaponIdentity.TryGetGuid(out var currentWeaponId) ||
                currentWeaponId != weaponId)
            {
                ShowCombatStatus(
                    "equip_cancelled",
                    CombatStatusSeverity.Warning);
                actionPending = false;
                yield break;
            }

            yield return ExecuteEquipRoutine(
                actorId,
                weaponId,
                definition);
            actionPending = false;
        }

        private IEnumerator ExecuteEquipRoutine(
            Guid actorId,
            Guid weaponId,
            OntologyAuthorityActionDefinitionProjection definition)
        {
            actionPending = true;
            OntologyAuthorityPlayerMotionState actorMotion = null;
            yield return authorityClient.LoadPlayerMotionRoutine(
                actorId,
                value => actorMotion = value);
            if (!IsRuntimePositionReady(actorMotion))
            {
                ShowCombatStatus(
                    "equip_position_sync",
                    CombatStatusSeverity.Warning);
                actionPending = false;
                yield break;
            }

            var payload = OntologyWorldAuthorityClient.CreateExecuteActionPayload(
                actorId,
                weaponId,
                null,
                definition.packageId,
                definition.packageVersion,
                definition.actionId,
                definition.definitionVersion);
            OntologyAuthorityCommandResult result = null;
            yield return authorityClient.SendCommandWithRevisionRetryRoutine(
                () => OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.ExecuteAction,
                    payload),
                value => result = value);
            if (result != null && result.accepted)
            {
                yield return authorityClient.LoadWorldRoutine();
                ShowCombatStatus(
                    "weapon_equipped",
                    CombatStatusSeverity.Positive);
            }
            else
            {
                LogTechnicalFailure(
                    "equip_failed",
                    authorityClient.LastStatus);
                ShowCombatStatus(
                    ResolveEquipFailureStatusId(result?.rejectionCode),
                    CombatStatusSeverity.Warning);
            }
            actionPending = false;
        }

        public static bool IsRuntimePositionReady(
            OntologyAuthorityPlayerMotionState motion) =>
            motion != null &&
            !string.IsNullOrWhiteSpace(motion.zoneKey);

        public static string ResolveEquipFailureMessage(
            string rejectionCode,
            string fallback)
        {
            var statusId = ResolveEquipFailureStatusId(rejectionCode);
            var key = "ui.combat.status." + statusId + ".detail";
            return OntologyLanguagePackService.Text(
                key,
                string.IsNullOrWhiteSpace(fallback)
                    ? "The Authority rejected the equip request."
                    : fallback);
        }

        public static string ResolveEquipFailureStatusId(
            string rejectionCode) =>
            rejectionCode switch
            {
                "action_actor_runtime_position_unavailable" =>
                    "equip_actor_position_unavailable",
                "action_target_position_unavailable" =>
                    "equip_target_position_unavailable",
                "action_target_out_of_range" =>
                    "equip_target_out_of_range",
                _ => "equip_failed"
            };

        private IEnumerator ExecuteUnequipRoutine(
            Guid actorId,
            Guid weaponId,
            OntologyAuthorityActionDefinitionProjection definition)
        {
            actionPending = true;
            // Arm the presentation before sending the command. A pushed
            // Authority projection may remove equipped_by before the HTTP
            // response coroutine resumes; preparing afterward would be too
            // late and the object would detach at its old durable position.
            var attachment = ResolveAttachedPresentation(weaponId);
            attachment?.PrepareForAuthorityDetach();
            var payload =
                OntologyWorldAuthorityClient.CreateExecuteActionPayload(
                    actorId,
                    weaponId,
                    null,
                    definition.packageId,
                    definition.packageVersion,
                    definition.actionId,
                    definition.definitionVersion);
            OntologyAuthorityCommandResult result = null;
            yield return authorityClient.SendCommandWithRevisionRetryRoutine(
                () => OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.ExecuteAction,
                    payload),
                value => result = value);
            if (result != null && result.accepted)
            {
                yield return authorityClient.LoadWorldRoutine();
                ShowCombatStatus(
                    "weapon_unequipped",
                    CombatStatusSeverity.Positive);
            }
            else
            {
                attachment?.CancelPreparedAuthorityDetach();
                LogTechnicalFailure("unequip_failed", authorityClient.LastStatus);
                ShowCombatStatus(
                    "unequip_failed",
                    CombatStatusSeverity.Warning);
            }
            actionPending = false;
        }

        private OntologyAttachmentAdapter ResolveAttachedPresentation(
            Guid entityId)
        {
            if (equippedToolIdentity != null &&
                equippedToolIdentity.TryGetGuid(out var equippedId) &&
                equippedId == entityId)
            {
                var current = equippedToolIdentity
                    .GetComponent<OntologyAttachmentAdapter>();
                if (current != null && current.IsAttached)
                {
                    return current;
                }
            }

            return FindObjectsByType<OntologyAuthorityEntityIdentity>(
                    FindObjectsInactive.Include)
                .Where(value =>
                    value != null &&
                    value.TryGetGuid(out var candidateId) &&
                    candidateId == entityId)
                .Select(value =>
                    value.GetComponent<OntologyAttachmentAdapter>())
                .FirstOrDefault(value => value != null && value.IsAttached);
        }

        private OntologyAuthorityEntityIdentity FindEquipCandidate(
            Vector2 screenPosition)
        {
            var selected = playerInput == null
                ? null
                : playerInput.SelectedInteractionTarget;
            var selectedIdentity = selected == null
                ? null
                : selected.GetComponent<OntologyAuthorityEntityIdentity>();
            if (IsEquipmentActionTarget(selectedIdentity) &&
                IsWithinInteractionRange(selectedIdentity))
            {
                return selectedIdentity;
            }

            if (worldCamera != null)
            {
                var ray = worldCamera.ScreenPointToRay(screenPosition);
                var aimed = Physics.RaycastAll(
                        ray,
                        500f,
                        equipmentCandidateLayers,
                        QueryTriggerInteraction.Collide)
                    .OrderBy(hit => hit.distance)
                    .Select(hit =>
                        hit.collider.GetComponentInParent<OntologyAuthorityEntityIdentity>())
                    .FirstOrDefault(candidate =>
                        IsEnabledEquipmentActionTarget(candidate) &&
                        !projectedEquippedItemIds.Contains(
                            ResolveEntityId(candidate)) &&
                        IsWithinInteractionRange(candidate));
                if (aimed != null) return aimed;
            }

            // F is a proximity interaction. The mouse ray is only a preference,
            // so a thin weapon or a terrain collider cannot make the key unusable.
            return FindNearestAvailableEquipCandidate();
        }

        private OntologyAuthorityEntityIdentity FindNearestAvailableEquipCandidate()
        {
            return FindObjectsByType<OntologyAuthorityEntityIdentity>(
                    FindObjectsInactive.Exclude)
                .Where(candidate =>
                    IsEnabledEquipmentActionTarget(candidate) &&
                    !projectedEquippedItemIds.Contains(
                        ResolveEntityId(candidate)) &&
                    !candidate.transform.IsChildOf(transform) &&
                    IsWithinInteractionRange(candidate))
                .OrderBy(candidate =>
                    ResolveInteractionSurfaceDistanceSqr(
                        transform.position,
                        candidate.transform))
                .FirstOrDefault();
        }

        private void RefreshEquipAvailabilityPresentation()
        {
            if (Time.unscaledTime < nextEquipPromptRefreshAt)
                return;
            nextEquipPromptRefreshAt = Time.unscaledTime +
                                       Mathf.Max(0.02f, equipPromptRefreshInterval);

            var candidate = actionPending
                ? null
                : FindNearestAvailableEquipCandidate();
            if (promptedEquipCandidate == candidate)
                return;
            promptedEquipCandidate = candidate;
            if (equipAvailabilityMarker == null)
                equipAvailabilityMarker =
                    OntologyRuntimeSelectionMarker.Create();
            if (equipInteractionPrompt == null)
                equipInteractionPrompt =
                    OntologyWorldInteractionPrompt.Create("F");

            var target = candidate == null ? null : candidate.transform;
            equipAvailabilityMarker.SetTarget(target);
            equipInteractionPrompt.SetTarget(target);
        }

        private void HideEquipAvailabilityPresentation()
        {
            promptedEquipCandidate = null;
            // Unity destroyed-object references require the overloaded null
            // check; the null-conditional operator would still invoke them.
            if (equipAvailabilityMarker != null)
                equipAvailabilityMarker.SetTarget(null);
            if (equipInteractionPrompt != null)
                equipInteractionPrompt.SetTarget(null);
        }

        private OntologyAuthorityEntityIdentity FindAvailableLootCandidate(
            Vector2 screenPosition)
        {
            var projection = authorityClient?.CurrentProjection;
            if (projection?.facts == null)
                return null;

            var candidates =
                FindObjectsByType<OntologyAuthorityEntityIdentity>(
                        FindObjectsInactive.Exclude)
                    .Where(candidate =>
                        candidate != null &&
                        candidate.TryGetGuid(out var entityId) &&
                        ProjectionDeclaresCanonicalFact(
                            projection,
                            entityId,
                            OntologyPredicates.LootStatus,
                            "Available") &&
                        TryResolveCanonicalFact(
                            projection,
                            entityId,
                            OntologyPredicates.LootAction,
                            out _) &&
                        IsWithinInteractionRange(candidate))
                    .ToArray();
            if (candidates.Length == 0)
                return null;

            if (worldCamera != null)
            {
                var ray = worldCamera.ScreenPointToRay(screenPosition);
                var aimed = candidates
                    .Select(candidate => new
                    {
                        Candidate = candidate,
                        Distance = DistanceToRay(
                            ray,
                            candidate.transform.position)
                    })
                    .Where(value =>
                        value.Distance <=
                        ResolveInteractionAimTolerance(value.Candidate))
                    .OrderBy(value => value.Distance)
                    .Select(value => value.Candidate)
                    .FirstOrDefault();
                if (aimed != null)
                    return aimed;
            }

            return candidates
                .OrderBy(candidate =>
                    Vector3.SqrMagnitude(
                        candidate.transform.position -
                        transform.position))
                .FirstOrDefault();
        }

        private float ResolveInteractionAimTolerance(
            OntologyAuthorityEntityIdentity candidate)
        {
            if (candidate != null &&
                candidate.TryGetGuid(out var entityId) &&
                TryResolvePositiveNumberFact(
                    candidate.GetComponent<OntologyObject>(),
                    authorityClient?.CurrentProjection,
                    entityId,
                    OntologyPredicates.InteractionRange,
                    out var range))
            {
                return range;
            }
            return 0f;
        }

        private static float DistanceToRay(Ray ray, Vector3 point)
        {
            var direction = ray.direction.normalized;
            var projection = Mathf.Max(
                0f,
                Vector3.Dot(point - ray.origin, direction));
            return Vector3.Distance(
                point,
                ray.origin + direction * projection);
        }

        private OntologyAuthorityEntityIdentity FindAimedEquippedPresentation(
            Vector2 screenPosition)
        {
            if (worldCamera == null ||
                projectedEquippedItemIds.Length == 0)
            {
                return null;
            }

            var ray = worldCamera.ScreenPointToRay(screenPosition);
            OntologyAuthorityEntityIdentity selected = null;
            var selectedDistance = float.PositiveInfinity;
            foreach (var attachment in FindObjectsByType<OntologyAttachmentAdapter>(
                         FindObjectsInactive.Exclude))
            {
                var identity = attachment == null
                    ? null
                    : attachment.GetComponent<OntologyAuthorityEntityIdentity>();
                if (identity == null ||
                    !identity.TryGetGuid(out var entityId) ||
                    !projectedEquippedItemIds.Contains(entityId) ||
                    !attachment.RaycastPresentation(
                        ray,
                        500f,
                        out var distance) ||
                    distance >= selectedDistance)
                {
                    continue;
                }

                selected = identity;
                selectedDistance = distance;
            }

            return selected;
        }

        private bool IsEquipmentActionTarget(
            OntologyAuthorityEntityIdentity candidate)
        {
            return TryResolveEquipActionId(candidate, out _) &&
                   IsCandidateSlotAvailable(candidate);
        }

        private bool IsEnabledEquipmentActionTarget(
            OntologyAuthorityEntityIdentity candidate)
        {
            return IsEquipmentActionTarget(candidate) &&
                   TryResolveEquipActionId(candidate, out var actionId) &&
                   authorityClient != null &&
                   authorityClient.TryResolveEnabledAction(actionId, out _);
        }

        private bool IsCandidateSlotAvailable(
            OntologyAuthorityEntityIdentity candidate)
        {
            if (candidate == null || authorityClient?.CurrentProjection == null ||
                actorIdentity == null ||
                !candidate.TryGetGuid(out var candidateId) ||
                !actorIdentity.TryGetGuid(out var actorId) ||
                !TryResolveCanonicalFact(
                    authorityClient.CurrentProjection,
                    candidateId,
                    OntologyPredicates.HasSlot,
                    out var candidateSlot))
            {
                return false;
            }

            foreach (var relation in authorityClient.CurrentProjection.facts)
            {
                if (relation == null ||
                    (relation.predicateId != OntologyPredicates.EquippedBy &&
                     relation.predicateId != OntologyPredicates.CarriedBy) ||
                    !string.Equals(
                        relation.objectEntityId,
                        actorId.ToString("D"),
                        StringComparison.OrdinalIgnoreCase) ||
                    !Guid.TryParse(relation.subjectEntityId, out var itemId) ||
                    itemId == candidateId ||
                    !TryResolveCanonicalFact(
                        authorityClient.CurrentProjection,
                        itemId,
                        OntologyPredicates.HasSlot,
                        out var occupiedSlot))
                {
                    continue;
                }

                if (string.Equals(
                        occupiedSlot,
                        candidateSlot,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryResolveEquipActionId(
            OntologyAuthorityEntityIdentity candidate,
            out string actionId)
        {
            actionId = string.Empty;
            if (candidate == null ||
                !candidate.TryGetGuid(out var entityId))
            {
                return false;
            }

            return TryResolveCanonicalFact(
                       candidate.GetComponent<OntologyObject>(),
                       OntologyPredicates.EquipAction,
                       out actionId) ||
                   TryResolveCanonicalFact(
                       authorityClient?.CurrentProjection,
                       entityId,
                       OntologyPredicates.EquipAction,
                       out actionId);
        }

        private bool IsWithinInteractionRange(
            OntologyAuthorityEntityIdentity candidate)
        {
            if (candidate == null ||
                !candidate.TryGetGuid(out var entityId) ||
                !TryResolvePositiveNumberFact(
                    candidate.GetComponent<OntologyObject>(),
                    authorityClient?.CurrentProjection,
                    entityId,
                    OntologyPredicates.InteractionRange,
                    out var range))
            {
                return false;
            }

            if (authorityClient != null &&
                authorityClient.IsWorldRuntimeReady)
            {
                if (!hasAuthorityInteractionPosition ||
                    !TryResolveProjectedEntityPosition(
                        authorityClient.CurrentProjection,
                        entityId,
                        out var targetPosition))
                {
                    return false;
                }

                // Authority distance is the permission boundary, while the
                // current Unity transform is collision-presentation evidence.
                // A Dynamic body can fall or roll before its next settled
                // checkpoint; its durable spawn transform must never make an
                // invisible/stale presentation interactable.
                return IsWithinInteractionRange(
                    authorityInteractionPosition,
                    targetPosition,
                    transform.position,
                    ResolveInteractionSurfacePoint(
                        transform.position,
                        candidate.transform),
                    range);
            }

            return Vector3.Distance(
                       transform.position,
                       ResolveInteractionSurfacePoint(
                           transform.position,
                           candidate.transform)) <=
                   range;
        }

        public static bool IsWithinInteractionRange(
            Vector3 authorityActorPosition,
            Vector3 authorityTargetPosition,
            Vector3 presentationActorPosition,
            Vector3 presentationTargetPosition,
            float range)
        {
            return IsFinite(authorityActorPosition) &&
                   IsFinite(authorityTargetPosition) &&
                   IsFinite(presentationActorPosition) &&
                   IsFinite(presentationTargetPosition) &&
                   float.IsFinite(range) &&
                   range > 0f &&
                   Vector3.Distance(
                       authorityActorPosition,
                       authorityTargetPosition) <= range &&
                   Vector3.Distance(
                       presentationActorPosition,
                       presentationTargetPosition) <= range;
        }

        public static Vector3 ResolveInteractionSurfacePoint(
            Vector3 actorPosition,
            Transform candidateRoot)
        {
            if (candidateRoot == null || !IsFinite(actorPosition))
                return candidateRoot == null
                    ? actorPosition
                    : candidateRoot.position;

            var closestPoint = candidateRoot.position;
            var closestDistanceSqr =
                (closestPoint - actorPosition).sqrMagnitude;
            var foundCollider = false;
            foreach (var collider in
                     candidateRoot.GetComponentsInChildren<Collider>(false))
            {
                if (collider == null || !collider.enabled ||
                    !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var point = collider.ClosestPoint(actorPosition);
                var distanceSqr = (point - actorPosition).sqrMagnitude;
                if (foundCollider && distanceSqr >= closestDistanceSqr)
                    continue;

                foundCollider = true;
                closestPoint = point;
                closestDistanceSqr = distanceSqr;
            }

            return closestPoint;
        }

        public static float ResolveInteractionSurfaceDistanceSqr(
            Vector3 actorPosition,
            Transform candidateRoot)
        {
            var point = ResolveInteractionSurfacePoint(
                actorPosition,
                candidateRoot);
            return (point - actorPosition).sqrMagnitude;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) &&
            float.IsFinite(value.y) &&
            float.IsFinite(value.z);

        private static bool TryResolveProjectedEntityPosition(
            OntologyAuthorityWorldProjection projection,
            Guid entityId,
            out Vector3 position)
        {
            position = default;
            var entity = projection?.entities?.FirstOrDefault(value =>
                value != null &&
                string.Equals(
                    value.entityId,
                    entityId.ToString("D"),
                    StringComparison.OrdinalIgnoreCase));
            if (entity?.transform == null)
                return false;

            position = new Vector3(
                entity.transform.positionX,
                entity.transform.positionY,
                entity.transform.positionZ);
            return true;
        }

        private static Guid ResolveEntityId(
            OntologyAuthorityEntityIdentity identity)
        {
            return identity != null &&
                   identity.TryGetGuid(out var entityId)
                ? entityId
                : Guid.Empty;
        }

        private static bool DeclaresCanonicalFact(
            OntologyObject ontology,
            string predicateId,
            string objectId)
        {
            return ontology != null &&
                   ontology.Facts.Any(value =>
                       value != null &&
                       string.Equals(
                           value.predicate,
                           predicateId,
                           StringComparison.Ordinal) &&
                       string.Equals(
                           value.obj,
                           objectId,
                           StringComparison.Ordinal));
        }

        private static bool TryResolveCanonicalFact(
            OntologyObject ontology,
            string predicateId,
            out string value)
        {
            value = string.Empty;
            if (ontology == null || string.IsNullOrWhiteSpace(predicateId))
                return false;
            var matches = ontology.Facts
                .Where(fact =>
                    fact != null &&
                    string.Equals(
                        fact.predicate,
                        predicateId,
                        StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(fact.obj))
                .Select(fact => fact.obj.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (matches.Length != 1) return false;
            value = matches[0];
            return true;
        }

        private static bool TryResolvePositiveNumberFact(
            OntologyObject ontology,
            OntologyAuthorityWorldProjection projection,
            Guid entityId,
            string predicateId,
            out float value)
        {
            value = 0f;
            var local = ontology?.Facts
                .Where(fact =>
                    fact != null &&
                    string.Equals(
                        fact.predicate,
                        predicateId,
                        StringComparison.Ordinal))
                .Select(fact => fact.obj)
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .Distinct(StringComparer.Ordinal)
                .ToArray() ?? Array.Empty<string>();
            if (local.Length == 1 &&
                float.TryParse(
                    local[0],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value) &&
                float.IsFinite(value) &&
                value > 0f)
            {
                return true;
            }

            if (projection?.facts == null || entityId == Guid.Empty)
                return false;
            var subject = entityId.ToString("D");
            var projected = projection.facts
                .Where(fact =>
                    fact != null &&
                    string.Equals(
                        fact.subjectEntityId,
                        subject,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        fact.predicateId,
                        predicateId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        fact.objectKind,
                        "number",
                        StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(fact.objectValueJson))
                .Select(fact => fact.objectValueJson.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return projected.Length == 1 &&
                   float.TryParse(
                       projected[0],
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out value) &&
                   float.IsFinite(value) &&
                   value > 0f;
        }

        public static bool ProjectionDeclaresCanonicalFact(
            OntologyAuthorityWorldProjection projection,
            Guid entityId,
            string predicateId,
            string objectId)
        {
            if (projection?.facts == null ||
                entityId == Guid.Empty ||
                string.IsNullOrWhiteSpace(predicateId) ||
                string.IsNullOrWhiteSpace(objectId))
            {
                return false;
            }

            var subjectId = entityId.ToString("D");
            return projection.facts.Any(fact =>
                fact != null &&
                string.Equals(
                    fact.subjectEntityId,
                    subjectId,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    fact.predicateId,
                    predicateId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    fact.objectKind,
                    "canonical",
                    StringComparison.Ordinal) &&
                string.Equals(
                    fact.objectCanonicalId,
                    objectId,
                    StringComparison.Ordinal));
        }

        public static bool ProjectionDeclaresConcept(
            OntologyAuthorityWorldProjection projection,
            Guid entityId,
            string conceptId)
        {
            if (projection?.facts == null ||
                entityId == Guid.Empty ||
                string.IsNullOrWhiteSpace(conceptId))
            {
                return false;
            }

            var subjectId = entityId.ToString("D");
            return projection.facts.Any(fact =>
                fact != null &&
                string.Equals(
                    fact.subjectEntityId,
                    subjectId,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    fact.predicateId,
                    OntologyPredicates.HasConcept,
                    StringComparison.Ordinal) &&
                string.Equals(
                    fact.objectKind,
                    "canonical",
                    StringComparison.Ordinal) &&
                string.Equals(
                    fact.objectCanonicalId,
                    conceptId,
                    StringComparison.Ordinal));
        }

        private IEnumerator RefreshCombatPackageAndEquipRoutine(
            Vector2 screenPosition)
        {
            actionPending = true;
            var prepared = false;
            yield return authorityClient.VerifyDevelopmentContentReleaseRoutine(
                value => prepared = value);
            actionPending = false;
            if (prepared)
            {
                ShowCombatStatus(
                    "equip_rule_ready",
                    CombatStatusSeverity.Positive);
                TryEquip(screenPosition);
            }
            else
            {
                LogTechnicalFailure(
                    "equip_rule_prepare_failed",
                    authorityClient.LastStatus);
                ShowCombatStatus(
                    "equip_rule_prepare_failed",
                    CombatStatusSeverity.Negative);
            }
        }

        private IEnumerator RefreshCombatPackageAndUnequipRoutine(
            Guid equippedItemId)
        {
            actionPending = true;
            var prepared = false;
            yield return authorityClient.VerifyDevelopmentContentReleaseRoutine(
                value => prepared = value);
            actionPending = false;
            if (prepared)
            {
                TryUnequip(equippedItemId);
            }
            else
            {
                LogTechnicalFailure(
                    "unequip_rule_unavailable",
                    authorityClient.LastStatus);
                ShowCombatStatus(
                    "unequip_rule_unavailable",
                    CombatStatusSeverity.Negative);
            }
        }

        private void ShowCombatStatus(
            string statusId,
            CombatStatusSeverity severity,
            params object[] arguments)
        {
            var keyRoot = "ui.combat.status." + statusId;
            var title = OntologyLanguagePackService.Format(
                keyRoot + ".title",
                OntologyLanguagePackService.Text(
                    "ui.combat.status.generic.title",
                    "Combat"),
                arguments);
            var detail = OntologyLanguagePackService.Format(
                keyRoot + ".detail",
                OntologyLanguagePackService.Text(
                    "ui.combat.status.generic.detail",
                    "The combat state changed."),
                arguments);
            var message = "[OntologyCombat] " + title + ": " + detail;
            if (severity == CombatStatusSeverity.Negative)
                Debug.LogError(message, this);
            else if (severity == CombatStatusSeverity.Warning)
                Debug.LogWarning(message, this);
            else
                Debug.Log(message, this);
        }

        private void LogTechnicalFailure(
            string operation,
            string technicalStatus)
        {
            if (string.IsNullOrWhiteSpace(technicalStatus))
                return;
            Debug.LogWarning(
                "[OntologyCombat] " + operation +
                " technical_status=" + technicalStatus,
                this);
        }

        private void ApplyEquipmentProjection(
            OntologyAuthorityWorldProjection projection)
        {
            if (actorIdentity == null ||
                !actorIdentity.TryGetGuid(out var actorId) ||
                projection?.facts == null)
            {
                return;
            }

            var equippedIds = projection.facts
                .Where(fact =>
                    fact != null &&
                    fact.predicateId ==
                        OntologyPredicates.EquippedBy &&
                    string.Equals(
                        fact.objectEntityId,
                        actorId.ToString("D"),
                        StringComparison.OrdinalIgnoreCase))
                .Select(fact =>
                    Guid.TryParse(
                        fact.subjectEntityId,
                        out var equippedId)
                        ? equippedId
                        : Guid.Empty)
                .Where(value => value != Guid.Empty)
                .Distinct()
                .ToArray();
            var equippedWeaponIds = equippedIds
                .Where(value =>
                    ProjectionDeclaresConcept(
                        projection,
                        value,
                        OntologyConcepts.Weapon))
                .ToArray();
            // F toggles every equipped relation that explicitly authors an
            // unequip_action. Weapon filtering belongs only to combat
            // presentation; proximity wearables without that action must not
            // make an unrelated right-hand F interaction ambiguous.
            projectedEquippedItemIds = equippedIds
                .Where(value => TryResolveCanonicalFact(
                    projection,
                    value,
                    OntologyPredicates.UnequipAction,
                    out _))
                .ToArray();
            if (equippedWeaponIds.Length > 1)
            {
                Debug.LogError(
                    "[OntologyCombat] Ambiguous equipped weapon projection. " +
                    "Presentation was disabled until Authority repairs it.",
                    this);
            }
            var equippedId = equippedWeaponIds.Length == 1
                ? equippedWeaponIds[0]
                : Guid.Empty;
            projectedEquippedItemId = equippedId;

            equippedToolIdentity = equippedId == Guid.Empty
                ? null
                : FindObjectsByType<OntologyAuthorityEntityIdentity>(
                        FindObjectsInactive.Include)
                    .FirstOrDefault(value =>
                        value != null &&
                        value.TryGetGuid(out var candidateId) &&
                        candidateId == equippedId);
            equippedWeapon = equippedToolIdentity == null
                ? null
                : equippedToolIdentity.GetComponent<OntologyCombatWeaponPresenter>();
        }

        public void Configure(
            OntologyWorldAuthorityClient client,
            OntologyAuthorityEntityIdentity actor,
            OntologyCombatWeaponPresenter weapon,
            Camera camera)
        {
            if (subscribedAuthorityClient != null &&
                subscribedAuthorityClient != client)
            {
                subscribedAuthorityClient.ProjectionReceived -=
                    ApplyEquipmentProjection;
                subscribedAuthorityClient = null;
            }
            authorityClient = client;
            actorIdentity = actor;
            actorAnimationAdapter = actor == null
                ? GetComponentInChildren<OntologyAnimationAdapter>(true)
                : actor.GetComponentInChildren<OntologyAnimationAdapter>(true);
            equippedWeapon = weapon;
            equippedToolIdentity = weapon == null ? null : weapon.AuthorityIdentity;
            worldCamera = camera;
            if (isActiveAndEnabled && authorityClient != null &&
                subscribedAuthorityClient == null)
            {
                subscribedAuthorityClient = authorityClient;
                subscribedAuthorityClient.ProjectionReceived +=
                    ApplyEquipmentProjection;
                ApplyEquipmentProjection(
                    subscribedAuthorityClient.CurrentProjection);
            }
        }

        /// <summary>
        /// Begins an Authority-owned interpretation of the shared primary
        /// pointer intent. Unity contributes only the pointer observation and
        /// action identities authored on the equipped tool. The callback is
        /// true only when both assigned Rule Blocks accept the route.
        /// </summary>
        public bool TryBeginPrimaryPointerIntent(
            Vector2 screenPosition,
            Action<bool> completed)
        {
            if (!TryResolvePrimaryPointerTarget(
                    screenPosition,
                    out var targetIdentity,
                    out var hitPoint))
            {
                return RejectPrimaryPointerRoute(
                    "projected_target_not_selected");
            }

            return TryBeginPrimaryTargetIntent(
                targetIdentity,
                hitPoint,
                completed);
        }

        /// <summary>
        /// Resolves only a projected combat target under the pointer. Callers can
        /// therefore consume a monster click even while an earlier attack route
        /// is busy instead of leaking the same click into terrain navigation.
        /// </summary>
        public bool TryResolvePrimaryPointerTarget(
            Vector2 screenPosition,
            out OntologyAuthorityEntityIdentity targetIdentity,
            out Vector3 hitPoint)
        {
            targetIdentity = null;
            hitPoint = default;
            if (authorityClient == null ||
                !authorityClient.IsWorldRuntimeReady ||
                actorIdentity == null ||
                equippedToolIdentity == null ||
                worldCamera == null ||
                targetingAdapter == null ||
                !targetingAdapter.TryResolvePointerCandidate(
                    worldCamera,
                    screenPosition,
                    actorIdentity,
                    equippedToolIdentity,
                    authorityClient,
                    out var candidate,
                    out hitPoint) ||
                candidate == null ||
                candidate.GetComponentInParent<
                    OntologyCombatTargetPresenter>() == null ||
                !candidate.TryGetGuid(out var candidateId) ||
                IsDefeated(authorityClient.CurrentProjection, candidateId))
            {
                return false;
            }

            targetIdentity = candidate;
            return true;
        }

        /// <summary>
        /// Re-evaluates one already observed target after navigation. World
        /// Authority remains the owner of range, cooldown, conditions, damage,
        /// and animation intent.
        /// </summary>
        public bool TryBeginPrimaryTargetIntent(
            OntologyAuthorityEntityIdentity targetIdentity,
            Vector3 hitPoint,
            Action<bool> completed)
        {
            _ = hitPoint;
            if (actionPending || primaryPointerRoutePending)
            {
                return RejectPrimaryPointerRoute("attack_route_busy");
            }
            if (authorityClient == null ||
                !authorityClient.IsWorldRuntimeReady)
                return RejectPrimaryPointerRoute("authority_not_ready");
            if (actorIdentity == null ||
                !actorIdentity.TryGetGuid(out var actorId))
                return RejectPrimaryPointerRoute("actor_identity_missing");
            if (equippedToolIdentity == null ||
                !equippedToolIdentity.TryGetGuid(out var toolId))
                return RejectPrimaryPointerRoute("equipped_tool_missing");
            if (targetIdentity == null ||
                !targetIdentity.TryGetGuid(out var targetId) ||
                !authorityClient.ContainsProjectedEntity(targetId) ||
                IsDefeated(authorityClient.CurrentProjection, targetId))
            {
                return RejectPrimaryPointerRoute(
                    "projected_target_not_selected");
            }
            if (!TryResolveSwingActionId(toolId, out var swingActionId))
                return RejectPrimaryPointerRoute(
                    "equipped_tool_swing_action_missing");
            if (!authorityClient.TryResolveEnabledAction(
                    swingActionId,
                    out var swingDefinition))
                return RejectPrimaryPointerRoute(
                    "swing_action_not_enabled:" + swingActionId);
            if (!TryResolveAttackActionId(toolId, out var attackActionId))
                return RejectPrimaryPointerRoute(
                    "equipped_tool_attack_action_missing");
            if (!authorityClient.TryResolveEnabledAction(
                    attackActionId,
                    out var attackDefinition))
                return RejectPrimaryPointerRoute(
                    "attack_action_not_enabled:" + attackActionId);
            if (!TryResolveCanonicalFact(
                    authorityClient.CurrentProjection,
                    toolId,
                    OntologyPredicates.AttackContactMode,
                    out var contactModeId))
            {
                return RejectPrimaryPointerRoute(
                    "attack_contact_mode_missing");
            }
            if (equippedWeapon == null ||
                !equippedWeapon.SupportsContactMode(contactModeId))
            {
                return RejectPrimaryPointerRoute(
                    "attack_contact_adapter_unavailable:" +
                    contactModeId);
            }
            var targetPresenter =
                targetIdentity.GetComponentInParent<
                    OntologyCombatTargetPresenter>();
            if (targetPresenter == null)
            {
                return RejectPrimaryPointerRoute(
                    "attack_target_adapter_missing");
            }
            if (TryResolveEquippedContactApproachDistance(
                    targetIdentity,
                    out var contactApproachDistance))
            {
                var toTarget =
                    targetIdentity.transform.position -
                    actorIdentity.transform.position;
                toTarget.y = 0f;
                if (toTarget.magnitude > contactApproachDistance)
                {
                    return RejectPrimaryPointerRoute(
                        "attack_contact_approach_required");
                }
            }

            primaryPointerRoutePending = true;
            SetAttackDiagnostic("authority_route_preview_requested");
            StartCoroutine(EvaluatePrimaryPointerRouteRoutine(
                actorId,
                toolId,
                targetId,
                swingDefinition,
                attackDefinition,
                targetPresenter,
                completed));
            return true;
        }

        public bool TryResolveEquippedAttackRange(out float attackRange)
        {
            attackRange = 0f;
            if (equippedToolIdentity == null ||
                !equippedToolIdentity.TryGetGuid(out var toolId) ||
                authorityClient?.CurrentProjection?.facts == null)
            {
                return false;
            }

            var subjectId = toolId.ToString("D");
            var matches = authorityClient.CurrentProjection.facts
                .Where(fact =>
                    fact != null &&
                    string.Equals(
                        fact.subjectEntityId,
                        subjectId,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        fact.predicateId,
                        "attack_range",
                        StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(fact.objectValueJson))
                .Select(fact => fact.objectValueJson.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return matches.Length == 1 &&
                   float.TryParse(
                       matches[0],
                       System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture,
                       out attackRange) &&
                   attackRange > 0f;
        }

        public bool TryResolveEquippedContactApproachDistance(
            OntologyAuthorityEntityIdentity targetIdentity,
            out float approachDistance)
        {
            approachDistance = 0f;
            if (equippedWeapon == null ||
                actorIdentity == null ||
                targetIdentity == null)
            {
                return false;
            }

            var targetPresenter =
                targetIdentity.GetComponentInParent<
                    OntologyCombatTargetPresenter>();
            return equippedWeapon.TryEstimateContactApproachDistance(
                actorIdentity.transform.position,
                targetPresenter,
                out approachDistance);
        }

        /// <summary>
        /// Resolves the minimum planar pivot separation required to keep the
        /// actor and approved target presentation bodies from overlapping.
        /// Collider geometry is Physical Meaning data; this calculation never
        /// grants attack permission or changes an Authority result.
        /// </summary>
        public bool TryResolveCombatBodyClearanceDistance(
            OntologyAuthorityEntityIdentity targetIdentity,
            Collider actorCollider,
            out float clearanceDistance)
        {
            clearanceDistance = 0f;
            if (actorIdentity == null ||
                actorCollider == null ||
                !actorCollider.enabled ||
                targetIdentity == null)
            {
                return false;
            }

            var targetPresenter =
                targetIdentity.GetComponentInParent<
                    OntologyCombatTargetPresenter>();
            var targetCollider = targetPresenter?.InteractionCollider;
            if (targetCollider == null || !targetCollider.enabled)
                return false;

            var presentationPadding =
                actorCollider is CharacterController controller
                    ? Mathf.Max(0f, controller.skinWidth)
                    : 0f;
            clearanceDistance = CalculatePlanarBodyClearanceDistance(
                actorCollider.bounds,
                actorIdentity.transform.position,
                targetCollider.bounds,
                targetIdentity.transform.position,
                presentationPadding);
            return clearanceDistance > 0f;
        }

        public static float CalculatePlanarBodyClearanceDistance(
            Bounds actorBounds,
            Vector3 actorPivot,
            Bounds targetBounds,
            Vector3 targetPivot,
            float presentationPadding = 0f)
        {
            var pivotDirection = targetPivot - actorPivot;
            pivotDirection.y = 0f;
            if (pivotDirection.sqrMagnitude <= Mathf.Epsilon)
                return 0f;

            var direction = pivotDirection.normalized;
            var actorCenterOffset = actorBounds.center - actorPivot;
            actorCenterOffset.y = 0f;
            var targetCenterOffset = targetBounds.center - targetPivot;
            targetCenterOffset.y = 0f;

            var actorForwardSurface =
                Vector3.Dot(actorCenterOffset, direction) +
                CalculatePlanarBoundsSupportRadius(
                    actorBounds.extents,
                    direction);
            var targetNearSurface =
                Vector3.Dot(targetCenterOffset, direction) -
                CalculatePlanarBoundsSupportRadius(
                    targetBounds.extents,
                    direction);
            return Mathf.Max(
                0f,
                actorForwardSurface -
                targetNearSurface +
                Mathf.Max(0f, presentationPadding));
        }

        private static float CalculatePlanarBoundsSupportRadius(
            Vector3 extents,
            Vector3 planarDirection)
        {
            return Mathf.Abs(planarDirection.x) * Mathf.Abs(extents.x) +
                   Mathf.Abs(planarDirection.z) * Mathf.Abs(extents.z);
        }

        public bool IsProjectedLivingCombatTarget(
            OntologyAuthorityEntityIdentity targetIdentity)
        {
            return targetIdentity != null &&
                   targetIdentity.GetComponentInParent<
                       OntologyCombatTargetPresenter>() != null &&
                   targetIdentity.TryGetGuid(out var targetId) &&
                   authorityClient != null &&
                   authorityClient.ContainsProjectedEntity(targetId) &&
                   !IsDefeated(authorityClient.CurrentProjection, targetId);
        }

        public static bool RequiresApproach(string diagnostic)
        {
            return string.Equals(
                       diagnostic,
                       "attack_preview_rejected:action_target_out_of_range",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       diagnostic,
                       "attack_contact_approach_required",
                       StringComparison.Ordinal);
        }

        public static bool ShouldRetryWithoutMovement(string diagnostic)
        {
            return string.Equals(
                       diagnostic,
                       "attack_route_busy",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       diagnostic,
                       "attack_preview_rejected:action_cooldown_active",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       diagnostic,
                       "swing_preview_rejected:action_cooldown_active",
                       StringComparison.Ordinal);
        }

        private bool RejectPrimaryPointerRoute(string diagnostic)
        {
            SetAttackDiagnostic(diagnostic);
            return false;
        }

        private IEnumerator EvaluatePrimaryPointerRouteRoutine(
            Guid actorId,
            Guid toolId,
            Guid targetId,
            OntologyAuthorityActionDefinitionProjection swingDefinition,
            OntologyAuthorityActionDefinitionProjection attackDefinition,
            OntologyCombatTargetPresenter target,
            Action<bool> completed)
        {
            OntologyAuthorityAttackOccurrenceResult attackOccurrence = null;
            yield return authorityClient.BeginPlayerAttackOccurrenceRoutine(
                actorId,
                targetId,
                toolId,
                attackDefinition,
                value => attackOccurrence = value);
            if (attackOccurrence == null || !attackOccurrence.accepted ||
                !Guid.TryParse(attackOccurrence.occurrenceId,
                    out var attackOccurrenceId))
            {
                FinishPrimaryPointerRoute(
                    completed,
                    false,
                    "attack_preview_rejected:" +
                    (attackOccurrence?.rejectionCode ??
                     "authority_no_response"));
                yield break;
            }

            OntologyAuthorityActionPreviewResult swingPreview = null;
            yield return authorityClient.PreviewActionRoutine(
                actorId,
                actorId,
                toolId,
                swingDefinition,
                value => swingPreview = value);
            if (swingPreview == null || !swingPreview.accepted)
            {
                FinishPrimaryPointerRoute(
                    completed,
                    false,
                    "swing_preview_rejected:" +
                    (swingPreview?.rejectionCode ??
                     "authority_no_response"));
                yield break;
            }

            FinishPrimaryPointerRoute(
                completed,
                true,
                "authority_route_preview_accepted");
            yield return ExecuteSwingRoutine(
                actorId,
                toolId,
                swingDefinition,
                attackOccurrenceId,
                true,
                targetId,
                attackDefinition,
                target);
        }

        private void FinishPrimaryPointerRoute(
            Action<bool> completed,
            bool consumed,
            string diagnostic)
        {
            primaryPointerRoutePending = false;
            SetAttackDiagnostic(diagnostic);
            completed?.Invoke(consumed);
        }

        private bool TryResolveAttackActionId(
            Guid toolId,
            out string actionId)
        {
            return TryResolveCanonicalFact(
                authorityClient?.CurrentProjection,
                toolId,
                OntologyPredicates.AttackAction,
                out actionId);
        }

        private bool TryResolveSwingActionId(
            Guid toolId,
            out string actionId)
        {
            return TryResolveCanonicalFact(
                authorityClient?.CurrentProjection,
                toolId,
                OntologyPredicates.SwingAction,
                out actionId);
        }

        public static bool TryResolveCanonicalFact(
            OntologyAuthorityWorldProjection projection,
            Guid entityId,
            string predicateId,
            out string value)
        {
            value = string.Empty;
            if (projection?.facts == null ||
                entityId == Guid.Empty ||
                string.IsNullOrWhiteSpace(predicateId))
            {
                return false;
            }

            var subjectId = entityId.ToString("D");
            var matches = projection.facts
                .Where(fact =>
                    fact != null &&
                    string.Equals(
                        fact.subjectEntityId,
                        subjectId,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        fact.predicateId,
                        predicateId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        fact.objectKind,
                        "canonical",
                        StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(
                        fact.objectCanonicalId))
                .Select(fact => fact.objectCanonicalId.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (matches.Length != 1) return false;
            value = matches[0];
            return true;
        }

        private IEnumerator ExecuteSwingRoutine(
            Guid actorId,
            Guid toolId,
            OntologyAuthorityActionDefinitionProjection swingDefinition,
            Guid attackOccurrenceId,
            bool hasDamageTarget,
            Guid targetId,
            OntologyAuthorityActionDefinitionProjection attackDefinition,
            OntologyCombatTargetPresenter target)
        {
            actionPending = true;
            OntologyAuthorityRuntimeActionResult swingResult = null;
            yield return authorityClient.SendRuntimeActionRoutine(
                actorId,
                actorId,
                toolId,
                swingDefinition,
                value => swingResult = value);
            if (swingResult == null || !swingResult.accepted)
            {
                SetAttackDiagnostic(
                    "swing_rejected:" +
                    (swingResult?.rejectionCode ??
                     "authority_no_response"));
                actionPending = false;
                yield break;
            }

            SetAttackDiagnostic("swing_accepted");
            var contactObserved = false;
            var contactPoint = default(Vector3);
            yield return WaitForApprovedWeaponContactRoutine(
                swingResult.actorAnimationIntent,
                hasDamageTarget ? target : null,
                (observed, point) =>
                {
                    contactObserved = observed;
                    contactPoint = point;
                });
            if (hasDamageTarget && attackDefinition != null)
            {
                if (contactObserved)
                {
                    yield return ExecuteDamageRoutine(
                        attackOccurrenceId,
                        targetId,
                        target,
                        contactPoint);
                }
                else
                {
                    SetAttackDiagnostic(
                        "attack_contact_window_completed_without_contact");
                }
            }
            else
            {
                SetAttackDiagnostic("swing_completed_without_target");
            }
            actionPending = false;
        }

        private IEnumerator WaitForApprovedWeaponContactRoutine(
            string animationIntent,
            OntologyCombatTargetPresenter approvedTarget,
            Action<bool, Vector3> completed)
        {
            if (actorAnimationAdapter == null)
            {
                actorAnimationAdapter =
                    actorIdentity == null
                        ? GetComponentInChildren<
                            OntologyAnimationAdapter>(true)
                        : actorIdentity.GetComponentInChildren<
                            OntologyAnimationAdapter>(true);
            }
            if (actorAnimationAdapter == null ||
                equippedWeapon == null ||
                string.IsNullOrWhiteSpace(animationIntent))
            {
                SetAttackDiagnostic(
                    "attack_contact_contract_not_ready");
                completed?.Invoke(false, default);
                yield break;
            }

            if (!actorAnimationAdapter.TryResolveContactWindowContract(
                    animationIntent,
                    out var clipDuration,
                    out _,
                    out _))
            {
                SetAttackDiagnostic(
                    "attack_animation_contact_window_missing");
                completed?.Invoke(false, default);
                yield break;
            }

            equippedWeapon.BeginSweepTracking(clipDuration);
            var contactContractObserved = false;
            var sweepVfxRequested = false;
            var observationDeadline =
                Time.unscaledTime + Mathf.Max(0.05f, clipDuration);
            while (Time.unscaledTime <= observationDeadline)
            {
                if (!actorAnimationAdapter.TryGetActiveContactWindow(
                        animationIntent,
                        out var normalizedTime,
                        out var windowStart,
                        out var windowEnd))
                {
                    equippedWeapon.PrimeContactSample();
                    if (contactContractObserved)
                        break;
                    yield return null;
                    continue;
                }

                contactContractObserved = true;
                if (normalizedTime > windowEnd)
                    break;
                if (normalizedTime < windowStart)
                {
                    equippedWeapon.PrimeContactSample();
                    yield return null;
                    continue;
                }
                if (OntologyAnimationAdapter.IsWithinContactWindow(
                        normalizedTime,
                        windowStart,
                        windowEnd))
                {
                    if (!sweepVfxRequested &&
                        equippedWeapon.TryGetDynamicSweepVfxAnchor(
                            out var sweepAnchor))
                    {
                        OntologyCombatPresentationBus.RequestVfx(
                            equippedWeapon.SwingVfxIntent,
                            sweepAnchor,
                            sweepAnchor.position,
                            sweepAnchor.rotation);
                        sweepVfxRequested = true;
                    }
                    if (approvedTarget != null &&
                        equippedWeapon.TryObserveApprovedContact(
                            approvedTarget,
                            out var contactPoint))
                    {
                        SetAttackDiagnostic(
                            "approved_weapon_contact_observed");
                        completed?.Invoke(true, contactPoint);
                        yield break;
                    }
                }

                yield return null;
            }

            if (!contactContractObserved)
            {
                SetAttackDiagnostic(
                    "attack_animation_contact_window_missing");
            }
            completed?.Invoke(false, default);
        }

        private IEnumerator ExecuteDamageRoutine(
            Guid attackOccurrenceId,
            Guid targetId,
            OntologyCombatTargetPresenter target,
            Vector3 hitPoint)
        {
            OntologyAuthorityAttackContactResult result = null;
            yield return authorityClient.ResolvePlayerAttackContactRoutine(
                attackOccurrenceId,
                value => result = value);
            if (result != null && result.accepted)
            {
                SetAttackDiagnostic("damage_authority_accepted");
                var loaded = false;
                yield return authorityClient.LoadWorldRoutine(
                    value => loaded = value);
                if (loaded && target != null)
                {
                    target.PresentConfirmedHit(
                        IsDefeated(authorityClient.CurrentProjection, targetId),
                        hitPoint);
                }
            }
            else
            {
                SetAttackDiagnostic(
                    "damage_authority_rejected:" +
                    (result?.rejectionCode ?? "authority_no_response"));
            }
        }

        private void SetAttackDiagnostic(string value)
        {
            var next = value ?? string.Empty;
            if (string.Equals(
                    lastAttackDiagnostic,
                    next,
                    StringComparison.Ordinal))
            {
                return;
            }

            lastAttackDiagnostic = next;
            Debug.Log(
                "[OntologyCombat] attack_stage=" + lastAttackDiagnostic,
                this);
        }

        private static bool IsDefeated(
            OntologyAuthorityWorldProjection projection,
            Guid targetId)
        {
            if (projection?.facts == null) return false;
            var id = targetId.ToString("D");
            foreach (var fact in projection.facts)
            {
                if (fact == null ||
                    !string.Equals(fact.subjectEntityId, id, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (fact.predicateId == OntologyPredicates.IsAlive &&
                    (string.Equals(
                         fact.objectCanonicalId,
                         "False",
                         StringComparison.Ordinal) ||
                     string.Equals(
                         fact.objectValueJson,
                         "false",
                         StringComparison.OrdinalIgnoreCase)))
                    return true;
                if (fact.predicateId == OntologyPredicates.CurrentHealth &&
                    long.TryParse(fact.objectValueJson, out var health) &&
                    health <= 0)
                    return true;
            }
            return false;
        }
    }
}
