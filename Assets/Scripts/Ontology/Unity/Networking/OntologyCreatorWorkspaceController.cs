using System;
using Tormia.Ontology.Core;
using UnityEngine;

namespace Tormia.Ontology.Unity.Networking
{
    /// <summary>
    /// Keeps creator assistants outside the normal world lifecycle. The workspace
    /// is visible only after the owning account has entered its selected world
    /// and explicitly has creator mode enabled.
    /// </summary>
    public sealed class OntologyCreatorWorkspaceController : MonoBehaviour
    {
        [SerializeField] private GameObject presentationRoot;
        [SerializeField] private OntologyGameSessionCoordinator sessionCoordinator;
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private OntologyCreatorServiceCatalog serviceCatalog;
        [SerializeField] private bool creatorModeEnabled = true;

        public GameObject PresentationRoot => presentationRoot;
        public bool CreatorModeEnabled => creatorModeEnabled;
        public OntologyCreatorServiceCatalog ServiceCatalog => serviceCatalog;
        public bool IsWorkspaceVisible => presentationRoot != null && presentationRoot.activeSelf;

        private void Awake()
        {
            ResolveDependencies();
            RefreshVisibility();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            if (sessionCoordinator != null) sessionCoordinator.StateChanged += HandleSessionStateChanged;
            if (authorityClient != null) authorityClient.StateChanged += RefreshVisibility;
            RefreshVisibility();
        }

        private void OnDisable()
        {
            if (sessionCoordinator != null) sessionCoordinator.StateChanged -= HandleSessionStateChanged;
            if (authorityClient != null) authorityClient.StateChanged -= RefreshVisibility;
        }

        public void SetCreatorModeEnabled(bool enabled)
        {
            creatorModeEnabled = enabled;
            RefreshVisibility();
        }

        public void Configure(
            GameObject root,
            OntologyGameSessionCoordinator coordinator,
            OntologyWorldAuthorityClient client,
            OntologyCreatorServiceCatalog catalog = null)
        {
            presentationRoot = root;
            sessionCoordinator = coordinator;
            authorityClient = client;
            serviceCatalog = catalog;
            RefreshVisibility();
        }

        public OntologyCreatorServiceDefinition FindService(string serviceId) =>
            serviceCatalog == null ? null : serviceCatalog.Find(serviceId);

        public static bool ShouldShowWorkspace(
            bool creatorMode,
            bool isInWorld,
            bool isWorldRuntimeReady,
            string worldRole)
        {
            return creatorMode &&
                   isInWorld &&
                   isWorldRuntimeReady &&
                   string.Equals(worldRole, "owner", StringComparison.OrdinalIgnoreCase);
        }

        private void HandleSessionStateChanged(
            OntologyGameSessionState previous,
            OntologyGameSessionState next)
        {
            RefreshVisibility();
        }

        private void ResolveDependencies()
        {
            if (sessionCoordinator == null)
            {
                sessionCoordinator = FindAnyObjectByType<OntologyGameSessionCoordinator>();
            }

            if (authorityClient == null)
            {
                authorityClient = FindAnyObjectByType<OntologyWorldAuthorityClient>();
            }
        }

        private void RefreshVisibility()
        {
            if (presentationRoot == null) return;

            var shouldShow = sessionCoordinator != null &&
                             authorityClient != null &&
                             ShouldShowWorkspace(
                                 creatorModeEnabled,
                                 sessionCoordinator.IsInWorld,
                                 authorityClient.IsWorldRuntimeReady,
                                 authorityClient.CurrentWorldRole);
            if (presentationRoot.activeSelf != shouldShow)
            {
                presentationRoot.SetActive(shouldShow);
            }
        }
    }
}
