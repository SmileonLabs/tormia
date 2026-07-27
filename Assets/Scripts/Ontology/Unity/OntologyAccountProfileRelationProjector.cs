using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Projects the selected account character's portable profile relations into
    /// the local avatar's runtime ontology view. The projection is deliberately
    /// ephemeral: account data remains owned by the account service and no
    /// projected relation is published as a world-authored Fact.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyAccountProfileRelationProjector : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private OntologyObject actorObject;
        [SerializeField, Tooltip("Legacy fallback only. Prefer OntologyObject.EntityId.")]
        private string actorId;
        [SerializeField] private bool runSimulationAfterProjection = true;

        private readonly List<OntologyFact> projectedFacts = new();

        private string ActorId => actorObject != null && !string.IsNullOrWhiteSpace(actorObject.EntityId)
            ? actorObject.EntityId
            : actorId;

        private void Awake()
        {
            if (actorObject == null) actorObject = GetComponentInParent<OntologyObject>();
        }

        private void OnDisable() => ClearProjection();

        public void ApplyProfileRelations(IReadOnlyList<OntologyAuthorityProfileRelation> relations)
        {
            EnsureWorld();
            if (bootstrap?.World == null || string.IsNullOrWhiteSpace(ActorId)) return;

            ClearProjection(runSimulation: false);
            if (relations != null)
            {
                foreach (var relation in relations)
                {
                    if (relation == null || string.IsNullOrWhiteSpace(relation.predicateId) ||
                        string.IsNullOrWhiteSpace(relation.objectId))
                    {
                        continue;
                    }

                    var subject = ResolveSubject(relation.subjectId);
                    if (string.IsNullOrWhiteSpace(subject)) continue;
                    var fact = new OntologyFact(subject, relation.predicateId.Trim(), relation.objectId.Trim());
                    if (bootstrap.World.AddFactContribution(
                            fact.Subject,
                            fact.Predicate,
                            fact.Object,
                            OntologyFactOrigin.AccountProfile))
                    {
                        projectedFacts.Add(fact);
                    }
                }
            }

            if (runSimulationAfterProjection) bootstrap.RunSimulation();
        }

        public void ClearProjection() => ClearProjection(runSimulationAfterProjection);

        private void ClearProjection(bool runSimulation)
        {
            if (bootstrap?.World == null || projectedFacts.Count == 0) return;
            foreach (var fact in projectedFacts)
            {
                bootstrap.World.RemoveFactContribution(
                    fact.Subject,
                    fact.Predicate,
                    fact.Object,
                    OntologyFactOrigin.AccountProfile);
            }
            projectedFacts.Clear();
            if (runSimulation) bootstrap.RunSimulation();
        }

        private void EnsureWorld()
        {
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            if (bootstrap != null && (bootstrap.World == null || bootstrap.Session == null))
                bootstrap.ResetWorld(logReport: false);
        }

        private string ResolveSubject(string profileSubject)
        {
            // Account profile triples use Self as their portable subject. Player
            // and Character are accepted for backwards compatibility with the
            // original profile-review input fields.
            if (string.IsNullOrWhiteSpace(profileSubject) ||
                string.Equals(profileSubject, "Self", StringComparison.Ordinal) ||
                string.Equals(profileSubject, "Player", StringComparison.Ordinal) ||
                string.Equals(profileSubject, "Character", StringComparison.Ordinal))
            {
                return ActorId;
            }

            return profileSubject.Trim();
        }
    }
}
