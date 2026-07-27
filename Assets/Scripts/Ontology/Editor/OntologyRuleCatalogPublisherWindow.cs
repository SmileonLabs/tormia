using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Explicit editor-only publisher for immutable server rule definitions.
    /// It reuses the Bearer session created by the normal account login flow.
    /// </summary>
    public sealed class OntologyRuleCatalogPublisherWindow : EditorWindow
    {
        private const string PackageIdKey = "Tormia.RuleCatalog.PackageId";
        private const string PackageVersionKey = "Tormia.RuleCatalog.PackageVersion";

        private OntologyRuleDatabase ruleDatabase;
        private OntologyWorldAuthoritySettings authoritySettings;
        private string packageId;
        private string packageVersion;
        private string status;
        private List<PublishedRuleSummary> serverRules = new();
        private UnityWebRequest pendingRequest;
        private Action<UnityWebRequest> requestCompleted;

        [MenuItem("Tormia/Ontology/Publish Rule Catalog")]
        private static void Open()
        {
            GetWindow<OntologyRuleCatalogPublisherWindow>("Rule Catalog Publish");
        }

        private void OnEnable()
        {
            packageId = EditorPrefs.GetString(PackageIdKey, "tormia_local");
            packageVersion = EditorPrefs.GetString(PackageVersionKey, "0.1.0");
            ruleDatabase ??= FindAsset<OntologyRuleDatabase>();
            authoritySettings ??= FindAsset<OntologyWorldAuthoritySettings>();
        }

        private void OnDisable()
        {
            StopRequest();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Server Rule Catalog", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Publishes immutable Rule Database versions for the headless server evaluator. " +
                "Increase a rule's Catalog Version before changing that rule's meaning.",
                MessageType.Info);

            ruleDatabase = (OntologyRuleDatabase)EditorGUILayout.ObjectField(
                "Rule Database", ruleDatabase, typeof(OntologyRuleDatabase), false);
            authoritySettings = (OntologyWorldAuthoritySettings)EditorGUILayout.ObjectField(
                "Authority Settings", authoritySettings, typeof(OntologyWorldAuthoritySettings), false);
            packageId = EditorGUILayout.TextField("Package Id", packageId);
            packageVersion = EditorGUILayout.TextField("Package Version", packageVersion);

            using (new EditorGUI.DisabledScope(pendingRequest != null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Publish Rules", GUILayout.Height(30)))
                    {
                        BeginPublish();
                    }
                    if (GUILayout.Button("Refresh Server Status", GUILayout.Height(30)))
                    {
                        BeginRefresh();
                    }
                }
            }

            if (ruleDatabase != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Rules", EditorStyles.boldLabel);
                foreach (var rule in ruleDatabase.Definitions.Where(value => value != null))
                {
                    var version = Mathf.Max(1, rule.catalogVersion);
                    var isPublished = serverRules.Any(value =>
                        value != null && value.ruleId == rule.id && value.definitionVersion == version);
                    EditorGUILayout.LabelField(
                        rule.id,
                        "Version " + version + (isPublished ? "  • Published" : "  • Not published"));
                }
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(status, MessageType.None);
            }
        }

        private void BeginPublish()
        {
            if (ruleDatabase == null || authoritySettings == null)
            {
                status = "Assign both Rule Database and World Authority Settings.";
                return;
            }

            var definitions = ruleDatabase.Definitions.Where(value => value != null).ToList();
            var warnings = OntologyRuleValidator.Validate(definitions);
            if (warnings.Count > 0)
            {
                status = "Rule validation stopped publish:\n" + string.Join("\n", warnings);
                return;
            }

            if (!TryGetAccessToken(out _)) return;
            PublishCatalog(definitions);
        }

        private void PublishCatalog(IReadOnlyList<OntologyRuleDefinition> definitions)
        {
            var payload = new RuleCatalogPublishRequest
            {
                packageVersion = packageVersion?.Trim(),
                rules = definitions.Select(rule => new RuleDefinitionPublishRequest
                {
                    ruleId = rule.id,
                    definitionVersion = Mathf.Max(1, rule.catalogVersion),
                    payloadJson = JsonUtility.ToJson(rule)
                }).ToList()
            };
            SendJson(
                "POST",
                "/v1/content/packages/" + Uri.EscapeDataString(packageId?.Trim() ?? string.Empty) + "/rules",
                JsonUtility.ToJson(payload),
                true,
                completed =>
                {
                    if (completed.result != UnityWebRequest.Result.Success)
                    {
                        status = "Rule catalog publish failed: " + completed.downloadHandler.text;
                        return;
                    }

                    var result = JsonUtility.FromJson<RuleCatalogPublishResponse>(completed.downloadHandler.text);
                    status = result != null && result.accepted
                        ? "Published " + result.publishedCount + ", unchanged " + result.unchangedCount + "."
                        : "Rule catalog publish was rejected.";
                    if (result != null && result.accepted)
                    {
                        RefreshCatalog();
                    }
                });
            EditorPrefs.SetString(PackageIdKey, packageId ?? string.Empty);
            EditorPrefs.SetString(PackageVersionKey, packageVersion ?? string.Empty);
        }

        private void BeginRefresh()
        {
            if (authoritySettings == null)
            {
                status = "Assign World Authority Settings.";
                return;
            }

            if (!TryGetAccessToken(out _)) return;
            RefreshCatalog();
        }

        private void RefreshCatalog()
        {
            SendJson(
                "GET",
                "/v1/content/packages/" + Uri.EscapeDataString(packageId?.Trim() ?? string.Empty) + "/rules",
                null,
                true,
                completed =>
                {
                    if (completed.result != UnityWebRequest.Result.Success)
                    {
                        status = "Catalog status request failed: " + completed.downloadHandler.text;
                        return;
                    }

                    var response = JsonUtility.FromJson<RuleCatalogReadResponse>(completed.downloadHandler.text);
                    serverRules = response?.rules ?? new List<PublishedRuleSummary>();
                    status = "Server catalog contains " + serverRules.Count + " published rule version(s).";
                });
        }

        private void SendJson(string method, string route, string json, bool authenticated, Action<UnityWebRequest> completed)
        {
            StopRequest();
            var baseUrl = authoritySettings.baseUrl?.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                status = "Authority base URL is empty.";
                return;
            }

            pendingRequest = new UnityWebRequest(baseUrl + route, method)
            {
                downloadHandler = new DownloadHandlerBuffer()
            };
            if (!string.IsNullOrEmpty(json))
            {
                pendingRequest.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
                pendingRequest.SetRequestHeader("Content-Type", "application/json");
            }
            if (authenticated)
            {
                if (!TryGetAccessToken(out var token))
                {
                    pendingRequest.Dispose();
                    pendingRequest = null;
                    return;
                }
                pendingRequest.SetRequestHeader("Authorization", "Bearer " + token);
            }
            requestCompleted = completed;
            pendingRequest.SendWebRequest();
            EditorApplication.update += PollRequest;
            status = "Publishing...";
        }

        private void PollRequest()
        {
            if (pendingRequest == null || !pendingRequest.isDone) return;
            var completed = pendingRequest;
            var callback = requestCompleted;
            pendingRequest = null;
            requestCompleted = null;
            EditorApplication.update -= PollRequest;
            try { callback?.Invoke(completed); }
            finally { completed.Dispose(); Repaint(); }
        }

        private void StopRequest()
        {
            EditorApplication.update -= PollRequest;
            pendingRequest?.Abort();
            pendingRequest?.Dispose();
            pendingRequest = null;
            requestCompleted = null;
        }

        private bool TryGetAccessToken(out string token)
        {
            token = string.Empty;
            if (authoritySettings == null || string.IsNullOrWhiteSpace(authoritySettings.baseUrl))
            {
                status = "Assign World Authority Settings first.";
                return false;
            }
            token = PlayerPrefs.GetString(
                OntologyWorldAuthorityClient.SessionTokenPreferenceKey(authoritySettings.baseUrl),
                string.Empty);
            if (!string.IsNullOrWhiteSpace(token)) return true;
            status = "Sign in through the account UI before publishing or refreshing the rule catalog.";
            return false;
        }

        private static T FindAsset<T>() where T : UnityEngine.Object
        {
            var guid = AssetDatabase.FindAssets("t:" + typeof(T).Name).FirstOrDefault();
            return string.IsNullOrWhiteSpace(guid)
                ? null
                : AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
        }

        [Serializable] private sealed class RuleCatalogPublishRequest { public string packageVersion; public List<RuleDefinitionPublishRequest> rules; }
        [Serializable] private sealed class RuleDefinitionPublishRequest { public string ruleId; public int definitionVersion; public string payloadJson; }
        [Serializable] private sealed class RuleCatalogPublishResponse { public bool accepted; public int publishedCount; public int unchangedCount; }
        [Serializable] private sealed class RuleCatalogReadResponse { public List<PublishedRuleSummary> rules; }
        [Serializable] private sealed class PublishedRuleSummary { public string ruleId; public int definitionVersion; public string packageVersion; public string checksum; }
    }
}
