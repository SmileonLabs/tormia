using System;
using System.Collections.Generic;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Client-side description of one Authority-owned semantic transaction.
    /// It contains data declarations only; executable behavior remains in
    /// published Rule Blocks and Unity Physical Meaning adapters.
    /// </summary>
    [Serializable]
    public sealed class OntologyMeaningPackageChange
    {
        public string operation = "apply";
        public string applicationId;
        public string slotId;
        public string packageId;
        // Authority may adopt matching unclaimed authored Facts and Rule
        // Blocks that already exist on the target.
        public bool adoptExistingContributions;
        // Packages whose behavior is expected to close when the final Rule Block
        // leaves must own at least one binding. Passive data-only packages opt out.
        public bool requiresOwnedBinding;
        public List<string> replacePredicateIds = new();
        public List<string> requiredConceptIds = new();
        // Preserves canonical, entity, number, boolean, text, and JSON object
        // kinds when a semantic package owns authored Authority facts.
        public List<OntologyAuthorityInitialFact> authorityFacts = new();
        // Legacy/local authoring model retained for existing Quick Setup assets.
        public List<OntologyFactEntry> authoredFacts = new();
        public List<OntologyMeaningPackageRuleBlock> ruleBlocks = new();
    }

    [Serializable]
    public sealed class OntologyMeaningPackageRuleBlock
    {
        public string bindingId;
        public string ruleId;
        public int ruleVersion = 1;
        public string bindingVariable = "?target";
    }
}
