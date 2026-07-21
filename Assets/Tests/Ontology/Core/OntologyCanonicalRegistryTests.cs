using NUnit.Framework;
using Tormia.Ontology.Core;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyCanonicalRegistryTests
    {
        [Test]
        public void CanonicalLookupIgnoresInputCaseButReturnsAuthoredCase()
        {
            var registry = new OntologyTermRegistry();
            registry.AddTerm(new OntologyTermDefinition
            {
                CanonicalId = "Plant",
                Kind = OntologyTermKind.Concept
            });

            Assert.That(
                registry.TryResolve(
                    "pLaNt",
                    "en",
                    OntologyTermKind.Concept,
                    out var canonical,
                    out _),
                Is.True);
            Assert.That(canonical, Is.EqualTo("Plant"));
        }

        [Test]
        public void LocalizedAliasResolvesToCanonicalId()
        {
            var registry = new OntologyTermRegistry();
            registry.AddTerm(new OntologyTermDefinition
            {
                CanonicalId = "Plant",
                Kind = OntologyTermKind.Concept
            });
            registry.AddAlias(new OntologyAliasDefinition
            {
                Locale = "ko",
                Alias = "식물",
                CanonicalId = "Plant"
            });

            Assert.That(
                registry.TryResolve(
                    "식물",
                    "ko",
                    OntologyTermKind.Concept,
                    out var canonical,
                    out _),
                Is.True);
            Assert.That(canonical, Is.EqualTo("Plant"));
        }

        [Test]
        public void AmbiguousAliasIsRejectedWithCandidates()
        {
            var registry = new OntologyTermRegistry();
            registry.AddTerm(Term("Plant"));
            registry.AddTerm(Term("Vegetation"));
            registry.AddAlias(Alias("ko", "초목", "Plant"));
            registry.AddAlias(Alias("ko", "초목", "Vegetation"));

            Assert.That(
                registry.TryResolve(
                    "초목",
                    "ko",
                    OntologyTermKind.Concept,
                    out _,
                    out var candidates),
                Is.False);
            Assert.That(candidates, Is.EquivalentTo(new[] { "Plant", "Vegetation" }));
            Assert.That(registry.ValidationMessages, Is.Not.Empty);
        }

        [Test]
        public void SuggestedIdentifiersFollowCanonicalConventions()
        {
            Assert.That(
                OntologyCanonicalId.SuggestConceptOrValue("floating object"),
                Is.EqualTo("FloatingObject"));
            Assert.That(
                OntologyCanonicalId.SuggestRelation("Has Temporary Skill"),
                Is.EqualTo("has_temporary_skill"));
        }

        private static OntologyTermDefinition Term(string id) =>
            new()
            {
                CanonicalId = id,
                Kind = OntologyTermKind.Concept
            };

        private static OntologyAliasDefinition Alias(
            string locale,
            string alias,
            string canonical) =>
            new()
            {
                Locale = locale,
                Alias = alias,
                CanonicalId = canonical
            };
    }
}
