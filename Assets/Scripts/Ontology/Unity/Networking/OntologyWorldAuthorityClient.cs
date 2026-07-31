using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// HTTP boundary for the durable authoring authority. It sends user intentions
    /// and receives versioned projections; it never reads or writes the database.
    /// Runtime observations and inferred facts remain in the simulation authority.
    /// </summary>
    public sealed class OntologyWorldAuthorityClient : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthoritySettings settings;
        [SerializeField, TextArea] private string lastStatus;
        [SerializeField] private string currentUserId;
        [SerializeField] private string accessToken;
        [SerializeField] private string currentCharacterId;
        [SerializeField] private string currentWorldId;
        [SerializeField] private long currentRevision;
        [SerializeField] private bool hasEnteredCurrentWorld;
        private bool isLoadingWorld;
        private bool realtimeNotificationsActive;
        private float nextProjectionPollAt;
        private bool hasRuntimeProjectionZoneScope;
        private string runtimeProjectionZoneKey;
        private bool isLoadingAutonomousActors;
        private float nextAutonomousActorPollAt;
        private bool runtimeSessionLeaseRequestInFlight;
        private string runtimeSessionLeaseId;
        private string runtimeSessionLeaseWorldId;
        private string runtimeSessionLeaseZoneKey;
        private float nextRuntimeSessionHeartbeatAt;
        private OntologyAuthorityWorldProjection currentProjection;
        private OntologyAuthorityAccountDashboard currentAccount;
        private OntologyAuthorityPendingCommandQueue pendingCommandQueue;

        public OntologyWorldAuthoritySettings Settings => settings;
        public string CurrentUserId => currentUserId;
        public string AccessToken => accessToken;
        public bool HasStoredSession =>
            OntologyAuthoritySessionStore.HasSession(SessionBaseUrl());
        public string CurrentCharacterId => currentCharacterId;
        public OntologyAuthorityPlayerCharacter CurrentCharacter
        {
            get
            {
                if (currentAccount == null || currentAccount.characters == null)
                {
                    return null;
                }

                foreach (var character in currentAccount.characters)
                {
                    if (character != null && string.Equals(character.characterId, currentCharacterId, StringComparison.Ordinal))
                    {
                        return character;
                    }
                }

                return null;
            }
        }
        public string CurrentWorldId => currentWorldId;
        /// <summary>Role returned by the authority for the selected world.</summary>
        public string CurrentWorldRole
        {
            get
            {
                if (currentAccount == null || currentAccount.worlds == null)
                    return string.Empty;

                foreach (var world in currentAccount.worlds)
                {
                    if (world != null && string.Equals(world.worldId, currentWorldId, StringComparison.Ordinal))
                        return world.role?.Trim() ?? string.Empty;
                }

                return string.Empty;
            }
        }
        /// <summary>
        /// Durable world authoring is allowed only for owner/editor roles. The
        /// server remains the final authority; this property is a client-side
        /// UX gate that prevents an optimistic edit in a viewer session.
        /// </summary>
        public bool CanAuthorSelectedWorld =>
            IsReady &&
            (string.Equals(CurrentWorldRole, "owner", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(CurrentWorldRole, "editor", StringComparison.OrdinalIgnoreCase));
        public bool CanEditCurrentWorld => IsWorldRuntimeReady && CanAuthorSelectedWorld;
        public long CurrentRevision => currentRevision;

        private bool durableCommandInFlight;
        public string LastStatus => lastStatus;
        public string CurrentProjectionZoneKey => hasRuntimeProjectionZoneScope
            ? runtimeProjectionZoneKey ?? string.Empty
            : settings == null ? string.Empty : settings.realtimeZoneKey?.Trim() ?? string.Empty;
        public OntologyAuthorityWorldProjection CurrentProjection => currentProjection;
        public OntologyAuthorityAccountDashboard CurrentAccount => currentAccount;
        public bool IsAuthenticated => Guid.TryParse(currentUserId, out _);
        public bool IsReady => IsAuthenticated &&
                               Guid.TryParse(currentWorldId, out _);
        public bool HasEnteredCurrentWorld => hasEnteredCurrentWorld;
        public bool IsWorldRuntimeReady => IsReady && hasEnteredCurrentWorld;
        public bool RealtimeNotificationsActive => realtimeNotificationsActive;

        public void ResetWorldEntryConfirmation()
        {
            BeginReleaseRuntimeSessionLease();
            hasEnteredCurrentWorld = false;
            realtimeNotificationsActive = false;
            StateChanged?.Invoke();
        }

        public IEnumerator ReplayPendingCommandsRoutine(Action<bool> completed = null)
        {
            if (pendingCommandQueue == null)
                pendingCommandQueue = GetComponent<OntologyAuthorityPendingCommandQueue>();
            if (pendingCommandQueue == null)
            {
                completed?.Invoke(true);
                yield break;
            }
            if (pendingCommandQueue.Count == 0)
            {
                completed?.Invoke(true);
                yield break;
            }
            yield return pendingCommandQueue.ReplayRoutine(completed);
        }

        public event Action StateChanged;
        public event Action<string, string> ProjectionZoneScopeChanged;
        public event Action<OntologyAuthorityWorldProjection> ProjectionReceived;
        public event Action<OntologyWorldCommand> CommandSending;
        public event Action<OntologyWorldCommand, OntologyAuthorityCommandResult> CommandCompleted;
        public event Action<OntologyAuthorityRuntimeActionResult>
            RuntimeActionCompleted;
        public event Action<OntologyAuthorityAutonomousActorMotionState[]>
            AutonomousActorMotionsReceived;

        private void Awake()
        {
            pendingCommandQueue = GetComponent<OntologyAuthorityPendingCommandQueue>();
            if (pendingCommandQueue == null)
                pendingCommandQueue = gameObject.AddComponent<OntologyAuthorityPendingCommandQueue>();
            pendingCommandQueue.Bind(this);
        }

        public bool TryResolveEnabledAction(
            string actionId,
            out OntologyAuthorityActionDefinitionProjection resolved)
        {
            resolved = null;
            if (string.IsNullOrWhiteSpace(actionId) || currentProjection?.actions == null)
            {
                return false;
            }

            foreach (var candidate in currentProjection.actions)
            {
                if (candidate == null ||
                    !string.Equals(candidate.actionId, actionId, StringComparison.Ordinal))
                {
                    continue;
                }

                // Ambiguous action IDs must be selected by a higher-level content
                // policy instead of silently choosing a package.
                if (resolved != null)
                {
                    resolved = null;
                    return false;
                }
                resolved = candidate;
            }

            return resolved != null;
        }

        public bool ContainsProjectedEntity(Guid entityId)
        {
            if (entityId == Guid.Empty || currentProjection?.entities == null)
            {
                return false;
            }

            var canonical = entityId.ToString("D");
            foreach (var entity in currentProjection.entities)
            {
                if (entity != null &&
                    string.Equals(
                        entity.entityId,
                        canonical,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public void Configure(OntologyWorldAuthoritySettings value)
        {
            settings = value;
        }

        private void Start()
        {
            RestoreStoredSession();
            if (settings != null && settings.connectOnStart && HasStoredSession) BeginConnect();
        }

        public IEnumerator RegisterRoutine(string email, string displayName, string password, Action<bool> completed = null)
        {
            var payload = JsonUtility.ToJson(new PasswordRegisterRequest { email=email?.Trim(), displayName=displayName?.Trim(), password=password });
            yield return AuthenticateRoutine("/v1/auth/register", payload, completed);
        }

        public IEnumerator LoginRoutine(string email, string password, Action<bool> completed = null)
        {
            var payload = JsonUtility.ToJson(new PasswordLoginRequest { email=email?.Trim(), password=password });
            yield return AuthenticateRoutine("/v1/auth/login", payload, completed);
        }

        public void Logout()
        {
            StartCoroutine(LogoutRoutine());
        }

        private IEnumerator LogoutRoutine()
        {
            BeginReleaseRuntimeSessionLease();
            if (!string.IsNullOrWhiteSpace(accessToken))
                yield return SendJson("POST", "/v1/auth/logout", null, currentUserId, (_, _, _) => { });
            ClearSession();
            SetStatus("Signed out.");
        }

        private IEnumerator AuthenticateRoutine(string path, string payload, Action<bool> completed)
        {
            var authenticated = false;
            yield return SendJson("POST", path, payload, null, (success, body, error) =>
            {
                if (!success) { SetStatus("Authentication failed: " + error); return; }
                var response = JsonUtility.FromJson<PasswordAuthResponse>(body);
                if (response == null || !Guid.TryParse(response.userId, out _) || string.IsNullOrWhiteSpace(response.accessToken)) { SetStatus("Authority returned an invalid authentication session."); return; }
                currentUserId=response.userId; accessToken=response.accessToken; PersistSession(); authenticated=true;
            });
            if (authenticated) { yield return LoadAccountDashboardRoutine(); SelectRememberedCharacter(); SelectRememberedWorld(); }
            completed?.Invoke(authenticated);
        }

        private void Update()
        {
            if (settings == null || !IsWorldRuntimeReady)
            {
                return;
            }

            if (!isLoadingWorld &&
                Time.unscaledTime >= nextProjectionPollAt)
            {
                var interval = realtimeNotificationsActive
                    ? Mathf.Max(
                        settings.projectionPollIntervalSeconds,
                        settings.realtimeRecoveryPollIntervalSeconds)
                    : Mathf.Max(
                        0.25f,
                        settings.projectionPollIntervalSeconds);
                nextProjectionPollAt = Time.unscaledTime + interval;
                StartCoroutine(LoadWorldRoutine());
            }

            var zoneKey = CurrentProjectionZoneKey;
            if (!runtimeSessionLeaseRequestInFlight &&
                !string.IsNullOrWhiteSpace(zoneKey) &&
                Time.unscaledTime >= nextRuntimeSessionHeartbeatAt)
            {
                StartCoroutine(
                    RenewRuntimeSessionLeaseRoutine(zoneKey));
            }
            if (!isLoadingAutonomousActors &&
                !string.IsNullOrWhiteSpace(zoneKey) &&
                Time.unscaledTime >= nextAutonomousActorPollAt)
            {
                nextAutonomousActorPollAt =
                    Time.unscaledTime +
                    (realtimeNotificationsActive ? 0.5f : 0.25f);
                StartCoroutine(
                    LoadAndPublishAutonomousActorMotionsRoutine(zoneKey));
            }
        }

        /// <summary>
        /// Called by the transport component only. A socket is an optimization
        /// signal, never a source of world facts, so HTTP remains the recovery path.
        /// </summary>
        public void SetRealtimeNotificationState(bool active)
        {
            if (realtimeNotificationsActive == active)
            {
                return;
            }

            realtimeNotificationsActive = active;
            nextProjectionPollAt = Time.unscaledTime;
            StateChanged?.Invoke();
        }

        [ContextMenu("Connect to Local Authority")]
        public void BeginConnect()
        {
            if (settings == null)
            {
                SetStatus("Authority settings are not assigned.");
                return;
            }

            StartCoroutine(ConnectRoutine());
        }

        [ContextMenu("Load Current Authority World")]
        public void BeginLoadWorld()
        {
            if (!IsReady)
            {
                BeginConnect();
                return;
            }

            StartCoroutine(LoadWorldRoutine());
        }

        /// <summary>
        /// Selects a transient projection scope without mutating the shared settings
        /// asset. An empty key deliberately means the whole-world fallback while an
        /// actor is outside all authored zones.
        /// </summary>
        public void SetProjectionZoneKey(
            string zoneKey,
            bool reloadProjection = true)
        {
            var normalized = zoneKey?.Trim() ?? string.Empty;
            if (!IsValidZoneKey(normalized))
            {
                SetStatus("Authority rejected an invalid local zone scope.");
                return;
            }

            var previous = CurrentProjectionZoneKey;
            hasRuntimeProjectionZoneScope = true;
            runtimeProjectionZoneKey = normalized;
            if (string.Equals(previous, CurrentProjectionZoneKey, StringComparison.Ordinal))
            {
                return;
            }

            nextProjectionPollAt = Time.unscaledTime;
            if (IsWorldRuntimeReady)
            {
                BeginReleaseRuntimeSessionLease();
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    StartCoroutine(
                        RenewRuntimeSessionLeaseRoutine(normalized));
                }
            }
            ProjectionZoneScopeChanged?.Invoke(previous, CurrentProjectionZoneKey);
            StateChanged?.Invoke();
            if (reloadProjection && IsReady)
            {
                StartCoroutine(LoadWorldRoutine());
            }
        }

        public IEnumerator LoadWorldZonesRoutine(
            Action<OntologyAuthorityWorldZoneProjection[]> completed = null)
        {
            if (!IsReady)
            {
                completed?.Invoke(null);
                yield break;
            }

            OntologyAuthorityWorldZoneProjection[] zones = null;
            yield return SendJson(
                "GET",
                "/v1/worlds/" + currentWorldId + "/zones",
                null,
                currentUserId,
                (success, body, error) =>
                {
                    if (!success)
                    {
                        SetStatus("Authority zone directory load failed: " + error);
                        return;
                    }

                    zones = JsonUtility.FromJson<OntologyAuthorityWorldZoneList>(
                        "{\"items\":" + body + "}")?.items;
                });
            completed?.Invoke(zones ?? Array.Empty<OntologyAuthorityWorldZoneProjection>());
        }

        public IEnumerator ConnectRoutine(Action<bool> completed = null)
        {
            if (settings == null || string.IsNullOrWhiteSpace(settings.baseUrl))
            {
                SetStatus("Authority settings are missing a base URL.");
                completed?.Invoke(false);
                yield break;
            }

            RestoreStoredSession();
            if (!Guid.TryParse(currentUserId, out _) || string.IsNullOrWhiteSpace(accessToken))
            {
                SetStatus("Sign in first.");
                completed?.Invoke(false);
                yield break;
            }

            yield return LoadAccountDashboardRoutine();
            if (currentAccount == null) { ClearSession(); completed?.Invoke(false); yield break; }
            SelectRememberedCharacter();
            SelectRememberedWorld();
            if (Guid.TryParse(currentWorldId, out _)) yield return LoadWorldRoutine();
            completed?.Invoke(IsAuthenticated);
        }

        /// <summary>
        /// Reads account-owned profiles and worlds after authentication. These
        /// profiles are not world Facts; entering a world later binds the selected
        /// profile to the user's registered avatar metadata.
        /// </summary>
        public IEnumerator LoadAccountDashboardRoutine(
            Action<OntologyAuthorityAccountDashboard> completed = null)
        {
            if (!IsAuthenticated)
            {
                completed?.Invoke(null);
                yield break;
            }

            OntologyAuthorityAccountDashboard dashboard = null;
            yield return SendJson(
                "GET",
                "/v1/account",
                null,
                currentUserId,
                (success, body, error) =>
                {
                    if (!success)
                    {
                        SetStatus("Authority account load failed: " + error);
                        return;
                    }

                    dashboard = JsonUtility.FromJson<OntologyAuthorityAccountDashboard>(body);
                    if (dashboard?.account == null ||
                        !Guid.TryParse(dashboard.account.userId, out _))
                    {
                        SetStatus("Authority returned an invalid account profile.");
                        dashboard = null;
                        return;
                    }

                    dashboard.characters ??= Array.Empty<OntologyAuthorityPlayerCharacter>();
                    dashboard.worlds ??= Array.Empty<OntologyAuthorityAccountWorld>();
                    currentAccount = dashboard;
                    SetStatus("Loaded account '" + dashboard.account.displayName + "' with " +
                              dashboard.characters.Length + " character(s) and " +
                              dashboard.worlds.Length + " world(s).");
                });
            completed?.Invoke(dashboard);
        }

        public bool SelectCharacter(string characterId)
        {
            if (currentAccount?.characters == null || !Guid.TryParse(characterId, out _)) return false;
            foreach (var character in currentAccount.characters)
            {
                if (character != null && string.Equals(character.characterId, characterId, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(currentCharacterId, character.characterId, StringComparison.OrdinalIgnoreCase))
                    {
                        BeginReleaseRuntimeSessionLease();
                        hasEnteredCurrentWorld = false;
                    }
                    currentCharacterId = character.characterId;
                    OntologyAuthoritySessionStore.SaveCharacterId(
                        SessionBaseUrl(),
                        currentUserId,
                        currentCharacterId);
                    SetStatus("Selected character '" + character.displayName + "'.");
                    return true;
                }
            }
            return false;
        }

        public bool SelectWorld(string worldId)
        {
            if (currentAccount?.worlds == null || !Guid.TryParse(worldId, out _)) return false;
            foreach (var world in currentAccount.worlds)
            {
                if (world != null && string.Equals(world.worldId, worldId, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(currentWorldId, world.worldId, StringComparison.OrdinalIgnoreCase))
                    {
                        BeginReleaseRuntimeSessionLease();
                        hasEnteredCurrentWorld = false;
                    }
                    currentWorldId = world.worldId;
                    currentRevision = world.revision;
                    OntologyAuthoritySessionStore.SaveWorldId(
                        SessionBaseUrl(),
                        currentUserId,
                        currentWorldId);
                    StateChanged?.Invoke();
                    return true;
                }
            }
            return false;
        }

        public IEnumerator CreateAccountCharacterRoutine(
            string displayName,
            string templateId,
            string[] equippedPartIds,
            Action<OntologyAuthorityPlayerCharacter> completed = null)
        {
            if (!IsAuthenticated)
            {
                completed?.Invoke(null);
                yield break;
            }

            var request = new AuthorityCreatePlayerCharacterRequest
            {
                displayName = displayName?.Trim(),
                templateId = templateId?.Trim(),
                equippedPartIds = equippedPartIds ?? Array.Empty<string>()
            };
            OntologyAuthorityPlayerCharacter character = null;
            yield return SendJson(
                "POST",
                "/v1/account/characters",
                JsonUtility.ToJson(request),
                currentUserId,
                (success, body, error) =>
                {
                    if (!success)
                    {
                        SetStatus("Character creation failed: " + error);
                        return;
                    }
                    character = JsonUtility.FromJson<OntologyAuthorityPlayerCharacter>(body);
                });

            if (character != null)
            {
                yield return LoadAccountDashboardRoutine();
                SelectCharacter(character.characterId);
            }
            completed?.Invoke(character);
        }

        /// <summary>
        /// Saves account-owned profile data through the Authority API. These
        /// triples are never sent as world Facts; world-entry adapters may only
        /// project the selected profile into the local avatar.
        /// </summary>
        public IEnumerator UpdateAccountCharacterProfileRoutine(
            OntologyAuthorityPlayerCharacter character,
            string displayName,
            string templateId,
            string[] equippedPartIds,
            OntologyAuthorityProfileRelation[] profileRelations,
            Action<bool> completed = null)
        {
            if (!IsAuthenticated || character == null || string.IsNullOrWhiteSpace(character.characterId))
            {
                completed?.Invoke(false);
                yield break;
            }
            var request = new AuthorityUpdatePlayerCharacterProfileRequest
            {
                commandId = Guid.NewGuid().ToString("D"),
                expectedRevision = character.profileRevision,
                displayName = displayName?.Trim(),
                templateId = templateId?.Trim(),
                equippedPartIds = equippedPartIds ?? Array.Empty<string>(),
                profileRelations = profileRelations ?? Array.Empty<OntologyAuthorityProfileRelation>()
            };
            var saved = false;
            yield return SendJson(
                "PUT",
                "/v1/account/characters/" + character.characterId,
                JsonUtility.ToJson(request),
                currentUserId,
                (success, _, error) =>
                {
                    saved = success;
                    SetStatus(success ? "Account character profile saved." : "Character profile save failed: " + error);
                });
            if (saved) yield return LoadAccountDashboardRoutine();
            completed?.Invoke(saved);
        }

        /// <summary>
        /// Binds the selected account character to a previously registered avatar
        /// in the selected world. This does not write world Facts or apply parts;
        /// presentation hydration remains a separate character adapter.
        /// </summary>
        public IEnumerator EnterWorldRoutine(
            Guid avatarEntityId,
            Action<bool> completed = null)
        {
            if (!IsReady || !Guid.TryParse(currentCharacterId, out var characterId) || avatarEntityId == Guid.Empty)
            {
                SetStatus("Select an account character and register a world avatar before entering.");
                completed?.Invoke(false);
                yield break;
            }

            var request = new AuthorityEnterWorldRequest
            {
                characterId = characterId.ToString("D"),
                avatarEntityId = avatarEntityId.ToString("D")
            };
            var entered = false;
            yield return SendJson(
                "POST",
                "/v1/worlds/" + currentWorldId + "/entry",
                JsonUtility.ToJson(request),
                currentUserId,
                (success, _, error) =>
                {
                    entered = success;
                    hasEnteredCurrentWorld = success;
                    SetStatus(success
                        ? "Entered authority world with the selected character."
                        : "World entry failed: " + error);
                });
            completed?.Invoke(entered);
        }

        /// <summary>Creates the small development package used by the first
        /// end-to-end player loop. Immutable publication is safe to replay;
        /// package activation remains a normal revisioned world command.</summary>
        public IEnumerator EnsureDevelopmentActionPackageRoutine(Action<bool> completed = null)
        {
            if (!IsReady) { completed?.Invoke(false); yield break; }
            if (settings == null || !settings.publishDevelopmentPackageOnWorldEntry)
            {
                completed?.Invoke(true);
                yield break;
            }

            var packageId = CreateDevelopmentPackageId(
                settings.developmentPackageId,
                currentUserId);
            var packageVersion = settings.developmentPackageVersion?.Trim();
            var configuredRules =
                settings.developmentRules ??
                Array.Empty<OntologyAuthorityDevelopmentRule>();
            var configuredActions =
                settings.developmentActions ?? Array.Empty<OntologyAuthorityDevelopmentAction>();
            if (!OntologyAuthorityAnimationPackageValidator.TryValidateConfiguration(
                    configuredActions,
                    configuredRules,
                    settings.developmentRuleDatabase,
                    settings.animationContentManifest,
                    out var animationValidationError))
            {
                SetStatus(
                    "Development action package validation failed: " +
                    animationValidationError);
                completed?.Invoke(false);
                yield break;
            }
            if (string.IsNullOrWhiteSpace(packageId) ||
                string.IsNullOrWhiteSpace(packageVersion) ||
                configuredActions.Length == 0)
            {
                SetStatus("Development action package metadata is incomplete.");
                completed?.Invoke(false);
                yield break;
            }

            if (configuredRules.Length > 0)
            {
                if (settings.developmentRuleDatabase == null)
                {
                    SetStatus(
                        "Development Rule Block database is not assigned.");
                    completed?.Invoke(false);
                    yield break;
                }

                var rules =
                    new AuthorityRuleDefinitionPublishRequest[
                        configuredRules.Length];
                for (var index = 0; index < configuredRules.Length; index++)
                {
                    var configured = configuredRules[index];
                    OntologyRuleDefinition definition = null;
                    if (configured != null &&
                        !string.IsNullOrWhiteSpace(configured.ruleId))
                    {
                        foreach (var candidate in
                                 settings.developmentRuleDatabase.Definitions)
                        {
                            if (candidate != null &&
                                string.Equals(
                                    candidate.id,
                                    configured.ruleId.Trim(),
                                    StringComparison.Ordinal))
                            {
                                definition = candidate;
                                break;
                            }
                        }
                    }

                    if (definition == null)
                    {
                        SetStatus(
                            "Development Rule Block metadata contains an " +
                            "unknown canonical rule id.");
                        completed?.Invoke(false);
                        yield break;
                    }

                    rules[index] =
                        new AuthorityRuleDefinitionPublishRequest
                        {
                            ruleId = definition.id.Trim(),
                            definitionVersion = Math.Max(
                                1,
                                configured.definitionVersion),
                            payloadJson = JsonUtility.ToJson(definition)
                        };
                }

                var ruleRequest =
                    new AuthorityRuleCatalogPublishRequest
                    {
                        packageVersion = packageVersion,
                        rules = rules
                    };
                var rulesPublished = false;
                yield return SendJson(
                    "POST",
                    "/v1/content/packages/" +
                    UnityWebRequest.EscapeURL(packageId) +
                    "/rules",
                    JsonUtility.ToJson(ruleRequest),
                    currentUserId,
                    (success, _, error) =>
                    {
                        rulesPublished = success;
                        if (!success)
                        {
                            SetStatus(
                                "Development Rule Block package publish " +
                                "failed: " + error);
                        }
                    });
                if (!rulesPublished)
                {
                    completed?.Invoke(false);
                    yield break;
                }
            }

            var actions = new AuthorityActionDefinitionPublishRequest[configuredActions.Length];
            for (var index = 0; index < configuredActions.Length; index++)
            {
                var configured = configuredActions[index];
                if (configured == null ||
                    string.IsNullOrWhiteSpace(configured.actionId) ||
                    string.IsNullOrWhiteSpace(configured.predicateId))
                {
                    SetStatus("Development action metadata contains an invalid entry.");
                    completed?.Invoke(false);
                    yield break;
                }

                actions[index] = new AuthorityActionDefinitionPublishRequest
                {
                    actionId = configured.actionId.Trim(),
                    definitionVersion = Math.Max(1, configured.definitionVersion),
                    payloadJson = string.IsNullOrWhiteSpace(
                            configured.structuredDefinitionJson)
                        ? DevelopmentActionJson(
                            configured.actionId.Trim(),
                            configured.predicateId.Trim(),
                            configured.requiresTool,
                            string.IsNullOrWhiteSpace(configured.objectPattern)
                                ? "?target"
                                : configured.objectPattern.Trim())
                        : configured.structuredDefinitionJson.Trim()
                };
            }

            var request = new AuthorityActionCatalogPublishRequest
            {
                packageVersion = packageVersion,
                actions = actions
            };
            var published = false;
            yield return SendJson(
                "POST",
                "/v1/content/packages/" + UnityWebRequest.EscapeURL(packageId) + "/actions",
                JsonUtility.ToJson(request),
                currentUserId,
                (success, _, error) =>
                {
                    published = success;
                    if (!success) SetStatus("Development action package publish failed: " + error);
                });
            if (!published) { completed?.Invoke(false); yield break; }

            var activated = false;
            var command = CreateCommand(
                "set_content_package",
                CreateSetContentPackagePayload(packageId, packageVersion, true));
            yield return SendCommandRoutine(command, result => activated = result != null && result.accepted);
            if (activated)
            {
                var projectionLoaded = false;
                var projectionValidationError =
                    "Authority projection reload failed.";
                yield return LoadWorldRoutine(value => projectionLoaded = value);
                activated = projectionLoaded &&
                            OntologyAuthorityAnimationPackageValidator
                                .TryValidateProjection(
                                    currentProjection,
                                    packageId,
                                    packageVersion,
                                    configuredActions,
                                    out projectionValidationError);
                if (!activated && !string.IsNullOrWhiteSpace(
                        projectionValidationError))
                {
                    SetStatus(
                        "Development action package projection validation failed: " +
                        projectionValidationError);
                }
            }
            if (!activated) SetStatus("Development action package activation failed: " + LastStatus);
            completed?.Invoke(activated);
        }

        public IEnumerator LoadWorldRoutine(Action<bool> completed = null)
        {
            if (isLoadingWorld)
            {
                while (isLoadingWorld)
                {
                    yield return null;
                }
                yield return LoadWorldRoutine(completed);
                yield break;
            }
            if (!Guid.TryParse(currentUserId, out _) ||
                !Guid.TryParse(currentWorldId, out _))
            {
                SetStatus("An authority user and world are required before loading.");
                completed?.Invoke(false);
                yield break;
            }

            isLoadingWorld = true;
            var loaded = false;
            yield return SendJson(
                "GET",
                BuildProjectionPath(),
                null,
                currentUserId,
                (success, body, error) =>
                {
                    if (!success)
                    {
                        SetStatus("Authority world load failed: " + error);
                        return;
                    }

                    var projection = JsonUtility.FromJson<OntologyAuthorityWorldProjection>(body);
                    if (projection == null || !Guid.TryParse(projection.worldId, out _))
                    {
                        SetStatus("Authority returned an invalid world projection.");
                        return;
                    }

                    currentRevision = projection.revision;
                    currentProjection = projection;
                    var scope = string.IsNullOrWhiteSpace(projection.scopeZoneKey)
                        ? "world"
                        : "zone '" + projection.scopeZoneKey + "'";
                    SetStatus("Connected to authority " + scope + " '" +
                              projection.title + "' at revision " + currentRevision + ".");
                    ProjectionReceived?.Invoke(projection);
                    loaded = true;
                });
            isLoadingWorld = false;
            completed?.Invoke(loaded);
        }

        public IEnumerator SendCommandRoutine(
            OntologyWorldCommand command,
            Action<OntologyAuthorityCommandResult> completed = null)
        {
            if (!IsReady)
            {
                completed?.Invoke(OntologyAuthorityCommandResult.Rejected("authority_not_ready"));
                yield break;
            }

            if (command == null)
            {
                completed?.Invoke(OntologyAuthorityCommandResult.Rejected("missing_command"));
                yield break;
            }

            // Durable commands share one revision stream. Serializing requests
            // from this client prevents movement/editor/equipment coroutines
            // from racing each other with the same expected revision.
            while (durableCommandInFlight)
            {
                yield return null;
            }
            durableCommandInFlight = true;

            command.worldId = currentWorldId;
            command.actorUserId = currentUserId;
            command.expectedRevision = currentRevision;
            if (string.Equals(
                    command.commandType,
                    OntologyWorldCommandKinds.ApplyMeaningPackage,
                    StringComparison.Ordinal))
            {
                // Meaning-package payloads created before nullable JSON was
                // enforced stored optional Guid values as "". Normalize those
                // durable outbox entries before replay so the same idempotent
                // command can still reach Authority as objectEntityId: null.
                command.payloadJson =
                    NormalizeMeaningPackagePayload(command.payloadJson);
            }
            if (string.IsNullOrWhiteSpace(command.commandId))
            {
                command.commandId = Guid.NewGuid().ToString("D");
            }

            if (!command.TryValidateEnvelope(out var rejectionCode))
            {
                durableCommandInFlight = false;
                completed?.Invoke(OntologyAuthorityCommandResult.Rejected(rejectionCode));
                yield break;
            }

            var body = "{\"contractVersion\":" + command.contractVersion.ToString(CultureInfo.InvariantCulture) +
                       ",\"commandId\":" + JsonString(command.commandId) +
                       ",\"expectedRevision\":" + command.expectedRevision.ToString(CultureInfo.InvariantCulture) +
                       ",\"commandType\":" + JsonString(command.commandType) +
                       ",\"payload\":" + command.payloadJson + "}";
            CommandSending?.Invoke(command);
            OntologyAuthorityCommandResult result = null;
            yield return SendJson(
                "POST",
                "/v1/worlds/" + currentWorldId + "/commands",
                body,
                currentUserId,
                (success, responseBody, error) =>
                {
                    AuthorityCommandResponse response = null;
                    if (!string.IsNullOrWhiteSpace(responseBody))
                    {
                        try
                        {
                            response = JsonUtility.FromJson<AuthorityCommandResponse>(responseBody);
                        }
                        catch (ArgumentException)
                        {
                            // A proxy or an unhandled server error may return
                            // plain text/HTML. Treat it as a transport failure
                            // instead of terminating the entry coroutine.
                        }
                    }
                    result = new OntologyAuthorityCommandResult
                    {
                        accepted = success && response != null && response.accepted,
                        rejectionCode = response?.rejectionCode ?? (success ? "invalid_authority_response" : error),
                        revision = response?.revision ?? currentRevision,
                        isReplay = response != null && response.isReplay,
                        // A valid 4xx Authority envelope is a completed durable
                        // rejection, not a lost transport response. Only keep
                        // commands in the outbox when no envelope was received.
                        transportFailure = !success && response == null
                    };
                    if (result.accepted)
                    {
                        currentRevision = result.revision;
                        SetStatus(result.isReplay
                            ? "Authority replay confirmed at revision " + currentRevision + "."
                            : "Authority accepted " + command.commandType + " at revision " + currentRevision + ".");
                    }
                    else
                    {
                        if (result.rejectionCode == "stale_revision" &&
                            result.revision >= currentRevision)
                        {
                            currentRevision = result.revision;
                        }
                        SetStatus("Authority rejected " + command.commandType + ": " + result.rejectionCode);
                    }
                });

            result ??= OntologyAuthorityCommandResult.TransportFailure("authority_no_response");
            durableCommandInFlight = false;
            CommandCompleted?.Invoke(command, result);
            completed?.Invoke(result);
        }

        /// <summary>
        /// Sends a revisioned command and retries one Authority-reported stale
        /// revision with a new idempotency key. The command factory is evaluated
        /// again so a rejected command record is never replayed as the retry.
        /// </summary>
        public IEnumerator SendCommandWithRevisionRetryRoutine(
            Func<OntologyWorldCommand> createCommand,
            Action<OntologyAuthorityCommandResult> completed = null)
        {
            if (createCommand == null)
            {
                completed?.Invoke(
                    OntologyAuthorityCommandResult.Rejected(
                        "missing_command_factory"));
                yield break;
            }

            OntologyAuthorityCommandResult result = null;
            yield return SendCommandRoutine(
                createCommand(),
                value => result = value);
            if (result != null &&
                !result.transportFailure &&
                string.Equals(
                    result.rejectionCode,
                    "stale_revision",
                    StringComparison.Ordinal))
            {
                yield return SendCommandRoutine(
                    createCommand(),
                    value => result = value);
            }

            completed?.Invoke(result);
        }

        /// <summary>
        /// Requests Authority evaluation for a presentation-only action. The
        /// endpoint validates the immutable action, assigned Rule Block,
        /// authored Facts, and ephemeral cooldown but never advances the world
        /// revision or writes a durable event.
        /// </summary>
        public IEnumerator SendRuntimeActionRoutine(
            Guid actorEntityId,
            Guid targetEntityId,
            Guid? toolEntityId,
            OntologyAuthorityActionDefinitionProjection definition,
            Action<OntologyAuthorityRuntimeActionResult> completed = null,
            bool? groundedObservation = null)
        {
            if (!IsReady ||
                actorEntityId == Guid.Empty ||
                targetEntityId == Guid.Empty ||
                definition == null)
            {
                var rejected =
                    OntologyAuthorityRuntimeActionResult.Rejected(
                        "runtime_action_not_ready");
                RuntimeActionCompleted?.Invoke(rejected);
                completed?.Invoke(rejected);
                yield break;
            }

            var payload = CreateExecuteActionPayload(
                actorEntityId,
                targetEntityId,
                toolEntityId,
                definition.packageId,
                definition.packageVersion,
                definition.actionId,
                definition.definitionVersion,
                groundedObservation);
            OntologyAuthorityRuntimeActionResult result = null;
            yield return SendJson(
                "POST",
                "/v1/worlds/" + currentWorldId + "/runtime/actions",
                payload,
                currentUserId,
                (success, responseBody, error) =>
                {
                    AuthorityRuntimeActionResponse response = null;
                    if (!string.IsNullOrWhiteSpace(responseBody))
                    {
                        try
                        {
                            response =
                                JsonUtility.FromJson<
                                    AuthorityRuntimeActionResponse>(
                                    responseBody);
                        }
                        catch (ArgumentException)
                        {
                            // Invalid proxy/server content is reported as a
                            // transport rejection below.
                        }
                    }

                    result = new OntologyAuthorityRuntimeActionResult
                    {
                        accepted =
                            success &&
                            response != null &&
                            response.accepted,
                        rejectionCode =
                            response?.rejectionCode ??
                            (success
                                ? "invalid_runtime_action_response"
                                : error),
                        actorEntityId =
                            response?.actorEntityId ?? string.Empty,
                        toolEntityId =
                            response?.toolEntityId ?? string.Empty,
                        packageId =
                            response?.packageId ?? string.Empty,
                        packageVersion =
                            response?.packageVersion ?? string.Empty,
                        actionId =
                            response?.actionId ?? string.Empty,
                        definitionVersion =
                            response?.definitionVersion ?? 0,
                        actorAnimationIntent =
                            response?.actorAnimationIntent ?? string.Empty,
                        ruleBindingId =
                            response?.ruleBindingId ?? string.Empty
                    };
                });

            result ??=
                OntologyAuthorityRuntimeActionResult.Rejected(
                    "authority_no_response");
            RuntimeActionCompleted?.Invoke(result);
            completed?.Invoke(result);
        }

        /// <summary>
        /// Evaluates an action through the enabled Authority Action + assigned
        /// Rule Block without acquiring cooldown or changing world state. Shared
        /// input adapters use this result to select an intent route; they do not
        /// reproduce the Rule Block's conditions in Unity.
        /// </summary>
        public IEnumerator PreviewActionRoutine(
            Guid actorEntityId,
            Guid targetEntityId,
            Guid? toolEntityId,
            OntologyAuthorityActionDefinitionProjection definition,
            Action<OntologyAuthorityActionPreviewResult> completed = null,
            bool? groundedObservation = null)
        {
            if (!IsReady ||
                actorEntityId == Guid.Empty ||
                targetEntityId == Guid.Empty ||
                definition == null)
            {
                completed?.Invoke(
                    OntologyAuthorityActionPreviewResult.Rejected(
                        "action_preview_not_ready"));
                yield break;
            }

            var payload = CreateExecuteActionPayload(
                actorEntityId,
                targetEntityId,
                toolEntityId,
                definition.packageId,
                definition.packageVersion,
                definition.actionId,
                definition.definitionVersion,
                groundedObservation);
            OntologyAuthorityActionPreviewResult result = null;
            yield return SendJson(
                "POST",
                "/v1/worlds/" + currentWorldId + "/actions/preview",
                payload,
                currentUserId,
                (success, responseBody, error) =>
                {
                    AuthorityActionPreviewResponse response = null;
                    if (!string.IsNullOrWhiteSpace(responseBody))
                    {
                        try
                        {
                            response =
                                JsonUtility.FromJson<
                                    AuthorityActionPreviewResponse>(
                                    responseBody);
                        }
                        catch (ArgumentException)
                        {
                            // Invalid proxy/server content is reported as a
                            // transport rejection below.
                        }
                    }

                    result = new OntologyAuthorityActionPreviewResult
                    {
                        accepted =
                            success &&
                            response != null &&
                            response.accepted,
                        rejectionCode =
                            response?.rejectionCode ??
                            (success
                                ? "invalid_action_preview_response"
                                : error),
                        actorEntityId =
                            response?.actorEntityId ?? string.Empty,
                        targetEntityId =
                            response?.targetEntityId ?? string.Empty,
                        toolEntityId =
                            response?.toolEntityId ?? string.Empty,
                        packageId =
                            response?.packageId ?? string.Empty,
                        packageVersion =
                            response?.packageVersion ?? string.Empty,
                        actionId =
                            response?.actionId ?? string.Empty,
                        definitionVersion =
                            response?.definitionVersion ?? 0,
                        actorAnimationIntent =
                            response?.actorAnimationIntent ?? string.Empty,
                        ruleBindingId =
                            response?.ruleBindingId ?? string.Empty,
                        mutationCount =
                            response?.mutationCount ?? 0
                    };
                });

            result ??=
                OntologyAuthorityActionPreviewResult.Rejected(
                    "authority_no_response");
            completed?.Invoke(result);
        }

        /// <summary>
        /// Sends a short-lived controller sample. Unlike an authoring command it
        /// never advances a world revision and never writes a Fact or world event.
        /// The server validates avatar ownership and retains only the latest sample.
        /// </summary>
        public IEnumerator SendPlayerIntentRoutine(
            Guid avatarEntityId,
            string zoneKey,
            long sequence,
            Vector2 worldMoveDirection,
            float requestedSpeed,
            OntologyAuthorityActionDefinitionProjection locomotionAction,
            Action<OntologyAuthorityRuntimeIntentResult> completed = null)
        {
            if (!IsReady || avatarEntityId == Guid.Empty ||
                string.IsNullOrWhiteSpace(zoneKey) || sequence <= 0 ||
                locomotionAction == null)
            {
                completed?.Invoke(OntologyAuthorityRuntimeIntentResult.Rejected("authority_intent_not_ready"));
                yield break;
            }

            var request = new AuthorityPlayerIntentRequest
            {
                avatarEntityId = avatarEntityId.ToString("D"),
                zoneKey = zoneKey.Trim(),
                sequence = sequence,
                moveX = Mathf.Clamp(worldMoveDirection.x, -1f, 1f),
                moveZ = Mathf.Clamp(worldMoveDirection.y, -1f, 1f),
                moveSpeed = Mathf.Clamp(requestedSpeed, 0f, 100f),
                packageId = locomotionAction.packageId,
                packageVersion = locomotionAction.packageVersion,
                actionId = locomotionAction.actionId,
                definitionVersion =
                    locomotionAction.definitionVersion
            };
            OntologyAuthorityRuntimeIntentResult result = null;
            yield return SendJson(
                "POST",
                "/v1/worlds/" + currentWorldId + "/runtime/intents",
                JsonUtility.ToJson(request),
                currentUserId,
                (success, body, error) =>
                {
                    var response = JsonUtility.FromJson<AuthorityRuntimeIntentResponse>(body);
                    result = new OntologyAuthorityRuntimeIntentResult
                    {
                        accepted = success && response != null && response.accepted,
                        rejectionCode = response?.rejectionCode ?? (success ? "invalid_authority_response" : error)
                    };
                });
            completed?.Invoke(result ?? OntologyAuthorityRuntimeIntentResult.Rejected("authority_no_response"));
        }

        /// <summary>
        /// Publishes the newest Unity collision-resolved pose as observation
        /// evidence. The sample is ephemeral and references an
        /// Authority-approved locomotion intent sequence; it never overwrites
        /// server motion or writes a world Fact, event, or durable Transform.
        /// </summary>
        public IEnumerator SendResolvedPlayerPoseRoutine(
            Guid avatarEntityId,
            string zoneKey,
            long intentSequence,
            long poseSequence,
            Vector3 position,
            string motionStatus,
            Action<OntologyAuthorityRuntimeIntentResult> completed = null)
        {
            if (!IsReady ||
                avatarEntityId == Guid.Empty ||
                string.IsNullOrWhiteSpace(zoneKey) ||
                intentSequence <= 0 ||
                poseSequence <= 0 ||
                !IsFinite(position))
            {
                completed?.Invoke(
                    OntologyAuthorityRuntimeIntentResult.Rejected(
                        "authority_resolved_pose_not_ready"));
                yield break;
            }

            var request = new AuthorityResolvedPlayerPoseRequest
            {
                zoneKey = zoneKey.Trim(),
                intentSequence = intentSequence,
                poseSequence = poseSequence,
                positionX = position.x,
                positionY = position.y,
                positionZ = position.z,
                motionStatus = motionStatus
            };
            OntologyAuthorityRuntimeIntentResult result = null;
            yield return SendJson(
                "POST",
                "/v1/worlds/" + currentWorldId +
                "/runtime/avatars/" +
                avatarEntityId.ToString("D") +
                "/resolved-pose",
                JsonUtility.ToJson(request),
                currentUserId,
                (success, body, error) =>
                {
                    var response =
                        JsonUtility.FromJson<
                            AuthorityRuntimeIntentResponse>(body);
                    result =
                        new OntologyAuthorityRuntimeIntentResult
                        {
                            accepted =
                                success &&
                                response != null &&
                                response.accepted,
                            rejectionCode =
                                response?.rejectionCode ??
                                (success
                                    ? "invalid_authority_response"
                                    : error)
                        };
                });
            completed?.Invoke(
                result ??
                OntologyAuthorityRuntimeIntentResult.Rejected(
                    "authority_no_response"));
        }

        /// <summary>
        /// Starts a new ephemeral avatar runtime session from the current durable
        /// checkpoint. This does not advance the world revision or author Facts.
        /// </summary>
        public IEnumerator ActivatePlayerRuntimeRoutine(
            Guid avatarEntityId,
            string zoneKey,
            Action<bool> completed = null)
        {
            if (!IsReady || avatarEntityId == Guid.Empty ||
                string.IsNullOrWhiteSpace(zoneKey))
            {
                completed?.Invoke(false);
                yield break;
            }

            var accepted = false;
            yield return SendJson(
                "POST",
                "/v1/worlds/" + currentWorldId +
                "/runtime/avatars/" + avatarEntityId.ToString("D") +
                "/activate",
                JsonUtility.ToJson(new AuthorityPlayerRuntimeActivationRequest
                {
                    zoneKey = zoneKey.Trim()
                }),
                currentUserId,
                (success, body, error) =>
                {
                    var response =
                        JsonUtility.FromJson<AuthorityRuntimeActivationResponse>(
                            body);
                    accepted = success && response != null &&
                               response.accepted;
                    SetStatus(
                        accepted
                            ? "Authority avatar runtime activated."
                            : "Authority runtime activation rejected: " +
                              (response?.rejectionCode ?? error));
                });
            if (accepted)
            {
                var leaseReady = false;
                yield return RenewRuntimeSessionLeaseRoutine(
                    zoneKey,
                    value => leaseReady = value);
                accepted = leaseReady;
                if (!accepted)
                {
                    SetStatus(
                        "Authority runtime activation rejected: " +
                        "runtime_zone_session_unavailable");
                }
            }
            completed?.Invoke(accepted);
        }

        private IEnumerator RenewRuntimeSessionLeaseRoutine(
            string zoneKey,
            Action<bool> completed = null)
        {
            if (!IsReady ||
                string.IsNullOrWhiteSpace(zoneKey) ||
                runtimeSessionLeaseRequestInFlight)
            {
                completed?.Invoke(false);
                yield break;
            }

            var normalizedZone = zoneKey.Trim();
            if (!IsValidZoneKey(normalizedZone))
            {
                completed?.Invoke(false);
                yield break;
            }

            if (string.IsNullOrWhiteSpace(runtimeSessionLeaseId) ||
                !string.Equals(
                    runtimeSessionLeaseWorldId,
                    currentWorldId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    runtimeSessionLeaseZoneKey,
                    normalizedZone,
                    StringComparison.Ordinal))
            {
                runtimeSessionLeaseId =
                    Guid.NewGuid().ToString("D");
                runtimeSessionLeaseWorldId = currentWorldId;
                runtimeSessionLeaseZoneKey = normalizedZone;
            }

            runtimeSessionLeaseRequestInFlight = true;
            var renewed = false;
            var leaseId = runtimeSessionLeaseId;
            var leaseWorldId = runtimeSessionLeaseWorldId;
            var leaseZoneKey = runtimeSessionLeaseZoneKey;
            var path =
                "/v1/worlds/" + leaseWorldId +
                "/runtime/zones/" +
                UnityWebRequest.EscapeURL(leaseZoneKey) +
                "/sessions/" + leaseId;
            yield return SendJson(
                "POST",
                path,
                null,
                currentUserId,
                (success, _, _) => renewed = success);
            runtimeSessionLeaseRequestInFlight = false;
            var stillCurrent =
                string.Equals(
                    runtimeSessionLeaseId,
                    leaseId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    runtimeSessionLeaseWorldId,
                    leaseWorldId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    runtimeSessionLeaseZoneKey,
                    leaseZoneKey,
                    StringComparison.Ordinal);
            if (renewed && !stillCurrent)
            {
                // Session/world exit may race an in-flight renewal. Close the
                // exact lease that was just accepted rather than leaving a
                // ghost Zone presence until TTL expiry.
                yield return ReleaseRuntimeSessionLeaseRoutine(
                    leaseWorldId,
                    leaseZoneKey,
                    leaseId);
                renewed = false;
            }
            if (stillCurrent)
            {
                nextRuntimeSessionHeartbeatAt =
                    Time.unscaledTime +
                    Mathf.Max(
                        5f,
                        settings == null
                            ? 25f
                            : settings.realtimeSessionHeartbeatSeconds);
            }
            completed?.Invoke(renewed && stillCurrent);
        }

        private void BeginReleaseRuntimeSessionLease()
        {
            if (runtimeSessionLeaseRequestInFlight ||
                string.IsNullOrWhiteSpace(runtimeSessionLeaseId) ||
                string.IsNullOrWhiteSpace(runtimeSessionLeaseWorldId) ||
                string.IsNullOrWhiteSpace(runtimeSessionLeaseZoneKey) ||
                string.IsNullOrWhiteSpace(accessToken))
            {
                ClearRuntimeSessionLeaseState();
                return;
            }

            var worldId = runtimeSessionLeaseWorldId;
            var zoneKey = runtimeSessionLeaseZoneKey;
            var sessionId = runtimeSessionLeaseId;
            ClearRuntimeSessionLeaseState();
            StartCoroutine(
                ReleaseRuntimeSessionLeaseRoutine(
                    worldId,
                    zoneKey,
                    sessionId));
        }

        private IEnumerator ReleaseRuntimeSessionLeaseRoutine(
            string worldId,
            string zoneKey,
            string sessionId)
        {
            yield return SendJson(
                "DELETE",
                "/v1/worlds/" + worldId +
                "/runtime/zones/" +
                UnityWebRequest.EscapeURL(zoneKey) +
                "/sessions/" + sessionId,
                null,
                currentUserId,
                (_, _, _) => { });
        }

        private void ClearRuntimeSessionLeaseState()
        {
            runtimeSessionLeaseId = string.Empty;
            runtimeSessionLeaseWorldId = string.Empty;
            runtimeSessionLeaseZoneKey = string.Empty;
            nextRuntimeSessionHeartbeatAt = 0f;
        }

        /// <summary>
        /// Reads the selected avatar's durable world-specific ontology overlay.
        /// This is separate from account profile relations and from ephemeral
        /// observations; callers project it only into the local runtime view.
        /// </summary>
        public IEnumerator LoadWorldAvatarProfileRoutine(
            Guid avatarEntityId,
            Action<OntologyAuthorityWorldAvatarProfile> completed = null)
        {
            if (!IsReady || avatarEntityId == Guid.Empty)
            {
                completed?.Invoke(null);
                yield break;
            }

            OntologyAuthorityWorldAvatarProfile profile = null;
            yield return SendJson(
                "GET",
                "/v1/worlds/" + currentWorldId + "/avatars/" + avatarEntityId.ToString("D") + "/profile",
                null,
                currentUserId,
                (success, body, error) =>
                {
                    if (!success)
                    {
                        SetStatus("World avatar profile load failed: " + error);
                        return;
                    }
                    profile = JsonUtility.FromJson<OntologyAuthorityWorldAvatarProfile>(body);
                    if (profile != null) profile.profileRelations ??= Array.Empty<OntologyAuthorityProfileRelation>();
                });
            completed?.Invoke(profile);
        }

        public IEnumerator LoadAvatarCheckpointRoutine(
            Guid avatarEntityId,
            Action<OntologyAuthorityAvatarCheckpoint> completed = null)
        {
            if (!IsReady || avatarEntityId == Guid.Empty)
            {
                completed?.Invoke(null);
                yield break;
            }

            OntologyAuthorityAvatarCheckpoint checkpoint = null;
            yield return SendJson(
                "GET",
                "/v1/worlds/" + currentWorldId + "/avatars/" +
                avatarEntityId.ToString("D") + "/checkpoint",
                null,
                currentUserId,
                (success, body, _) =>
                {
                    if (!success) return;
                    var response = JsonUtility.FromJson<AuthorityAvatarCheckpointResponse>(body);
                    if (response == null || !response.accepted || response.transform == null) return;
                    checkpoint = new OntologyAuthorityAvatarCheckpoint
                    {
                        zoneKey = response.zoneKey,
                        transform = response.transform,
                        revision = response.revision
                    };
                });
            completed?.Invoke(checkpoint);
        }

        /// <summary>
        /// Retrieves the server's ephemeral kinematic position for an avatar.
        /// It is intentionally a runtime read, not a world projection or Fact.
        /// </summary>
        public IEnumerator LoadPlayerMotionRoutine(
            Guid avatarEntityId,
            Action<OntologyAuthorityPlayerMotionState> completed = null)
        {
            if (!IsReady || avatarEntityId == Guid.Empty)
            {
                completed?.Invoke(null);
                yield break;
            }

            OntologyAuthorityPlayerMotionState state = null;
            yield return SendJson(
                "GET",
                "/v1/worlds/" + currentWorldId + "/runtime/avatars/" + avatarEntityId.ToString("D"),
                null,
                currentUserId,
                (success, body, _) =>
                {
                    if (success)
                    {
                        state = JsonUtility.FromJson<OntologyAuthorityPlayerMotionState>(body);
                    }
                });
            completed?.Invoke(state);
        }

        /// <summary>
        /// Reads the current Zone's ephemeral avatar states. This is a presence
        /// snapshot, not a durable world projection and not an ontology query.
        /// </summary>
        public IEnumerator LoadZoneAvatarMotionsRoutine(
            string zoneKey,
            Action<OntologyAuthorityPlayerMotionState[]> completed = null)
        {
            if (!IsReady || string.IsNullOrWhiteSpace(zoneKey))
            {
                completed?.Invoke(null);
                yield break;
            }

            OntologyAuthorityPlayerMotionState[] states = null;
            yield return SendJson(
                "GET",
                "/v1/worlds/" + currentWorldId + "/runtime/zones/" + UnityWebRequest.EscapeURL(zoneKey) + "/avatars",
                null,
                currentUserId,
                (success, body, _) =>
                {
                    if (!success) return;
                    var response = JsonUtility.FromJson<OntologyAuthorityPlayerMotionStateList>(body);
                    states = response?.items ?? Array.Empty<OntologyAuthorityPlayerMotionState>();
                });
            completed?.Invoke(states);
        }

        /// <summary>
        /// Reads ephemeral Authority motion for entities selected by the
        /// autonomous Rule Block + Physical Meaning contract. This is
        /// presentation data, never a durable world Transform or Fact.
        /// </summary>
        public IEnumerator LoadZoneAutonomousActorMotionsRoutine(
            string zoneKey,
            Action<OntologyAuthorityAutonomousActorMotionState[]> completed =
                null)
        {
            if (!IsWorldRuntimeReady ||
                string.IsNullOrWhiteSpace(zoneKey))
            {
                completed?.Invoke(null);
                yield break;
            }

            OntologyAuthorityAutonomousActorMotionState[] states = null;
            yield return SendJson(
                "GET",
                "/v1/worlds/" + currentWorldId +
                "/runtime/zones/" +
                UnityWebRequest.EscapeURL(zoneKey) +
                "/actors",
                null,
                currentUserId,
                (success, body, _) =>
                {
                    if (!success) return;
                    var response =
                        JsonUtility.FromJson<
                            OntologyAuthorityAutonomousActorMotionStateList>(
                            body);
                    states = response?.items ??
                             Array.Empty<
                                 OntologyAuthorityAutonomousActorMotionState>();
                });
            completed?.Invoke(states);
        }

        private IEnumerator LoadAndPublishAutonomousActorMotionsRoutine(
            string requestedZoneKey)
        {
            isLoadingAutonomousActors = true;
            yield return LoadZoneAutonomousActorMotionsRoutine(
                requestedZoneKey,
                states =>
                {
                    if (states == null ||
                        !string.Equals(
                            requestedZoneKey,
                            CurrentProjectionZoneKey,
                            StringComparison.Ordinal))
                    {
                        return;
                    }
                    AutonomousActorMotionsReceived?.Invoke(states);
                });
            isLoadingAutonomousActors = false;
        }

        public static OntologyWorldCommand CreateCommand(string commandType, string payloadJson)
        {
            return new OntologyWorldCommand
            {
                commandId = Guid.NewGuid().ToString("D"),
                commandType = commandType,
                payloadJson = payloadJson
            };
        }

        public static string CreatePlaceEntityPayload(
            Guid entityId,
            string templateId,
            string displayName,
            Transform transform,
            string zoneKey = null,
            IReadOnlyList<OntologyAuthorityInitialFact> initialFacts = null)
        {
            return CreatePlaceEntityPayload(
                entityId,
                templateId,
                displayName,
                transform.position,
                transform.eulerAngles,
                transform.localScale,
                zoneKey,
                initialFacts);
        }

        public static string CreatePlaceEntityPayload(
            Guid entityId,
            string templateId,
            string displayName,
            Vector3 position,
            Vector3 rotationEuler,
            Vector3 localScale,
            string zoneKey = null,
            IReadOnlyList<OntologyAuthorityInitialFact> initialFacts = null)
        {
            return "{\"entityId\":" + JsonString(entityId.ToString("D")) +
                   ",\"templateId\":" + JsonString(templateId) +
                   ",\"templateVersion\":1" +
                   ",\"displayName\":" + JsonString(displayName) +
                   ",\"zoneKey\":" + JsonNullableString(zoneKey) +
                   ",\"transform\":" +
                   TransformJson(position, rotationEuler, localScale) +
                   ",\"initialFacts\":" + InitialFactsJson(initialFacts) + "}";
        }

        public static OntologyAuthorityInitialFact CreateInitialFact(
            string predicateId,
            string objectValue)
        {
            if (bool.TryParse(objectValue, out var booleanValue))
            {
                return new OntologyAuthorityInitialFact
                {
                    predicateId = predicateId,
                    objectKind = "boolean",
                    objectValueJson = booleanValue ? "true" : "false"
                };
            }

            if (long.TryParse(
                    objectValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var integerValue))
            {
                return new OntologyAuthorityInitialFact
                {
                    predicateId = predicateId,
                    objectKind = "number",
                    objectValueJson = integerValue.ToString(
                        CultureInfo.InvariantCulture)
                };
            }
            if (double.TryParse(
                    objectValue,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var numberValue) &&
                double.IsFinite(numberValue))
            {
                return new OntologyAuthorityInitialFact
                {
                    predicateId = predicateId,
                    objectKind = "number",
                    objectValueJson = numberValue.ToString(
                        "R",
                        CultureInfo.InvariantCulture)
                };
            }

            return new OntologyAuthorityInitialFact
            {
                predicateId = predicateId,
                objectKind = "canonical",
                objectCanonicalId = objectValue
            };
        }

        public static string CreateMoveEntityPayload(Guid entityId, Transform transform)
        {
            return "{\"entityId\":" + JsonString(entityId.ToString("D")) +
                   ",\"transform\":" + TransformJson(transform) + "}";
        }

        public static string CreateRetireEntityPayload(Guid entityId)
        {
            return "{\"entityId\":" + JsonString(entityId.ToString("D")) + "}";
        }

        public static string CreateAvatarCheckpointPayload(
            Guid avatarEntityId,
            string zoneKey,
            Transform transform)
        {
            return CreateAvatarCheckpointPayload(
                avatarEntityId,
                zoneKey,
                transform.position,
                transform.eulerAngles,
                transform.localScale);
        }

        public static string CreateAvatarCheckpointPayload(
            Guid avatarEntityId,
            string zoneKey,
            OntologyAuthorityTransform transform)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            return CreateAvatarCheckpointPayload(
                avatarEntityId,
                zoneKey,
                new Vector3(
                    transform.positionX,
                    transform.positionY,
                    transform.positionZ),
                new Vector3(
                    transform.rotationX,
                    transform.rotationY,
                    transform.rotationZ),
                new Vector3(
                    transform.scaleX,
                    transform.scaleY,
                    transform.scaleZ));
        }

        private static string CreateAvatarCheckpointPayload(
            Guid avatarEntityId,
            string zoneKey,
            Vector3 position,
            Vector3 rotationEuler,
            Vector3 localScale)
        {
            return "{\"avatarEntityId\":" + JsonString(avatarEntityId.ToString("D")) +
                   ",\"zoneKey\":" + JsonNullableString(zoneKey) +
                   ",\"transform\":" +
                   TransformJson(position, rotationEuler, localScale) + "}";
        }

        public static string CreateRegisterPlayerAvatarPayload(Guid entityId)
        {
            return "{\"entityId\":" + JsonString(entityId.ToString("D")) + "}";
        }

        public static string CreateSetContentPackagePayload(
            string packageId,
            string packageVersion,
            bool enabled)
        {
            return "{\"packageId\":" + JsonString(packageId) +
                   ",\"packageVersion\":" + JsonString(packageVersion) +
                   ",\"enabled\":" + (enabled ? "true" : "false") + "}";
        }

        public static string CreateDevelopmentPackageId(
            string basePackageId,
            string ownerUserId)
        {
            var normalizedBase = basePackageId?.Trim() ?? string.Empty;
            return Guid.TryParse(ownerUserId, out var ownerId)
                ? normalizedBase + "_" + ownerId.ToString("N")
                : normalizedBase;
        }

        /// <summary>
        /// A durable action intent. The authority resolves its published effect from
        /// the enabled package/version; this payload never contains a predicate or
        /// a client-authored ontology effect.
        /// </summary>
        public static string CreateExecuteActionPayload(
            Guid actorEntityId,
            Guid targetEntityId,
            Guid? toolEntityId,
            string packageId,
            string packageVersion,
            string actionId,
            int definitionVersion,
            bool? groundedObservation = null)
        {
            return "{\"actorEntityId\":" + JsonString(actorEntityId.ToString("D")) +
                   ",\"targetEntityId\":" + JsonString(targetEntityId.ToString("D")) +
                   ",\"toolEntityId\":" + (toolEntityId.HasValue ? JsonString(toolEntityId.Value.ToString("D")) : "null") +
                   ",\"packageId\":" + JsonString(packageId) +
                   ",\"packageVersion\":" + JsonString(packageVersion) +
                   ",\"actionId\":" + JsonString(actionId) +
                   ",\"definitionVersion\":" +
                   definitionVersion.ToString(CultureInfo.InvariantCulture) +
                   (groundedObservation.HasValue
                       ? ",\"groundedObservation\":" +
                         (groundedObservation.Value ? "true" : "false")
                       : string.Empty) +
                   "}";
        }

        public static string CreateSetAvatarProfileRelationsPayload(
            Guid avatarEntityId,
            OntologyAuthorityProfileRelation[] relations)
        {
            return JsonUtility.ToJson(new AvatarProfileRelationsPayload
            {
                avatarEntityId = avatarEntityId.ToString("D"),
                profileRelations = relations ?? Array.Empty<OntologyAuthorityProfileRelation>()
            });
        }

        public static string CreateDefineZonePayload(
            string zoneKey,
            float minX,
            float minZ,
            float maxX,
            float maxZ,
            string simulationMode)
        {
            return "{\"zoneKey\":" + JsonString(zoneKey) +
                   ",\"minX\":" + Number(minX) +
                   ",\"minZ\":" + Number(minZ) +
                   ",\"maxX\":" + Number(maxX) +
                   ",\"maxZ\":" + Number(maxZ) +
                   ",\"simulationMode\":" + JsonString(simulationMode) + "}";
        }

        public static string CreateCanonicalFactPayload(
            Guid subjectEntityId,
            string predicateId,
            string objectCanonicalId)
        {
            return "{\"subjectEntityId\":" + JsonString(subjectEntityId.ToString("D")) +
                   ",\"predicateId\":" + JsonString(predicateId) +
                   ",\"objectKind\":\"canonical\"" +
                   ",\"objectEntityId\":null" +
                   ",\"objectCanonicalId\":" + JsonString(objectCanonicalId) +
                   ",\"objectValueJson\":null}";
        }

        public static string CreateFactPayload(
            Guid subjectEntityId,
            OntologyAuthorityInitialFact fact)
        {
            return "{\"subjectEntityId\":" +
                   JsonString(subjectEntityId.ToString("D")) +
                   ",\"predicateId\":" + JsonString(fact.predicateId) +
                   ",\"objectKind\":" + JsonString(fact.objectKind) +
                   ",\"objectEntityId\":" +
                   JsonNullableString(fact.objectEntityId) +
                   ",\"objectCanonicalId\":" +
                   JsonNullableString(fact.objectCanonicalId) +
                   ",\"objectValueJson\":" +
                   JsonNullableString(fact.objectValueJson) + "}";
        }

        public static string CreateEntityFactPayload(
            Guid subjectEntityId,
            string predicateId,
            Guid objectEntityId)
        {
            return "{\"subjectEntityId\":" + JsonString(subjectEntityId.ToString("D")) +
                   ",\"predicateId\":" + JsonString(predicateId) +
                   ",\"objectKind\":\"entity\"" +
                   ",\"objectEntityId\":" + JsonString(objectEntityId.ToString("D")) +
                   ",\"objectCanonicalId\":null,\"objectValueJson\":null}";
        }

        public static string CreateRetractFactPayload(Guid factId)
        {
            return "{\"factId\":" + JsonString(factId.ToString("D")) + "}";
        }

        public static string CreateRemoveRuleBlockPayload(Guid bindingId)
        {
            return "{\"bindingId\":" + JsonString(bindingId.ToString("D")) + "}";
        }

        public static string CreateMeaningPackagePayload(
            Guid targetEntityId,
            OntologyMeaningPackageChange change)
        {
            if (change == null) return "{}";
            var replacePredicates = change.replacePredicateIds?
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(JsonString).ToArray() ?? Array.Empty<string>();
            var requiredConcepts = change.requiredConceptIds?
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(JsonString).ToArray() ?? Array.Empty<string>();
            var canonicalAuthoredFacts = change.authoredFacts?
                .Where(value => value != null)
                .Select(value =>
                    "{\"predicateId\":" + JsonString(value.predicate) +
                    ",\"objectKind\":\"canonical\"" +
                    ",\"objectEntityId\":null" +
                    ",\"objectCanonicalId\":" + JsonNullableString(value.obj) +
                    ",\"objectValueJson\":null}")
                .ToArray() ?? Array.Empty<string>();
            var typedAuthoredFacts = change.authorityFacts?
                .Where(value => value != null)
                .Select(value =>
                    "{\"predicateId\":" + JsonString(value.predicateId) +
                    ",\"objectKind\":" + JsonString(value.objectKind) +
                    ",\"objectEntityId\":" +
                    JsonNullableString(value.objectEntityId) +
                    ",\"objectCanonicalId\":" +
                    JsonNullableString(value.objectCanonicalId) +
                    ",\"objectValueJson\":" +
                    JsonNullableString(value.objectValueJson) + "}")
                .ToArray() ?? Array.Empty<string>();
            var authoredFacts = canonicalAuthoredFacts
                .Concat(typedAuthoredFacts)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var ruleBlocks = change.ruleBlocks?
                .Where(value => value != null)
                .Select(value =>
                {
                    var parameters =
                        "{\"bindingVariable\":" +
                        JsonString(value.bindingVariable) + "}";
                    return "{\"bindingId\":" +
                           JsonGuidString(value.bindingId) +
                           ",\"ruleId\":" + JsonString(value.ruleId) +
                           ",\"ruleVersion\":" +
                           Math.Max(1, value.ruleVersion)
                               .ToString(CultureInfo.InvariantCulture) +
                           ",\"parameterValuesJson\":" +
                           JsonString(parameters) + "}";
                }).ToArray() ?? Array.Empty<string>();

            return "{\"operation\":" + JsonString(change.operation) +
                   ",\"applicationId\":" +
                   JsonGuidString(change.applicationId) +
                   ",\"targetEntityId\":" +
                   JsonString(targetEntityId.ToString("D")) +
                   ",\"slotId\":" + JsonString(change.slotId) +
                   ",\"packageId\":" + JsonString(change.packageId) +
                   ",\"adoptExistingContributions\":" +
                   (change.adoptExistingContributions ? "true" : "false") +
                   ",\"replacePredicateIds\":[" +
                   string.Join(",", replacePredicates) + "]" +
                   ",\"requiredConceptIds\":[" +
                   string.Join(",", requiredConcepts) + "]" +
                   ",\"authoredFacts\":[" +
                   string.Join(",", authoredFacts) + "]" +
                   ",\"ruleBlocks\":[" +
                   string.Join(",", ruleBlocks) + "]}";
        }

        public static string NormalizeMeaningPackagePayload(string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return payloadJson;
            return payloadJson
                .Replace(
                    "\"objectEntityId\":\"\"",
                    "\"objectEntityId\":null")
                .Replace(
                    "\"applicationId\":\"\"",
                    "\"applicationId\":\"" +
                    Guid.Empty.ToString("D") + "\"")
                .Replace(
                    "\"bindingId\":\"\"",
                    "\"bindingId\":\"" +
                    Guid.Empty.ToString("D") + "\"");
        }

        public static string CreateRuleBlockPayload(
            Guid bindingId,
            Guid targetEntityId,
            string ruleId,
            string bindingVariable,
            int ruleVersion = 1)
        {
            var parameters = "{\"bindingVariable\":" + JsonString(bindingVariable) + "}";
            return "{\"bindingId\":" + JsonString(bindingId.ToString("D")) +
                   ",\"targetEntityId\":" + JsonString(targetEntityId.ToString("D")) +
                   ",\"ruleId\":" + JsonString(ruleId) +
                   ",\"ruleVersion\":" + Math.Max(1, ruleVersion) +
                   ",\"parameterValuesJson\":" + JsonString(parameters) + "}";
        }

        private IEnumerator CreateDevelopmentWorldRoutine()
        {
            var slug = string.IsNullOrWhiteSpace(settings.newWorldSlug)
                ? "local-sandbox"
                : settings.newWorldSlug.Trim();
            var title = string.IsNullOrWhiteSpace(settings.newWorldTitle)
                ? "Local Sandbox"
                : settings.newWorldTitle.Trim();
            var request = new CreateWorldRequest
            {
                slug = slug,
                title = title,
                visibility = settings.newWorldVisibility
            };

            yield return SendJson(
                "POST",
                "/v1/worlds",
                JsonUtility.ToJson(request),
                currentUserId,
                (success, body, error) =>
                {
                    if (!success)
                    {
                        SetStatus("Authority world creation failed: " + error);
                        return;
                    }

                    var response = JsonUtility.FromJson<CreateWorldResponse>(body);
                    if (response == null || !Guid.TryParse(response.worldId, out _))
                    {
                        SetStatus("Authority returned an invalid new world id.");
                        return;
                    }

                    currentWorldId = response.worldId;
                    currentRevision = response.revision;
                    OntologyAuthoritySessionStore.SaveWorldId(
                        SessionBaseUrl(),
                        currentUserId,
                        currentWorldId);
                    SetStatus("Created authority world '" + title + "'.");
                });

            if (Guid.TryParse(currentWorldId, out _))
            {
                yield return LoadWorldRoutine();
            }
        }

        /// <summary>Creates a user-authored world and selects it for the current account.</summary>
        public IEnumerator CreateWorldRoutine(string slug, string title, string visibility, Action<bool> completed = null)
        {
            if (!IsAuthenticated) { completed?.Invoke(false); yield break; }
            var request = new CreateWorldRequest { slug = slug?.Trim(), title = title?.Trim(), visibility = visibility?.Trim() };
            var created = false;
            yield return SendJson("POST", "/v1/worlds", JsonUtility.ToJson(request), currentUserId, (success, body, error) =>
            {
                if (!success) { SetStatus("Authority world creation failed: " + error); return; }
                var response = JsonUtility.FromJson<CreateWorldResponse>(body);
                if (response == null || !Guid.TryParse(response.worldId, out _)) { SetStatus("Authority returned an invalid new world id."); return; }
                currentWorldId = response.worldId; currentRevision = response.revision;
                OntologyAuthoritySessionStore.SaveWorldId(
                    SessionBaseUrl(),
                    currentUserId,
                    currentWorldId);
                SetStatus("Created and selected authority world '" + request.title + "'."); created = true;
            });
            if (created) { yield return LoadAccountDashboardRoutine(); SelectWorld(currentWorldId); }
            completed?.Invoke(created);
        }

        private static string DevelopmentActionJson(
            string verb,
            string predicate,
            bool requiresTool,
            string objectPattern)
        {
            return "{\"actionVerb\":" + JsonString(verb) +
                   ",\"subjectPattern\":\"?actor\",\"predicate\":" + JsonString(predicate) +
                   ",\"objectPattern\":" + JsonString(objectPattern) +
                   ",\"requiresTool\":" + (requiresTool ? "true" : "false") +
                   ",\"conditions\":[],\"effects\":[]}";
        }

        private IEnumerator SendJson(
            string method,
            string relativePath,
            string jsonBody,
            string userId,
            Action<bool, string, string> completed)
        {
            var url = NormalizeBaseUrl() + relativePath;
            using var request = new UnityWebRequest(url, method)
            {
                downloadHandler = new DownloadHandlerBuffer()
            };
            if (!string.IsNullOrWhiteSpace(jsonBody))
            {
                request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonBody));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            if (!string.IsNullOrWhiteSpace(accessToken) && !relativePath.StartsWith("/v1/auth/register") && !relativePath.StartsWith("/v1/auth/login"))
            {
                request.SetRequestHeader("Authorization", "Bearer " + accessToken);
            }

            yield return request.SendWebRequest();
            var body = request.downloadHandler == null ? string.Empty : request.downloadHandler.text;
            var success = request.result == UnityWebRequest.Result.Success &&
                          request.responseCode >= 200 && request.responseCode < 300;
            var error = success
                ? string.Empty
                : ExtractRejectionCode(body, request.error, request.responseCode);
            completed?.Invoke(success, body, error);
        }

        private string NormalizeBaseUrl()
        {
            return settings.baseUrl.Trim().TrimEnd('/');
        }

        private string BuildProjectionPath()
        {
            var path = "/v1/worlds/" + currentWorldId;
            var zoneKey = CurrentProjectionZoneKey;
            return string.IsNullOrWhiteSpace(zoneKey)
                ? path
                : path + "?zoneKey=" + UnityWebRequest.EscapeURL(zoneKey);
        }

        private static bool IsValidZoneKey(string value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            if (value.Length > 128 || !IsAsciiLetter(value[0])) return false;
            foreach (var character in value)
            {
                if (!IsAsciiLetter(character) &&
                    !(character >= '0' && character <= '9') &&
                    character != '_' &&
                    character != '-')
                    return false;
            }
            return true;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) &&
            float.IsFinite(value.y) &&
            float.IsFinite(value.z);

        private static bool IsAsciiLetter(char value) =>
            (value >= 'A' && value <= 'Z') ||
            (value >= 'a' && value <= 'z');

        public static string SessionTokenPreferenceKey(string baseUrl) =>
            OntologyAuthoritySessionStore.TokenKey(baseUrl);

        private string SessionBaseUrl() =>
            settings == null ? string.Empty : NormalizeBaseUrl();

        private void PersistSession()
        {
            OntologyAuthoritySessionStore.Save(
                SessionBaseUrl(),
                currentUserId,
                accessToken);
        }

        private void RestoreStoredSession()
        {
            if (settings == null)
            {
                return;
            }

            var session = OntologyAuthoritySessionStore.Load(SessionBaseUrl());
            accessToken = session.AccessToken;
            currentUserId = session.UserId;
        }

        private void ClearSession()
        {
            if (settings != null)
            {
                OntologyAuthoritySessionStore.Clear(SessionBaseUrl());
            }

            accessToken = string.Empty;
            currentUserId = string.Empty;
            currentCharacterId = string.Empty;
            currentWorldId = string.Empty;
            currentAccount = null;
            currentProjection = null;
            hasEnteredCurrentWorld = false;
            ClearRuntimeSessionLeaseState();
        }

        private void SelectRememberedWorld()
        {
            if (currentAccount?.worlds == null ||
                currentAccount.worlds.Length == 0)
            {
                currentWorldId = string.Empty;
                return;
            }

            var remembered = OntologyAuthoritySessionStore.LoadWorldId(
                SessionBaseUrl(),
                currentUserId);
            if (SelectWorld(remembered))
            {
                return;
            }

            SelectWorld(currentAccount.worlds[0].worldId);
        }

        private void SelectRememberedCharacter()
        {
            if (currentAccount?.characters == null || currentAccount.characters.Length == 0)
            {
                currentCharacterId = string.Empty;
                return;
            }

            var remembered = OntologyAuthoritySessionStore.LoadCharacterId(
                SessionBaseUrl(),
                currentUserId);
            if (SelectCharacter(remembered)) return;
            SelectCharacter(currentAccount.characters[0].characterId);
        }

        private void SetStatus(string value)
        {
            lastStatus = value ?? string.Empty;
            StateChanged?.Invoke();
        }

        private static string ExtractRejectionCode(string body, string transportError, long responseCode)
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    var response = JsonUtility.FromJson<AuthorityCommandResponse>(body);
                    if (response != null && !string.IsNullOrWhiteSpace(response.rejectionCode))
                    {
                        return response.rejectionCode;
                    }
                }
                catch (ArgumentException)
                {
                    // Transport or proxy errors can be HTML/plain text. They are
                    // not authority command envelopes and must not interrupt the
                    // client recovery path.
                }
            }

            return string.IsNullOrWhiteSpace(transportError)
                ? "http_" + responseCode.ToString(CultureInfo.InvariantCulture)
                : transportError;
        }

        private static string TransformJson(Transform transform)
        {
            return TransformJson(
                transform.position,
                transform.eulerAngles,
                transform.localScale);
        }

        private static string TransformJson(
            Vector3 position,
            Vector3 rotation,
            Vector3 scale)
        {
            return "{\"positionX\":" + Number(position.x) +
                   ",\"positionY\":" + Number(position.y) +
                   ",\"positionZ\":" + Number(position.z) +
                   ",\"rotationX\":" + Number(rotation.x) +
                   ",\"rotationY\":" + Number(rotation.y) +
                   ",\"rotationZ\":" + Number(rotation.z) +
                   ",\"scaleX\":" + Number(scale.x) +
                   ",\"scaleY\":" + Number(scale.y) +
                   ",\"scaleZ\":" + Number(scale.z) + "}";
        }

        private static string InitialFactsJson(
            IReadOnlyList<OntologyAuthorityInitialFact> facts)
        {
            if (facts == null || facts.Count == 0) return "[]";
            var values = new string[facts.Count];
            for (var index = 0; index < facts.Count; index++)
            {
                var fact = facts[index];
                values[index] =
                    "{\"predicateId\":" + JsonString(fact.predicateId) +
                    ",\"objectKind\":" + JsonString(fact.objectKind) +
                    ",\"objectEntityId\":" +
                    JsonNullableString(fact.objectEntityId) +
                    ",\"objectCanonicalId\":" +
                    JsonNullableString(fact.objectCanonicalId) +
                    ",\"objectValueJson\":" +
                    JsonNullableString(fact.objectValueJson) + "}";
            }
            return "[" + string.Join(",", values) + "]";
        }

        private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string JsonNullableString(string value) => string.IsNullOrWhiteSpace(value) ? "null" : JsonString(value);
        private static string JsonGuidString(string value) =>
            JsonString(
                Guid.TryParse(value, out var parsed)
                    ? parsed.ToString("D")
                    : Guid.Empty.ToString("D"));
        private static string JsonString(string value)
        {
            var escaped = (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
            return "\"" + escaped + "\"";
        }

        [Serializable] private sealed class PasswordRegisterRequest { public string email; public string displayName; public string password; }
        [Serializable] private sealed class PasswordLoginRequest { public string email; public string password; }
        [Serializable] private sealed class PasswordAuthResponse { public string userId; public string accessToken; }
        [Serializable] private sealed class AuthorityCreatePlayerCharacterRequest
        {
            public string displayName;
            public string templateId;
            public string[] equippedPartIds;
        }
        [Serializable] private sealed class AuthorityEnterWorldRequest
        {
            public string characterId;
            public string avatarEntityId;
        }
        [Serializable] private sealed class AuthorityRuleCatalogPublishRequest
        {
            public string packageVersion;
            public AuthorityRuleDefinitionPublishRequest[] rules;
        }
        [Serializable] private sealed class AuthorityRuleDefinitionPublishRequest
        {
            public string ruleId;
            public int definitionVersion;
            public string payloadJson;
        }
        [Serializable] private sealed class AuthorityActionCatalogPublishRequest { public string packageVersion; public AuthorityActionDefinitionPublishRequest[] actions; }
        [Serializable] private sealed class AuthorityActionDefinitionPublishRequest { public string actionId; public int definitionVersion; public string payloadJson; }
        [Serializable] private sealed class AuthorityUpdatePlayerCharacterProfileRequest
        {
            public string commandId;
            public long expectedRevision;
            public string displayName;
            public string templateId;
            public string[] equippedPartIds;
            public OntologyAuthorityProfileRelation[] profileRelations;
        }
        [Serializable] private sealed class CreateWorldRequest { public string slug; public string title; public string visibility; }
        [Serializable] private sealed class CreateWorldResponse { public string worldId; public long revision; }
        [Serializable] private sealed class AuthorityCommandResponse { public bool accepted; public string rejectionCode; public long revision; public bool isReplay; }
        [Serializable] private sealed class AuthorityAvatarCheckpointResponse
        {
            public bool accepted;
            public string rejectionCode;
            public string zoneKey;
            public OntologyAuthorityTransform transform;
            public long revision;
        }
        [Serializable] private sealed class AuthorityPlayerIntentRequest
        {
            public string avatarEntityId;
            public string zoneKey;
            public long sequence;
            public float moveX;
            public float moveZ;
            public float moveSpeed;
            public string packageId;
            public string packageVersion;
            public string actionId;
            public int definitionVersion;
        }
        [Serializable]
        private sealed class AuthorityResolvedPlayerPoseRequest
        {
            public string zoneKey;
            public long intentSequence;
            public long poseSequence;
            public float positionX;
            public float positionY;
            public float positionZ;
            public string motionStatus;
        }
        [Serializable] private sealed class AuthorityPlayerRuntimeActivationRequest
        {
            public string zoneKey;
        }
        [Serializable] private sealed class AuthorityRuntimeActivationResponse
        {
            public bool accepted;
            public string rejectionCode;
        }
        [Serializable] private sealed class AuthorityRuntimeIntentResponse { public bool accepted; public string rejectionCode; }
        [Serializable]
        private sealed class AuthorityRuntimeActionResponse
        {
            public bool accepted;
            public string rejectionCode;
            public string actorEntityId;
            public string toolEntityId;
            public string packageId;
            public string packageVersion;
            public string actionId;
            public int definitionVersion;
            public string actorAnimationIntent;
            public string ruleBindingId;
        }
        [Serializable]
        private sealed class AuthorityActionPreviewResponse
        {
            public bool accepted;
            public string rejectionCode;
            public string actorEntityId;
            public string targetEntityId;
            public string toolEntityId;
            public string packageId;
            public string packageVersion;
            public string actionId;
            public int definitionVersion;
            public string actorAnimationIntent;
            public string ruleBindingId;
            public int mutationCount;
        }
        [Serializable] private sealed class AvatarProfileRelationsPayload
        {
            public string avatarEntityId;
            public OntologyAuthorityProfileRelation[] profileRelations;
        }
    }

    [Serializable]
    public sealed class OntologyAuthorityAccountDashboard
    {
        public OntologyAuthorityAccount account;
        public OntologyAuthorityPlayerCharacter[] characters;
        public OntologyAuthorityAccountWorld[] worlds;
    }

    [Serializable]
    public sealed class OntologyAuthorityAccount
    {
        public string userId;
        public string displayName;
    }

    [Serializable]
    public sealed class OntologyAuthorityPlayerCharacter
    {
        public string characterId;
        public string displayName;
        public string templateId;
        public string[] equippedPartIds;
        public long profileRevision;
        public OntologyAuthorityProfileRelation[] profileRelations;
    }

    [Serializable]
    public sealed class OntologyAuthorityProfileRelation
    {
        public string subjectId;
        public string predicateId;
        public string objectId;
    }

    [Serializable]
    public sealed class OntologyAuthorityWorldAvatarProfile
    {
        public bool accepted;
        public string rejectionCode;
        public OntologyAuthorityProfileRelation[] profileRelations;
        public long revision;
    }

    [Serializable]
    public sealed class OntologyAuthorityAccountWorld
    {
        public string worldId;
        public string title;
        public string slug;
        public string role;
        public long revision;
    }

    [Serializable]
    public sealed class OntologyAuthorityCommandResult
    {
        public bool accepted;
        public string rejectionCode;
        public long revision;
        public bool isReplay;
        public bool transportFailure;

        public static OntologyAuthorityCommandResult Rejected(string code)
        {
            return new OntologyAuthorityCommandResult { rejectionCode = code };
        }

        public static OntologyAuthorityCommandResult TransportFailure(string code)
        {
            return new OntologyAuthorityCommandResult
            {
                rejectionCode = code,
                transportFailure = true
            };
        }
    }

    [Serializable]
    public sealed class OntologyAuthorityRuntimeIntentResult
    {
        public bool accepted;
        public string rejectionCode;

        public static OntologyAuthorityRuntimeIntentResult Rejected(string code)
        {
            return new OntologyAuthorityRuntimeIntentResult { rejectionCode = code };
        }
    }

    [Serializable]
    public sealed class OntologyAuthorityRuntimeActionResult
    {
        public bool accepted;
        public string rejectionCode;
        public string actorEntityId;
        public string toolEntityId;
        public string packageId;
        public string packageVersion;
        public string actionId;
        public int definitionVersion;
        public string actorAnimationIntent;
        public string ruleBindingId;

        public static OntologyAuthorityRuntimeActionResult Rejected(
            string code)
        {
            return new OntologyAuthorityRuntimeActionResult
            {
                rejectionCode = code
            };
        }
    }

    [Serializable]
    public sealed class OntologyAuthorityActionPreviewResult
    {
        public bool accepted;
        public string rejectionCode;
        public string actorEntityId;
        public string targetEntityId;
        public string toolEntityId;
        public string packageId;
        public string packageVersion;
        public string actionId;
        public int definitionVersion;
        public string actorAnimationIntent;
        public string ruleBindingId;
        public int mutationCount;

        public static OntologyAuthorityActionPreviewResult Rejected(
            string code)
        {
            return new OntologyAuthorityActionPreviewResult
            {
                rejectionCode = code
            };
        }
    }

    [Serializable]
    public sealed class OntologyAuthorityPlayerMotionState
    {
        public string worldId;
        public string avatarEntityId;
        public string zoneKey;
        public double positionX;
        public double positionY;
        public double positionZ;
        public double velocityX;
        public double velocityY;
        public double velocityZ;
        public double groundReferenceY;
        public string groundSupportEntityId;
        public bool grounded;
        public long serverTick;
        public long lastProcessedIntentSequence;
        public string motionStatus;
        public long updatedAtUnixMilliseconds;
    }

    [Serializable]
    public sealed class OntologyAuthorityPlayerMotionStateList
    {
        public OntologyAuthorityPlayerMotionState[] items;
    }

    [Serializable]
    public sealed class OntologyAuthorityAutonomousActorMotionState
    {
        public string worldId;
        public string actorEntityId;
        public string zoneKey;
        public double positionX;
        public double positionY;
        public double positionZ;
        public double forwardX;
        public double forwardZ;
        public string motionStatus;
        public string targetEntityId;
        public string actorAnimationIntent;
        public long presentationSequence;
        public long updatedAtUnixMilliseconds;
    }

    [Serializable]
    public sealed class OntologyAuthorityAutonomousActorMotionStateList
    {
        public OntologyAuthorityAutonomousActorMotionState[] items;
    }

    [Serializable]
    public sealed class OntologyAuthorityAvatarCheckpoint
    {
        public string zoneKey;
        public OntologyAuthorityTransform transform;
        public long revision;
    }

    [Serializable]
    public sealed class OntologyAuthorityWorldProjection
    {
        public string worldId;
        public string title;
        public long revision;
        public string scopeZoneKey;
        public OntologyAuthorityEntityProjection[] entities;
        public OntologyAuthorityFactProjection[] facts;
        public OntologyAuthorityRuleBindingProjection[] ruleBindings;
        public OntologyAuthorityActionDefinitionProjection[] actions;
    }

    [Serializable]
    public sealed class OntologyAuthorityActionDefinitionProjection
    {
        public string packageId;
        public string packageVersion;
        public string actionId;
        public int definitionVersion;
        public string actorAnimationIntent;
    }

    [Serializable]
    public sealed class OntologyAuthorityWorldZoneList
    {
        public OntologyAuthorityWorldZoneProjection[] items;
    }

    [Serializable]
    public sealed class OntologyAuthorityWorldZoneProjection
    {
        public string zoneKey;
        public float minX;
        public float minZ;
        public float maxX;
        public float maxZ;
        public string simulationMode;
    }

    [Serializable]
    public sealed class OntologyAuthorityEntityProjection
    {
        public string entityId;
        public string templateId;
        public int templateVersion;
        public string displayName;
        public string zoneKey;
        public OntologyAuthorityTransform transform;
    }

    [Serializable]
    public sealed class OntologyAuthorityInitialFact
    {
        public string predicateId;
        public string objectKind;
        public string objectEntityId;
        public string objectCanonicalId;
        public string objectValueJson;
    }

    [Serializable]
    public sealed class OntologyAuthorityFactProjection
    {
        public string factId;
        public string subjectEntityId;
        public string predicateId;
        public string objectKind;
        public string objectEntityId;
        public string objectCanonicalId;
        public string objectValueJson;
    }

    [Serializable]
    public sealed class OntologyAuthorityRuleBindingProjection
    {
        public string bindingId;
        public string targetEntityId;
        public string ruleId;
        public int ruleVersion;
        public bool enabled;
        public string parameterValuesJson;
    }

    [Serializable]
    public sealed class OntologyAuthorityTransform
    {
        public float positionX;
        public float positionY;
        public float positionZ;
        public float rotationX;
        public float rotationY;
        public float rotationZ;
        public float scaleX;
        public float scaleY;
        public float scaleZ;
    }
}
