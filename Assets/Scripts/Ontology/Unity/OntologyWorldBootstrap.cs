using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyWorldBootstrap : MonoBehaviour
    {
        [SerializeField] private bool runOnStart;
        [SerializeField] private int maxIterations = 8;

        [Header("Sample Action")]
        [SerializeField] private string actorId = "Player";
        [SerializeField] private string actionTargetId = "Tree";
        [SerializeField] private string actionToolId = "FireSword";

        [Header("Quest Data")]
        [SerializeField] private OntologyQuestDatabase questDatabase;

        [Header("Rule Data")]
        [SerializeField] private OntologyRuleDatabase ruleDatabase;

        [Header("Action Data")]
        [SerializeField] private OntologyActionCandidateDatabase actionCandidateDatabase;
        [SerializeField] private OntologyActionEffectDatabase actionEffectDatabase;

        private OntologyWorldState world;
        private OntologySession session;
        private OntologySimulationResult lastResult;

        public OntologyWorldState World => world;
        public OntologySession Session => session;
        public OntologySimulationResult LastResult => lastResult;

        private void Start()
        {
            ResetWorld(logReport: false);
            if (runOnStart)
            {
                RunSimulation();
            }
        }

        public string ResetWorld(bool logReport = true)
        {
            world = BuildWorldFromScene();
            session = new OntologySession();
            lastResult = null;
            var report = BuildReport(world, session, lastResult, actorId);
            if (logReport)
            {
                Debug.Log(report);
            }

            return report;
        }

        public string RestoreSnapshot(OntologySaveData saveData, bool logReport = true)
        {
            world = OntologySaveDataConverter.RestoreWorld(saveData);
            session = OntologySaveDataConverter.RestoreSession(saveData);
            lastResult = null;

            var report = BuildReport(world, session, lastResult, actorId);
            if (logReport)
            {
                Debug.Log(report);
            }

            return report;
        }

        public string RunSimulation()
        {
            if (world == null)
            {
                world = BuildWorldFromScene();
                session = new OntologySession();
            }

            if (session == null)
            {
                session = new OntologySession();
            }

            var engine = BuildRuleEngine();

            var simulation = new OntologySimulation(maxIterations);
            lastResult = simulation.RunUntilStable(world, engine);
            session.RecordEvents(lastResult);
            if (ApplyQuestCompletionFacts(world, actorId) > 0)
            {
                var followUpResult = simulation.RunUntilStable(world, engine);
                AppendSimulationResult(lastResult, followUpResult);
                session.RecordEvents(followUpResult);
            }

            var report = BuildReport(world, session, lastResult, actorId);
            Debug.Log(report);
            return report;
        }

        public string AttackTargetWithTool()
        {
            return ExecuteAction(new OntologyAction(actorId, "attack", actionTargetId, actionToolId));
        }

        public string ExecuteAction(OntologyAction action)
        {
            ApplyAction(action);
            return RunSimulation();
        }

        public List<OntologyActionCandidate> GetActionCandidates()
        {
            if (world == null)
            {
                world = BuildWorldFromScene();
                session = new OntologySession();
            }

            var generator = CreateActionCandidateGenerator();
            var candidates = generator.Generate(world, actorId);
            return MarkQuestGoalCandidates(world, actorId, candidates);
        }

        public bool ApplyAction(OntologyAction action)
        {
            if (world == null)
            {
                world = BuildWorldFromScene();
                session = new OntologySession();
            }

            if (session == null)
            {
                session = new OntologySession();
            }

            var runner = CreateActionRunner();
            return runner.ApplyAction(world, action, session);
        }

        private OntologyActionRunner CreateActionRunner()
        {
            return actionEffectDatabase != null && actionEffectDatabase.Definitions.Count > 0
                ? new OntologyActionRunner(actionEffectDatabase.Definitions)
                : new OntologyActionRunner();
        }

        private OntologyWorldState BuildWorldFromScene()
        {
            var state = new OntologyWorldState();
            var objects = FindObjectsByType<OntologyObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var ontologyObject in objects)
            {
                ontologyObject.ApplyTo(state);
            }

            return state;
        }

        private OntologyRuleEngine BuildRuleEngine()
        {
            var engine = new OntologyRuleEngine();
            OntologyDefaultRules.RegisterRules(engine, GetRuleDefinitions());
            return engine;
        }

        private IReadOnlyList<OntologyRuleDefinition> GetRuleDefinitions()
        {
            return ruleDatabase != null && ruleDatabase.Definitions.Count > 0
                ? ruleDatabase.Definitions
                : OntologyDefaultRules.CreateDefaultDefinitions();
        }

        private int ApplyQuestCompletionFacts(OntologyWorldState state, string actorId)
        {
            var questGenerator = CreateQuestGenerator();
            var quests = questGenerator.Generate(state, actorId);
            var completionSystem = new OntologyQuestCompletionSystem();
            return completionSystem.ApplyCompletionFacts(state, actorId, quests);
        }

        private OntologyQuestGenerator CreateQuestGenerator()
        {
            return questDatabase != null
                ? new OntologyQuestGenerator(questDatabase.Definitions)
                : new OntologyQuestGenerator();
        }

        private static void AppendSimulationResult(OntologySimulationResult target, OntologySimulationResult followUp)
        {
            if (target == null || followUp == null)
            {
                return;
            }

            var offset = target.Iterations;
            target.TotalAddedFacts += followUp.TotalAddedFacts;
            target.TotalChangedFacts += followUp.TotalChangedFacts;
            target.Iterations += followUp.Iterations;
            target.ReachedStableState = followUp.ReachedStableState;
            foreach (var step in followUp.Steps)
            {
                target.Steps.Add(new OntologySimulationStep(offset + step.Iteration, step.Events, step.AddedFactCount, step.ChangedFactCount));
            }
        }

        private string BuildReport(OntologyWorldState state, OntologySession session, OntologySimulationResult result, string actorId)
        {
            var builder = new StringBuilder();
            builder.AppendLine("[OntologyWorldBootstrap]");
            builder.AppendLine(DumpDatabaseStatus());
            builder.AppendLine(state != null ? state.DumpFacts() : "No world state.");
            builder.AppendLine(result != null ? result.DumpEvents() : "[Simulation] Not run yet.");
            builder.AppendLine(session != null ? session.DumpHistory() : "[History]\n- none");
            builder.AppendLine(DumpQuests(state, actorId));
            builder.AppendLine(DumpCandidates(state, actorId));
            return builder.ToString().TrimEnd();
        }

        private string DumpDatabaseStatus()
        {
            var rules = GetRuleDefinitions();
            var questDefinitions = questDatabase != null && questDatabase.Definitions.Count > 0 ? questDatabase.Definitions : null;
            var builder = new StringBuilder();
            builder.AppendLine("[Databases]");
            builder.Append("RuleDatabase: ");
            builder.Append(ruleDatabase != null && ruleDatabase.Definitions.Count > 0 ? "asset" : "default");
            builder.Append(" (");
            builder.Append(rules.Count);
            builder.AppendLine(" rule(s))");
            builder.Append("QuestDatabase: ");
            builder.Append(questDefinitions != null ? "asset" : "default");
            builder.Append(" (");
            builder.Append(questDefinitions != null ? questDefinitions.Count : 1);
            builder.AppendLine(" definition(s))");
            builder.Append("ActionCandidateDatabase: ");
            builder.Append(actionCandidateDatabase != null && actionCandidateDatabase.Definitions.Count > 0 ? "asset" : "default");
            builder.Append(" (");
            builder.Append(GetActionCandidateDefinitions().Count);
            builder.AppendLine(" definition(s))");
            builder.Append("ActionEffectDatabase: ");
            builder.Append(actionEffectDatabase != null && actionEffectDatabase.Definitions.Count > 0 ? "asset" : "default");
            builder.Append(" (");
            builder.Append(GetActionEffectDefinitions().Count);
            builder.AppendLine(" definition(s))");

            var warnings = OntologyRuleValidator.Validate(rules);
            AppendValidation(builder, "RuleValidation", warnings);
            AppendValidation(builder, "QuestValidation", OntologyQuestValidator.Validate(questDefinitions ?? new OntologyQuestGenerator().Definitions));
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

        private string DumpQuests(OntologyWorldState state, string actorId)
        {
            if (state == null)
            {
                return "[Generated Quests]\n- none";
            }

            var generator = CreateQuestGenerator();
            var quests = generator.Generate(state, actorId);
            if (quests.Count == 0)
            {
                return "[Generated Quests]\n- none";
            }

            var builder = new StringBuilder();
            builder.AppendLine("[Generated Quests]");
            foreach (var quest in quests)
            {
                builder.Append("- ");
                builder.AppendLine(quest.ToString());
            }

            return builder.ToString().TrimEnd();
        }

        private string DumpCandidates(OntologyWorldState state, string actorId)
        {
            if (state == null)
            {
                return "[Available Actions]\n- none";
            }

            var generator = CreateActionCandidateGenerator();
            var candidates = MarkQuestGoalCandidates(state, actorId, generator.Generate(state, actorId));
            if (candidates.Count == 0)
            {
                return "[Available Actions]\n- none";
            }

            var builder = new StringBuilder();
            builder.AppendLine("[Available Actions]");
            foreach (var candidate in candidates)
            {
                builder.Append("- ");
                builder.AppendLine(candidate.ToString());
            }

            return builder.ToString().TrimEnd();
        }

        private OntologyActionCandidateGenerator CreateActionCandidateGenerator()
        {
            return actionCandidateDatabase != null && actionCandidateDatabase.Definitions.Count > 0
                ? new OntologyActionCandidateGenerator(actionCandidateDatabase.Definitions)
                : new OntologyActionCandidateGenerator();
        }

        private IReadOnlyList<OntologyActionCandidateDefinition> GetActionCandidateDefinitions()
        {
            return actionCandidateDatabase != null && actionCandidateDatabase.Definitions.Count > 0
                ? actionCandidateDatabase.Definitions
                : OntologyActionCandidateGenerator.CreateDefaultDefinitions();
        }

        private IReadOnlyList<OntologyActionEffectDefinition> GetActionEffectDefinitions()
        {
            return actionEffectDatabase != null && actionEffectDatabase.Definitions.Count > 0
                ? actionEffectDatabase.Definitions
                : OntologyActionRunner.CreateDefaultDefinitions();
        }

        private List<OntologyActionCandidate> MarkQuestGoalCandidates(
            OntologyWorldState state,
            string actorId,
            List<OntologyActionCandidate> candidates)
        {
            var quests = CreateQuestGenerator().Generate(state, actorId);
            if (quests.Count == 0 || candidates.Count == 0)
            {
                return candidates;
            }

            var marked = new List<OntologyActionCandidate>(candidates.Count);
            foreach (var candidate in candidates)
            {
                var questTitle = FindMatchingQuestTitle(candidate.Action, quests);
                marked.Add(string.IsNullOrWhiteSpace(questTitle)
                    ? candidate
                    : candidate.WithQuestGoal(questTitle));
            }

            return marked;
        }

        private static string FindMatchingQuestTitle(OntologyAction action, List<OntologyQuest> quests)
        {
            foreach (var quest in quests)
            {
                foreach (var goal in quest.Goals)
                {
                    if (goal.RecommendedAction.Equals(action))
                    {
                        return quest.Title;
                    }
                }
            }

            return string.Empty;
        }
    }
}
