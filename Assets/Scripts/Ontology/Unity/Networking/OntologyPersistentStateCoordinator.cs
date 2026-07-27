using System;
using System.Collections;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public enum OntologyPersistenceStatus
    {
        Saved,
        Dirty,
        Saving,
        Retrying,
        Conflict,
        Failed,
        Offline
    }

    /// <summary>
    /// Coordinates account-owned appearance persistence. Durable world edits
    /// remain immediate Authority commands; this component never mirrors them
    /// into hidden local Facts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyPersistentStateCoordinator : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityAccountEntryFlow entryFlow;
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private OntologyCharacterPartAdapter characterPartAdapter;
        [SerializeField, Min(0.1f)] private float appearanceDebounceSeconds = 0.75f;
        [SerializeField] private OntologyPersistenceStatus status = OntologyPersistenceStatus.Saved;

        private Coroutine appearanceSaveRoutine;
        private OntologyAuthorityPlayerCharacter pendingCharacter;
        private string[] pendingPartIds = Array.Empty<string>();

        public OntologyPersistenceStatus Status => status;
        public event Action<OntologyPersistenceStatus> StatusChanged;

        private void Awake() => ResolveDependencies();

        private void OnEnable()
        {
            ResolveDependencies();
            if (characterPartAdapter != null)
                characterPartAdapter.EquippedPartsChanged += OnEquippedPartsChanged;
        }

        private void OnDisable()
        {
            if (characterPartAdapter != null)
                characterPartAdapter.EquippedPartsChanged -= OnEquippedPartsChanged;
            if (appearanceSaveRoutine != null)
            {
                StopCoroutine(appearanceSaveRoutine);
                appearanceSaveRoutine = null;
            }
            pendingCharacter = null;
            pendingPartIds = Array.Empty<string>();
        }

        private void OnEquippedPartsChanged()
        {
            if (authorityClient == null || !authorityClient.IsAuthenticated ||
                entryFlow == null || entryFlow.CurrentCharacter == null)
            {
                SetStatus(OntologyPersistenceStatus.Offline);
                return;
            }

            SetStatus(OntologyPersistenceStatus.Dirty);
            pendingCharacter = entryFlow.CurrentCharacter;
            pendingPartIds = characterPartAdapter.GetEquippedPartIds();
            if (appearanceSaveRoutine != null) StopCoroutine(appearanceSaveRoutine);
            appearanceSaveRoutine = StartCoroutine(SaveAppearanceAfterDelay());
        }

        private IEnumerator SaveAppearanceAfterDelay()
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, appearanceDebounceSeconds));
            SetStatus(OntologyPersistenceStatus.Saving);
            var characterSnapshot = pendingCharacter;
            var partSnapshot = pendingPartIds;
            pendingCharacter = null;
            pendingPartIds = Array.Empty<string>();
            var completed = false;
            var saved = false;
            entryFlow.SaveAccountAppearanceSnapshot(
                characterSnapshot,
                partSnapshot,
                value =>
            {
                saved = value;
                completed = true;
            });
            while (!completed) yield return null;
            SetStatus(saved ? OntologyPersistenceStatus.Saved : OntologyPersistenceStatus.Failed);
            appearanceSaveRoutine = null;
        }

        private void SetStatus(OntologyPersistenceStatus value)
        {
            if (status == value) return;
            status = value;
            StatusChanged?.Invoke(value);
        }

        private void ResolveDependencies()
        {
            if (entryFlow == null)
                entryFlow = FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>();
            if (authorityClient == null)
                authorityClient = FindAnyObjectByType<OntologyWorldAuthorityClient>();
            if (characterPartAdapter == null)
                characterPartAdapter = OntologyCharacterPartAdapter.FindAvailable();
        }
    }
}
