using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;

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
        public void ActionReferencePreservesPublishedActionIdSpelling()
        {
            Assert.That(
                OntologyLanguagePackService.CanonicalObjectForRelation(
                    OntologyPredicates.AttackAction,
                    OntologyActions.PrimaryAttack),
                Is.EqualTo(OntologyActions.PrimaryAttack));
            Assert.That(
                OntologyLanguagePackService.CanonicalObjectForRelation(
                    OntologyPredicates.AttackAction,
                    "Attack"),
                Is.EqualTo("Attack"));
            Assert.That(
                OntologyLanguagePackService.GetRelationCardinality(
                    OntologyPredicates.AttackAction),
                Is.EqualTo(OntologyCardinalityKind.Single));
        }

        [Test]
        public void AnimationIntentStillUsesGlobalCanonicalValueSpelling()
        {
            Assert.That(
                OntologyLanguagePackService.CanonicalObjectForRelation(
                    OntologyPredicates.IdleAnimationIntent,
                    "attack"),
                Is.EqualTo("Attack"));
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

        [Test]
        public void RuntimeUiContractKeysExistInBothLanguages()
        {
            var keys = new[]
            {
                "category.Combat",
                "physical_profile.AuthorityKinematic",
                "physical_profile.HandheldWeapon",
                "character_template.player",
                "common.none",
                "ui.account.profile_relation",
                "ui.account.status.operation_failed",
                "ui.quest_panel.authority_not_ready",
                "placement.surface.Ground"
            };

            foreach (var key in keys)
            foreach (var locale in new[] { "en", "ko" })
                Assert.That(
                    OntologyLanguagePackService.HasText(locale, key),
                    Is.True,
                    "Missing " + locale + " runtime UI key: " + key);
        }

        [Test]
        public void DynamicAccountValuesAreLocalizedWithoutChangingCanonicalIds()
        {
            OntologyLanguagePackService.SetLanguage(
                OntologyDisplayLanguage.Korean);

            Assert.That(
                OntologyLanguagePackService.CharacterTemplateName("player"),
                Is.EqualTo("플레이어"));
            Assert.That(
                OntologyLanguagePackService.FormatProfileRelation(
                    "Player",
                    "has_concept",
                    "Combatant"),
                Does.Contain("전투"));
            Assert.That(
                OntologyLanguagePackService.Format(
                    "ui.account.status.dashboard_loaded",
                    "Loaded {0} character(s) and {1} world(s).",
                    2,
                    3),
                Is.EqualTo("캐릭터 2개와 월드 3개를 불러왔습니다."));
        }

        [Test]
        public void DefaultQuestsProvideLocalizedKeysAndEnglishFallbacks()
        {
            foreach (var quest in OntologyQuestGenerator.CreateDefaultDefinitions())
            {
                Assert.That(quest.title, Is.Not.Empty);
                Assert.That(quest.titleKey, Is.Not.Empty);
                Assert.That(quest.reasonFormat, Is.Not.Empty);
                Assert.That(quest.reasonKey, Is.Not.Empty);
                Assert.That(
                    OntologyLanguagePackService.HasText("en", quest.titleKey),
                    Is.True);
                Assert.That(
                    OntologyLanguagePackService.HasText("ko", quest.titleKey),
                    Is.True);
                foreach (var goal in quest.goals)
                {
                    Assert.That(goal.descriptionFormat, Is.Not.Empty);
                    Assert.That(goal.descriptionKey, Is.Not.Empty);
                    Assert.That(
                        OntologyLanguagePackService.HasText(
                            "en",
                            goal.descriptionKey),
                        Is.True);
                    Assert.That(
                        OntologyLanguagePackService.HasText(
                            "ko",
                            goal.descriptionKey),
                        Is.True);
                }
            }
        }

        [Test]
        public void PlayerFacingStatusBoundariesDoNotAssignRawLiterals()
        {
            var accountFlow = File.ReadAllText(Path.Combine(
                Application.dataPath,
                "Scripts/Ontology/Unity/Networking/OntologyWorldAuthorityAccountEntryFlow.cs"));
            var questPanel = File.ReadAllText(Path.Combine(
                Application.dataPath,
                "Scripts/Ontology/UI/OntologyQuestActionPanel.cs"));

            Assert.That(accountFlow, Does.Not.Contain("SetStatus(\""));
            Assert.That(
                questPanel,
                Does.Not.Contain("actionEmptyLabel.text = \""));
        }

        [Test]
        public void RegisteredCombatConceptsHaveLocalizedLabels()
        {
            Assert.That(
                OntologyLanguagePackService.ValidateRegisteredTerms(
                    new[]
                    {
                        "Damageable",
                        "Item",
                        "Monster",
                        "Sword",
                        "Weapon"
                    },
                    OntologyTermKind.Concept),
                Is.Empty);

            OntologyLanguagePackService.SetLanguage(
                OntologyDisplayLanguage.Korean);
            Assert.That(
                OntologyLanguagePackService.Term("Damageable"),
                Is.EqualTo("피해 가능 대상"));
            Assert.That(
                OntologyLanguagePackService.Term("Monster"),
                Is.EqualTo("몬스터"));
            Assert.That(
                OntologyLanguagePackService.CanonicalTerm("Damageable"),
                Is.EqualTo("Damageable"));
        }

        [Test]
        public void MissingCanonicalConceptReferenceFailsCoverageValidation()
        {
            var messages =
                OntologyLanguagePackService.ValidateRegisteredTerms(
                    new[] { "ConceptThatWasNeverRegistered" },
                    OntologyTermKind.Concept);

            Assert.That(messages, Has.Count.EqualTo(1));
            Assert.That(
                messages[0],
                Does.Contain("ConceptThatWasNeverRegistered"));
        }

        [Test]
        public void EntityDisplayNameAndCanonicalTermCoverageContract()
        {
            OntologyLanguagePackService.SetLanguage(
                OntologyDisplayLanguage.Korean);
            var definition = new OntologyPlaceableDefinition
            {
                definitionId = "BeholderBasic",
                displayNameKey = "placeable.BeholderBasic",
                displayName = "Beholder"
            };

            Assert.That(
                OntologyLanguagePackService.FormatInstanceName(
                    definition,
                    "BeholderBasic_c267940a"),
                Is.EqualTo("비홀더"));
            Assert.That(
                OntologyLanguagePackService.FormatInstanceName(
                    definition,
                    "BeholderBasic_001"),
                Is.EqualTo("비홀더"));
            Assert.That(
                OntologyLanguagePackService.FormatInstanceName(
                    definition,
                    "BeholderBasic_c267940a_Copy"),
                Is.EqualTo("비홀더"));
            Assert.That(
                OntologyLanguagePackService.FormatInstanceName(
                    definition,
                    "BeholderBasic_c267940a_Copy_001"),
                Is.EqualTo("비홀더"));
            Assert.That(
                OntologyLanguagePackService.FormatInstanceName(
                    definition,
                    "나의 보스"),
                Is.EqualTo("나의 보스"));
            Assert.That(
                OntologyLanguagePackService.FormatInstanceName(
                    definition,
                    "BeholderBasic_나의복제"),
                Is.EqualTo("BeholderBasic_나의복제"));
            Assert.That(
                OntologyEntityDisplayNameResolver.ResolveForList(
                    definition,
                    "BeholderBasic_c267940a",
                    2),
                Is.EqualTo("비홀더 2"));
            Assert.That(
                OntologyLanguagePackService.ValidateRegisteredTerms(
                    new[] { "Damageable", "Monster", "Item", "Sword", "Weapon" },
                    OntologyTermKind.Concept),
                Is.Empty);
            Assert.That(
                OntologyLanguagePackService.ValidateRegisteredTerms(
                    new[] { "UnregisteredDisplayContractConcept" },
                    OntologyTermKind.Concept),
                Is.Not.Empty);
        }
    }
}
