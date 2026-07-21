using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Non-secret connection data for a development or production authority endpoint.
    /// Authentication tokens deliberately do not live here; the production identity
    /// provider supplies them at runtime.
    /// </summary>
    [CreateAssetMenu(
        fileName = "WorldAuthoritySettings",
        menuName = "Tormia/Ontology/World Authority Settings")]
    public sealed class OntologyWorldAuthoritySettings : ScriptableObject
    {
        [Header("Endpoint")]
        [Tooltip("Authority endpoint only. Unity clients never receive a PostgreSQL or Redis connection string.")]
        public string baseUrl = "http://127.0.0.1:5272";

        [Header("Local development identity")]
        [Tooltip("Stable local development subject. Give each local client a different value when testing two players.")]
        public string developmentSubject = "local-editor-1";
        public string developmentDisplayName = "Local Editor 1";

        [Header("Development world")]
        [Tooltip("Paste a shared world GUID here to join an existing development world. Leave empty to create and remember a personal local world.")]
        public string sharedWorldId;
        public string newWorldSlug = "local-sandbox";
        public string newWorldTitle = "Local Sandbox";
        public string newWorldVisibility = "private";

        [Header("Behaviour")]
        [Tooltip("Disabled by default so existing local single-player editing keeps working until this asset is intentionally configured.")]
        public bool connectOnStart;
        [Min(0.25f), Tooltip("How often a connected client refreshes the durable world projection. Commands remain immediate; this is for other clients' accepted changes.")]
        public float projectionPollIntervalSeconds = 1f;

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
}
