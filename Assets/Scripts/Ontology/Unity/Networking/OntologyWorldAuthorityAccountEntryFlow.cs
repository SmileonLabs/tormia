using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Development-facing account → character → world entry coordinator.
    /// It contains no credentials and does not store passwords. The current
    /// local identity remains the development subject in WorldAuthoritySettings;
    /// production replaces that source with a verified identity provider.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyWorldAuthorityAccountEntryFlow : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private OntologyWorldAuthorityPlayerIntentSender intentSender;
        [SerializeField] private OntologyAuthorityEntityIdentity avatarIdentity;
        [SerializeField] private OntologyCharacterPartAdapter characterPartAdapter;
        [SerializeField, TextArea] private string lastStatus;
        [SerializeField] private bool enteredCurrentWorld;

        public string LastStatus => lastStatus;
        public OntologyAuthorityAccountDashboard CurrentAccount => authorityClient == null ? null : authorityClient.CurrentAccount;
        public IReadOnlyList<OntologyAuthorityPlayerCharacter> Characters =>
            CurrentAccount?.characters ?? Array.Empty<OntologyAuthorityPlayerCharacter>();
        public IReadOnlyList<OntologyAuthorityAccountWorld> Worlds =>
            CurrentAccount?.worlds ?? Array.Empty<OntologyAuthorityAccountWorld>();
        public OntologyAuthorityPlayerCharacter CurrentCharacter
        {
            get
            {
                var characters = CurrentAccount?.characters;
                if (characters == null || string.IsNullOrWhiteSpace(SelectedCharacterId)) return null;
                foreach (var character in characters)
                {
                    if (character != null &&
                        string.Equals(character.characterId, SelectedCharacterId, StringComparison.Ordinal))
                    {
                        return character;
                    }
                }
                return null;
            }
        }
        public string SelectedCharacterId => authorityClient?.CurrentCharacterId;
        public string SelectedWorldId => authorityClient?.CurrentWorldId;
        public string SelectedWorldRole => authorityClient?.CurrentWorldRole ?? string.Empty;
        public bool CanEditSelectedWorld => authorityClient != null && authorityClient.CanEditCurrentWorld;
        public bool EnteredCurrentWorld => enteredCurrentWorld;
        public event Action StateChanged;

        private void Awake()
        {
            ResolveDependencies();
        }

        [ContextMenu("Connect Development Account")]
        public void ConnectDevelopmentAccount()
        {
            ResolveDependencies();
            if (authorityClient == null)
            {
                SetStatus("No authority client is available.");
                return;
            }
            StartCoroutine(ConnectRoutine());
        }

        [ContextMenu("Refresh Account, Characters and Worlds")]
        public void RefreshAccount()
        {
            ResolveDependencies();
            if (authorityClient == null || !authorityClient.IsAuthenticated)
            {
                SetStatus("Connect a development account first.");
                return;
            }
            StartCoroutine(RefreshRoutine());
        }

        [ContextMenu("Enter Selected Character in Current World")]
        public void EnterSelectedCharacterInCurrentWorld()
        {
            ResolveDependencies();
            if (authorityClient == null || !authorityClient.IsReady)
            {
                SetStatus("Connect an account and select a world first.");
                return;
            }
            StartCoroutine(EnterRoutine());
        }

        /// <summary>
        /// Persists only the selected account character's appearance profile.
        /// It intentionally preserves profile ontology relations and never
        /// writes selected parts into shared-world Facts.
        /// </summary>
        [ContextMenu("Save Current Account Appearance")]
        public void SaveCurrentAccountAppearance()
        {
            ResolveDependencies();
            var character = CurrentCharacter;
            if (authorityClient == null || character == null || characterPartAdapter == null)
            {
                SetStatus("Connect an account and select a character before saving appearance.");
                return;
            }
            StartCoroutine(SaveCurrentAppearanceRoutine(character));
        }

        public bool SelectCharacter(string characterId)
        {
            ResolveDependencies();
            var selected = authorityClient != null && authorityClient.SelectCharacter(characterId);
            if (selected) SetStatus("Character selection saved.");
            return selected;
        }

        public bool SelectWorld(string worldId)
        {
            ResolveDependencies();
            var selected = authorityClient != null && authorityClient.SelectWorld(worldId);
            if (selected) SetStatus("World selection saved. Load or enter it next.");
            return selected;
        }

        private IEnumerator ConnectRoutine()
        {
            var connected = false;
            yield return authorityClient.ConnectRoutine(success => connected = success);
            enteredCurrentWorld = false;
            SetStatus(connected
                ? "Account, selected character and world are ready. Register and enter the avatar next."
                : authorityClient.LastStatus);
        }

        private IEnumerator RefreshRoutine()
        {
            OntologyAuthorityAccountDashboard account = null;
            yield return authorityClient.LoadAccountDashboardRoutine(result => account = result);
            SetStatus(account == null
                ? authorityClient.LastStatus
                : "Loaded " + account.characters.Length + " character(s) and " + account.worlds.Length + " world(s).");
        }

        private IEnumerator EnterRoutine()
        {
            if (avatarIdentity == null || !avatarIdentity.TryGetGuid(out var avatarId))
            {
                SetStatus("Assign a stable authority Entity GUID to the local player first.");
                yield break;
            }

            var registered = false;
            if (intentSender != null)
            {
                yield return intentSender.RegisterCurrentAvatarRoutine(result => registered = result);
            }
            else
            {
                var command = OntologyWorldAuthorityClient.CreateCommand(
                    "register_player_avatar",
                    OntologyWorldAuthorityClient.CreateRegisterPlayerAvatarPayload(avatarId));
                yield return authorityClient.SendCommandRoutine(command, result => registered = result != null && result.accepted);
            }

            if (!registered)
            {
                SetStatus("Avatar registration failed. Publish the scene player to the authority world first.");
                yield break;
            }

            var entered = false;
            yield return authorityClient.EnterWorldRoutine(avatarId, result => entered = result);
            enteredCurrentWorld = entered;
            if (!entered)
            {
                SetStatus(authorityClient.LastStatus);
                yield break;
            }

            var character = authorityClient.CurrentCharacter;
            var appearanceApplied = characterPartAdapter != null
                && characterPartAdapter.ApplyAccountProfile(character == null ? null : character.equippedPartIds);
            SetStatus(appearanceApplied
                ? "Character entered the selected world and its saved appearance was applied."
                : "Character entered the selected world. No local appearance adapter was found.");
        }

        private IEnumerator SaveCurrentAppearanceRoutine(OntologyAuthorityPlayerCharacter character)
        {
            var saved = false;
            yield return authorityClient.UpdateAccountCharacterProfileRoutine(
                character,
                character.displayName,
                character.templateId,
                characterPartAdapter.GetEquippedPartIds(),
                character.profileRelations,
                value => saved = value);
            SetStatus(saved ? "Account appearance profile saved." : authorityClient.LastStatus);
        }

        private void ResolveDependencies()
        {
            if (authorityClient == null) authorityClient = FindAnyObjectByType<OntologyWorldAuthorityClient>();
            if (intentSender == null) intentSender = FindAnyObjectByType<OntologyWorldAuthorityPlayerIntentSender>();
            if (avatarIdentity == null)
            {
                // The intent bridge and the player identity may intentionally live on
                // separate scene objects, so a missing same-object component must not
                // prevent the scene-wide stable identity fallback.
                if (intentSender != null)
                {
                    avatarIdentity = intentSender.GetComponent<OntologyAuthorityEntityIdentity>();
                }

                if (avatarIdentity == null)
                {
                    avatarIdentity = FindAnyObjectByType<OntologyAuthorityEntityIdentity>();
                }
            }
            if (characterPartAdapter == null) characterPartAdapter = FindAnyObjectByType<OntologyCharacterPartAdapter>();
        }

        private void SetStatus(string value)
        {
            lastStatus = value ?? string.Empty;
            StateChanged?.Invoke();
        }
    }
}
