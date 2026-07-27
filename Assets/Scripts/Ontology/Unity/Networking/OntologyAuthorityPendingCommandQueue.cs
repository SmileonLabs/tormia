using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Durable local outbox for Authority commands whose HTTP response was lost.
    /// It stores no bearer token and never treats queued data as accepted world
    /// state. Replays keep the command ID so server idempotency remains decisive.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAuthorityPendingCommandQueue : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private string fileName = "pending-commands.json";

        private readonly List<OntologyWorldCommand> commands = new();
        private bool loaded;
        private string loadedPath;

        public int Count => commands.Count;

        public void Bind(OntologyWorldAuthorityClient client)
        {
            if (authorityClient != null)
            {
                authorityClient.CommandSending -= OnCommandSending;
                authorityClient.CommandCompleted -= OnCommandCompleted;
            }

            authorityClient = client;
            if (authorityClient != null)
            {
                authorityClient.CommandSending += OnCommandSending;
                authorityClient.CommandCompleted += OnCommandCompleted;
            }
        }

        private void OnDestroy()
        {
            if (authorityClient == null) return;
            authorityClient.CommandSending -= OnCommandSending;
            authorityClient.CommandCompleted -= OnCommandCompleted;
        }

        public IEnumerator ReplayRoutine(Action<bool> completed = null)
        {
            EnsureLoaded();
            if (authorityClient == null || !authorityClient.IsReady || commands.Count == 0)
            {
                completed?.Invoke(true);
                yield break;
            }

            var snapshot = commands.ToArray();
            foreach (var queued in snapshot)
            {
                if (queued == null ||
                    !string.Equals(queued.worldId, authorityClient.CurrentWorldId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                OntologyAuthorityCommandResult result = null;
                yield return authorityClient.SendCommandRoutine(queued, value => result = value);
                if (result == null || result.transportFailure)
                {
                    completed?.Invoke(false);
                    yield break;
                }
            }

            completed?.Invoke(true);
        }

        private void OnCommandSending(OntologyWorldCommand command)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.commandId)) return;
            EnsureLoaded();
            if (commands.Exists(value => value != null &&
                                         string.Equals(value.commandId, command.commandId, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            commands.Add(Clone(command));
            Save();
        }

        private void OnCommandCompleted(
            OntologyWorldCommand command,
            OntologyAuthorityCommandResult result)
        {
            if (command == null || result == null || result.transportFailure) return;
            EnsureLoaded();
            if (commands.RemoveAll(value => value != null &&
                                             string.Equals(value.commandId, command.commandId, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                Save();
            }
        }

        private void EnsureLoaded()
        {
            var path = QueuePath;
            if (loaded && string.Equals(loadedPath, path, StringComparison.OrdinalIgnoreCase)) return;
            loaded = true;
            loadedPath = path;
            commands.Clear();
            if (!File.Exists(path)) return;
            try
            {
                var data = JsonUtility.FromJson<PendingCommandData>(File.ReadAllText(path));
                if (data?.commands != null) commands.AddRange(data.commands);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[OntologyAuthorityPendingCommandQueue] Ignored unreadable outbox: " + exception.Message, this);
            }
        }

        private void Save()
        {
            var path = QueuePath;
            OntologyAtomicFileStore.WriteAllText(
                path,
                JsonUtility.ToJson(new PendingCommandData
                {
                    commands = commands.ToArray()
                }, true),
                keepBackup: false);
        }

        private string QueuePath => Path.Combine(
            Application.persistentDataPath,
            "Saves",
            "account-" + Scope(authorityClient?.CurrentUserId, "local"),
            "world-" + Scope(authorityClient?.CurrentWorldId, "default"),
            fileName);

        private static OntologyWorldCommand Clone(OntologyWorldCommand source) =>
            new()
            {
                contractVersion = source.contractVersion,
                commandId = source.commandId,
                worldId = source.worldId,
                actorUserId = source.actorUserId,
                expectedRevision = source.expectedRevision,
                commandType = source.commandType,
                payloadJson = source.payloadJson
            };

        private static string Scope(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            foreach (var invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value.Trim();
        }

        [Serializable]
        private sealed class PendingCommandData
        {
            public OntologyWorldCommand[] commands = Array.Empty<OntologyWorldCommand>();
        }
    }
}
