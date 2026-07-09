using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyActorFactToastEmitter : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private OntologyActorToast actorToast;
        [SerializeField] private OntologyUILabels labels;
        [SerializeField] private OntologyCharacterPartDatabase partDatabase;
        [SerializeField] private string actorId = "Player";
        [SerializeField] private float initialBaselineDelay = 0.5f;
        [SerializeField] private float pollInterval = 0.1f;
        [SerializeField] private string[] predicateWhitelist =
        {
            "standing_on",
            "status",
            "movement_state",
            "exposed_to",
            "has_capability",
            "equipped_part",
            "animation_intent"
        };

        private readonly HashSet<string> knownFacts = new();
        private readonly HashSet<string> currentFacts = new();
        private readonly List<OntologyFact> addedFacts = new();
        private readonly List<string> removedFactKeys = new();
        private readonly Dictionary<string, float> lastShownAt = new();
        private float nextPollTime;
        private float baselineReadyTime;
        private bool initialized;

        private void Awake()
        {
            EnsureReferences();
        }

        private void Start()
        {
            EnsureReferences();
            baselineReadyTime = Time.time + Mathf.Max(0f, initialBaselineDelay);
        }

        private void Update()
        {
            if (Time.time < nextPollTime)
            {
                return;
            }

            nextPollTime = Time.time + Mathf.Max(0.01f, pollInterval);
            PollFacts();
        }

        public void Configure(OntologyWorldBootstrap targetBootstrap, OntologyActorToast targetToast, OntologyUILabels targetLabels, string targetActorId)
        {
            bootstrap = targetBootstrap;
            actorToast = targetToast;
            labels = targetLabels;
            actorId = targetActorId;
            CaptureBaseline();
        }

        public void ResetBaseline()
        {
            baselineReadyTime = Time.time + Mathf.Max(0f, initialBaselineDelay);
            initialized = false;
            knownFacts.Clear();
            currentFacts.Clear();
            addedFacts.Clear();
            removedFactKeys.Clear();
        }

        private void PollFacts()
        {
            EnsureReferences();
            if (!EnsureWorldReady())
            {
                return;
            }

            if (!initialized)
            {
                if (Time.time < baselineReadyTime)
                {
                    return;
                }

                CaptureBaseline();
                return;
            }

            currentFacts.Clear();
            addedFacts.Clear();
            removedFactKeys.Clear();
            foreach (var fact in bootstrap.World.Facts)
            {
                if (fact.Subject.ToString() != actorId || !IsWhitelisted(fact.Predicate.ToString()))
                {
                    continue;
                }

                var key = MakeKey(fact);
                currentFacts.Add(key);
                if (!knownFacts.Add(key))
                {
                    continue;
                }

                addedFacts.Add(fact);
            }

            foreach (var knownFact in knownFacts)
            {
                if (!currentFacts.Contains(knownFact))
                {
                    removedFactKeys.Add(knownFact);
                }
            }

            addedFacts.Sort((left, right) => GetPriority(left.Predicate.ToString()).CompareTo(GetPriority(right.Predicate.ToString())));
            removedFactKeys.Sort((left, right) => GetPriority(GetPredicateFromKey(left)).CompareTo(GetPriority(GetPredicateFromKey(right))));

            foreach (var fact in addedFacts)
            {
                ShowFact(fact, removed: false);
            }

            foreach (var removedKey in removedFactKeys)
            {
                ShowRemovedFact(removedKey);
                knownFacts.Remove(removedKey);
            }
        }

        private void CaptureBaseline()
        {
            EnsureReferences();
            knownFacts.Clear();
            if (!EnsureWorldReady())
            {
                initialized = false;
                return;
            }

            foreach (var fact in bootstrap.World.Facts)
            {
                if (fact.Subject.ToString() == actorId && IsWhitelisted(fact.Predicate.ToString()))
                {
                    knownFacts.Add(MakeKey(fact));
                }
            }

            initialized = true;
        }

        private void ShowFact(OntologyFact fact, bool removed)
        {
            if (actorToast == null)
            {
                return;
            }

            var predicate = fact.Predicate.ToString();
            var rawObj = fact.Object.ToString();
            var obj = FormatObject(predicate, rawObj);
            var cooldownKey = (removed ? "removed|" : "added|") + predicate + "|" + rawObj;
            if (lastShownAt.TryGetValue(cooldownKey, out var lastTime) && Time.time - lastTime < 0.5f)
            {
                return;
            }

            lastShownAt[cooldownKey] = Time.time;
            var format = removed ? Labels.actorToastRemovedFactFormat : Labels.actorToastFactFormat;
            actorToast.Show(string.Format(format, GetPredicateLabel(predicate), obj), GetSeverity(predicate, removed));
        }

        private void ShowRemovedFact(string key)
        {
            var predicate = GetPredicateFromKey(key);
            var obj = GetObjectFromKey(key);
            ShowFact(new OntologyFact(actorId, predicate, obj), removed: true);
        }

        private int GetPriority(string predicate)
        {
            switch (predicate)
            {
                case "status":
                case "movement_state":
                case "exposed_to":
                    return 0;
                case "has_capability":
                    return 1;
                case "equipped_part":
                    return 2;
                case "animation_intent":
                    return 3;
                case "standing_on":
                    return 4;
                default:
                    return 10;
            }
        }

        private OntologyActorToast.Severity GetSeverity(string predicate, bool removed)
        {
            if (removed)
            {
                return OntologyActorToast.Severity.Negative;
            }

            switch (predicate)
            {
                case "has_capability":
                case "equipped_part":
                    return OntologyActorToast.Severity.Positive;
                case "status":
                case "movement_state":
                case "exposed_to":
                    return OntologyActorToast.Severity.Warning;
                default:
                    return OntologyActorToast.Severity.Info;
            }
        }

        private string GetPredicateFromKey(string key)
        {
            var parts = key.Split('|');
            return parts.Length > 1 ? parts[1] : string.Empty;
        }

        private string GetObjectFromKey(string key)
        {
            var parts = key.Split('|');
            return parts.Length > 2 ? parts[2] : string.Empty;
        }

        private string GetPredicateLabel(string predicate)
        {
            switch (predicate)
            {
                case "standing_on":
                    return Labels.actorToastStandingOn;
                case "status":
                    return Labels.actorToastStatus;
                case "movement_state":
                    return Labels.actorToastMovementState;
                case "exposed_to":
                    return Labels.actorToastExposure;
                case "has_capability":
                    return Labels.actorToastCapability;
                case "equipped_part":
                    return Labels.actorToastEquippedPart;
                case "animation_intent":
                    return Labels.actorToastAnimationIntent;
                default:
                    return predicate;
            }
        }

        private string FormatObject(string predicate, string obj)
        {
            if (predicate != "equipped_part" || partDatabase == null || partDatabase.Definitions == null)
            {
                return obj;
            }

            foreach (var definition in partDatabase.Definitions)
            {
                if (definition == null || definition.partId != obj)
                {
                    continue;
                }

                return string.IsNullOrWhiteSpace(definition.displayName) ? definition.partId : definition.displayName;
            }

            return obj;
        }

        private bool IsWhitelisted(string predicate)
        {
            if (predicateWhitelist == null || predicateWhitelist.Length == 0)
            {
                return true;
            }

            for (var i = 0; i < predicateWhitelist.Length; i++)
            {
                if (predicateWhitelist[i] == predicate)
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsureReferences()
        {
            if (bootstrap == null)
            {
                bootstrap = FindFirstObjectByType<OntologyWorldBootstrap>();
            }

            if (actorToast == null)
            {
                actorToast = GetComponent<OntologyActorToast>();
            }

            if (partDatabase == null)
            {
                var partAdapter = GetComponent<OntologyCharacterPartAdapter>();
                if (partAdapter != null)
                {
                    partDatabase = partAdapter.PartDatabase;
                }
            }
        }

        private bool EnsureWorldReady()
        {
            if (bootstrap == null)
            {
                return false;
            }

            if (bootstrap.World == null || bootstrap.Session == null)
            {
                bootstrap.ResetWorld(logReport: false);
            }

            return bootstrap.World != null;
        }

        private string MakeKey(OntologyFact fact)
        {
            return fact.Subject + "|" + fact.Predicate + "|" + fact.Object;
        }

        private OntologyUILabels Labels => labels != null ? labels : ScriptableObject.CreateInstance<OntologyUILabels>();
    }
}
