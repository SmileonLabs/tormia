using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// A deliberately small reusable NPC decision adapter. It only selects from
    /// ontology-generated actions, then asks the shared action service to apply
    /// the result. It contains no mesh-name or prefab-name gameplay exceptions.
    /// A server-authoritative NPC scheduler can replace this adapter later while
    /// preserving the same actor/profile/rule data.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyNpcDecisionController : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private OntologyActorRuntimeContext actorContext;
        [SerializeField] private OntologyNpcDecisionPolicy decisionPolicy;
        [SerializeField, TextArea] private string lastDecision;

        private float nextDecisionAt;

        public string LastDecision => lastDecision;

        private void Awake()
        {
            if (actorContext == null) actorContext = GetComponent<OntologyActorRuntimeContext>();
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
        }

        private void Update()
        {
            if (decisionPolicy == null ||
                !decisionPolicy.AutoRun ||
                Time.unscaledTime < nextDecisionAt)
            {
                return;
            }

            nextDecisionAt = Time.unscaledTime +
                             Mathf.Max(0.1f, decisionPolicy.DecisionIntervalSeconds);
            DecideOnce();
        }

        public bool DecideOnce()
        {
            if (decisionPolicy == null ||
                !decisionPolicy.AllowLocalSimulation ||
                bootstrap == null ||
                actorContext == null ||
                string.IsNullOrWhiteSpace(actorContext.ActorId))
                return false;

            var candidates = bootstrap.GetActionCandidates(actorContext.ActorId);
            var selected = SelectCandidate(candidates);
            if (selected == null)
            {
                lastDecision = "No ontology action is currently available.";
                return false;
            }

            var applied = bootstrap.ExecuteActionForActor(selected.Action);
            lastDecision = applied
                ? "Applied " + selected.Action.Verb + " for " + actorContext.ActorId + "."
                : "Action became unavailable: " + selected.Action.Verb + ".";
            return applied;
        }

        private OntologyActionCandidate SelectCandidate(IReadOnlyList<OntologyActionCandidate> candidates)
        {
            if (candidates == null) return null;
            foreach (var verb in decisionPolicy?.PreferredActionVerbs ??
                                 Array.Empty<string>())
            {
                foreach (var candidate in candidates)
                {
                    if (candidate != null && string.Equals(candidate.Action.Verb.Value, verb, StringComparison.Ordinal))
                        return candidate;
                }
            }
            return null;
        }
    }
}
