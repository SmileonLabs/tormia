using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        [SerializeField]
        private OntologyWorldEntryPresentationCoordinator
            entryPresentationCoordinator;
        [SerializeField] private OntologyAuthorityRespawnController respawnController;
        [SerializeField] private OntologyPersistentStateCoordinator persistentStateCoordinator;
        [SerializeField, TextArea] private string lastStatus;
        [SerializeField] private string lastStatusKey;
        [SerializeField] private string lastStatusFallback;
        [SerializeField] private string[] lastStatusArguments = Array.Empty<string>();
        [SerializeField, TextArea] private string lastTechnicalStatus;
        [SerializeField] private bool enteredCurrentWorld;
        [SerializeField] private bool isEnteringWorld;
        private Guid baseAvatarEntityId;
        private Guid entryRuntimeRollbackAvatarId;
        private string entryRuntimeRollbackSessionId = string.Empty;

        public string LastStatus => string.IsNullOrWhiteSpace(lastStatusKey)
            ? lastStatus
            : OntologyLanguagePackService.Format(
                lastStatusKey,
                lastStatusFallback,
                lastStatusArguments);
        public string LastStatusKey => lastStatusKey;
        public IReadOnlyList<string> LastStatusArguments =>
            lastStatusArguments ?? Array.Empty<string>();
        public string LastTechnicalStatus => lastTechnicalStatus;
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
            CaptureBaseAvatarEntityId();
            sessionCoordinator?.Bind(authorityClient);
        }

        [ContextMenu("Restore Account Session")]
        public void RestoreAccountSession(Action<bool> completed = null)
        {
            ResolveDependencies();
            if (authorityClient == null)
            {
                SetLocalizedStatus("ui.account.status.no_authority_client", "The world service is unavailable.");
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
                SetLocalizedStatus("ui.account.status.sign_in_first", "Sign in to an account first.");
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
                SetLocalizedStatus("ui.account.status.select_world_first", "Select an account and world first.");
                completed?.Invoke(false);
                return;
            }
            if (isEnteringWorld) return;
            enteredCurrentWorld = false;
            isEnteringWorld = true;
            lastTechnicalStatus = string.Empty;
            sessionCoordinator?.BeginWorldEntry();
            if (entryPresentationCoordinator == null ||
                !entryPresentationCoordinator.BeginPreparation())
            {
                isEnteringWorld = false;
                sessionCoordinator?.WorldEntryCompleted(false);
                SetLocalizedStatus(
                    "ui.account.status.entry_presentation_unavailable",
                    "World entry stopped because the local player presentation could not be prepared.");
                completed?.Invoke(false);
                return;
            }
            SetLocalizedStatus("ui.account.status.preparing_world_entry", "Preparing world entry.");
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
                SetLocalizedStatus("ui.account.status.sign_in_before_character", "Sign in before creating a character.");
                return;
            }
            if (CurrentCharacter != null)
            {
                SetLocalizedStatus("ui.account.status.character_already_selected", "A character is already selected.");
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
                SetLocalizedStatus("ui.account.status.sign_in_before_character", "Sign in before creating a character.");
                completed?.Invoke(null);
                return;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                SetLocalizedStatus("ui.account.status.character_name_required", "Enter a character name first.");
                completed?.Invoke(null);
                return;
            }

            if (string.IsNullOrWhiteSpace(templateId))
            {
                SetLocalizedStatus("ui.account.status.template_required", "Choose a character template first.");
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
                SetLocalizedStatus("ui.account.status.no_authority_client", "The world service is unavailable.");
                completed?.Invoke(false);
                return;
            }

            email = email?.Trim();
            displayName = displayName?.Trim();
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(password))
            {
                SetLocalizedStatus("ui.account.status.account_fields_required", "Enter email, display name and password.");
                completed?.Invoke(false);
                return;
            }
            StartCoroutine(RegisterRoutine(email, displayName, password, completed));
        }

        public void LoginAccount(string email, string password, Action<bool> completed = null)
        {
            ResolveDependencies();
            if (authorityClient == null) { SetLocalizedStatus("ui.account.status.no_authority_client", "The world service is unavailable."); completed?.Invoke(false); return; }
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
            SetLocalizedStatus("ui.account.status.signed_out", "Signed out.");
        }

        private IEnumerator RegisterRoutine(string email, string displayName, string password, Action<bool> completed)
        {
            var success = false; yield return authorityClient.RegisterRoutine(email, displayName, password, value => success = value);
            sessionCoordinator?.AuthenticationCompleted(success);
            if (success)
                SetLocalizedStatus("ui.account.status.account_created", "Account created. Create your character next.");
            else
                SetAuthorityFailureStatus(authorityClient.LastStatus);
            completed?.Invoke(success);
        }

        private IEnumerator LoginRoutine(string email, string password, Action<bool> completed)
        {
            var success = false; yield return authorityClient.LoginRoutine(email, password, value => success = value);
            sessionCoordinator?.AuthenticationCompleted(success);
            if (success)
                SetLocalizedStatus("ui.account.status.signed_in", "Signed in.");
            else
                SetAuthorityFailureStatus(authorityClient.LastStatus);
            completed?.Invoke(success);
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
                SetLocalizedStatus("ui.account.status.appearance_requires_character", "Select an account and character before saving appearance.");
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
                SetLocalizedStatus("ui.account.status.character_selection_saved", "Character selection saved.");
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
                SetLocalizedStatus("ui.account.status.world_selection_saved", "World selection saved. Load or enter it next.");
            }
            return selected;
        }

        public void CreateWorld(string title, string slug, string visibility, Action<bool> completed = null)
        {
            ResolveDependencies();
            if (authorityClient == null || !authorityClient.IsAuthenticated) { SetLocalizedStatus("ui.account.status.create_world_requires_account", "Sign in before creating a world."); completed?.Invoke(false); return; }
            StartCoroutine(CreateWorldRoutine(title, slug, visibility, completed));
        }

        /// <summary>
        /// Explicit development/world-authoring operation that atomically
        /// prepares the selected avatar's semantic contract. Normal entry only
        /// performs read-only preflight and never invokes this routine.
        /// </summary>
        [ContextMenu("Prepare Selected Avatar Semantic Contract")]
        public void PrepareSelectedAvatarSemanticContract()
        {
            PrepareSelectedAvatarSemanticContract(null);
        }

        public void PrepareSelectedAvatarSemanticContract(
            Action<bool> completed)
        {
            ResolveDependencies();
            if (authorityClient == null || !authorityClient.IsReady ||
                avatarIdentity == null ||
                !TryResolveSelectedWorldAvatarId(out var avatarId))
            {
                completed?.Invoke(false);
                return;
            }

            StartCoroutine(
                PrepareAvatarSemanticContractRoutine(
                    avatarId,
                    completed));
        }

        /// <summary>
        /// Explicit development preparation entry point. This intentionally
        /// lives outside EnterRoutineCore so admission never authors content
        /// or repairs an avatar semantic contract.
        /// </summary>
        [ContextMenu("Prepare Selected Development World")]
        public void PrepareSelectedDevelopmentWorld()
        {
            PrepareSelectedDevelopmentWorld(null);
        }

        public void PrepareSelectedDevelopmentWorld(Action<bool> completed)
        {
            PrepareSelectedAvatarSemanticContract(completed);
        }

        private IEnumerator CreateWorldRoutine(string title, string slug, string visibility, Action<bool> completed)
        {
            if (string.IsNullOrWhiteSpace(title)) { SetLocalizedStatus("ui.account.status.world_name_required", "Enter a world name first."); completed?.Invoke(false); yield break; }
            if (string.IsNullOrWhiteSpace(slug)) slug = title.Trim().ToLowerInvariant().Replace(" ", "-");
            var created = false;
            yield return authorityClient.CreateWorldRoutine(slug, title, string.IsNullOrWhiteSpace(visibility) ? "private" : visibility, value => created = value);
            if (created)
            {
                var zoneKey = string.Empty;
                yield return EnsureRuntimeZoneRoutine(
                    value => zoneKey = value);
                created = !string.IsNullOrWhiteSpace(zoneKey);
            }
            if (created && TryResolveSelectedWorldAvatarId(out var avatarId))
            {
                // World creation is an explicit authoring/preparation phase,
                // so it may provision the avatar contract before the user
                // reaches admission. EnterRoutineCore remains read-only.
                yield return PrepareAvatarSemanticContractRoutine(
                    avatarId,
                    value => created = value);
            }
            if (created)
                SetLocalizedStatus("ui.account.status.world_created", "World created with its Authority runtime zone.");
            else
                SetAuthorityFailureStatus(authorityClient.LastStatus);
            completed?.Invoke(created);
        }

        private IEnumerator ConnectRoutine(Action<bool> completed = null)
        {
            var connected = false;
            yield return authorityClient.ConnectRoutine(success => connected = success);
            enteredCurrentWorld = false;
            sessionCoordinator?.AuthenticationCompleted(connected);
            if (connected) sessionCoordinator?.SynchronizeFromAuthority();
            if (connected)
                SetLocalizedStatus("ui.account.status.session_restored", "Account session restored.");
            else
                SetAuthorityFailureStatus(authorityClient.LastStatus);
            completed?.Invoke(connected);
        }

        private IEnumerator RefreshRoutine()
        {
            OntologyAuthorityAccountDashboard account = null;
            yield return authorityClient.LoadAccountDashboardRoutine(result => account = result);
            if (account == null)
                SetAuthorityFailureStatus(authorityClient.LastStatus);
            else
                SetLocalizedStatus(
                    "ui.account.status.dashboard_loaded",
                    "Loaded {0} character(s) and {1} world(s).",
                    account.characters.Length.ToString(CultureInfo.InvariantCulture),
                    account.worlds.Length.ToString(CultureInfo.InvariantCulture));
        }

        private IEnumerator EnterRoutine(Action<bool> completed)
        {
            try
            {
                var memberAvatarReady = false;
                yield return PrepareSelectedMemberAvatarRoutine(
                    value => memberAvatarReady = value);
                if (!memberAvatarReady)
                {
                    SetLocalizedStatus(
                        "ui.account.status.member_avatar_preparation_failed",
                        "World entry stopped because your member avatar " +
                        "could not be prepared.");
                    yield break;
                }
                yield return EnterRoutineCore();
            }
            finally
            {
                isEnteringWorld = false;
                if (!enteredCurrentWorld)
                {
                    if (entryRuntimeRollbackAvatarId != Guid.Empty &&
                        !string.IsNullOrWhiteSpace(
                            entryRuntimeRollbackSessionId))
                    {
                        var rollbackAvatarId =
                            entryRuntimeRollbackAvatarId;
                        var rollbackSessionId =
                            entryRuntimeRollbackSessionId;
                        entryRuntimeRollbackAvatarId = Guid.Empty;
                        entryRuntimeRollbackSessionId = string.Empty;
                        StartCoroutine(RollbackEntryRuntimeRoutine(
                            rollbackAvatarId,
                            rollbackSessionId));
                    }
                    checkpointController?.CancelPendingRestore();
                    entryPresentationCoordinator?.AbortPreparation();
                }
                sessionCoordinator?.WorldEntryCompleted(enteredCurrentWorld);
                StateChanged?.Invoke();
                completed?.Invoke(enteredCurrentWorld);
            }
        }

        private IEnumerator EnterRoutineCore()
        {
            if (avatarIdentity == null ||
                !TryResolveSelectedWorldAvatarId(out var avatarId))
            {
                SetLocalizedStatus("ui.account.status.avatar_identity_missing", "The local player does not have a stable world identity.");
                yield break;
            }

            avatarIdentity.SetGuid(avatarId);

            // Content publication is a release/deployment operation. Entry is
            // allowed to inspect the exact immutable package contract, but it
            // must not publish or activate content as a side effect of login.
            var contentPreflightReady = false;
            yield return authorityClient
                .VerifyDevelopmentContentReleaseRoutine(
                    value => contentPreflightReady = value);
            if (!contentPreflightReady)
            {
                authorityClient.ResetWorldEntryConfirmation();
                var technicalStatus = authorityClient.LastStatus;
                Debug.LogWarning(
                    "World entry package preflight failed: " +
                    technicalStatus,
                    authorityClient);
                SetLocalizedStatus(
                    ResolvePackageFailureLocalizationKey(technicalStatus),
                    "World entry was stopped because the gameplay data could not be verified. Please try again.");
                yield break;
            }

            authorityClient.SetProjectionZoneKey(string.Empty, false);
            var admissionProjectionLoaded = false;
            yield return LoadWorldForEntryWithRetryRoutine(
                value => admissionProjectionLoaded = value);
            var runtimeZoneKey = admissionProjectionLoaded
                ? ResolveEntityZone(
                    authorityClient.CurrentProjection,
                    avatarId)
                : string.Empty;
            if (string.IsNullOrWhiteSpace(runtimeZoneKey))
            {
                SetLocalizedStatus(
                    "ui.account.status.runtime_zone_missing",
                    "World entry stopped because the prepared avatar or its runtime zone is missing.");
                yield break;
            }

            var avatarSemanticContractReady = false;
            yield return VerifyAvatarSemanticContractRoutine(
                avatarId,
                value => avatarSemanticContractReady = value);
            if (!avatarSemanticContractReady)
            {
                authorityClient.ResetWorldEntryConfirmation();
                SetLocalizedStatus(
                    "ui.account.status.player_ontology_failed",
                    "World entry stopped because the player ontology contract could not be prepared.");
                yield break;
            }

            var projectionLoaded = false;
            yield return LoadWorldForEntryWithRetryRoutine(
                value => projectionLoaded = value);
            if (!projectionLoaded)
            {
                authorityClient.ResetWorldEntryConfirmation();
                SetLocalizedStatus(
                    "ui.account.status.world_projection_failed",
                    "World entry was accepted but its durable world data could not be loaded.");
                yield break;
            }
            intentSender?.SetAvatarRegistered(true);

            // Recover durable commands before admission commits the account
            // binding. Recovery failure is a separate state and must not
            // leave a half-entered session behind.
            SetLocalizedStatus(
                "ui.account.status.recovering_changes",
                "Recovering pending world changes.");
            var pendingCommandsRecovered = false;
            yield return authorityClient.ReplayPendingCommandsRoutine(
                value => pendingCommandsRecovered = value);
            if (!pendingCommandsRecovered)
            {
                sessionCoordinator?.Recovering();
                SetLocalizedStatus(
                    "ui.account.status.pending_changes_waiting",
                    "Pending changes are waiting for the Authority connection.");
                yield break;
            }

            var runtimeActivated = false;
            yield return authorityClient.ActivatePlayerRuntimeRoutine(
                avatarId,
                runtimeZoneKey,
                value => runtimeActivated = value);
            if (!runtimeActivated)
            {
                authorityClient.ResetWorldEntryConfirmation();
                SetLocalizedStatus(
                    "ui.account.status.avatar_runtime_failed",
                    "World entry stopped because the ephemeral player runtime could not be activated.");
                yield break;
            }
            entryRuntimeRollbackAvatarId = avatarId;
            entryRuntimeRollbackSessionId =
                authorityClient.ActiveRuntimeSessionId;

            SetLocalizedStatus(
                "ui.account.status.preparing_avatar_life",
                "Preparing the player life state.");
            var avatarLifeReady = false;
            if (respawnController != null)
            {
                yield return respawnController
                    .PrepareAliveForWorldEntryRoutine(
                        value => avatarLifeReady = value);
            }
            if (!avatarLifeReady)
            {
                authorityClient.ResetWorldEntryConfirmation();
                lastTechnicalStatus =
                    respawnController == null
                        ? "Authority respawn controller is unavailable."
                        : respawnController.LastStatus;
                if (!string.IsNullOrWhiteSpace(lastTechnicalStatus))
                {
                    Debug.LogWarning(
                        "[AccountEntry] Player life preparation failed: " +
                        lastTechnicalStatus,
                        this);
                }
                SetLocalizedStatus(
                    "ui.account.status.avatar_life_failed",
                    "World entry stopped because the Authority could not " +
                    "restore the player life state.");
                yield break;
            }

            var motionReady = false;
            yield return WaitForAvatarMotionRoutine(
                avatarId,
                runtimeZoneKey,
                value => motionReady = value);
            if (!motionReady)
            {
                authorityClient.ResetWorldEntryConfirmation();
                SetLocalizedStatus(
                    "ui.account.status.avatar_motion_missing",
                    "World entry stopped because the Authority did not publish the player position.");
                yield break;
            }

            var locomotionPresentationReady = false;
            if (intentSender != null)
            {
                yield return intentSender.PrepareLocomotionPresentationRoutine(
                    runtimeZoneKey,
                    value => locomotionPresentationReady = value);
            }
            if (!locomotionPresentationReady)
            {
                authorityClient.ResetWorldEntryConfirmation();
                lastTechnicalStatus = intentSender == null
                    ? "locomotion_intent_sender_missing"
                    : intentSender.LastStatus;
                Debug.LogWarning(
                    "[AccountEntry] Locomotion presentation preparation " +
                    "failed: " + lastTechnicalStatus,
                    this);
                SetLocalizedStatus(
                    "ui.account.status.locomotion_presentation_failed",
                    "World entry stopped because the Authority did not approve the player's locomotion contract.");
                yield break;
            }

            SetLocalizedStatus("ui.account.status.restoring_checkpoint", "Restoring the player checkpoint.");
            var checkpointRestored = checkpointController == null;
            if (checkpointController != null)
                yield return checkpointController.RestoreRoutine(value => checkpointRestored = value);
            if (!checkpointRestored)
            {
                authorityClient.ResetWorldEntryConfirmation();
                SetLocalizedStatus(
                    "ui.account.status.checkpoint_failed",
                    "World entry stopped because the player checkpoint could not be restored.");
                yield break;
            }

            var presentationPrepared = false;
            yield return entryPresentationCoordinator.PrepareRoutine(
                value => presentationPrepared = value);
            if (!presentationPrepared)
            {
                authorityClient.ResetWorldEntryConfirmation();
                SetLocalizedStatus(
                    "ui.account.status.entry_presentation_failed",
                    "World entry stopped because the local player could not be grounded before activation.");
                yield break;
            }

            var checkpointConfirmed = checkpointController == null;
            if (checkpointController != null)
            {
                yield return checkpointController.ConfirmRestoredPoseRoutine(
                    value => checkpointConfirmed = value);
            }
            if (!checkpointConfirmed)
            {
                authorityClient.ResetWorldEntryConfirmation();
                SetLocalizedStatus(
                    "ui.account.status.checkpoint_confirmation_failed",
                    "World entry stopped because the prepared player checkpoint could not be confirmed.");
                yield break;
            }

            var character = authorityClient.CurrentCharacter;
            var appearanceApplied = characterPartAdapter != null
                && characterPartAdapter.ApplyAccountProfile(character == null ? null : character.equippedPartIds);
            if (appearanceApplied)
            {
                entryPresentationCoordinator
                    .CaptureProjectedAppearanceWhileGated();
            }
            OntologyAuthorityWorldAvatarProfile worldAvatarProfile = null;
            yield return authorityClient.LoadWorldAvatarProfileRoutine(avatarId, value => worldAvatarProfile = value);
            if (profileRelationProjector != null)
                profileRelationProjector.ApplyProfileRelations(CombineProfileRelations(
                    character == null ? null : character.profileRelations,
                    worldAvatarProfile == null ? null : worldAvatarProfile.profileRelations));

            // This is the admission commit. Every read-only preflight,
            // recovery, runtime readiness check and gated local presentation
            // preparation has succeeded before the account/world binding is
            // changed.
            var entered = false;
            yield return authorityClient.EnterWorldRoutine(
                avatarId,
                result => entered = result);
            if (!entered)
            {
                SetAuthorityFailureStatus(authorityClient.LastStatus);
                yield break;
            }
            if (!entryPresentationCoordinator.CommitBeforeSessionActivation())
            {
                authorityClient.ResetWorldEntryConfirmation();
                SetLocalizedStatus(
                    "ui.account.status.entry_activation_failed",
                    "World entry stopped because the local player presentation could not be activated.");
                yield break;
            }
            enteredCurrentWorld = true;
            lastTechnicalStatus = string.Empty;
            entryRuntimeRollbackAvatarId = Guid.Empty;
            entryRuntimeRollbackSessionId = string.Empty;
            SetLocalizedStatus(
                appearanceApplied
                    ? "ui.account.status.entered_with_appearance"
                    : "ui.account.status.entered_with_relations",
                appearanceApplied
                    ? "Entered the selected world with the saved appearance and profile. Gameplay actions are ready."
                    : "Entered the selected world with the saved profile relations. Gameplay actions are ready.");
        }

        private IEnumerator PrepareSelectedMemberAvatarRoutine(
            Action<bool> completed)
        {
            if (SelectedWorldHasRegisteredAvatar())
            {
                completed?.Invoke(true);
                yield break;
            }

            if (authorityClient == null ||
                !TryResolveSelectedWorldAvatarId(out var avatarId))
            {
                completed?.Invoke(false);
                yield break;
            }

            // This is member-scoped, idempotent avatar provisioning. It does
            // not publish content, activate a package, define a Zone, or grant
            // authoring rights. The Authority validates membership and binds
            // only the authenticated user's world-scoped avatar.
            var releaseReady = false;
            yield return authorityClient.VerifyDevelopmentContentReleaseRoutine(
                value => releaseReady = value);
            if (!releaseReady)
            {
                completed?.Invoke(false);
                yield break;
            }

            var zoneKey = string.Empty;
            yield return EnsureRuntimeZoneRoutine(value => zoneKey = value);
            if (string.IsNullOrWhiteSpace(zoneKey) ||
                !TryCreateAvatarSemanticContractPayload(
                    avatarId,
                    out var semanticPayload) ||
                !TryCreateAvatarEntityPayload(
                    avatarId,
                    zoneKey,
                    out var entityPayload))
            {
                completed?.Invoke(false);
                yield break;
            }

            var prepared = false;
            yield return authorityClient.PreparePlayerAvatarRoutine(
                entityPayload,
                semanticPayload,
                value => prepared = value);
            if (!prepared)
            {
                completed?.Invoke(false);
                yield break;
            }

            OntologyAuthorityAccountDashboard refreshed = null;
            yield return authorityClient.LoadAccountDashboardRoutine(
                value => refreshed = value);
            completed?.Invoke(
                refreshed != null && SelectedWorldHasRegisteredAvatar());
        }

        private bool SelectedWorldHasRegisteredAvatar()
        {
            var selectedWorldId = SelectedWorldId;
            if (string.IsNullOrWhiteSpace(selectedWorldId)) return false;
            foreach (var world in Worlds)
            {
                if (world != null &&
                    string.Equals(
                        world.worldId,
                        selectedWorldId,
                        StringComparison.Ordinal) &&
                    Guid.TryParse(world.avatarEntityId, out var avatarId) &&
                    avatarId != Guid.Empty)
                {
                    return true;
                }
            }
            return false;
        }

        private IEnumerator RollbackEntryRuntimeRoutine(
            Guid avatarId,
            string runtimeSessionId)
        {
            if (authorityClient == null)
                yield break;
            yield return authorityClient.DeactivatePlayerRuntimeRoutine(
                avatarId,
                runtimeSessionId);
        }

        private bool TryResolveSelectedWorldAvatarId(out Guid avatarId)
        {
            avatarId = Guid.Empty;
            var selectedWorldId = SelectedWorldId;
            if (!string.IsNullOrWhiteSpace(selectedWorldId))
            {
                foreach (var world in Worlds)
                {
                    if (world == null ||
                        !string.Equals(
                            world.worldId,
                            selectedWorldId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (Guid.TryParse(world.avatarEntityId, out avatarId) &&
                        avatarId != Guid.Empty)
                    {
                        return true;
                    }
                    break;
                }
            }

            CaptureBaseAvatarEntityId();
            if (baseAvatarEntityId == Guid.Empty ||
                !Guid.TryParse(selectedWorldId, out var worldId) ||
                worldId == Guid.Empty ||
                authorityClient == null ||
                !Guid.TryParse(authorityClient.CurrentUserId, out var userId) ||
                userId == Guid.Empty)
                return false;

            avatarId = CreateWorldScopedAvatarId(
                baseAvatarEntityId,
                worldId,
                userId);
            return avatarId != Guid.Empty;
        }

        private void CaptureBaseAvatarEntityId()
        {
            if (baseAvatarEntityId != Guid.Empty || avatarIdentity == null)
                return;
            if (avatarIdentity.TryGetGuid(out var sceneAvatarId) &&
                sceneAvatarId != Guid.Empty)
            {
                baseAvatarEntityId = sceneAvatarId;
            }
        }

        private IEnumerator RegisterAvatarRoutine(
            Guid avatarId,
            Action<OntologyAuthorityCommandResult> completed)
        {
            yield return SendCommandWithRevisionRetryRoutine(
                () => OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.RegisterPlayerAvatar,
                    OntologyWorldAuthorityClient
                        .CreateRegisterPlayerAvatarPayload(avatarId)),
                completed);
        }

        private IEnumerator PlaceAvatarRoutine(
            Guid avatarId,
            string zoneKey,
            Action<OntologyAuthorityCommandResult> completed)
        {
            var settings = authorityClient.Settings;
            var profile = settings == null
                ? null
                : settings.playerAvatarProfile;
            if (profile == null ||
                settings == null ||
                string.IsNullOrWhiteSpace(
                    settings.playerAvatarTemplateId) ||
                string.IsNullOrWhiteSpace(
                    settings.playerAvatarDisplayName))
            {
                SetLocalizedStatus(
                    "ui.account.status.player_template_incomplete",
                    "World entry stopped because the authored player template and profile contract is incomplete.");
                completed?.Invoke(null);
                yield break;
            }
            yield return SendCommandWithRevisionRetryRoutine(
                () => OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.PlaceEntity,
                    OntologyWorldAuthorityClient.CreatePlaceEntityPayload(
                        avatarId,
                        settings.playerAvatarTemplateId,
                        settings.playerAvatarDisplayName,
                        avatarIdentity.transform,
                        zoneKey,
                        CreateAvatarSemanticFacts(
                            profile,
                            settings.defaultAvatarMovementSpeed))),
                completed);
        }

        /// <summary>
        /// World-role semantics belong to the durable world entity, not to the
        /// account profile. Explicit player, faction, life, and damage semantics
        /// let Rule Blocks evaluate the registered avatar without relying on a
        /// scene or prefab name.
        /// </summary>
        public static OntologyAuthorityInitialFact[] CreateAvatarSemanticFacts(
            OntologyActorProfile profile,
            float movementSpeed)
        {
            if (profile == null)
                return Array.Empty<OntologyAuthorityInitialFact>();
            var facts = new List<OntologyAuthorityInitialFact>(
                CreateProfileSemanticFacts(profile));

            facts.RemoveAll(value =>
                value != null &&
                string.Equals(
                    value.predicateId,
                    OntologyPredicates.MovementSpeed,
                    StringComparison.Ordinal));
            facts.Add(new OntologyAuthorityInitialFact
            {
                predicateId = OntologyPredicates.MovementSpeed,
                objectKind = "number",
                objectValueJson = Mathf.Max(0.01f, movementSpeed)
                    .ToString(CultureInfo.InvariantCulture)
            });
            return DeduplicateInitialFacts(facts);
        }

        public static OntologyAuthorityInitialFact[]
            CreateAvatarLocomotionSemanticFacts(
                OntologyActorProfile profile,
                float movementSpeed)
        {
            var predicates = new HashSet<string>(StringComparer.Ordinal)
            {
                OntologyPredicates.GrantsCapability,
                OntologyPredicates.LocomotionAction,
                OntologyPredicates.JumpAction,
                OntologyPredicates.PhysicalProfile,
                OntologyPredicates.IdleAnimationIntent,
                OntologyPredicates.MoveAnimationIntent,
                OntologyPredicates.SprintSpeed,
                OntologyPredicates.MovementSpeed,
                OntologyPredicates.GravityAcceleration,
                OntologyPredicates.JumpTakeoffSpeed,
                OntologyPredicates.GroundStickVelocity,
                OntologyPredicates.MaximumStepHeight,
                OntologyPredicates.GroundClearance,
                OntologyPredicates.ImpactResponseProfile,
                OntologyPredicates.CollisionRole,
                OntologyPredicates.CollisionProxyShape,
                OntologyPredicates.CollisionRadius,
                OntologyPredicates.CollisionHeight,
                OntologyPredicates.CollisionCenterOffsetX,
                OntologyPredicates.CollisionCenterOffsetY,
                OntologyPredicates.CollisionCenterOffsetZ
            };
            return Array.FindAll(
                CreateAvatarSemanticFacts(profile, movementSpeed),
                fact =>
                    fact != null &&
                    predicates.Contains(fact.predicateId) &&
                    (!string.Equals(
                         fact.predicateId,
                         OntologyPredicates.GrantsCapability,
                         StringComparison.Ordinal) ||
                     (string.Equals(
                          fact.objectCanonicalId,
                          "Locomotion",
                          StringComparison.Ordinal) ||
                       string.Equals(
                           fact.objectCanonicalId,
                           "Jump",
                           StringComparison.Ordinal))));
        }

        public static OntologyAuthorityInitialFact[]
            CreateAvatarCollisionProxySemanticFacts(
                OntologyActorProfile profile)
        {
            var predicates = new HashSet<string>(StringComparer.Ordinal)
            {
                OntologyPredicates.CollisionRole,
                OntologyPredicates.CollisionProxyShape,
                OntologyPredicates.CollisionRadius,
                OntologyPredicates.CollisionHeight,
                OntologyPredicates.CollisionCenterOffsetX,
                OntologyPredicates.CollisionCenterOffsetY,
                OntologyPredicates.CollisionCenterOffsetZ
            };
            return Array.FindAll(
                CreateProfileSemanticFacts(profile),
                fact =>
                    fact != null &&
                    predicates.Contains(fact.predicateId));
        }

        public static OntologyAuthorityInitialFact[]
            CreateAvatarHitPresentationSemanticFacts(
                OntologyActorProfile profile)
        {
            return Array.FindAll(
                CreateProfileSemanticFacts(profile),
                fact =>
                    fact != null &&
                    string.Equals(
                        fact.predicateId,
                        OntologyPredicates.HitAnimationIntent,
                        StringComparison.Ordinal));
        }

        public static OntologyAuthorityInitialFact[]
            CreateAvatarDeathPresentationSemanticFacts(
                OntologyActorProfile profile)
        {
            return Array.FindAll(
                CreateProfileSemanticFacts(profile),
                fact =>
                    fact != null &&
                    string.Equals(
                        fact.predicateId,
                        OntologyPredicates.DeathAnimationIntent,
                        StringComparison.Ordinal));
        }

        public static OntologyAuthorityInitialFact[]
            CreateAvatarCombatRelationshipSemanticFacts(
                OntologyActorProfile profile)
        {
            return Array.FindAll(
                CreateProfileSemanticFacts(profile),
                fact => fact != null &&
                        (string.Equals(
                             fact.predicateId,
                             OntologyPredicates.BelongsToFaction,
                             StringComparison.Ordinal) ||
                         string.Equals(
                             fact.predicateId,
                             OntologyPredicates.HostileToFaction,
                             StringComparison.Ordinal)));
        }

        private static OntologyAuthorityInitialFact[]
            CreateProfileSemanticFacts(OntologyActorProfile profile)
        {
            if (profile == null)
                return Array.Empty<OntologyAuthorityInitialFact>();

            var facts = new List<OntologyAuthorityInitialFact>();
            foreach (var concept in profile.defaultConcepts ??
                                    Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(concept))
                {
                    facts.Add(
                        OntologyWorldAuthorityClient.CreateInitialFact(
                            OntologyPredicates.HasConcept,
                            concept.Trim()));
                }
            }
            foreach (var fact in profile.defaultFacts ??
                                 Array.Empty<OntologyFactEntry>())
            {
                if (fact == null ||
                    string.IsNullOrWhiteSpace(fact.predicate) ||
                    string.IsNullOrWhiteSpace(fact.obj))
                {
                    continue;
                }
                facts.Add(
                    OntologyWorldAuthorityClient.CreateInitialFact(
                        fact.predicate.Trim(),
                        fact.obj.Trim()));
            }
            foreach (var capability in profile.ontologyCapabilities ??
                                       Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(capability))
                {
                    facts.Add(
                        OntologyWorldAuthorityClient.CreateInitialFact(
                            OntologyPredicates.GrantsCapability,
                            capability.Trim()));
                }
            }
            return DeduplicateInitialFacts(facts);
        }

        private static OntologyAuthorityInitialFact[]
            DeduplicateInitialFacts(
                IEnumerable<OntologyAuthorityInitialFact> values)
        {
            var result = new List<OntologyAuthorityInitialFact>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values ??
                                  Array.Empty<OntologyAuthorityInitialFact>())
            {
                if (value == null) continue;
                var key = string.Join(
                    "\n",
                    value.predicateId ?? string.Empty,
                    value.objectKind ?? string.Empty,
                    value.objectEntityId ?? string.Empty,
                    value.objectCanonicalId ?? string.Empty,
                    value.objectValueJson ?? string.Empty);
                if (keys.Add(key)) result.Add(value);
            }
            return result.ToArray();
        }

        private IEnumerator EnsureRuntimeZoneRoutine(Action<string> completed)
        {
            OntologyAuthorityWorldZoneProjection[] zones = null;
            yield return authorityClient.LoadWorldZonesRoutine(value => zones = value);
            var selected = SelectRuntimeZone(
                zones,
                avatarIdentity == null
                    ? Vector3.zero
                    : avatarIdentity.transform.position);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                var supportReady = false;
                yield return EnsureDefaultRuntimeWalkableSupportRoutine(
                    selected,
                    value => supportReady = value);
                completed?.Invoke(
                    supportReady
                        ? selected
                        : string.Empty);
                yield break;
            }

            var settings = authorityClient.Settings;
            if (!authorityClient.CanAuthorSelectedWorld ||
                !TryGetDefaultRuntimeZone(
                    settings,
                    out var zoneKey,
                    out var min,
                    out var max,
                    out var simulationMode))
            {
                completed?.Invoke(string.Empty);
                yield break;
            }

            OntologyAuthorityCommandResult result = null;
            yield return SendCommandWithRevisionRetryRoutine(
                () => OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.DefineZone,
                    OntologyWorldAuthorityClient.CreateDefineZonePayload(
                        zoneKey,
                        min.x,
                        min.y,
                        max.x,
                        max.y,
                        simulationMode)),
                value => result = value);
            if (result == null || !result.accepted)
            {
                completed?.Invoke(string.Empty);
                yield break;
            }

            var newSupportReady = false;
            yield return EnsureDefaultRuntimeWalkableSupportRoutine(
                zoneKey,
                value => newSupportReady = value);
            completed?.Invoke(
                newSupportReady
                    ? zoneKey
                    : string.Empty);
        }

        /// <summary>
        /// Authors the starter scene support once through a revisioned world
        /// command. The deterministic entity identity is also the migration
        /// marker: removing its collision_role or retiring the entity later
        /// does not cause this entry flow to reconstruct the behavior.
        /// </summary>
        private IEnumerator EnsureDefaultRuntimeWalkableSupportRoutine(
            string zoneKey,
            Action<bool> completed)
        {
            var settings = authorityClient == null
                ? null
                : authorityClient.Settings;
            if (settings == null ||
                !settings.authorDefaultRuntimeWalkableSupport ||
                !authorityClient.CanAuthorSelectedWorld)
            {
                completed?.Invoke(true);
                yield break;
            }

            if (!Guid.TryParse(
                    authorityClient.CurrentWorldId,
                    out var worldId) ||
                string.IsNullOrWhiteSpace(zoneKey) ||
                !TryGetDefaultRuntimeWalkableSupport(
                    settings,
                    out var templateId,
                    out var displayName,
                    out var center,
                    out var size))
            {
                completed?.Invoke(false);
                yield break;
            }

            var supportId = CreateWorldScopedSupportId(
                worldId,
                zoneKey);
            var loaded = false;
            yield return authorityClient.LoadWorldRoutine(
                value => loaded = value);
            if (!loaded)
            {
                completed?.Invoke(false);
                yield break;
            }
            if (ContainsEntity(
                    authorityClient.CurrentProjection,
                    supportId))
            {
                completed?.Invoke(true);
                yield break;
            }

            var facts = new[]
            {
                OntologyWorldAuthorityClient.CreateInitialFact(
                    OntologyPredicates.PhysicalProfile,
                    OntologyObjects.StaticAnchored),
                OntologyWorldAuthorityClient.CreateInitialFact(
                    OntologyPredicates.CollisionRole,
                    OntologyObjects.WalkableSupport),
                OntologyWorldAuthorityClient.CreateInitialFact(
                    OntologyPredicates.CollisionProxyShape,
                    OntologyObjects.Box),
                OntologyWorldAuthorityClient.CreateInitialFact(
                    OntologyPredicates.CollisionSizeX,
                    size.x.ToString(CultureInfo.InvariantCulture)),
                OntologyWorldAuthorityClient.CreateInitialFact(
                    OntologyPredicates.CollisionSizeY,
                    size.y.ToString(CultureInfo.InvariantCulture)),
                OntologyWorldAuthorityClient.CreateInitialFact(
                    OntologyPredicates.CollisionSizeZ,
                    size.z.ToString(CultureInfo.InvariantCulture))
            };
            OntologyAuthorityCommandResult result = null;
            yield return SendCommandWithRevisionRetryRoutine(
                () => OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.PlaceEntity,
                    OntologyWorldAuthorityClient.CreatePlaceEntityPayload(
                        supportId,
                        templateId,
                        displayName,
                        center,
                        Vector3.zero,
                        Vector3.one,
                        zoneKey,
                        facts)),
                value => result = value);
            completed?.Invoke(
                result != null &&
                (result.accepted ||
                 string.Equals(
                     result.rejectionCode,
                     "entity_id_already_exists",
                     StringComparison.Ordinal)));
        }

        private IEnumerator EnsureAvatarRuntimeFoundationRoutine(
            Guid avatarId,
            string zoneKey,
            Action<string> completed)
        {
            authorityClient.SetProjectionZoneKey(
                string.Empty,
                false);
            var projectionLoaded = false;
            yield return authorityClient.LoadWorldRoutine(
                value => projectionLoaded = value);
            if (!projectionLoaded)
            {
                completed?.Invoke(string.Empty);
                yield break;
            }

            OntologyAuthorityAvatarCheckpoint checkpoint = null;
            yield return authorityClient.LoadAvatarCheckpointRoutine(
                avatarId,
                value => checkpoint = value);
            OntologyAuthorityWorldZoneProjection[] zones = null;
            yield return authorityClient.LoadWorldZonesRoutine(
                value => zones = value);
            var resolvedZoneKey = ResolveRuntimeZone(
                zoneKey,
                checkpoint,
                zones);
            if (string.IsNullOrWhiteSpace(resolvedZoneKey))
            {
                completed?.Invoke(string.Empty);
                yield break;
            }

            if (authorityClient.CanAuthorSelectedWorld &&
                ContainsLegacyEquipmentRelation(
                    authorityClient.CurrentProjection))
            {
                OntologyAuthorityCommandResult migrationResult = null;
                yield return SendCommandWithRevisionRetryRoutine(
                    () => OntologyWorldAuthorityClient.CreateCommand(
                        OntologyWorldCommandKinds
                            .MigrateLegacyEquipmentRelations,
                        "{}"),
                    value => migrationResult = value);
                if (migrationResult == null || !migrationResult.accepted)
                {
                    completed?.Invoke(string.Empty);
                    yield break;
                }
            }

            if (authorityClient.CanAuthorSelectedWorld &&
                ContainsUniquelyZonableEntity(
                    authorityClient.CurrentProjection,
                    zones))
            {
                OntologyAuthorityCommandResult zoneMigrationResult = null;
                yield return SendCommandWithRevisionRetryRoutine(
                    () => OntologyWorldAuthorityClient.CreateCommand(
                        OntologyWorldCommandKinds
                            .MigrateUnassignedEntityZones,
                        "{}"),
                    value => zoneMigrationResult = value);
                if (zoneMigrationResult == null ||
                    !zoneMigrationResult.accepted)
                {
                    completed?.Invoke(string.Empty);
                    yield break;
                }
            }

            if (checkpoint == null ||
                checkpoint.transform == null ||
                !string.Equals(
                    checkpoint.zoneKey?.Trim(),
                    resolvedZoneKey,
                    StringComparison.Ordinal))
            {
                OntologyAuthorityCommandResult checkpointResult = null;
                yield return SendCommandWithRevisionRetryRoutine(
                    () => OntologyWorldAuthorityClient.CreateCommand(
                        OntologyWorldCommandKinds.SaveAvatarCheckpoint,
                        checkpoint?.transform == null
                            ? OntologyWorldAuthorityClient
                                .CreateAvatarCheckpointPayload(
                                    avatarId,
                                    resolvedZoneKey,
                                    avatarIdentity.transform)
                            : OntologyWorldAuthorityClient
                                .CreateAvatarCheckpointPayload(
                                    avatarId,
                                    resolvedZoneKey,
                                    checkpoint.transform)),
                    value => checkpointResult = value);
                if (checkpointResult == null || !checkpointResult.accepted)
                {
                    completed?.Invoke(string.Empty);
                    yield break;
                }
            }

            authorityClient.SetProjectionZoneKey(
                resolvedZoneKey,
                false);
            projectionLoaded = false;
            yield return authorityClient.LoadWorldRoutine(
                value => projectionLoaded = value);
            if (!projectionLoaded)
            {
                completed?.Invoke(string.Empty);
                yield break;
            }

            var runtimeActivated = false;
            yield return authorityClient.ActivatePlayerRuntimeRoutine(
                avatarId,
                resolvedZoneKey,
                value => runtimeActivated = value);
            completed?.Invoke(
                runtimeActivated
                    ? resolvedZoneKey
                    : string.Empty);
        }

        private IEnumerator VerifyAvatarSemanticContractRoutine(
            Guid avatarId,
            Action<bool> completed)
        {
            if (!TryCreateAvatarSemanticContractPayload(
                    avatarId,
                    out var payload))
            {
                completed?.Invoke(false);
                yield break;
            }

            // Semantic preflight is read-only. A remote mobile/test connection
            // can close an HTTP response without changing Authority state, so
            // retry the exact immutable manifest rather than treating a
            // transport interruption as an ontology contract failure. A
            // rejected or still-unreadable contract continues to fail closed.
            const int maximumAttempts = 3;
            for (var attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                var verified = false;
                yield return authorityClient.VerifySemanticContractRoutine(
                    payload,
                    value => verified = value);
                if (verified)
                {
                    completed?.Invoke(true);
                    yield break;
                }

                if (attempt < maximumAttempts)
                    yield return new WaitForSecondsRealtime(0.2f * attempt);
            }

            completed?.Invoke(false);
        }

        private IEnumerator PrepareAvatarSemanticContractRoutine(
            Guid avatarId,
            Action<bool> completed)
        {
            if (!authorityClient.CanAuthorSelectedWorld)
            {
                completed?.Invoke(false);
                yield break;
            }

            // Explicit preparation owns both release publication/activation
            // and the following atomic avatar contract transaction. Neither
            // operation is reachable from normal runtime entry.
            var releaseReady = false;
            yield return authorityClient
                .PrepareDevelopmentContentReleaseRoutine(
                    value => releaseReady = value);
            if (!releaseReady)
            {
                completed?.Invoke(false);
                yield break;
            }

            var zoneKey = string.Empty;
            yield return EnsureRuntimeZoneRoutine(value => zoneKey = value);
            if (string.IsNullOrWhiteSpace(zoneKey) ||
                !TryCreateAvatarSemanticContractPayload(
                    avatarId,
                    out var payload) ||
                !TryCreateAvatarEntityPayload(
                    avatarId,
                    zoneKey,
                    out var entityPayload))
            {
                completed?.Invoke(false);
                yield break;
            }

            var contractPrepared = false;
            yield return authorityClient.PreparePlayerAvatarRoutine(
                entityPayload,
                payload,
                value => contractPrepared = value);
            if (!contractPrepared)
            {
                completed?.Invoke(false);
                yield break;
            }

            // Preparation is not considered complete until the exact same
            // immutable manifest passes the read-only admission preflight.
            var contractVerified = false;
            yield return authorityClient.VerifySemanticContractRoutine(
                payload,
                value => contractVerified = value);
            if (!contractVerified)
            {
                completed?.Invoke(false);
                yield break;
            }

            var preparedRuntimeZoneKey = string.Empty;
            yield return EnsureAvatarRuntimeFoundationRoutine(
                avatarId,
                zoneKey,
                value => preparedRuntimeZoneKey = value);
            completed?.Invoke(!string.IsNullOrWhiteSpace(
                preparedRuntimeZoneKey));
        }

        private bool TryCreateAvatarEntityPayload(
            Guid avatarId,
            string zoneKey,
            out string payload)
        {
            payload = string.Empty;
            var settings = authorityClient?.Settings;
            var profile = settings?.playerAvatarProfile;
            if (avatarId == Guid.Empty || settings == null || profile == null ||
                avatarIdentity == null || string.IsNullOrWhiteSpace(zoneKey) ||
                string.IsNullOrWhiteSpace(settings.playerAvatarTemplateId) ||
                string.IsNullOrWhiteSpace(settings.playerAvatarDisplayName))
            {
                return false;
            }

            payload = OntologyWorldAuthorityClient.CreatePlaceEntityPayload(
                avatarId,
                settings.playerAvatarTemplateId,
                settings.playerAvatarDisplayName,
                avatarIdentity.transform,
                zoneKey,
                CreateAvatarSemanticFacts(
                    profile,
                    settings.defaultAvatarMovementSpeed));
            return !string.IsNullOrWhiteSpace(payload);
        }

        private bool TryCreateAvatarSemanticContractPayload(
            Guid avatarId,
            out string payload)
        {
            payload = string.Empty;
            var settings = authorityClient?.Settings;
            var profile = settings?.playerAvatarProfile;
            if (avatarId == Guid.Empty || settings == null || profile == null ||
                !TryResolveDevelopmentRuleVersion(
                    settings,
                    OntologyRuleBlocks.RespawnPlayerOnDeath,
                    out var respawnVersion) ||
                !TryResolveDevelopmentRuleVersion(
                    settings,
                    OntologyRuleBlocks.MovePlayerFromIntent,
                    out var locomotionVersion) ||
                !TryResolveDevelopmentRuleVersion(
                    settings,
                    OntologyRuleBlocks.JumpPlayerFromIntent,
                    out var jumpVersion))
            {
                return false;
            }

            const string slotId = "player_avatar_semantic_contract";
            const string contractId = "player_avatar";
            if (!authorityClient.TryGetSelectedWorldContentPackage(
                    out var packageId,
                    out var packageVersion))
            {
                return false;
            }

            var staticFacts = CreateAvatarSemanticFacts(
                    profile,
                    settings.defaultAvatarMovementSpeed)
                .Where(value =>
                    value != null &&
                    !string.Equals(
                        value.predicateId,
                        OntologyPredicates.CurrentHealth,
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        value.predicateId,
                        OntologyPredicates.IsAlive,
                        StringComparison.Ordinal))
                .ToList();
            var change = new OntologyMeaningPackageChange
            {
                operation = "apply",
                applicationId = OntologyWorldAuthorityBridge
                    .CreateMeaningPackageApplicationId(
                        avatarId,
                        slotId,
                        contractId)
                    .ToString("D"),
                slotId = slotId,
                packageId = packageId,
                adoptExistingContributions = true,
                requiresOwnedBinding = true,
                authorityFacts = staticFacts,
                replacePredicateIds = staticFacts
                    .Select(value => value.predicateId)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                requiredConceptIds = (profile.defaultConcepts ??
                                      Array.Empty<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                ruleBlocks = new List<OntologyMeaningPackageRuleBlock>
                {
                    CreateAvatarContractRuleBlock(
                        avatarId,
                        OntologyRuleBlocks.RespawnPlayerOnDeath,
                        respawnVersion),
                    CreateAvatarContractRuleBlock(
                        avatarId,
                        OntologyRuleBlocks.MovePlayerFromIntent,
                        locomotionVersion),
                    CreateAvatarContractRuleBlock(
                        avatarId,
                        OntologyRuleBlocks.JumpPlayerFromIntent,
                        jumpVersion)
                }
            };
            payload = OntologyWorldAuthorityClient
                .CreateSemanticContractPayload(
                    avatarId,
                    change,
                    contractId,
                    OntologySemanticContracts.PlayerAvatarVersion,
                    packageVersion,
                    OntologyPredicates.SemanticContractVersion,
                    OntologyPredicates.SemanticContractChecksum);
            return !string.IsNullOrWhiteSpace(payload) && payload != "{}";
        }

        private static OntologyMeaningPackageRuleBlock
            CreateAvatarContractRuleBlock(
                Guid avatarId,
                string ruleId,
                int ruleVersion)
        {
            return new OntologyMeaningPackageRuleBlock
            {
                bindingId = OntologyWorldAuthorityBridge.CreateRuleBindingId(
                        avatarId,
                        ruleId,
                        "?actor")
                    .ToString("D"),
                ruleId = ruleId,
                ruleVersion = ruleVersion,
                bindingVariable = "?actor"
            };
        }

        // Retained temporarily as an explicit legacy migration implementation
        // for source compatibility. Runtime entry never calls this method; the
        // atomic PrepareAvatarSemanticContractRoutine is the supported path.
        [Obsolete("Use PrepareAvatarSemanticContractRoutine atomic contract preparation.")]
        private IEnumerator EnsureAvatarSemanticContractRoutine(
            Guid avatarId,
            Action<bool> completed)
        {
            if (authorityClient == null)
            {
                completed?.Invoke(false);
                yield break;
            }

            var projectionLoaded = false;
            yield return authorityClient.LoadWorldRoutine(
                value => projectionLoaded = value);
            if (!projectionLoaded)
            {
                completed?.Invoke(false);
                yield break;
            }

            var projection = authorityClient?.CurrentProjection;
            var currentVersion = ResolveSemanticContractVersion(
                projection,
                avatarId);
            var playerProfile = authorityClient.Settings == null
                ? null
                : authorityClient.Settings.playerAvatarProfile;
            var respawnBindingWasEnabled =
                FindActiveRuleBinding(
                    projection,
                    avatarId,
                    OntologyRuleBlocks.RespawnPlayerOnDeath) != null;
            var durableRespawnRuleVersion = 0;
            var locomotionRuleVersion = 0;
            var jumpRuleVersion = 0;
            if (playerProfile == null)
            {
                completed?.Invoke(false);
                yield break;
            }
            if (currentVersion >=
                OntologySemanticContracts.PlayerAvatarVersion)
            {
                completed?.Invoke(true);
                yield break;
            }

            if (authorityClient == null ||
                !authorityClient.CanAuthorSelectedWorld)
            {
                completed?.Invoke(false);
                yield break;
            }

            if (currentVersion < 1 &&
                !HasActiveFact(
                    projection,
                    avatarId,
                    OntologyPredicates.RespawnAction))
            {
                OntologyAuthorityCommandResult actionFactResult = null;
                yield return SendCommandWithRevisionRetryRoutine(
                    () => OntologyWorldAuthorityClient.CreateCommand(
                        OntologyWorldCommandKinds.SetAuthoredFact,
                        OntologyWorldAuthorityClient.CreateFactPayload(
                            avatarId,
                            OntologyWorldAuthorityClient.CreateInitialFact(
                                OntologyPredicates.RespawnAction,
                                OntologyActions.RespawnAvatar))),
                    value => actionFactResult = value);
                if (actionFactResult == null ||
                    !actionFactResult.accepted)
                {
                    completed?.Invoke(false);
                    yield break;
                }
            }

            if (currentVersion < 1 &&
                !HasActiveRuleBinding(
                    projection,
                    avatarId,
                    OntologyRuleBlocks.RespawnPlayerOnDeath))
            {
                var bindingId =
                    OntologyWorldAuthorityBridge.CreateRuleBindingId(
                        avatarId,
                        OntologyRuleBlocks.RespawnPlayerOnDeath,
                        "?actor");
                OntologyAuthorityCommandResult bindingResult = null;
                yield return SendCommandWithRevisionRetryRoutine(
                    () => OntologyWorldAuthorityClient.CreateCommand(
                        OntologyWorldCommandKinds.AddRuleBlock,
                        OntologyWorldAuthorityClient.CreateRuleBlockPayload(
                            bindingId,
                            avatarId,
                            OntologyRuleBlocks.RespawnPlayerOnDeath,
                            "?actor")),
                    value => bindingResult = value);
                if (bindingResult == null ||
                    !bindingResult.accepted)
                {
                    completed?.Invoke(false);
                    yield break;
                }
            }

            if (currentVersion < 2)
            {
                var movementSpeed = authorityClient.Settings == null
                    ? 5f
                    : authorityClient.Settings
                        .defaultAvatarMovementSpeed;
                foreach (var semanticFact in
                         CreateAvatarLocomotionSemanticFacts(
                             playerProfile,
                             movementSpeed))
                {
                    var hasFact = string.Equals(
                            semanticFact.objectKind,
                            "canonical",
                            StringComparison.Ordinal)
                        ? HasActiveFactObject(
                            authorityClient.CurrentProjection,
                            avatarId,
                            semanticFact.predicateId,
                            semanticFact.objectCanonicalId)
                        : HasActiveFact(
                            authorityClient.CurrentProjection,
                            avatarId,
                            semanticFact.predicateId);
                    if (hasFact) continue;

                    OntologyAuthorityCommandResult factResult = null;
                    yield return SendCommandWithRevisionRetryRoutine(
                        () => OntologyWorldAuthorityClient.CreateCommand(
                            OntologyWorldCommandKinds.SetAuthoredFact,
                            OntologyWorldAuthorityClient.CreateFactPayload(
                                avatarId,
                                semanticFact)),
                        value => factResult = value);
                    if (factResult == null || !factResult.accepted)
                    {
                        completed?.Invoke(false);
                        yield break;
                    }
                }

                if (!HasActiveRuleBinding(
                        authorityClient.CurrentProjection,
                        avatarId,
                        OntologyRuleBlocks.MovePlayerFromIntent))
                {
                    var locomotionBindingId =
                        OntologyWorldAuthorityBridge.CreateRuleBindingId(
                            avatarId,
                            OntologyRuleBlocks.MovePlayerFromIntent,
                            "?actor");
                    OntologyAuthorityCommandResult locomotionBindingResult =
                        null;
                    yield return SendCommandWithRevisionRetryRoutine(
                        () => OntologyWorldAuthorityClient.CreateCommand(
                            OntologyWorldCommandKinds.AddRuleBlock,
                            OntologyWorldAuthorityClient
                                .CreateRuleBlockPayload(
                                    locomotionBindingId,
                                    avatarId,
                                    OntologyRuleBlocks
                                        .MovePlayerFromIntent,
                                    "?actor")),
                        value => locomotionBindingResult = value);
                    if (locomotionBindingResult == null ||
                        !locomotionBindingResult.accepted)
                    {
                        completed?.Invoke(false);
                        yield break;
                    }
                }
            }

            if (currentVersion < 4)
            {
                // Version 4 repairs the durable vitality foundation without
                // inventing a health constant. A living avatar resumes at its
                // authored maximum; a dead avatar remains at zero.
                if (!HasActiveFact(
                        authorityClient.CurrentProjection,
                        avatarId,
                        OntologyPredicates.CurrentHealth))
                {
                    if (!TryResolveSingleFactValue(
                            authorityClient.CurrentProjection,
                            avatarId,
                            OntologyPredicates.MaximumHealth,
                            out var maximumHealth) ||
                        !TryResolveSingleFactValue(
                            authorityClient.CurrentProjection,
                            avatarId,
                            OntologyPredicates.IsAlive,
                            out var aliveValue) ||
                        !bool.TryParse(
                            aliveValue,
                            out var isAlive))
                    {
                        completed?.Invoke(false);
                        yield break;
                    }

                    OntologyAuthorityCommandResult healthResult = null;
                    yield return SendCommandWithRevisionRetryRoutine(
                        () => OntologyWorldAuthorityClient.CreateCommand(
                            OntologyWorldCommandKinds.SetAuthoredFact,
                            OntologyWorldAuthorityClient.CreateFactPayload(
                                avatarId,
                                OntologyWorldAuthorityClient
                                    .CreateInitialFact(
                                        OntologyPredicates.CurrentHealth,
                                        isAlive ? maximumHealth : "0"))),
                        value => healthResult = value);
                    if (healthResult == null ||
                        !healthResult.accepted)
                    {
                        completed?.Invoke(false);
                        yield break;
                    }
                }

                var respawnBinding = FindActiveRuleBinding(
                    authorityClient.CurrentProjection,
                    avatarId,
                    OntologyRuleBlocks.RespawnPlayerOnDeath);
                if (respawnBinding != null &&
                    !TryResolveDevelopmentRuleVersion(
                        authorityClient.Settings,
                        OntologyRuleBlocks.RespawnPlayerOnDeath,
                        out durableRespawnRuleVersion))
                {
                    completed?.Invoke(false);
                    yield break;
                }
                if (respawnBinding != null &&
                    respawnBinding.ruleVersion !=
                    durableRespawnRuleVersion &&
                    Guid.TryParse(
                        respawnBinding.bindingId,
                        out var oldBindingId))
                {
                    OntologyAuthorityCommandResult removeResult = null;
                    yield return SendCommandWithRevisionRetryRoutine(
                        () => OntologyWorldAuthorityClient.CreateCommand(
                            OntologyWorldCommandKinds.RemoveRuleBlock,
                            OntologyWorldAuthorityClient
                                .CreateRemoveRuleBlockPayload(
                                    oldBindingId)),
                        value => removeResult = value);
                    if (removeResult == null ||
                        !removeResult.accepted)
                    {
                        completed?.Invoke(false);
                        yield break;
                    }

                    OntologyAuthorityCommandResult addResult = null;
                    yield return SendCommandWithRevisionRetryRoutine(
                        () => OntologyWorldAuthorityClient.CreateCommand(
                            OntologyWorldCommandKinds.AddRuleBlock,
                            OntologyWorldAuthorityClient
                                .CreateRuleBlockPayload(
                                    Guid.NewGuid(),
                                    avatarId,
                                    OntologyRuleBlocks
                                        .RespawnPlayerOnDeath,
                                    "?actor",
                                    durableRespawnRuleVersion)),
                        value => addResult = value);
                    if (addResult == null ||
                        !addResult.accepted)
                    {
                        completed?.Invoke(false);
                        yield break;
                    }
                }
            }

            if (currentVersion < 6)
            {
                if (!TryResolveDevelopmentRuleVersion(
                        authorityClient.Settings,
                        OntologyRuleBlocks.JumpPlayerFromIntent,
                        out jumpRuleVersion))
                {
                    completed?.Invoke(false);
                    yield break;
                }
                var movementSpeed = authorityClient.Settings == null
                    ? 5f
                    : authorityClient.Settings
                        .defaultAvatarMovementSpeed;
                var jumpPredicates =
                    new HashSet<string>(StringComparer.Ordinal)
                    {
                        OntologyPredicates.GrantsCapability,
                        OntologyPredicates.JumpAction,
                        OntologyPredicates.JumpTakeoffSpeed,
                        OntologyPredicates.GravityAcceleration,
                        OntologyPredicates.GroundStickVelocity,
                        OntologyPredicates.ImpactResponseProfile
                    };
                foreach (var semanticFact in
                         CreateAvatarLocomotionSemanticFacts(
                             playerProfile,
                             movementSpeed))
                {
                    if (!jumpPredicates.Contains(
                            semanticFact.predicateId) ||
                        (string.Equals(
                             semanticFact.predicateId,
                             OntologyPredicates.GrantsCapability,
                             StringComparison.Ordinal) &&
                         !string.Equals(
                             semanticFact.objectCanonicalId,
                             "Jump",
                             StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    var hasFact = string.Equals(
                            semanticFact.objectKind,
                            "canonical",
                            StringComparison.Ordinal)
                        ? HasActiveFactObject(
                            authorityClient.CurrentProjection,
                            avatarId,
                            semanticFact.predicateId,
                            semanticFact.objectCanonicalId)
                        : HasActiveFact(
                            authorityClient.CurrentProjection,
                            avatarId,
                            semanticFact.predicateId);
                    if (hasFact) continue;

                    OntologyAuthorityCommandResult factResult = null;
                    yield return SendCommandWithRevisionRetryRoutine(
                        () => OntologyWorldAuthorityClient.CreateCommand(
                            OntologyWorldCommandKinds.SetAuthoredFact,
                            OntologyWorldAuthorityClient.CreateFactPayload(
                                avatarId,
                                semanticFact)),
                        value => factResult = value);
                    if (factResult == null || !factResult.accepted)
                    {
                        completed?.Invoke(false);
                        yield break;
                    }
                }

                var jumpBinding = FindActiveRuleBinding(
                    authorityClient.CurrentProjection,
                    avatarId,
                    OntologyRuleBlocks.JumpPlayerFromIntent);
                if (jumpBinding != null &&
                    jumpBinding.ruleVersion != jumpRuleVersion &&
                    Guid.TryParse(
                        jumpBinding.bindingId,
                        out var previousJumpBindingId))
                {
                    OntologyAuthorityCommandResult removeResult = null;
                    yield return SendCommandWithRevisionRetryRoutine(
                        () => OntologyWorldAuthorityClient.CreateCommand(
                            OntologyWorldCommandKinds.RemoveRuleBlock,
                            OntologyWorldAuthorityClient
                                .CreateRemoveRuleBlockPayload(
                                    previousJumpBindingId)),
                        value => removeResult = value);
                    if (removeResult == null || !removeResult.accepted)
                    {
                        completed?.Invoke(false);
                        yield break;
                    }
                    jumpBinding = null;
                }

                if (jumpBinding == null)
                {
                    OntologyAuthorityCommandResult addResult = null;
                    yield return SendCommandWithRevisionRetryRoutine(
                        () => OntologyWorldAuthorityClient.CreateCommand(
                            OntologyWorldCommandKinds.AddRuleBlock,
                            OntologyWorldAuthorityClient
                                .CreateRuleBlockPayload(
                                    Guid.NewGuid(),
                                    avatarId,
                                    OntologyRuleBlocks
                                        .JumpPlayerFromIntent,
                                    "?actor",
                                    jumpRuleVersion)),
                        value => addResult = value);
                    if (addResult == null || !addResult.accepted)
                    {
                        completed?.Invoke(false);
                        yield break;
                    }
                }
            }

            if (currentVersion < 7)
            {
                if (!HasActiveFactObject(
                        authorityClient.CurrentProjection,
                        avatarId,
                        OntologyPredicates.PhysicalProfile,
                        OntologyObjects.LocalCharacterController))
                {
                    OntologyAuthorityCommandResult profileResult = null;
                    yield return SendCommandWithRevisionRetryRoutine(
                        () => OntologyWorldAuthorityClient.CreateCommand(
                            OntologyWorldCommandKinds.SetAuthoredFact,
                            OntologyWorldAuthorityClient.CreateFactPayload(
                                avatarId,
                                OntologyWorldAuthorityClient
                                    .CreateInitialFact(
                                        OntologyPredicates.PhysicalProfile,
                                        OntologyObjects
                                            .LocalCharacterController))),
                        value => profileResult = value);
                    if (profileResult == null || !profileResult.accepted)
                    {
                        completed?.Invoke(false);
                        yield break;
                    }
                }

                if (!TryResolveDevelopmentRuleVersion(
                        authorityClient.Settings,
                        OntologyRuleBlocks.MovePlayerFromIntent,
                        out locomotionRuleVersion) ||
                    !TryResolveDevelopmentRuleVersion(
                        authorityClient.Settings,
                        OntologyRuleBlocks.JumpPlayerFromIntent,
                        out jumpRuleVersion))
                {
                    completed?.Invoke(false);
                    yield break;
                }

                var locomotionBindingReady = false;
                yield return EnsureRuleBindingVersionRoutine(
                    avatarId,
                    OntologyRuleBlocks.MovePlayerFromIntent,
                    locomotionRuleVersion,
                    value => locomotionBindingReady = value);
                if (!locomotionBindingReady)
                {
                    completed?.Invoke(false);
                    yield break;
                }

                var jumpBindingReady = false;
                yield return EnsureRuleBindingVersionRoutine(
                    avatarId,
                    OntologyRuleBlocks.JumpPlayerFromIntent,
                    jumpRuleVersion,
                    value => jumpBindingReady = value);
                if (!jumpBindingReady)
                {
                    completed?.Invoke(false);
                    yield break;
                }
            }

            if (currentVersion < 8)
            {
                foreach (var semanticFact in
                         CreateAvatarCollisionProxySemanticFacts(
                             playerProfile))
                {
                    var hasFact = string.Equals(
                            semanticFact.objectKind,
                            "canonical",
                            StringComparison.Ordinal)
                        ? HasActiveFactObject(
                            authorityClient.CurrentProjection,
                            avatarId,
                            semanticFact.predicateId,
                            semanticFact.objectCanonicalId)
                        : HasActiveFact(
                            authorityClient.CurrentProjection,
                            avatarId,
                            semanticFact.predicateId);
                    if (hasFact) continue;

                    OntologyAuthorityCommandResult factResult = null;
                    yield return SendCommandWithRevisionRetryRoutine(
                        () => OntologyWorldAuthorityClient.CreateCommand(
                            OntologyWorldCommandKinds.SetAuthoredFact,
                            OntologyWorldAuthorityClient.CreateFactPayload(
                                avatarId,
                                semanticFact)),
                        value => factResult = value);
                    if (factResult == null || !factResult.accepted)
                    {
                        completed?.Invoke(false);
                        yield break;
                    }
                }
            }

            if (currentVersion < 9)
            {
                var movementSpeed = authorityClient.Settings == null
                    ? 5f
                    : authorityClient.Settings
                        .defaultAvatarMovementSpeed;
                foreach (var semanticFact in
                         CreateAvatarLocomotionSemanticFacts(
                             playerProfile,
                             movementSpeed))
                {
                    if (semanticFact == null ||
                        (semanticFact.predicateId !=
                         OntologyPredicates.MaximumStepHeight &&
                         semanticFact.predicateId !=
                         OntologyPredicates.GroundClearance) ||
                        HasActiveFact(
                            authorityClient.CurrentProjection,
                            avatarId,
                            semanticFact.predicateId))
                    {
                        continue;
                    }

                    OntologyAuthorityCommandResult factResult = null;
                    yield return SendCommandWithRevisionRetryRoutine(
                        () => OntologyWorldAuthorityClient.CreateCommand(
                            OntologyWorldCommandKinds.SetAuthoredFact,
                            OntologyWorldAuthorityClient.CreateFactPayload(
                                avatarId,
                                semanticFact)),
                        value => factResult = value);
                    if (factResult == null || !factResult.accepted)
                    {
                        completed?.Invoke(false);
                        yield break;
                    }
                }
            }

            if (currentVersion < 10)
            {
                // Version 10 migrates only the authored presentation intent.
                // It does not re-author unrelated player semantics, so a later
                // user removal remains effective once this migration is marked.
                foreach (var semanticFact in
                         CreateAvatarHitPresentationSemanticFacts(
                             playerProfile))
                {
                    var hasFact = string.Equals(
                            semanticFact.objectKind,
                            "canonical",
                            StringComparison.Ordinal)
                        ? HasActiveFactObject(
                            authorityClient.CurrentProjection,
                            avatarId,
                            semanticFact.predicateId,
                            semanticFact.objectCanonicalId)
                        : HasActiveFact(
                            authorityClient.CurrentProjection,
                            avatarId,
                            semanticFact.predicateId);
                    if (hasFact) continue;

                    OntologyAuthorityCommandResult factResult = null;
                    yield return SendCommandWithRevisionRetryRoutine(
                        () => OntologyWorldAuthorityClient.CreateCommand(
                            OntologyWorldCommandKinds.SetAuthoredFact,
                            OntologyWorldAuthorityClient.CreateFactPayload(
                                avatarId,
                                semanticFact)),
                        value => factResult = value);
                    if (factResult == null || !factResult.accepted)
                    {
                        completed?.Invoke(false);
                        yield break;
                    }
                }
            }

            if (currentVersion < 11)
            {
                // Version 11 adds only the Authority-projected death
                // presentation contract. Death state still belongs to the
                // evaluated vitality/lifecycle rules, never to this adapter.
                foreach (var semanticFact in
                         CreateAvatarDeathPresentationSemanticFacts(
                             playerProfile))
                {
                    var hasFact = string.Equals(
                            semanticFact.objectKind,
                            "canonical",
                            StringComparison.Ordinal)
                        ? HasActiveFactObject(
                            authorityClient.CurrentProjection,
                            avatarId,
                            semanticFact.predicateId,
                            semanticFact.objectCanonicalId)
                        : HasActiveFact(
                            authorityClient.CurrentProjection,
                            avatarId,
                            semanticFact.predicateId);
                    if (hasFact) continue;

                    OntologyAuthorityCommandResult factResult = null;
                    yield return SendCommandWithRevisionRetryRoutine(
                        () => OntologyWorldAuthorityClient.CreateCommand(
                            OntologyWorldCommandKinds.SetAuthoredFact,
                            OntologyWorldAuthorityClient.CreateFactPayload(
                                avatarId,
                                semanticFact)),
                        value => factResult = value);
                    if (factResult == null || !factResult.accepted)
                    {
                        completed?.Invoke(false);
                        yield break;
                    }
                }
            }

            if (currentVersion < 12)
            {
                foreach (var semanticFact in
                         CreateAvatarCombatRelationshipSemanticFacts(
                             playerProfile))
                {
                    if (HasActiveFactObject(
                            authorityClient.CurrentProjection,
                            avatarId,
                            semanticFact.predicateId,
                            semanticFact.objectCanonicalId))
                    {
                        continue;
                    }
                    OntologyAuthorityCommandResult factResult = null;
                    yield return SendCommandWithRevisionRetryRoutine(
                        () => OntologyWorldAuthorityClient.CreateCommand(
                            OntologyWorldCommandKinds.SetAuthoredFact,
                            OntologyWorldAuthorityClient.CreateFactPayload(
                                avatarId,
                                semanticFact)),
                        value => factResult = value);
                    if (factResult == null || !factResult.accepted)
                    {
                        completed?.Invoke(false);
                        yield break;
                    }
                }
            }

            OntologyAuthorityCommandResult markerResult = null;
            yield return SendCommandWithRevisionRetryRoutine(
                () => OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.SetAuthoredFact,
                    OntologyWorldAuthorityClient.CreateFactPayload(
                        avatarId,
                        OntologyWorldAuthorityClient.CreateInitialFact(
                            OntologyPredicates.SemanticContractVersion,
                            OntologySemanticContracts.PlayerAvatarVersion
                                .ToString(
                                    CultureInfo.InvariantCulture)))),
                value => markerResult = value);
            if (markerResult == null || !markerResult.accepted)
            {
                completed?.Invoke(false);
                yield break;
            }

            var reloaded = false;
            yield return authorityClient.LoadWorldRoutine(
                value => reloaded = value);
            completed?.Invoke(
                reloaded &&
                ResolveSemanticContractVersion(
                    authorityClient.CurrentProjection,
                    avatarId) >=
                OntologySemanticContracts.PlayerAvatarVersion &&
                (currentVersion >= 1 ||
                 (HasActiveFact(
                      authorityClient.CurrentProjection,
                      avatarId,
                      OntologyPredicates.RespawnAction) &&
                  HasActiveRuleBinding(
                      authorityClient.CurrentProjection,
                      avatarId,
                      OntologyRuleBlocks.RespawnPlayerOnDeath))) &&
                (currentVersion >= 2 ||
                 (HasActiveFactObject(
                      authorityClient.CurrentProjection,
                      avatarId,
                      OntologyPredicates.LocomotionAction,
                      OntologyActions.MoveAvatar) &&
                  HasActiveRuleBinding(
                      authorityClient.CurrentProjection,
                      avatarId,
                      OntologyRuleBlocks.MovePlayerFromIntent))) &&
                (currentVersion >= 4 ||
                 (HasActiveFact(
                      authorityClient.CurrentProjection,
                      avatarId,
                      OntologyPredicates.CurrentHealth) &&
                  (!respawnBindingWasEnabled ||
                   HasActiveRuleBindingVersion(
                       authorityClient.CurrentProjection,
                       avatarId,
                       OntologyRuleBlocks.RespawnPlayerOnDeath,
                       durableRespawnRuleVersion)))) &&
                (currentVersion >= 6 ||
                 (HasActiveFactObject(
                      authorityClient.CurrentProjection,
                      avatarId,
                      OntologyPredicates.JumpAction,
                      OntologyActions.JumpAvatar) &&
                  HasActiveFact(
                      authorityClient.CurrentProjection,
                      avatarId,
                      OntologyPredicates.JumpTakeoffSpeed) &&
                  HasActiveRuleBindingVersion(
                      authorityClient.CurrentProjection,
                      avatarId,
                      OntologyRuleBlocks.JumpPlayerFromIntent,
                      jumpRuleVersion))) &&
                (currentVersion >= 7 ||
                 (HasActiveFactObject(
                      authorityClient.CurrentProjection,
                      avatarId,
                      OntologyPredicates.PhysicalProfile,
                      OntologyObjects.LocalCharacterController) &&
                  HasActiveRuleBindingVersion(
                      authorityClient.CurrentProjection,
                      avatarId,
                      OntologyRuleBlocks.MovePlayerFromIntent,
                      locomotionRuleVersion) &&
                   HasActiveRuleBindingVersion(
                       authorityClient.CurrentProjection,
                       avatarId,
                       OntologyRuleBlocks.JumpPlayerFromIntent,
                       jumpRuleVersion))) &&
                (currentVersion >= 8 ||
                 (HasActiveFactObject(
                      authorityClient.CurrentProjection,
                      avatarId,
                      OntologyPredicates.CollisionRole,
                      OntologyObjects.ActorBody) &&
                  HasActiveFactObject(
                      authorityClient.CurrentProjection,
                      avatarId,
                      OntologyPredicates.CollisionProxyShape,
                      OntologyObjects.Capsule) &&
                  HasActiveFact(
                      authorityClient.CurrentProjection,
                      avatarId,
                      OntologyPredicates.CollisionRadius) &&
                  HasActiveFact(
                      authorityClient.CurrentProjection,
                      avatarId,
                      OntologyPredicates.CollisionHeight))) &&
                (currentVersion >= 9 ||
                 (HasActiveFact(
                      authorityClient.CurrentProjection,
                      avatarId,
                      OntologyPredicates.MaximumStepHeight) &&
                  HasActiveFact(
                      authorityClient.CurrentProjection,
                      avatarId,
                      OntologyPredicates.GroundClearance))) &&
                (currentVersion >= 10 ||
                 HasActiveFactObject(
                     authorityClient.CurrentProjection,
                     avatarId,
                     OntologyPredicates.HitAnimationIntent,
                     OntologyAnimationIntentIds.HitReaction)) &&
                (currentVersion >= 11 ||
                 HasActiveFactObject(
                     authorityClient.CurrentProjection,
                     avatarId,
                     OntologyPredicates.DeathAnimationIntent,
                     OntologyAnimationIntentIds.Death)));
        }

        private IEnumerator EnsureRuleBindingVersionRoutine(
            Guid avatarId,
            string ruleId,
            int ruleVersion,
            Action<bool> completed)
        {
            var binding = FindActiveRuleBinding(
                authorityClient?.CurrentProjection,
                avatarId,
                ruleId);
            if (binding != null &&
                binding.ruleVersion == ruleVersion)
            {
                completed?.Invoke(true);
                yield break;
            }

            if (binding != null)
            {
                if (!Guid.TryParse(binding.bindingId, out var bindingId))
                {
                    completed?.Invoke(false);
                    yield break;
                }

                OntologyAuthorityCommandResult removeResult = null;
                yield return SendCommandWithRevisionRetryRoutine(
                    () => OntologyWorldAuthorityClient.CreateCommand(
                        OntologyWorldCommandKinds.RemoveRuleBlock,
                        OntologyWorldAuthorityClient
                            .CreateRemoveRuleBlockPayload(bindingId)),
                    value => removeResult = value);
                if (removeResult == null || !removeResult.accepted)
                {
                    completed?.Invoke(false);
                    yield break;
                }
            }

            OntologyAuthorityCommandResult addResult = null;
            yield return SendCommandWithRevisionRetryRoutine(
                () => OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.AddRuleBlock,
                    OntologyWorldAuthorityClient.CreateRuleBlockPayload(
                        Guid.NewGuid(),
                        avatarId,
                        ruleId,
                        "?actor",
                        ruleVersion)),
                value => addResult = value);
            completed?.Invoke(
                addResult != null && addResult.accepted);
        }

        private IEnumerator WaitForAvatarMotionRoutine(
            Guid avatarId,
            string zoneKey,
            Action<bool> completed)
        {
            var timeout = Mathf.Max(
                0.5f,
                authorityClient.Settings == null
                    ? 6f
                    : authorityClient.Settings
                        .runtimeMotionReadinessTimeoutSeconds);
            var deadline = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup <= deadline)
            {
                OntologyAuthorityPlayerMotionState motion = null;
                yield return authorityClient.LoadPlayerMotionRoutine(
                    avatarId,
                    value => motion = value);
                if (motion != null &&
                    string.Equals(
                        motion.zoneKey,
                        zoneKey,
                        StringComparison.Ordinal))
                {
                    completed?.Invoke(true);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(0.2f);
            }

            completed?.Invoke(false);
        }

        /// <summary>
        /// Projection reads are safe to retry because they are read-only. This
        /// deliberately does not retry package conflicts, ontology migrations,
        /// durable commands, or entry commits; those require an explicit new
        /// decision rather than an automatic duplicate mutation.
        /// </summary>
        private IEnumerator LoadWorldForEntryWithRetryRoutine(
            Action<bool> completed)
        {
            const int maximumAttempts = 3;
            for (var attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                var loaded = false;
                yield return authorityClient.LoadWorldRoutine(
                    value => loaded = value);
                if (loaded)
                {
                    completed?.Invoke(true);
                    yield break;
                }

                if (attempt < maximumAttempts)
                {
                    yield return new WaitForSecondsRealtime(0.2f * attempt);
                }
            }

            completed?.Invoke(false);
        }

        private IEnumerator SendCommandWithRevisionRetryRoutine(
            Func<OntologyWorldCommand> createCommand,
            Action<OntologyAuthorityCommandResult> completed)
        {
            OntologyAuthorityCommandResult result = null;
            yield return authorityClient.SendCommandRoutine(
                createCommand(),
                value => result = value);
            if (result != null &&
                !result.transportFailure &&
                string.Equals(
                    result.rejectionCode,
                    "stale_revision",
                    StringComparison.Ordinal))
            {
                var reloaded = false;
                yield return authorityClient.LoadWorldRoutine(
                    value => reloaded = value);
                if (reloaded)
                {
                    yield return authorityClient.SendCommandRoutine(
                        createCommand(),
                        value => result = value);
                }
            }

            completed?.Invoke(result);
        }

        public static string SelectRuntimeZone(
            IReadOnlyList<OntologyAuthorityWorldZoneProjection> zones,
            Vector3 actorPosition)
        {
            if (zones == null) return string.Empty;

            OntologyAuthorityWorldZoneProjection nearest = null;
            var nearestDistance = float.PositiveInfinity;
            foreach (var zone in zones)
            {
                if (zone == null ||
                    string.IsNullOrWhiteSpace(zone.zoneKey) ||
                    string.Equals(
                        zone.simulationMode,
                        "dormant",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (actorPosition.x >= zone.minX &&
                    actorPosition.x <= zone.maxX &&
                    actorPosition.z >= zone.minZ &&
                    actorPosition.z <= zone.maxZ)
                {
                    return zone.zoneKey.Trim();
                }

                var nearestX = Mathf.Clamp(
                    actorPosition.x,
                    zone.minX,
                    zone.maxX);
                var nearestZ = Mathf.Clamp(
                    actorPosition.z,
                    zone.minZ,
                    zone.maxZ);
                var distance = new Vector2(
                    actorPosition.x - nearestX,
                    actorPosition.z - nearestZ).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearest = zone;
                    nearestDistance = distance;
                }
            }

            return nearest?.zoneKey?.Trim() ?? string.Empty;
        }

        public static int ResolveSemanticContractVersion(
            OntologyAuthorityWorldProjection projection,
            Guid entityId)
        {
            return OntologyAuthorityProjectionSemantics
                .ResolveSemanticContractVersion(projection, entityId);
        }

        public static bool HasActiveRuleBinding(
            OntologyAuthorityWorldProjection projection,
            Guid entityId,
            string ruleId)
        {
            if (projection?.ruleBindings == null ||
                entityId == Guid.Empty ||
                string.IsNullOrWhiteSpace(ruleId))
            {
                return false;
            }

            var canonicalId = entityId.ToString("D");
            foreach (var binding in projection.ruleBindings)
            {
                if (binding != null &&
                    binding.enabled &&
                    string.Equals(
                        binding.targetEntityId,
                        canonicalId,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        binding.ruleId,
                        ruleId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static OntologyAuthorityRuleBindingProjection
            FindActiveRuleBinding(
                OntologyAuthorityWorldProjection projection,
                Guid entityId,
                string ruleId)
        {
            if (projection?.ruleBindings == null ||
                entityId == Guid.Empty ||
                string.IsNullOrWhiteSpace(ruleId))
            {
                return null;
            }

            var canonicalId = entityId.ToString("D");
            return projection.ruleBindings.FirstOrDefault(binding =>
                binding != null &&
                binding.enabled &&
                string.Equals(
                    binding.targetEntityId,
                    canonicalId,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    binding.ruleId,
                    ruleId,
                    StringComparison.Ordinal));
        }

        private static bool HasActiveRuleBindingVersion(
            OntologyAuthorityWorldProjection projection,
            Guid entityId,
            string ruleId,
            int ruleVersion)
        {
            var binding = FindActiveRuleBinding(
                projection,
                entityId,
                ruleId);
            return binding != null &&
                   ruleVersion > 0 &&
                   binding.ruleVersion == ruleVersion;
        }

        private static bool TryResolveDevelopmentRuleVersion(
            OntologyWorldAuthoritySettings settings,
            string ruleId,
            out int ruleVersion)
        {
            ruleVersion = 0;
            if (settings == null ||
                string.IsNullOrWhiteSpace(ruleId))
            {
                return false;
            }

            if (settings.developmentRules != null)
            {
                foreach (var publishedRule in settings.developmentRules)
                {
                    if (publishedRule == null ||
                        publishedRule.definitionVersion <= 0 ||
                        !string.Equals(
                            publishedRule.ruleId,
                            ruleId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ruleVersion = publishedRule.definitionVersion;
                    return true;
                }
            }

            var definitions =
                settings.developmentRuleDatabase?.Definitions;
            if (definitions == null)
            {
                return false;
            }

            foreach (var definition in definitions)
            {
                if (definition == null ||
                    definition.catalogVersion <= 0 ||
                    !string.Equals(
                        definition.id,
                        ruleId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                ruleVersion = definition.catalogVersion;
                return true;
            }

            return false;
        }

        private static bool TryResolveSingleFactValue(
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

            var canonicalId = entityId.ToString("D");
            var matches = projection.facts
                .Where(fact =>
                    fact != null &&
                    string.Equals(
                        fact.subjectEntityId,
                        canonicalId,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        fact.predicateId,
                        predicateId,
                        StringComparison.Ordinal))
                .Select(fact =>
                    string.Equals(
                        fact.objectKind,
                        "canonical",
                        StringComparison.Ordinal)
                        ? fact.objectCanonicalId
                        : fact.objectValueJson)
                .Where(candidate =>
                    !string.IsNullOrWhiteSpace(candidate))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (matches.Length != 1)
            {
                return false;
            }

            value = matches[0].Trim();
            return true;
        }

        public static string ResolveRuntimeZone(
            string requestedZoneKey,
            OntologyAuthorityAvatarCheckpoint checkpoint,
            IReadOnlyList<OntologyAuthorityWorldZoneProjection> zones)
        {
            if (zones == null) return string.Empty;

            var checkpointZone = checkpoint?.zoneKey?.Trim();
            if (IsUsableRuntimeZone(zones, checkpointZone))
                return checkpointZone;

            if (checkpoint?.transform != null)
            {
                var selectedFromCheckpoint = SelectRuntimeZone(
                    zones,
                    new Vector3(
                        checkpoint.transform.positionX,
                        checkpoint.transform.positionY,
                        checkpoint.transform.positionZ));
                if (!string.IsNullOrWhiteSpace(selectedFromCheckpoint))
                    return selectedFromCheckpoint;
            }

            var requested = requestedZoneKey?.Trim();
            return IsUsableRuntimeZone(zones, requested)
                ? requested
                : string.Empty;
        }

        private static bool IsUsableRuntimeZone(
            IReadOnlyList<OntologyAuthorityWorldZoneProjection> zones,
            string zoneKey)
        {
            if (zones == null || string.IsNullOrWhiteSpace(zoneKey))
                return false;

            foreach (var zone in zones)
            {
                if (zone != null &&
                    string.Equals(
                        zone.zoneKey?.Trim(),
                        zoneKey.Trim(),
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        zone.simulationMode,
                        "dormant",
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasActiveFact(
            OntologyAuthorityWorldProjection projection,
            Guid subjectEntityId,
            string predicateId)
        {
            if (projection?.facts == null ||
                subjectEntityId == Guid.Empty ||
                string.IsNullOrWhiteSpace(predicateId))
            {
                return false;
            }

            var subject = subjectEntityId.ToString("D");
            foreach (var fact in projection.facts)
            {
                if (fact != null &&
                    string.Equals(
                        fact.subjectEntityId,
                        subject,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        fact.predicateId,
                        predicateId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasActiveFactObject(
            OntologyAuthorityWorldProjection projection,
            Guid subjectEntityId,
            string predicateId,
            string canonicalObjectId)
        {
            if (projection?.facts == null ||
                subjectEntityId == Guid.Empty ||
                string.IsNullOrWhiteSpace(predicateId) ||
                string.IsNullOrWhiteSpace(canonicalObjectId))
            {
                return false;
            }

            var subject = subjectEntityId.ToString("D");
            foreach (var fact in projection.facts)
            {
                if (fact != null &&
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
                        "canonical",
                        StringComparison.Ordinal) &&
                    string.Equals(
                        fact.objectCanonicalId,
                        canonicalObjectId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ContainsLegacyEquipmentRelation(
            OntologyAuthorityWorldProjection projection)
        {
            if (projection?.facts == null) return false;
            foreach (var fact in projection.facts)
            {
                if (fact != null &&
                    string.Equals(
                        fact.predicateId,
                        "equips",
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            foreach (var attackRelation in projection.facts)
            {
                if (attackRelation == null ||
                    !string.Equals(
                        attackRelation.predicateId,
                        OntologyPredicates.AttacksWith,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        attackRelation.objectKind,
                        "entity",
                        StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(
                        attackRelation.subjectEntityId) ||
                    string.IsNullOrWhiteSpace(
                        attackRelation.objectEntityId))
                {
                    continue;
                }

                var hasMatchingEquipmentRelation =
                    Array.Exists(
                        projection.facts,
                        equipment =>
                            equipment != null &&
                            string.Equals(
                                equipment.subjectEntityId,
                                attackRelation.objectEntityId,
                                StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(
                                equipment.predicateId,
                                OntologyPredicates.EquippedBy,
                                StringComparison.Ordinal) &&
                            string.Equals(
                                equipment.objectKind,
                                "entity",
                                StringComparison.Ordinal) &&
                            string.Equals(
                                equipment.objectEntityId,
                                attackRelation.subjectEntityId,
                                StringComparison.OrdinalIgnoreCase));
                if (!hasMatchingEquipmentRelation)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ContainsUniquelyZonableEntity(
            OntologyAuthorityWorldProjection projection,
            OntologyAuthorityWorldZoneProjection[] zones)
        {
            if (projection?.entities == null ||
                zones == null ||
                zones.Length == 0)
            {
                return false;
            }

            foreach (var entity in projection.entities)
            {
                if (entity?.transform == null ||
                    !string.IsNullOrWhiteSpace(entity.zoneKey))
                {
                    continue;
                }

                var matchingZoneCount = 0;
                foreach (var zone in zones)
                {
                    if (zone == null ||
                        string.IsNullOrWhiteSpace(zone.zoneKey) ||
                        entity.transform.positionX < zone.minX ||
                        entity.transform.positionX > zone.maxX ||
                        entity.transform.positionZ < zone.minZ ||
                        entity.transform.positionZ > zone.maxZ)
                    {
                        continue;
                    }

                    matchingZoneCount++;
                    if (matchingZoneCount > 1) break;
                }

                if (matchingZoneCount == 1) return true;
            }

            return false;
        }

        private static bool TryGetDefaultRuntimeZone(
            OntologyWorldAuthoritySettings settings,
            out string zoneKey,
            out Vector2 min,
            out Vector2 max,
            out string simulationMode)
        {
            zoneKey = settings == null
                ? string.Empty
                : settings.defaultRuntimeZoneKey?.Trim() ?? string.Empty;
            min = settings == null
                ? default
                : settings.defaultRuntimeZoneMin;
            max = settings == null
                ? default
                : settings.defaultRuntimeZoneMax;
            simulationMode = settings == null
                ? string.Empty
                : settings.defaultRuntimeZoneSimulationMode?.Trim() ??
                  string.Empty;
            return !string.IsNullOrWhiteSpace(zoneKey) &&
                   min.x < max.x &&
                   min.y < max.y &&
                   simulationMode is "active" or "reduced";
        }

        private static bool TryGetDefaultRuntimeWalkableSupport(
            OntologyWorldAuthoritySettings settings,
            out string templateId,
            out string displayName,
            out Vector3 center,
            out Vector3 size)
        {
            templateId =
                settings?.defaultRuntimeWalkableSupportTemplateId?.Trim() ??
                string.Empty;
            displayName =
                settings?.defaultRuntimeWalkableSupportDisplayName?.Trim() ??
                string.Empty;
            center = settings == null
                ? default
                : settings.defaultRuntimeWalkableSupportCenter;
            size = settings == null
                ? default
                : settings.defaultRuntimeWalkableSupportSize;
            return !string.IsNullOrWhiteSpace(templateId) &&
                   !string.IsNullOrWhiteSpace(displayName) &&
                   IsFinite(center) &&
                   IsFinite(size) &&
                   size.x > 0f &&
                   size.y > 0f &&
                   size.z > 0f;
        }

        private static bool ContainsEntity(
            OntologyAuthorityWorldProjection projection,
            Guid entityId)
        {
            if (projection?.entities == null ||
                entityId == Guid.Empty)
            {
                return false;
            }

            var canonicalId = entityId.ToString("D");
            return Array.Exists(
                projection.entities,
                value =>
                    value != null &&
                    string.Equals(
                        value.entityId,
                        canonicalId,
                        StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolveEntityZone(
            OntologyAuthorityWorldProjection projection,
            Guid entityId)
        {
            if (projection?.entities == null || entityId == Guid.Empty)
                return string.Empty;
            var canonicalId = entityId.ToString("D");
            var entity = Array.Find(
                projection.entities,
                value => value != null && string.Equals(
                    value.entityId,
                    canonicalId,
                    StringComparison.OrdinalIgnoreCase));
            return entity?.zoneKey?.Trim() ?? string.Empty;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) &&
            float.IsFinite(value.y) &&
            float.IsFinite(value.z);

        public static Guid CreateWorldScopedSupportId(
            Guid worldId,
            string zoneKey)
        {
            if (worldId == Guid.Empty ||
                string.IsNullOrWhiteSpace(zoneKey))
            {
                return Guid.Empty;
            }

            var worldBytes = worldId.ToByteArray();
            var zoneBytes = Encoding.UTF8.GetBytes(
                "walkable-support:" + zoneKey.Trim());
            var input = new byte[worldBytes.Length + zoneBytes.Length];
            Buffer.BlockCopy(
                worldBytes,
                0,
                input,
                0,
                worldBytes.Length);
            Buffer.BlockCopy(
                zoneBytes,
                0,
                input,
                worldBytes.Length,
                zoneBytes.Length);
            byte[] hash;
            using (var sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(input);
            }
            var bytes = new byte[16];
            Buffer.BlockCopy(hash, 0, bytes, 0, bytes.Length);
            return new Guid(bytes);
        }

        public static Guid CreateWorldScopedAvatarId(
            Guid baseAvatarId,
            Guid worldId,
            Guid userId)
        {
            if (baseAvatarId == Guid.Empty ||
                worldId == Guid.Empty ||
                userId == Guid.Empty)
                return Guid.Empty;

            var input = new byte[48];
            Buffer.BlockCopy(baseAvatarId.ToByteArray(), 0, input, 0, 16);
            Buffer.BlockCopy(worldId.ToByteArray(), 0, input, 16, 16);
            Buffer.BlockCopy(userId.ToByteArray(), 0, input, 32, 16);
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
            if (created == null)
                SetAuthorityFailureStatus(authorityClient.LastStatus);
            else
                SetLocalizedStatus(
                    "ui.account.status.character_created",
                    "Created and selected character '{0}'. Choose a world next.",
                    created.displayName);
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
            if (saved)
                SetLocalizedStatus("ui.account.status.appearance_saved", "Account appearance profile saved.");
            else
                SetAuthorityFailureStatus(authorityClient.LastStatus);
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
            if (respawnController == null)
            {
                respawnController = FindAnyObjectByType<
                    OntologyAuthorityRespawnController>(
                    FindObjectsInactive.Include);
                if (respawnController == null)
                    respawnController =
                        gameObject.AddComponent<
                            OntologyAuthorityRespawnController>();
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
            var localInput = FindAnyObjectByType<OntologyInputSystemPlayerInput>(
                FindObjectsInactive.Include);
            var resolvedAvatarIdentity = localInput == null
                ? null
                : localInput.GetComponent<OntologyAuthorityEntityIdentity>();
            if (resolvedAvatarIdentity != null &&
                resolvedAvatarIdentity != avatarIdentity)
            {
                avatarIdentity = resolvedAvatarIdentity;
            }
            if (avatarIdentity != null)
            {
                if (entryPresentationCoordinator == null ||
                    entryPresentationCoordinator.gameObject !=
                    avatarIdentity.gameObject)
                {
                    entryPresentationCoordinator =
                        avatarIdentity.GetComponent<
                            OntologyWorldEntryPresentationCoordinator>();
                }
                entryPresentationCoordinator?.Configure(
                    sessionCoordinator,
                    avatarIdentity);
            }
            if (profileRelationProjector == null)
                profileRelationProjector = FindAnyObjectByType<OntologyAccountProfileRelationProjector>(
                    FindObjectsInactive.Include);
            if (characterPartAdapter == null)
                characterPartAdapter = OntologyCharacterPartAdapter.FindAvailable();
        }

        private void SetLocalizedStatus(
            string key,
            string fallback,
            params string[] arguments)
        {
            lastStatusKey = key ?? string.Empty;
            lastStatusFallback = fallback ?? string.Empty;
            lastStatusArguments = arguments ?? Array.Empty<string>();
            lastStatus = OntologyLanguagePackService.Format(
                lastStatusKey,
                lastStatusFallback,
                lastStatusArguments);
            StateChanged?.Invoke();
        }

        private void SetAuthorityFailureStatus(string technicalStatus)
        {
            lastTechnicalStatus = technicalStatus ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(lastTechnicalStatus))
                Debug.LogWarning(
                    "[AccountEntry] Authority operation failed: " +
                    lastTechnicalStatus,
                    this);
            SetLocalizedStatus(
                "ui.account.status.operation_failed",
                "The requested world operation failed. Please try again.");
        }

        public static string ResolvePackageFailureLocalizationKey(
            string authorityStatus)
        {
            return !string.IsNullOrWhiteSpace(authorityStatus) &&
                   (authorityStatus.IndexOf(
                        "action_definition_version_conflict",
                        StringComparison.Ordinal) >= 0 ||
                    authorityStatus.IndexOf(
                        "rule_definition_version_conflict",
                        StringComparison.Ordinal) >= 0)
                ? "ui.account.world_entry.package_version_conflict"
                : "ui.account.world_entry.package_verification_failed";
        }
    }
}
