using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presentation gate for the account-to-world boundary. Explicit hierarchy
    /// assignments remain editable; known runtime components are discovered only
    /// as a safe scene fallback.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyWorldRuntimeActivationGate : MonoBehaviour
    {
        [SerializeField] private OntologyGameSessionCoordinator sessionCoordinator;
        [SerializeField] private GameObject[] runtimeRoots = Array.Empty<GameObject>();
        [SerializeField] private Behaviour[] runtimeBehaviours = Array.Empty<Behaviour>();
        [SerializeField] private Renderer[] localAvatarRenderers = Array.Empty<Renderer>();
        [SerializeField] private bool discoverKnownRuntimeObjects = true;

        private readonly List<GameObject> discoveredRoots = new();
        private readonly List<GameObject> discoveredTransientRoots = new();
        private readonly List<Behaviour> discoveredBehaviours = new();
        private readonly List<Renderer> discoveredRenderers = new();
        private OntologyGameSessionCoordinator subscribedCoordinator;

        public bool RuntimeVisible { get; private set; }

        private void Awake()
        {
            ResolveDependencies();
            DiscoverFallbackTargets();
            Apply();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            Subscribe();
            Apply();
        }

        private void Start()
        {
            ResolveDependencies();
            Subscribe();
            RefreshTargets();
            Apply();
        }

        private void OnDisable()
        {
            if (subscribedCoordinator != null)
                subscribedCoordinator.StateChanged -= OnSessionStateChanged;
            subscribedCoordinator = null;
        }

        public void RefreshTargets()
        {
            ResolveDependencies();
            Subscribe();
            discoveredRoots.Clear();
            discoveredTransientRoots.Clear();
            discoveredBehaviours.Clear();
            discoveredRenderers.Clear();
            DiscoverFallbackTargets();
            Apply();
        }

        private void OnSessionStateChanged(OntologyGameSessionState previous, OntologyGameSessionState next) => Apply();

        private void Apply()
        {
            var active = sessionCoordinator != null && sessionCoordinator.IsInWorld;
            RuntimeVisible = active;

            SetGameObjects(runtimeRoots, active);
            SetGameObjects(discoveredRoots, active);
            if (!active)
                SetGameObjects(discoveredTransientRoots, false);
            SetBehaviours(runtimeBehaviours, active);
            SetBehaviours(discoveredBehaviours, active);
            SetRenderers(localAvatarRenderers, active);
            SetRenderers(discoveredRenderers, active);
        }

        private void ResolveDependencies()
        {
            if (sessionCoordinator == null)
                sessionCoordinator = FindAnyObjectByType<OntologyGameSessionCoordinator>();
        }

        private void Subscribe()
        {
            if (subscribedCoordinator == sessionCoordinator) return;
            if (subscribedCoordinator != null)
                subscribedCoordinator.StateChanged -= OnSessionStateChanged;
            subscribedCoordinator = sessionCoordinator;
            if (subscribedCoordinator != null)
                subscribedCoordinator.StateChanged += OnSessionStateChanged;
        }

        private void DiscoverFallbackTargets()
        {
            if (!discoverKnownRuntimeObjects) return;

            AddRoot(FindAnyObjectByType<OntologyRuntimeStatusHUD>(FindObjectsInactive.Include));
            AddRoot(FindAnyObjectByType<OntologyActorToast>(FindObjectsInactive.Include));
            var questPanel = FindAnyObjectByType<OntologyQuestActionPanel>(FindObjectsInactive.Include);
            AddRoot(questPanel);
            if (questPanel != null && questPanel.RuntimeToggleObject != null &&
                !discoveredRoots.Contains(questPanel.RuntimeToggleObject))
            {
                discoveredRoots.Add(questPanel.RuntimeToggleObject);
            }
            var placementPanel = FindAnyObjectByType<OntologyObjectPlacementPanel>(
                FindObjectsInactive.Include);
            AddRoot(FindAnyObjectByType<OntologyRuntimeWorldEditorPanel>(
                FindObjectsInactive.Include));
            AddRoot(FindAnyObjectByType<OntologyRuntimeWorldEditHandle>(
                FindObjectsInactive.Include));
            if (placementPanel != null && placementPanel.RuntimeToggleObject != null &&
                !discoveredRoots.Contains(placementPanel.RuntimeToggleObject))
            {
                discoveredRoots.Add(placementPanel.RuntimeToggleObject);
            }
            if (placementPanel != null &&
                placementPanel.RuntimePanelObject != null &&
                !discoveredTransientRoots.Contains(
                    placementPanel.RuntimePanelObject))
            {
                discoveredTransientRoots.Add(
                    placementPanel.RuntimePanelObject);
            }
            foreach (var customization in FindObjectsByType<OntologyCharacterCustomizationPanel>(
                         FindObjectsInactive.Include))
            {
                if (customization != null && customization.RuntimeToggleEnabled)
                {
                    AddRoot(customization);
                    var toggleObject = customization.RuntimeToggleObject;
                    if (toggleObject != null && !discoveredRoots.Contains(toggleObject))
                        discoveredRoots.Add(toggleObject);
                }
            }
            AddRoot(placementPanel);
            AddRoot(FindAnyObjectByType<OntologyRuntimeWorldFactEditorPanel>(FindObjectsInactive.Include));

            AddBehaviour(FindAnyObjectByType<OntologyInputSystemPlayerInput>(FindObjectsInactive.Include));
            AddBehaviour(FindAnyObjectByType<OntologyPlayerController>(FindObjectsInactive.Include));
            AddBehaviour(FindAnyObjectByType<OntologyPlayerPositionTracker>(FindObjectsInactive.Include));
            AddBehaviour(FindAnyObjectByType<OntologyWorldAuthorityPlayerIntentSender>(FindObjectsInactive.Include));
            AddBehaviour(FindAnyObjectByType<OntologyWorldAuthorityPlayerMotionReconciler>(FindObjectsInactive.Include));
            AddBehaviour(FindAnyObjectByType<OntologyWorldAuthorityRealtimeClient>(FindObjectsInactive.Include));
            AddBehaviour(FindAnyObjectByType<OntologyWorldAuthorityRemoteAvatarPresenter>(FindObjectsInactive.Include));
            AddBehaviour(FindAnyObjectByType<OntologyWorldZoneStreamer>(FindObjectsInactive.Include));
            AddBehaviour(FindAnyObjectByType<OntologyRuntimeObjectPlacementController>(FindObjectsInactive.Include));
            AddBehaviour(FindAnyObjectByType<OntologyRuntimeWorldEditorController>(FindObjectsInactive.Include));
            AddBehaviour(FindAnyObjectByType<OntologyObjectPlacementPanel>(FindObjectsInactive.Include));
            AddBehaviour(FindAnyObjectByType<OntologyRuntimeWorldEditorPanel>(FindObjectsInactive.Include));
            AddBehaviour(FindAnyObjectByType<OntologyRuntimeWorldFactEditorPanel>(FindObjectsInactive.Include));

            var entryFlow = FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>(
                FindObjectsInactive.Include);
            var identity = entryFlow == null ? null : entryFlow.AvatarIdentity;
            if (identity != null)
                discoveredRenderers.AddRange(identity.GetComponentsInChildren<Renderer>(true));
        }

        private void AddRoot(Component component)
        {
            if (component != null && component.gameObject != gameObject && !discoveredRoots.Contains(component.gameObject))
                discoveredRoots.Add(component.gameObject);
        }

        private void AddBehaviour(Behaviour behaviour)
        {
            if (behaviour != null && behaviour != this && !discoveredBehaviours.Contains(behaviour))
                discoveredBehaviours.Add(behaviour);
        }

        private static void SetGameObjects(IEnumerable<GameObject> targets, bool active)
        {
            if (targets == null) return;
            foreach (var target in targets)
                if (target != null && target.activeSelf != active) target.SetActive(active);
        }

        private static void SetBehaviours(IEnumerable<Behaviour> targets, bool active)
        {
            if (targets == null) return;
            foreach (var target in targets)
                if (target != null) target.enabled = active;
        }

        private static void SetRenderers(IEnumerable<Renderer> targets, bool active)
        {
            if (targets == null) return;
            foreach (var target in targets)
                if (target != null) target.enabled = active;
        }
    }
}
