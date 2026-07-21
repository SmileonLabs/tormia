using System.Collections.Generic;
using NUnit.Framework;
using Tormia.Ontology.Core;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyLanguagePackServiceTests
    {
        private OntologyDisplayLanguage originalLanguage;

        [SetUp]
        public void SetUp()
        {
            OntologyLanguagePackService.Reload();
            originalLanguage = OntologyLanguagePackService.CurrentLanguage;
        }

        [TearDown]
        public void TearDown()
        {
            OntologyLanguagePackService.SetLanguage(originalLanguage);
        }

        [Test]
        public void ShippedCsvFilesValidateWithoutWarnings()
        {
            Assert.That(OntologyLanguagePackService.ValidationMessages, Is.Empty);
            Assert.That(OntologyLanguagePackService.Registry.Terms.Count, Is.GreaterThan(60));
        }

        [Test]
        public void KoreanConceptInputIsStoredAsEnglishCanonicalId()
        {
            OntologyLanguagePackService.SetLanguage(OntologyDisplayLanguage.Korean);

            var result = OntologyLanguagePackService.ResolveObjectInput(
                "식물",
                "has_concept",
                new string[0]);

            Assert.That(result.Success, Is.True);
            Assert.That(result.CanonicalValue, Is.EqualTo("Plant"));
        }

        [Test]
        public void EnglishInputCaseDoesNotChangeCanonicalSpelling()
        {
            OntologyLanguagePackService.SetLanguage(OntologyDisplayLanguage.English);

            var result = OntologyLanguagePackService.ResolveObjectInput(
                "pLaNt",
                "has_concept",
                new string[0]);

            Assert.That(result.Success, Is.True);
            Assert.That(result.CanonicalValue, Is.EqualTo("Plant"));
        }

        [Test]
        public void RelationValueTypeRestrictsLocalizedResolution()
        {
            OntologyLanguagePackService.SetLanguage(OntologyDisplayLanguage.Korean);

            var result = OntologyLanguagePackService.ResolveObjectInput(
                "깊은 물",
                "water_depth",
                new string[0]);

            Assert.That(result.Success, Is.True);
            Assert.That(result.CanonicalValue, Is.EqualTo("Deep"));
        }

        [Test]
        public void EveryRegisteredTermAndRelationHasBothLanguageEntries()
        {
            foreach (var term in OntologyLanguagePackService.Registry.Terms)
            {
                Assert.That(
                    OntologyLanguagePackService.HasText("en", term.LabelKey),
                    Is.True,
                    "Missing English label: " + term.CanonicalId);
                Assert.That(
                    OntologyLanguagePackService.HasText("ko", term.LabelKey),
                    Is.True,
                    "Missing Korean label: " + term.CanonicalId);
                if (term.Kind != OntologyTermKind.Relation)
                    continue;
                Assert.That(
                    OntologyLanguagePackService.HasText(
                        "en",
                        "fact." + term.CanonicalId),
                    Is.True,
                    "Missing English fact template: " + term.CanonicalId);
                Assert.That(
                    OntologyLanguagePackService.HasText(
                        "ko",
                        "fact." + term.CanonicalId),
                    Is.True,
                    "Missing Korean fact template: " + term.CanonicalId);
            }
        }

        [Test]
        public void KoreanDisplayLabelsDoNotChangeCanonicalProfileRuleOrPartIds()
        {
            OntologyLanguagePackService.SetLanguage(
                OntologyDisplayLanguage.Korean);

            Assert.That(
                OntologyLanguagePackService.PhysicalProfileName("HeavySinking"),
                Is.Not.EqualTo("Heavy Falling"));
            Assert.That(
                OntologyLanguagePackService.RuleName(
                    "DeepWaterOccupancySwimming"),
                Is.Not.EqualTo("Deep Water Occupancy Swimming"));
            Assert.That(
                OntologyLanguagePackService.CharacterPartName(
                    "Part_Body_Base",
                    "Body 010"),
                Is.EqualTo("기본 몸 010"));

            var value = OntologyLanguagePackService.ResolveObjectInput(
                "부력",
                "supports_behavior",
                new string[0]);
            Assert.That(value.Success, Is.True);
            Assert.That(value.CanonicalValue, Is.EqualTo("Buoyancy"));
        }

        [Test]
        public void SaveMigrationNormalizesSemanticTermsButPreservesEntityText()
        {
            var save = new OntologySaveData
            {
                version = 3,
                placedObjects = new List<OntologyPlacedObjectRecord>
                {
                    new()
                    {
                        definitionId = "polystyle_stone_1",
                        concepts = new List<string> { "plant" },
                        facts = new List<OntologyFactRecord>
                        {
                            new()
                            {
                                subject = "My Tree 01",
                                predicate = "water_depth",
                                obj = "deep"
                            },
                            new()
                            {
                                subject = "My Tree 01",
                                predicate = "located_in",
                                obj = "My Korean Named Zone"
                            },
                            new()
                            {
                                subject = "My Tree 01",
                                predicate = "physical_profile",
                                obj = "InflatableLight"
                            }
                        }
                    }
                }
            };

            OntologyLanguagePackService.MigrateSaveData(save);

            Assert.That(save.version, Is.EqualTo(4));
            Assert.That(save.placedObjects[0].concepts[0], Is.EqualTo("Plant"));
            Assert.That(save.placedObjects[0].facts[0].obj, Is.EqualTo("Deep"));
            Assert.That(
                save.placedObjects[0].facts[1].obj,
                Is.EqualTo("My Korean Named Zone"));
            Assert.That(
                save.placedObjects[0].facts[2].obj,
                Is.EqualTo("LightBuoyant"));
        }
    }
}
