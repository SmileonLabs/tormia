using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Explicit scene-to-authority publisher for Zone boundaries. This is separate
    /// from the streamer: a volume is only a local authoring proposal until this
    /// component receives an accepted server revision.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyWorldZonePublisher : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField, TextArea] private string lastStatus;
        private bool isPublishing;

        public string LastStatus => lastStatus;

        private void Awake()
        {
            ResolveDependencies();
        }

        [ContextMenu("Publish Scene Zone Volumes to Authority")]
        public void PublishSceneZones()
        {
            if (isPublishing) return;
            ResolveDependencies();
            if (authorityClient == null)
            {
                SetStatus("No world authority client is available.");
                return;
            }
            StartCoroutine(PublishRoutine());
        }

        public IEnumerator PublishRoutine()
        {
            isPublishing = true;
            if (!authorityClient.IsReady)
            {
                var connected = false;
                yield return authorityClient.ConnectRoutine(value => connected = value);
                if (!connected)
                {
                    SetStatus("Zone publish stopped: authority connection failed.");
                    isPublishing = false;
                    yield break;
                }
            }

            var volumes = FindObjectsByType<OntologyWorldZoneVolume>(FindObjectsInactive.Exclude)
                .OrderBy(value => value.ZoneKey, System.StringComparer.Ordinal).ToArray();
            if (volumes.Length == 0)
            {
                SetStatus("No Zone Volumes were found in this scene.");
                isPublishing = false;
                yield break;
            }

            var published = 0;
            foreach (var volume in volumes)
            {
                if (!volume.TryBuildDefinition(out var definition, out var validationError))
                {
                    SetStatus("Zone publish stopped for '" + volume.name + "': " + validationError);
                    isPublishing = false;
                    yield break;
                }

                var command = OntologyWorldAuthorityClient.CreateCommand(
                    OntologyWorldCommandKinds.DefineZone,
                    OntologyWorldAuthorityClient.CreateDefineZonePayload(
                        definition.ZoneKey,
                        definition.MinX,
                        definition.MinZ,
                        definition.MaxX,
                        definition.MaxZ,
                        definition.SimulationMode));
                OntologyAuthorityCommandResult result = null;
                yield return authorityClient.SendCommandRoutine(command, value => result = value);
                if (result == null || !result.accepted)
                {
                    SetStatus("Zone publish stopped for '" + definition.ZoneKey + "': " +
                              (result?.rejectionCode ?? "authority_no_response"));
                    isPublishing = false;
                    yield break;
                }

                published++;
            }

            var streamer = FindAnyObjectByType<OntologyWorldZoneStreamer>();
            streamer?.RefreshZoneDirectory();
            SetStatus("Authority accepted " + published + " Zone definition(s).");
            isPublishing = false;
        }

        private void ResolveDependencies()
        {
            if (authorityClient == null)
            {
                authorityClient = GetComponent<OntologyWorldAuthorityClient>() ??
                                  FindAnyObjectByType<OntologyWorldAuthorityClient>();
            }
        }

        private void SetStatus(string value)
        {
            lastStatus = value ?? string.Empty;
            Debug.Log("[WorldZonePublisher] " + lastStatus, this);
        }
    }
}
