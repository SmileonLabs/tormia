namespace Tormia.Ontology.Core
{
    public sealed class OntologyActionCandidate
    {
        public OntologyActionCandidate(OntologyAction action, string label, string reason, bool isQuestGoal = false, string questTitle = "")
        {
            Action = action;
            Label = label;
            Reason = reason;
            IsQuestGoal = isQuestGoal;
            QuestTitle = questTitle;
        }

        public OntologyAction Action { get; }
        public string Label { get; }
        public string Reason { get; }
        public bool IsQuestGoal { get; }
        public string QuestTitle { get; }

        public OntologyActionCandidate WithQuestGoal(string questTitle)
        {
            return new OntologyActionCandidate(Action, Label, Reason, true, questTitle);
        }

        public override string ToString()
        {
            var prefix = IsQuestGoal ? $"[Quest: {QuestTitle}] " : string.Empty;
            return string.IsNullOrWhiteSpace(Reason)
                ? prefix + Label
                : $"{prefix}{Label} - {Reason}";
        }
    }
}
