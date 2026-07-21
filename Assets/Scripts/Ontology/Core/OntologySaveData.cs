using System;
using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    [Serializable]
    public sealed class OntologySaveData
    {
        // Version 7 persists per-instance semantic contribution ownership, so a
        // preset can be removed without erasing independently authored triples.
        public int version = 7;
        public List<OntologyFactRecord> facts = new();
        public List<OntologyActionRecord> actionHistory = new();
        public List<OntologyEventRecord> eventHistory = new();
        public List<OntologyPlacedObjectRecord> placedObjects = new();
        public List<string> controlledRuleIds = new();
    }

    [Serializable]
    public sealed class OntologyPlacedObjectRecord
    {
        public string instanceName;
        public string definitionId;
        public OntologyTransformRecord transform = new();
        public List<string> concepts = new();
        public List<OntologyFactRecord> facts = new();
        public List<OntologyRuleBlockRecord> ruleBlocks = new();
        public List<OntologySemanticContributionRecord> semanticContributions = new();
    }

    [Serializable]
    public sealed class OntologyRuleBlockRecord
    {
        public string ruleId;
        public string bindingVariable = "?target";
    }

    [Serializable]
    public sealed class OntologySemanticContributionRecord
    {
        public string ownerId;
        public List<string> concepts = new();
        public List<OntologyFactRecord> facts = new();
        public List<OntologyRuleBlockRecord> ruleBlocks = new();
    }

    [Serializable]
    public sealed class OntologyTransformRecord
    {
        public float positionX;
        public float positionY;
        public float positionZ;
        public float rotationX;
        public float rotationY;
        public float rotationZ;
        public float rotationW = 1f;
        public float scaleX = 1f;
        public float scaleY = 1f;
        public float scaleZ = 1f;
    }

    [Serializable]
    public sealed class OntologyFactRecord
    {
        public string subject;
        public string predicate;
        public string obj;
    }

    [Serializable]
    public sealed class OntologyActionRecord
    {
        public string actor;
        public string verb;
        public string target;
        public string tool;
    }

    [Serializable]
    public sealed class OntologyEventRecord
    {
        public string eventType;
        public string reason;
    }

    public static class OntologySaveDataConverter
    {
        public static OntologySaveData Capture(
            OntologyWorldState world,
            OntologySession session,
            IReadOnlyList<OntologyRuleDefinition> ruleDefinitions = null)
        {
            var saveData = new OntologySaveData();
            if (world != null)
            {
                foreach (var fact in world.Facts)
                {
                    if (OntologyDerivedFactPolicy.IsDerived(
                            fact,
                            ruleDefinitions))
                    {
                        continue;
                    }

                    saveData.facts.Add(new OntologyFactRecord
                    {
                        subject = fact.Subject.Value,
                        predicate = fact.Predicate.Value,
                        obj = fact.Object.Value
                    });
                }
            }

            if (session != null)
            {
                foreach (var action in session.ActionHistory)
                {
                    saveData.actionHistory.Add(new OntologyActionRecord
                    {
                        actor = action.ActorId.Value,
                        verb = action.Verb.Value,
                        target = action.TargetId.Value,
                        tool = action.ToolId.Value
                    });
                }

                foreach (var ontologyEvent in session.EventHistory)
                {
                    saveData.eventHistory.Add(new OntologyEventRecord
                    {
                        eventType = ontologyEvent.EventType,
                        reason = ontologyEvent.Reason
                    });
                }
            }

            return saveData;
        }

        public static OntologyWorldState RestoreWorld(
            OntologySaveData saveData,
            IReadOnlyList<OntologyRuleDefinition> ruleDefinitions = null)
        {
            var world = new OntologyWorldState();
            if (saveData == null)
            {
                return world;
            }

            foreach (var record in saveData.facts)
            {
                if (record == null)
                {
                    continue;
                }

                var fact = new OntologyFact(
                    record.subject,
                    record.predicate,
                    record.obj);
                if (OntologyDerivedFactPolicy.IsDerived(
                        fact,
                        ruleDefinitions))
                {
                    continue;
                }

                if (record.predicate == "has_concept")
                {
                    world.AddConcept(record.subject, record.obj);
                }
                else
                {
                    world.GetOrCreateEntity(record.subject);
                    world.GetOrCreateEntity(record.obj);
                    world.AddFact(record.subject, record.predicate, record.obj);
                }
            }

            return world;
        }

        public static OntologySession RestoreSession(OntologySaveData saveData)
        {
            var session = new OntologySession();
            if (saveData == null)
            {
                return session;
            }

            foreach (var record in saveData.actionHistory)
            {
                if (record != null)
                {
                    session.RecordAction(new OntologyAction(record.actor, record.verb, record.target, record.tool));
                }
            }

            foreach (var record in saveData.eventHistory)
            {
                if (record != null)
                {
                    session.RecordEvent(new OntologyEvent(record.eventType, record.reason));
                }
            }

            return session;
        }
    }
}
