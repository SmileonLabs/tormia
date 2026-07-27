using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyWorldBootstrap : MonoBehaviour
    {
        [SerializeField] private bool runOnStart;
        [SerializeField] private int maxIterations = 8;

        [Header("Diagnostics")]
        [Tooltip("Writes full reports only for an explicitly requested diagnostic run.")]
        [SerializeField] private bool logExplicitSimulationReports;

        [Header("Sample Action")]
        [SerializeField] private OntologyObject actorObject;
        [SerializeField, Tooltip("Legacy fallback for the sample action only. Assign an OntologyObject for a data-owned actor.")]
        private string actorId = "Player";
        [SerializeField] private string actionTargetId = "Tree";
        [SerializeField] private string actionToolId = "FireSword";

        [Header("Quest Data")]
        [SerializeField] private OntologyQuestDatabase questDatabase;

        [Header("Rule Data")]
        [SerializeField] private OntologyRuleDatabase ruleDatabase;
        [SerializeField] private OntologyRuleBlockRegistry ruleBlockRegistry;
        [SerializeField] private OntologyRuleBlockPresetDatabase ruleBlockPresetDatabase;

        [Header("Concept Policy")]
        [SerializeField] private OntologyConceptPolicyDatabase conceptPolicyDatabase;

        [Header("Physical Profiles")]
        [SerializeField] private OntologyPhysicalProfileDatabase physicalProfileDatabase;
        [SerializeField] private OntologyPhysicalEffectDatabase physicalEffectDatabase;
        [SerializeField] private OntologyAttachmentProfileDatabase attachmentProfileDatabase;

        [Header("Action Data")]
        [SerializeField] private OntologyActionCandidateDatabase actionCandidateDatabase;
        [SerializeField] private OntologyActionEffectDatabase actionEffectDatabase;

        [Header("Optional Feature Packs")]
        [Tooltip("Opt-in genre/content pack catalog. Presence in the catalog never activates a pack.")]
        [SerializeField] private OntologyFeaturePackDatabase featurePackDatabase;

        private readonly OntologyReportService reportService = new();
        private readonly OntologyEntityRegistry entityRegistry = new();
        private readonly Dictionary<string, HashSet<OntologyFact>> authoredFactsByEntity = new();
        private OntologyWorldService worldService;
        private bool simulationRequested;

        public OntologyWorldState World => worldService?.World;
        public OntologySession Session => worldService?.Session;
        public OntologySimulationResult LastResult => worldService?.LastResult;
        public OntologyRuleDatabase RuleDatabase => ruleDatabase;
        public OntologyRuleBlockPresetDatabase RuleBlockPresetDatabase =>
            ruleBlockPresetDatabase;
        public IReadOnlyList<OntologyRuleDefinition> AllRuleDefinitions =>
            ruleDatabase != null
                ? ruleDatabase.Definitions
                : Array.Empty<OntologyRuleDefinition>();
        public OntologyRuleBlockRegistry RuleBlockRegistry => EnsureRuleBlockRegistry();
        public OntologyConceptPolicyDatabase ConceptPolicyDatabase => conceptPolicyDatabase;
        public OntologyPhysicalProfileDatabase PhysicalProfileDatabase => physicalProfileDatabase;
        public OntologyPhysicalEffectDatabase PhysicalEffectDatabase =>
            physicalEffectDatabase;
        public OntologyAttachmentProfileDatabase AttachmentProfileDatabase =>
            attachmentProfileDatabase;
        public OntologyFeaturePackDatabase FeaturePackDatabase => featurePackDatabase;
        public OntologyEntityRegistry EntityRegistry => entityRegistry;
        /// <summary>
        /// Raised only after the runtime world instance is replaced by reset or restore.
        /// Runtime observation adapters use this to discard their publication ownership
        /// and republish facts into the new world on their next observation pass.
        /// </summary>
        public event Action WorldRebuilt;
        /// <summary>
        /// Raised after any world data or inference change. Presentation adapters use this
        /// to refresh visuals; it does not imply that the world instance was replaced.
        /// </summary>
        public event Action WorldChanged;

        private string ActiveActorId => actorObject != null &&
                                        !string.IsNullOrWhiteSpace(actorObject.EntityId)
            ? actorObject.EntityId
            : actorId;

        private void Start()
        {
            ResetWorld(logReport: false);
            if (runOnStart)
            {
                RunSimulation(buildReport: false);
            }
        }

        private void LateUpdate()
        {
            if (simulationRequested)
            {
                RunSimulation(buildReport: false);
            }
        }

        public string ResetWorld(bool logReport = true)
        {
            simulationRequested = false;
            EnsureWorldService();
            worldService.Reset(BuildWorldFromScene());
            var report = BuildReport();
            if (logReport)
            {
                Debug.Log(report);
            }

            WorldRebuilt?.Invoke();
            WorldChanged?.Invoke();
            return report;
        }

        public string RestoreSnapshot(OntologySaveData saveData, bool logReport = true)
        {
            EnsureWorldService();
            worldService.Restore(saveData, AllRuleDefinitions);
            worldService.Simulate(
                GetRuleDefinitions(),
                CreateQuestService(),
                ActiveActorId,
                maxIterations,
                AllRuleDefinitions);
            var report = BuildReport();
            if (logReport)
            {
                Debug.Log(report);
            }

            WorldRebuilt?.Invoke();
            WorldChanged?.Invoke();
            return report;
        }

        /// <summary>
        /// Runs ontology inference. Runtime sensors do not construct diagnostic text;
        /// full reports are requested only by explicit editor/debug actions.
        /// </summary>
        public string RunSimulation(bool buildReport = true)
        {
            simulationRequested = false;
            EnsureWorldService();
            if (World == null)
            {
                worldService.Reset(BuildWorldFromScene());
                WorldRebuilt?.Invoke();
            }

            worldService.Simulate(
                GetRuleDefinitions(),
                CreateQuestService(),
                ActiveActorId,
                maxIterations,
                AllRuleDefinitions);
            var report = buildReport ? BuildReport() : string.Empty;
            if (buildReport && logExplicitSimulationReports)
            {
                Debug.Log(report);
            }
            WorldChanged?.Invoke();
            return report;
        }

        /// <summary>
        /// Queues one inference pass after all observation adapters have published their
        /// facts for the current frame. Multiple sensor changes are intentionally
        /// coalesced so inference never depends on MonoBehaviour execution order.
        /// Commands and editor operations may still call RunSimulation for an immediate
        /// explicit transaction.
        /// </summary>
        public void RequestSimulation()
        {
            if (!Application.isPlaying)
            {
                RunSimulation(buildReport: false);
                return;
            }

            simulationRequested = true;
        }

        /// <summary>
        /// Injects one new or edited scene entity without rebuilding the runtime world.
        /// Persistent gameplay state such as equipment slots therefore remains intact.
        /// </summary>
        public string RegisterSceneObject(
            OntologyObject ontologyObject,
            bool runSimulation = true)
        {
            if (ontologyObject == null)
            {
                return BuildReport();
            }

            EnsureWorldReady();
            if (!entityRegistry.Register(ontologyObject))
            {
                Debug.LogError(
                    $"Cannot register ontology entity '{ontologyObject.EntityId}': " +
                    "another live Unity object already owns that id.",
                    ontologyObject);
                return BuildReport();
            }
            RemoveAuthoredFacts(ontologyObject.EntityId);
            foreach (var contributor in entityRegistry.GetContributors(
                         ontologyObject.EntityId))
            {
                ApplySceneObjectToWorld(contributor, World);
                RecordAuthoredFacts(contributor);
            }
            physicalProfileDatabase?.ApplyTo(World);
            physicalEffectDatabase?.ApplyTo(World);
            attachmentProfileDatabase?.ApplyTo(World);

            if (runSimulation)
            {
                return RunSimulation();
            }

            WorldChanged?.Invoke();
            return BuildReport();
        }

        /// <summary>
        /// Re-syncs authored scene triples while preserving runtime state for entities
        /// that still exist. Removed scene entities have all of their relations removed.
        /// </summary>
        public string SynchronizeSceneObjects(bool runSimulation = true)
        {
            EnsureWorldReady();
            var objects = FindObjectsByType<OntologyObject>(FindObjectsInactive.Include);
            var currentIds = new HashSet<string>();
            foreach (var ontologyObject in objects)
            {
                if (ontologyObject != null)
                {
                    currentIds.Add(ontologyObject.EntityId);
                }
            }

            var removedEntityIds = new List<string>();
            foreach (var entityId in authoredFactsByEntity.Keys)
            {
                if (!currentIds.Contains(entityId))
                {
                    removedEntityIds.Add(entityId);
                }
            }

            foreach (var authoredFacts in authoredFactsByEntity.Values)
            {
                foreach (var fact in authoredFacts)
                {
                    World.RemoveFact(fact.Subject, fact.Predicate, fact.Object);
                }
            }

            if (removedEntityIds.Count > 0)
            {
                var currentFacts = new List<OntologyFact>(World.Facts);
                foreach (var fact in currentFacts)
                {
                    if (removedEntityIds.Contains(fact.Subject.Value) ||
                        removedEntityIds.Contains(fact.Object.Value))
                    {
                        World.RemoveFact(fact.Subject, fact.Predicate, fact.Object);
                    }
                }
            }

            entityRegistry.Rebuild(objects);
            ReportDuplicateEntityIds();
            authoredFactsByEntity.Clear();
            foreach (var ontologyObject in objects)
            {
                ApplySceneObjectToWorld(ontologyObject, World);
                RecordAuthoredFacts(ontologyObject);
            }

            physicalProfileDatabase?.ApplyTo(World);
            physicalEffectDatabase?.ApplyTo(World);
            attachmentProfileDatabase?.ApplyTo(World);
            if (runSimulation)
            {
                return RunSimulation();
            }

            WorldChanged?.Invoke();
            return BuildReport();
        }

        public string AttackTargetWithTool()
        {
            return ExecuteAction(new OntologyAction(ActiveActorId, "attack", actionTargetId, actionToolId));
        }

        public string ExecuteAction(OntologyAction action)
        {
            if (!IsActionCurrentlyAvailable(action))
            {
                var report = BuildUnavailableActionReport(action);
                Debug.LogWarning(report);
                return report;
            }

            ApplyAction(action);
            return RunSimulation();
        }

        public bool IsActionCurrentlyAvailable(OntologyAction action)
        {
            EnsureWorldReady();
            return CreateActionService().IsAvailable(World, action.ActorId, action);
        }

        public List<OntologyActionCandidate> GetActionCandidates()
        {
            return GetActionCandidates(ActiveActorId);
        }

        /// <summary>Returns candidates for any registered actor, including NPCs.</summary>
        public List<OntologyActionCandidate> GetActionCandidates(string actorId)
        {
            EnsureWorldReady();
            var quests = GetGeneratedQuests(actorId);
            return CreateActionService().GetCandidates(World, actorId, quests);
        }

        /// <summary>
        /// Returns the quests inferred from the current world state for the
        /// active actor. This is a read-only presentation query: it does not
        /// author Facts, create a quest log, or mutate the session.
        /// </summary>
        public List<OntologyQuest> GetGeneratedQuests()
        {
            return GetGeneratedQuests(ActiveActorId);
        }

        /// <summary>Returns read-only inferred quests for the specified actor.</summary>
        public List<OntologyQuest> GetGeneratedQuests(string actorId)
        {
            EnsureWorldReady();
            return CreateQuestService().Generate(World, actorId);
        }

        /// <summary>
        /// Applies an already validated action for any actor and refreshes
        /// inference using that actor's context. Presentation callers should
        /// use this instead of treating the local player as the sole actor.
        /// </summary>
        public bool ExecuteActionForActor(OntologyAction action)
        {
            if (!IsActionCurrentlyAvailable(action)) return false;
            if (!ApplyAction(action)) return false;
            RunSimulationForActor(action.ActorId);
            return true;
        }

        public void RunSimulationForActor(string actorId)
        {
            EnsureWorldReady();
            if (string.IsNullOrWhiteSpace(actorId)) return;
            worldService.Simulate(
                GetRuleDefinitions(),
                CreateQuestService(),
                actorId,
                maxIterations,
                AllRuleDefinitions);
            WorldChanged?.Invoke();
        }

        public bool ApplyAction(OntologyAction action)
        {
            EnsureWorldReady();
            return CreateActionService().Apply(World, Session, action);
        }

        private OntologyWorldState BuildWorldFromScene()
        {
            var state = new OntologyWorldState();
            var objects = FindObjectsByType<OntologyObject>(FindObjectsInactive.Include);
            entityRegistry.Rebuild(objects);
            ReportDuplicateEntityIds();
            authoredFactsByEntity.Clear();
            foreach (var ontologyObject in objects)
            {
                ApplySceneObjectToWorld(ontologyObject, state);
                RecordAuthoredFacts(ontologyObject);
            }

            physicalProfileDatabase?.ApplyTo(state);
            physicalEffectDatabase?.ApplyTo(state);
            attachmentProfileDatabase?.ApplyTo(state);

            return state;
        }

        private void ReportDuplicateEntityIds()
        {
            foreach (var duplicateId in entityRegistry.DuplicateEntityIds)
            {
                Debug.LogError(
                    $"Duplicate ontology entity id '{duplicateId}' exists in the scene. " +
                    "Each live object must own a unique id.");
            }
        }

        private static void ApplySceneObjectToWorld(
            OntologyObject ontologyObject,
            OntologyWorldState state)
        {
            if (ontologyObject == null || state == null)
            {
                return;
            }

            ontologyObject.ApplyTo(state);
            var assignment =
                ontologyObject.GetComponent<OntologyRuleBlockAssignment>();
            assignment?.ApplyTo(state, ontologyObject.EntityId);
        }

        private static HashSet<OntologyFact> CaptureAuthoredFacts(
            OntologyObject ontologyObject)
        {
            var temporary = new OntologyWorldState();
            ApplySceneObjectToWorld(ontologyObject, temporary);
            return new HashSet<OntologyFact>(temporary.Facts);
        }

        private void RecordAuthoredFacts(OntologyObject ontologyObject)
        {
            if (ontologyObject == null ||
                string.IsNullOrWhiteSpace(ontologyObject.EntityId))
            {
                return;
            }

            if (!authoredFactsByEntity.TryGetValue(
                    ontologyObject.EntityId,
                    out var aggregate))
            {
                aggregate = new HashSet<OntologyFact>();
                authoredFactsByEntity.Add(ontologyObject.EntityId, aggregate);
            }

            aggregate.UnionWith(CaptureAuthoredFacts(ontologyObject));
        }

        private void RemoveAuthoredFacts(string entityId)
        {
            if (World == null || string.IsNullOrWhiteSpace(entityId) ||
                !authoredFactsByEntity.TryGetValue(entityId, out var authoredFacts))
            {
                return;
            }

            foreach (var fact in authoredFacts)
            {
                World.RemoveFact(fact.Subject, fact.Predicate, fact.Object);
            }
        }

        private IReadOnlyList<OntologyRuleDefinition> GetRuleDefinitions()
        {
            if (ruleDatabase == null || ruleDatabase.Definitions.Count == 0)
                return Array.Empty<OntologyRuleDefinition>();

            return OntologyRuleBlockResolver.Resolve(
                ruleDatabase.Definitions,
                EnsureRuleBlockRegistry(),
                FindObjectsByType<OntologyRuleBlockAssignment>(FindObjectsInactive.Exclude));
        }

        private OntologyRuleBlockRegistry EnsureRuleBlockRegistry()
        {
            if (ruleBlockRegistry == null)
                ruleBlockRegistry = GetComponent<OntologyRuleBlockRegistry>();
            if (ruleBlockRegistry == null)
                ruleBlockRegistry = gameObject.AddComponent<OntologyRuleBlockRegistry>();
            return ruleBlockRegistry;
        }

        private IReadOnlyList<OntologyQuestDefinition> GetQuestDefinitions()
        {
            return questDatabase != null && questDatabase.Definitions.Count > 0
                ? questDatabase.Definitions
                : Array.Empty<OntologyQuestDefinition>();
        }

        private OntologyActionService CreateActionService()
        {
            return new OntologyActionService(GetActionCandidateDefinitions(), GetActionEffectDefinitions());
        }

        private OntologyQuestService CreateQuestService()
        {
            return new OntologyQuestService(GetQuestDefinitions());
        }

        private void EnsureWorldService()
        {
            worldService ??= new OntologyWorldService();
        }

        private void EnsureWorldReady()
        {
            EnsureWorldService();
            if (World == null || Session == null)
            {
                worldService.Reset(BuildWorldFromScene());
            }
        }

        private string BuildReport()
        {
            var quests = World != null ? CreateQuestService().Generate(World, ActiveActorId) : null;
            var candidates = World != null ? CreateActionService().GetCandidates(World, ActiveActorId, quests) : null;
            return reportService.Build(World, Session, LastResult, quests, candidates, DumpDatabaseStatus());
        }

        private string BuildUnavailableActionReport(OntologyAction action)
        {
            var builder = new StringBuilder();
            builder.AppendLine("[OntologyWorldBootstrap]");
            builder.Append("Action is not currently available: ");
            builder.AppendLine(action.ToString());
            builder.AppendLine();
            builder.Append(BuildReport());
            return builder.ToString().TrimEnd();
        }

        private string DumpDatabaseStatus()
        {
            var rules = GetRuleDefinitions();
            var questDefinitions = GetQuestDefinitions();
            var builder = new StringBuilder();
            builder.AppendLine("[Databases]");
            builder.Append("RuleDatabase: ");
            builder.Append(ruleDatabase != null && ruleDatabase.Definitions.Count > 0 ? "asset" : "missing");
            builder.Append(" (");
            builder.Append(rules.Count);
            builder.AppendLine(" rule(s))");
            builder.Append("QuestDatabase: ");
            builder.Append(questDatabase != null && questDatabase.Definitions.Count > 0 ? "asset" : "missing");
            builder.Append(" (");
            builder.Append(questDefinitions.Count);
            builder.AppendLine(" definition(s))");
            builder.Append("ActionCandidateDatabase: ");
            builder.Append(actionCandidateDatabase != null && actionCandidateDatabase.Definitions.Count > 0 ? "asset" : "missing");
            builder.Append(" (");
            builder.Append(GetActionCandidateDefinitions().Count);
            builder.AppendLine(" definition(s))");
            builder.Append("ActionEffectDatabase: ");
            builder.Append(actionEffectDatabase != null && actionEffectDatabase.Definitions.Count > 0 ? "asset" : "missing");
            builder.Append(" (");
            builder.Append(GetActionEffectDefinitions().Count);
            builder.AppendLine(" definition(s))");

            var warnings = OntologyRuleValidator.Validate(rules);
            AppendValidation(builder, "RuleValidation", warnings);
            AppendValidation(builder, "PhysicalProfileValidation",
                ValidatePhysicalProfileRuleLinks(AllRuleDefinitions));
            AppendValidation(builder, "QuestValidation", OntologyQuestValidator.Validate(questDefinitions));
            AppendValidation(builder, "ActionCandidateValidation", OntologyActionValidator.ValidateCandidates(GetActionCandidateDefinitions()));
            AppendValidation(builder, "ActionEffectValidation", OntologyActionValidator.ValidateEffects(GetActionEffectDefinitions()));

            return builder.ToString().TrimEnd();
        }

        private static void AppendValidation(StringBuilder builder, string label, List<string> warnings)
        {
            if (warnings.Count == 0)
            {
                builder.Append(label);
                builder.AppendLine(": ok");
                return;
            }

            builder.Append(label);
            builder.AppendLine(": warning(s)");
            foreach (var warning in warnings)
            {
                builder.Append("- ");
                builder.AppendLine(warning);
            }
        }

        /// <summary>
        /// Validates the authored bridge between a numeric physical profile and the rule
        /// that is allowed to infer its presentation state.  The profile owns this link so
        /// neither Unity code nor rule ordering can silently choose a buoyancy rule.
        /// </summary>
        private List<string> ValidatePhysicalProfileRuleLinks(
            IReadOnlyList<OntologyRuleDefinition> rules)
        {
            var warnings = new List<string>();
            if (physicalProfileDatabase == null)
            {
                return warnings;
            }

            var knownRuleIds = new HashSet<string>(
                rules.Where(rule => rule != null && !string.IsNullOrWhiteSpace(rule.id))
                    .Select(rule => rule.id),
                StringComparer.Ordinal);

            foreach (var profile in physicalProfileDatabase.Profiles)
            {
                if (profile == null || !profile.supportsBuoyancy)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(profile.buoyancyRuleId))
                {
                    warnings.Add(
                        $"Physical Profile '{profile.profileId}' supports buoyancy but has no buoyancy rule link.");
                }
                else if (!knownRuleIds.Contains(profile.buoyancyRuleId))
                {
                    warnings.Add(
                        $"Physical Profile '{profile.profileId}' references missing rule '{profile.buoyancyRuleId}'.");
                }
            }

            return warnings;
        }

        private IReadOnlyList<OntologyActionCandidateDefinition> GetActionCandidateDefinitions()
        {
            return actionCandidateDatabase != null && actionCandidateDatabase.Definitions.Count > 0
                ? actionCandidateDatabase.Definitions
                : Array.Empty<OntologyActionCandidateDefinition>();
        }

        private IReadOnlyList<OntologyActionEffectDefinition> GetActionEffectDefinitions()
        {
            return actionEffectDatabase != null && actionEffectDatabase.Definitions.Count > 0
                ? actionEffectDatabase.Definitions
                : Array.Empty<OntologyActionEffectDefinition>();
        }

    }
}
