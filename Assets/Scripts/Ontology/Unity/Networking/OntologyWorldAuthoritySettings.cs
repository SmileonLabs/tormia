using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Non-secret connection data for a local or production authority endpoint.
    /// Authentication tokens deliberately do not live in this asset.
    /// </summary>
    [CreateAssetMenu(
        fileName = "WorldAuthoritySettings",
        menuName = "Tormia/Ontology/World Authority Settings")]
    public sealed class OntologyWorldAuthoritySettings : ScriptableObject
    {
        [Header("Endpoint")]
        [Tooltip("Authority endpoint only. Unity clients never receive a PostgreSQL or Redis connection string.")]
        public string baseUrl = "http://127.0.0.1:5272";

        [Header("Local world defaults")]
        [Tooltip("Paste a shared world GUID here to join an existing local world. Leave empty to create and remember a personal world.")]
        public string sharedWorldId;
        public string newWorldSlug = "local-sandbox";
        public string newWorldTitle = "Local Sandbox";
        public string newWorldVisibility = "private";

        [Header("Runtime foundation")]
        [Tooltip(
            "Canonical Zone created through a revisioned Authority command when " +
            "an editable world has no authored Zones yet.")]
        public string defaultRuntimeZoneKey = "world_main";
        [Tooltip("X/Z minimum of the data-defined starter runtime Zone.")]
        public Vector2 defaultRuntimeZoneMin = new(-512f, -512f);
        [Tooltip("X/Z maximum of the data-defined starter runtime Zone.")]
        public Vector2 defaultRuntimeZoneMax = new(512f, 512f);
        [Tooltip("Authority simulation mode for the starter Zone.")]
        public string defaultRuntimeZoneSimulationMode = "active";
        [Header("Authored starter support")]
        [Tooltip(
            "When enabled, the owner authors one deterministic WalkableSupport " +
            "entity for the starter scene. Removing its collision_role later " +
            "does not cause the entry flow to restore it.")]
        public bool authorDefaultRuntimeWalkableSupport = true;
        [Tooltip(
            "Canonical content template ID for the starter support entity.")]
        public string defaultRuntimeWalkableSupportTemplateId =
            "world_flat_support";
        [Tooltip("Presentation label for the starter support entity.")]
        public string defaultRuntimeWalkableSupportDisplayName =
            "World Walkable Support";
        [Tooltip(
            "Authored center of the server Box proxy. This is project data, " +
            "not a value inferred from a Unity Mesh or object name.")]
        public Vector3 defaultRuntimeWalkableSupportCenter =
            new(-24.8492f, 27.3f, 140.75f);
        [Tooltip(
            "Authored size of the server Box proxy. Its top surface matches " +
            "the starter scene's walkable plane.")]
        public Vector3 defaultRuntimeWalkableSupportSize =
            new(366.06f, 1f, 186.692f);
        [Tooltip(
            "Project-owned authored player ontology profile used to create and " +
            "version durable world-avatar semantics. Account appearance remains " +
            "outside world Facts.")]
        public OntologyActorProfile playerAvatarProfile;
        [Tooltip(
            "Canonical content template ID used for durable player-avatar " +
            "entities. This is authored data, never inferred from a prefab or " +
            "scene object name.")]
        public string playerAvatarTemplateId = "player_avatar";
        [Tooltip(
            "Presentation label for newly placed player-avatar entities.")]
        public string playerAvatarDisplayName = "Player Avatar";
        [Min(0.01f), Tooltip(
            "Authored maximum movement_speed used only when a newly registered " +
            "world avatar has no movement_speed Fact.")]
        public float defaultAvatarMovementSpeed = 5f;
        [Min(0.5f), Tooltip(
            "Maximum wait for the Authority's ephemeral avatar motion state " +
            "during world entry.")]
        public float runtimeMotionReadinessTimeoutSeconds = 6f;

        [Header("Behaviour")]
        [Tooltip("Disabled by default so existing local single-player editing keeps working until this asset is intentionally configured.")]
        public bool connectOnStart;
        [Min(0.25f), Tooltip("How often a connected client refreshes the durable world projection. Commands remain immediate; this is for other clients' accepted changes.")]
        public float projectionPollIntervalSeconds = 1f;

        [Header("Development content seed")]
        [Tooltip("Development-only convenience. Production worlds should be provisioned by an authenticated content workflow.")]
        public bool publishDevelopmentPackageOnWorldEntry = true;
        [Tooltip("Canonical Authority package ID used by the development seed.")]
        public string developmentPackageId = "social_village";
        public string developmentPackageVersion = "1.2.0";
        [Tooltip(
            "Canonical Rule Block definitions published before development " +
            "objects may bind them. Unity still evaluates presentation only; " +
            "the immutable definitions remain Authority content.")]
        public OntologyRuleDatabase developmentRuleDatabase;
        public OntologyAuthorityDevelopmentRule[] developmentRules =
            Array.Empty<OntologyAuthorityDevelopmentRule>();
        [Tooltip(
            "Presentation manifest used to validate action animation intents " +
            "before an immutable development package is published.")]
        public OntologyAnimationContentManifest animationContentManifest;
        public OntologyAuthorityDevelopmentAction[] developmentActions =
            Array.Empty<OntologyAuthorityDevelopmentAction>();

        [Header("Realtime notifications")]
        [Tooltip("Uses the authority SignalR endpoint only as a revision notification channel. The client still reloads the server projection; it never applies socket data as ontology facts directly.")]
        public bool useRealtimeNotifications = true;
        [Tooltip("Optional world zone subscription. Leave empty while the local world has no authored zones.")]
        public string realtimeZoneKey;
        [Min(1f), Tooltip("Slow HTTP recovery refresh used while a realtime connection is healthy. Normal polling resumes automatically if it disconnects.")]
        public float realtimeRecoveryPollIntervalSeconds = 10f;
        [Min(0.25f), Tooltip("Delay before retrying a lost realtime connection.")]
        public float realtimeReconnectDelaySeconds = 2f;
        [Min(5f), Tooltip("Interval for a connected client to renew its server-side Zone session lease. This is presence only; it never sends world facts or authoring commands.")]
        public float realtimeSessionHeartbeatSeconds = 25f;
    }

    [Serializable]
    public sealed class OntologyAuthorityDevelopmentRule
    {
        public string ruleId;
        [Min(1)] public int definitionVersion = 1;
    }

    [Serializable]
    public sealed class OntologyAuthorityDevelopmentAction
    {
        public string actionId;
        [Min(1)] public int definitionVersion = 1;
        public string predicateId;
        public bool requiresTool;
        public string objectPattern = "?target";
        [TextArea(3, 14), Tooltip(
            "Optional complete immutable action definition. When empty, the legacy single-Fact definition is generated.")]
        public string structuredDefinitionJson;
    }
}
