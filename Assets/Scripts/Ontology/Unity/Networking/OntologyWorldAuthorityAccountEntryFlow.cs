using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Password-authenticated account → character → world entry coordinator.
    /// Passwords are sent only to Authority authentication endpoints and are
    /// never stored by this component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyWorldAuthorityAccountEntryFlow : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private OntologyWorldAuthorityPlayerIntentSender intentSender;
        [SerializeField] private OntologyAuthorityEntityIdentity avatarIdentity;
        [SerializeField] private OntologyCharacterPartAdapter characterPartAdapter;
        [SerializeField] private OntologyAccountProfileRelationProjector profileRelationProjector;
        [SerializeField] private OntologyGameSessionCoordinator sessionCoordinator;
        [SerializeField] private OntologyAvatarCheckpointController checkpointController;
        [SerializeField] private OntologyPersistentStateCoordinator persistentStateCoordinator;
        [SerializeField, TextArea] private string lastStatus;
        [SerializeField] private bool enteredCurrentWorld;
        [SerializeField] private bool isEnteringWorld;
        private Guid baseAvatarEntityId;

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
        public bool CanEditSelectedWorld => authorityClient != null && authorityClient.CanAuthorSelectedWorld;
        public bool EnteredCurrentWorld => enteredCurrentWorld;
        public bool IsEnteringWorld => isEnteringWorld;
        public bool HasStoredSession => authorityClient != null && authorityClient.HasStoredSession;
        public OntologyGameSessionCoordinator SessionCoordinator => sessionCoordinator;
        public OntologyAuthorityEntityIdentity AvatarIdentity => avatarIdentity;
        public event Action StateChanged;

        private void Awake()
        {
            ResolveDependencies();
            sessionCoordinator?.Bind(authorityClient);
        }

        [ContextMenu("Restore Account Session")]
        public void RestoreAccountSession(Action<bool> completed = null)
        {
            ResolveDependencies();
            if (authorityClient == null)
            {
                SetStatus("No authority client is available.");
                return;
            }
            StartCoroutine(ConnectRoutine(completed));
        }

        [ContextMenu("Refresh Account, Characters and Worlds")]
        public void RefreshAccount()
        {
            ResolveDependencies();
            if (authorityClient == null || !authorityClient.IsAuthenticated)
            {
                SetStatus("Sign in to an account first.");
                return;
            }
            StartCoroutine(RefreshRoutine());
        }

        [ContextMenu("Enter Selected Character in Current World")]
        public void EnterSelectedCharacterInCurrentWorld(Action<bool> completed = null)
        {
            ResolveDependencies();
            if (authorityClient == null || !authorityClient.IsReady)
            {
                SetStatus("Connect an account and select a world first.");
                completed?.Invoke(false);
                return;
            }
            if (isEnteringWorld) return;
            enteredCurrentWorld = false;
            isEnteringWorld = true;
            sessionCoordinator?.BeginWorldEntry();
            SetStatus("Preparing world entry.");
            StartCoroutine(EnterRoutine(completed));
        }

        /// <summary>
        /// First-time entry creates one portable account character
        /// before any world data is touched. The chosen visual parts remain
        /// account-owned and are later projected by the normal entry flow.
        /// </summary>
        [ContextMenu("Create Default Account Character")]
        public void CreateDefaultAccountCharacter()
        {
            ResolveDependencies();
            if (authorityClient == null || !authorityClient.IsAuthenticated)
            {
                SetStatus("Sign in before creating a character.");
                return;
            }
            if (CurrentCharacter != null)
            {
                SetStatus("A character is already selected.");
                return;
            }
            StartCoroutine(CreateDefaultCharacterRoutine());
        }

        /// <summary>
        /// Creates an account-owned character from the hierarchy-authored
        /// character creation screen.  Name, template and appearance belong to
        /// the account profile; this method deliberately does not create any
        /// shared-world Fact.
        /// </summary>
        public void CreateAccountCharacter(
            string displayName,
            string templateId,
            Action<OntologyAuthorityPlayerCharacter> completed = null)
        {
            ResolveDependencies();
            if (authorityClient == null || !authorityClient.IsAuthenticated)
            {
                SetStatus("Sign in before creating a character.");
                completed?.Invoke(null);
                return;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                SetStatus("Enter a character name first.");
                completed?.Invoke(null);
                return;
            }

            if (string.IsNullOrWhiteSpace(templateId))
            {
                SetStatus("Choose a character template first.");
                completed?.Invoke(null);
                return;
            }

            StartCoroutine(CreateAccountCharacterRoutine(displayName.Trim(), templateId.Trim(), completed));
        }

        /// <summary>
        /// Registers a password-authenticated account through the Authority,
        /// then leaves character creation to the account profile flow. It never
        /// writes account data into shared-world Facts.
        /// </summary>
        public void RegisterAccount(string email, string displayName, string password, Action<bool> completed = null)
        {
            ResolveDependencies();
            if (authorityClient == null)
            {
                SetStatus("No authority client is available.");
                completed?.Invoke(false);
                return;
            }

            email = email?.Trim();
            displayName = displayName?.Trim();
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(password))
            {
                SetStatus("Enter email, display name and password.");
                completed?.Invoke(false);
                return;
            }
            StartCoroutine(RegisterRoutine(email, displayName, password, completed));
        }

        public void LoginAccount(string email, string password, Action<bool> completed = null)
        {
            ResolveDependencies();
            if (authorityClient == null) { SetStatus("No authority client is available."); completed?.Invoke(false); return; }
            sessionCoordinator?.BeginAuthentication();
            StartCoroutine(LoginRoutine(email, password, completed));
        }

        public void LogoutAccount()
        {
            ResolveDependencies();
            sessionCoordinator?.BeginLeavingWorld();
            StartCoroutine(LogoutRoutine());
        }

        private IEnumerator LogoutRoutine()
        {
            if (enteredCurrentWorld && checkpointController != null)
                yield return checkpointController.SaveRoutine(null);
            authorityClient?.Logout();
            enteredCurrentWorld = false;
            intentSender?.SetAvatarRegistered(false);
            profileRelationProjector?.ClearProjection();
            sessionCoordinator?.SignedOut();
            SetStatus("Signed out.");
        }

        private IEnumerator RegisterRoutine(string email, string displayName, string password, Action<bool> completed)
        {
            var success = false; yield return authorityClient.RegisterRoutine(email, displayName, password, value => success = value);
            sessionCoordinator?.AuthenticationCompleted(success);
            SetStatus(success ? "Account created. Create your character next." : authorityClient.LastStatus); completed?.Invoke(success);
        }

        private IEnumerator LoginRoutine(string email, string password, Action<bool> completed)
        {
            var success = false; yield return authorityClient.LoginRoutine(email, password, value => success = value);
            sessionCoordinator?.AuthenticationCompleted(success);
            SetStatus(success ? "Signed in." : authorityClient.LastStatus); completed?.Invoke(success);
        }

        /// <summary>
        /// Persists only the selected account character's appearance profile.
        /// It intentionally preserves profile ontology relations and never
        /// writes selected parts into shared-world Facts.
        /// </summary>
        [ContextMenu("Save Current Account Appearance")]
        public void SaveCurrentAccountAppearance(Action<bool> completed = null)
        {
            ResolveDependencies();
            var character = CurrentCharacter;
            if (authorityClient == null || character == null || characterPartAdapter == null)
            {
                SetStatus("Connect an account and select a character before saving appearance.");
                completed?.Invoke(false);
                return;
            }
            SaveAccountAppearanceSnapshot(
                character,
                characterPartAdapter.GetEquippedPartIds(),
                completed);
        }

        public void SaveAccountAppearanceSnapshot(
            OntologyAuthorityPlayerCharacter character,
            string[] equippedPartIds,
            Action<bool> completed = null)
        {
            ResolveDependencies();
            if (authorityClient == null || character == null ||
                !Guid.TryParse(character.characterId, out _))
            {
                completed?.Invoke(false);
                return;
            }

            StartCoroutine(SaveCurrentAppearanceRoutine(
                character,
                equippedPartIds == null
                    ? Array.Empty<string>()
                    : (string[])equippedPartIds.Clone(),
                completed));
        }

        public bool SelectCharacter(string characterId)
        {
            ResolveDependencies();
            var selected = authorityClient != null && authorityClient.SelectCharacter(characterId);
            if (selected)
            {
                sessionCoordinator?.CharacterSelected();
                SetStatus("Character selection saved.");
            }
            return selected;
        }

        public bool SelectWorld(string worldId)
        {
            ResolveDependencies();
            var selected = authorityClient != null && authorityClient.SelectWorld(worldId);
            if (selected)
            {
                sessionCoordinator?.WorldSelected();
                SetStatus("World selection saved. Load or enter it next.");
            }
            return selected;
        }

        public void CreateWorld(string title, string slug, string visibility, Action<bool> completed = null)
        {
            ResolveDependencies();
            if (authorityClient == null || !authorityClient.IsAuthenticated) { SetStatus("Connect an account before creating a world."); completed?.Invoke(false); return; }
            StartCoroutine(CreateWorldRoutine(title, slug, visibility, completed));
        }

        private IEnumerator CreateWorldRoutine(string title, string slug, string visibility, Action<bool> completed)
        {
            if (string.IsNullOrWhiteSpace(title)) { SetStatus("Enter a world name first."); completed?.Invoke(false); yield break; }
            if (string.IsNullOrWhiteSpace(slug)) slug = title.Trim().ToLowerInvariant().Replace(" ", "-");
            var created = false;
            yield return authorityClient.CreateWorldRoutine(slug, title, string.IsNullOrWhiteSpace(visibility) ? "private" : visibility, value => created = value);
            SetStatus(created ? authorityClient.LastStatus : authorityClient.LastStatus);
            completed?.Invoke(created);
        }

        private IEnumerator ConnectRoutine(Action<bool> completed = null)
        {
            var connected = false;
            yield return authorityClient.ConnectRoutine(success => connected = success);
            enteredCurrentWorld = false;
            sessionCoordinator?.AuthenticationCompleted(connected);
            if (connected) sessionCoordinator?.SynchronizeFromAuthority();
            SetStatus(connected
                ? "Account session restored."
                : authorityClient.LastStatus);
            completed?.Invoke(connected);
        }

        private IEnumerator RefreshRoutine()
        {
            OntologyAuthorityAccountDashboard account = null;
            yield return authorityClient.LoadAccountDashboardRoutine(result => account = result);
            SetStatus(account == null
                ? authorityClient.LastStatus
                : "Loaded " + account.characters.Length + " character(s) and " + account.worlds.Length + " world(s).");
        }

        private IEnumerator EnterRoutine(Action<bool> completed)
        {
            try
            {
                yield return EnterRoutineCore();
            }
            finally
            {
                isEnteringWorld = false;
                sessionCoordinator?.WorldEntryCompleted(enteredCurrentWorld);
                StateChanged?.Invoke();
                completed?.Invoke(enteredCurrentWorld);
            }
        }

        private IEnumerator EnterRoutineCore()
        {
            if (avatarIdentity == null || !avatarIdentity.TryGetGuid(out var avatarId))
            {
                SetStatus("Assign a stable authority Entity GUID to the local player first.");
                yield break;
            }

            if (baseAvatarEntityId == Guid.Empty)
                baseAvatarEntityId = avatarId;
            avatarId = baseAvatarEntityId;
            avatarIdentity.SetGuid(avatarId);

            OntologyAuthorityCommandResult registrationResult = null;
            var registered = false;
            yield return RegisterAvatarRoutine(avatarId, result =>
                {
                    registrationResult = result;
                    registered = result != null && result.accepted;
                });

            // A local player is a scene presentation, not a placeable editor
            // object. On first entry it still needs one Authority-owned entity
            // before the normal ownership registration can succeed.
            if (!registered && registrationResult != null &&
                string.Equals(registrationResult.rejectionCode, "avatar_entity_not_found", StringComparison.Ordinal))
            {
                OntologyAuthorityCommandResult placeResult = null;
                yield return PlaceAvatarRoutine(avatarId, result => placeResult = result);
                if (placeResult != null && placeResult.accepted)
                {
                    yield return RegisterAvatarRoutine(
                        avatarId,
                        result => registered = result != null && result.accepted);
                }
                else if (placeResult != null &&
                         string.Equals(
                             placeResult.rejectionCode,
                             "entity_id_already_exists",
                             StringComparison.Ordinal) &&
                         Guid.TryParse(authorityClient.CurrentWorldId, out var worldId))
                {
                    // World entities are globally keyed, while an avatar is
                    // world-owned. Reusing a scene's base identity in another
                    // world therefore resolves to a deterministic world scope.
                    avatarId = CreateWorldScopedAvatarId(baseAvatarEntityId, worldId);
                    avatarIdentity.SetGuid(avatarId);

                    OntologyAuthorityCommandResult scopedRegistration = null;
                    yield return RegisterAvatarRoutine(
                        avatarId,
                        result => scopedRegistration = result);
                    registered = scopedRegistration != null && scopedRegistration.accepted;
                    if (!registered && scopedRegistration != null &&
                        string.Equals(
                            scopedRegistration.rejectionCode,
                            "avatar_entity_not_found",
                            StringComparison.Ordinal))
                    {
                        OntologyAuthorityCommandResult scopedPlacement = null;
                        yield return PlaceAvatarRoutine(
                            avatarId,
                            result => scopedPlacement = result);
                        if (scopedPlacement != null && scopedPlacement.accepted)
                        {
                            yield return RegisterAvatarRoutine(
                                avatarId,
                                result => registered = result != null && result.accepted);
                        }
                    }
                }
            }

            if (registered) intentSender?.SetAvatarRegistered(true);

            if (!registered)
            {
                SetStatus("Avatar registration failed. Publish the scene player to the authority world first.");
                yield break;
            }

            var entered = false;
            yield return authorityClient.EnterWorldRoutine(avatarId, result => entered = result);
            if (!entered)
            {
                SetStatus(authorityClient.LastStatus);
                yield break;
            }

            var projectionLoaded = false;
            yield return authorityClient.LoadWorldRoutine(value => projectionLoaded = value);
            if (!projectionLoaded)
            {
                authorityClient.ResetWorldEntryConfirmation();
                SetStatus("World entry was accepted, but its durable projection could not be loaded.");
                yield break;
            }

            SetStatus("Recovering pending durable changes.");
            var pendingCommandsRecovered = false;
            yield return authorityClient.ReplayPendingCommandsRoutine(
                value => pendingCommandsRecovered = value);
            if (!pendingCommandsRecovered)
            {
                sessionCoordinator?.Recovering();
                SetStatus("World loaded, but pending durable changes are waiting for the Authority connection.");
                yield break;
            }

            SetStatus("Restoring the avatar checkpoint.");
            var checkpointRestored = checkpointController == null;
            if (checkpointController != null)
                yield return checkpointController.RestoreRoutine(value => checkpointRestored = value);
            if (!checkpointRestored)
            {
                authorityClient.ResetWorldEntryConfirmation();
                SetStatus("World entry stopped because the avatar checkpoint could not be restored.");
                yield break;
            }

            SetStatus("Preparing development actions.");
            var packageReady = false;
            yield return authorityClient.EnsureDevelopmentActionPackageRoutine(value => packageReady = value);

            var character = authorityClient.CurrentCharacter;
            var appearanceApplied = characterPartAdapter != null
                && characterPartAdapter.ApplyAccountProfile(character == null ? null : character.equippedPartIds);
            OntologyAuthorityWorldAvatarProfile worldAvatarProfile = null;
            yield return authorityClient.LoadWorldAvatarProfileRoutine(avatarId, value => worldAvatarProfile = value);
            if (profileRelationProjector != null)
                profileRelationProjector.ApplyProfileRelations(CombineProfileRelations(
                    character == null ? null : character.profileRelations,
                    worldAvatarProfile == null ? null : worldAvatarProfile.profileRelations));
            enteredCurrentWorld = true;
            SetStatus((appearanceApplied
                ? "Character entered the selected world and its saved profile was projected."
                : "Character entered the selected world and its saved profile relations were projected.") +
                (packageReady ? " Development actions are ready." : " Development actions were not prepared: " + authorityClient.LastStatus));
        }

        private IEnumerator RegisterAvatarRoutine(
            Guid avatarId,
            Action<OntologyAuthorityCommandResult> completed)
        {
            var command = OntologyWorldAuthorityClient.CreateCommand(
                "register_player_avatar",
                OntologyWorldAuthorityClient.CreateRegisterPlayerAvatarPayload(avatarId));
            yield return authorityClient.SendCommandRoutine(command, completed);
        }

        private IEnumerator PlaceAvatarRoutine(
            Guid avatarId,
            Action<OntologyAuthorityCommandResult> completed)
        {
            var command = OntologyWorldAuthorityClient.CreateCommand(
                "place_entity",
                OntologyWorldAuthorityClient.CreatePlaceEntityPayload(
                    avatarId,
                    "player_avatar",
                    "Player Avatar",
                    avatarIdentity.transform,
                    authorityClient.CurrentProjectionZoneKey));
            yield return authorityClient.SendCommandRoutine(command, completed);
        }

        public static Guid CreateWorldScopedAvatarId(Guid baseAvatarId, Guid worldId)
        {
            if (baseAvatarId == Guid.Empty || worldId == Guid.Empty)
                return Guid.Empty;

            var input = new byte[32];
            Buffer.BlockCopy(baseAvatarId.ToByteArray(), 0, input, 0, 16);
            Buffer.BlockCopy(worldId.ToByteArray(), 0, input, 16, 16);
            byte[] hash;
            using (var sha256 = SHA256.Create())
                hash = sha256.ComputeHash(input);
            var scopedBytes = new byte[16];
            Buffer.BlockCopy(hash, 0, scopedBytes, 0, scopedBytes.Length);
            return new Guid(scopedBytes);
        }

        private IEnumerator CreateDefaultCharacterRoutine()
        {
            yield return CreateAccountCharacterRoutine("Explorer", "player", null);
        }

        private IEnumerator CreateAccountCharacterRoutine(
            string displayName,
            string templateId,
            Action<OntologyAuthorityPlayerCharacter> completed)
        {
            var equippedParts = characterPartAdapter == null
                ? Array.Empty<string>()
                : characterPartAdapter.GetEquippedPartIds();
            OntologyAuthorityPlayerCharacter created = null;
            yield return authorityClient.CreateAccountCharacterRoutine(
                displayName,
                templateId,
                equippedParts,
                value => created = value);
            SetStatus(created == null
                ? authorityClient.LastStatus
                : "Created and selected character '" + created.displayName + "'. Choose a world and enter it next.");
            completed?.Invoke(created);
        }

        private IEnumerator SaveCurrentAppearanceRoutine(
            OntologyAuthorityPlayerCharacter character,
            string[] equippedPartIds,
            Action<bool> completed = null)
        {
            var saved = false;
            yield return authorityClient.UpdateAccountCharacterProfileRoutine(
                character,
                character.displayName,
                character.templateId,
                equippedPartIds,
                character.profileRelations,
                value => saved = value);
            SetStatus(saved ? "Account appearance profile saved." : authorityClient.LastStatus);
            completed?.Invoke(saved);
        }

        private static OntologyAuthorityProfileRelation[] CombineProfileRelations(
            OntologyAuthorityProfileRelation[] accountRelations,
            OntologyAuthorityProfileRelation[] worldRelations)
        {
            var accountCount = accountRelations == null ? 0 : accountRelations.Length;
            var worldCount = worldRelations == null ? 0 : worldRelations.Length;
            var combined = new OntologyAuthorityProfileRelation[accountCount + worldCount];
            if (accountCount > 0) Array.Copy(accountRelations, combined, accountCount);
            if (worldCount > 0) Array.Copy(worldRelations, 0, combined, accountCount, worldCount);
            return combined;
        }

        private void ResolveDependencies()
        {
            if (authorityClient == null)
                authorityClient = FindAnyObjectByType<OntologyWorldAuthorityClient>(
                    FindObjectsInactive.Include);
            if (sessionCoordinator == null)
            {
                sessionCoordinator = FindAnyObjectByType<OntologyGameSessionCoordinator>(
                    FindObjectsInactive.Include);
                if (sessionCoordinator == null)
                    sessionCoordinator = gameObject.AddComponent<OntologyGameSessionCoordinator>();
            }
            if (checkpointController == null)
            {
                checkpointController = FindAnyObjectByType<OntologyAvatarCheckpointController>(
                    FindObjectsInactive.Include);
                if (checkpointController == null)
                    checkpointController = gameObject.AddComponent<OntologyAvatarCheckpointController>();
            }
            if (persistentStateCoordinator == null)
            {
                persistentStateCoordinator = FindAnyObjectByType<OntologyPersistentStateCoordinator>(
                    FindObjectsInactive.Include);
                if (persistentStateCoordinator == null)
                    persistentStateCoordinator = gameObject.AddComponent<OntologyPersistentStateCoordinator>();
            }
            if (intentSender == null)
                intentSender = FindAnyObjectByType<OntologyWorldAuthorityPlayerIntentSender>(
                    FindObjectsInactive.Include);
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
                    avatarIdentity = FindAnyObjectByType<OntologyAuthorityEntityIdentity>(
                        FindObjectsInactive.Include);
                }
            }
            if (profileRelationProjector == null)
                profileRelationProjector = FindAnyObjectByType<OntologyAccountProfileRelationProjector>(
                    FindObjectsInactive.Include);
            if (characterPartAdapter == null)
                characterPartAdapter = OntologyCharacterPartAdapter.FindAvailable();
        }

        private void SetStatus(string value)
        {
            lastStatus = value ?? string.Empty;
            StateChanged?.Invoke();
        }
    }
}
