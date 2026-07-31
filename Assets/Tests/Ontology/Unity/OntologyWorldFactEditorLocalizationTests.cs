using System.IO;
using System.Reflection;
using NUnit.Framework;
using Tormia.Ontology.Core;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyWorldFactEditorLocalizationTests
    {
        private static readonly string[] DataPanelTerms =
        {
            OntologyPredicates.AttackAction,
            OntologyPredicates.AttackCooldown,
            OntologyPredicates.AttackDamage,
            OntologyPredicates.AttackRange,
            OntologyPredicates.BelongsToFaction,
            OntologyPredicates.CanEquip,
            OntologyPredicates.ChaseAction,
            OntologyPredicates.ChaseProfile,
            OntologyPredicates.CombatDisposition,
            OntologyPredicates.CurrentHealth,
            OntologyPredicates.DamageProfile,
            OntologyPredicates.DeathAnimationIntent,
            OntologyPredicates.EquipAction,
            OntologyPredicates.Faction,
            OntologyPredicates.GravityAcceleration,
            OntologyPredicates.HitAnimationIntent,
            OntologyPredicates.HitVfxIntent,
            OntologyPredicates.InteractionRange,
            OntologyPredicates.IsAlive,
            OntologyPredicates.JumpAction,
            OntologyPredicates.JumpTakeoffSpeed,
            OntologyPredicates.GroundStickVelocity,
            OntologyPredicates.ImpactResponseProfile,
            OntologyPredicates.LocomotionAction,
            OntologyPredicates.LootAction,
            OntologyPredicates.LootItem,
            OntologyPredicates.LootStatus,
            OntologyPredicates.LootTable,
            OntologyPredicates.Location,
            OntologyPredicates.MaximumHealth,
            OntologyPredicates.MovementSpeed,
            OntologyPredicates.RespawnAction,
            OntologyPredicates.SemanticContractVersion,
            OntologyPredicates.SwingAction,
            OntologyPredicates.TargetAction,
            OntologyPredicates.TargetConcept,
            OntologyPredicates.TargetingProfile,
            OntologyPredicates.UnequipAction,
            OntologyActions.AcquireAutonomousTarget,
            OntologyActions.AutonomousMeleeAttack,
            OntologyActions.ChaseAutonomousTarget,
            OntologyActions.CollectLoot,
            OntologyActions.EquipWeapon,
            OntologyActions.JumpAvatar,
            OntologyActions.MoveAvatar,
            OntologyActions.RespawnAvatar,
            OntologyActions.SwingWeapon,
            OntologyActions.UnequipEquipment,
            OntologyObjects.ChaseWithinLeash,
            OntologyObjects.NearestHostileWithinDetection,
            "Available",
            "BasicSwordDamage",
            "BeholderBasicLoot",
            "Death",
            "False",
            "HandheldWeapon",
            "HitPhysicalLight",
            "HitReaction",
            "Hostile",
            "Jump",
            "Locomotion",
            "MeleeAttack",
            "OntologyDataFragment",
            "True"
        };

        [SetUp]
        public void SetUp()
        {
            OntologyLanguagePackService.Reload();
        }

        [Test]
        public void DataPanelProductionTermsHaveEnglishAndKoreanLabels()
        {
            foreach (var canonicalId in DataPanelTerms)
            {
                var term = OntologyLanguagePackService.Registry.Find(canonicalId);
                Assert.That(
                    term,
                    Is.Not.Null,
                    "The data panel cannot localize unregistered id: " + canonicalId);
                Assert.That(
                    OntologyLanguagePackService.HasText("en", term.LabelKey),
                    Is.True,
                    "Missing English data-panel label: " + canonicalId);
                Assert.That(
                    OntologyLanguagePackService.HasText("ko", term.LabelKey),
                    Is.True,
                    "Missing Korean data-panel label: " + canonicalId);
            }
        }

        [Test]
        public void DataPanelUiAndPhysicalChoicesHaveBothLanguages()
        {
            var keys = new[]
            {
                "ui.title.ontology_data",
                "ui.tab.triples",
                "ui.tab.rule_blocks",
                "ui.tab.physics",
                "ui.tab.results",
                "ui.button.add_triple",
                "ui.rule_block.quick_action",
                "ui.footer.rule_blocks",
                "ui.button.save_world",
                "ui.button.load_world",
                "ui.placeholder.object_value",
                "physical_choice.AuthorityKinematic",
                "physical_result.AuthorityKinematic",
                "physical_choice.HandheldWeapon",
                "physical_result.HandheldWeapon"
            };

            foreach (var key in keys)
            foreach (var locale in new[] { "en", "ko" })
                Assert.That(
                    OntologyLanguagePackService.HasText(locale, key),
                    Is.True,
                    "Missing " + locale + " data-panel UI key: " + key);
        }

        [Test]
        public void ResultRowsShowOnlyTheFactAndUseTheAuthoredLargerFont()
        {
            var panelSource = File.ReadAllText(Path.Combine(
                Application.dataPath,
                "Scripts/Ontology/UI/OntologyRuntimeWorldFactEditorPanel.cs"));
            Assert.That(
                panelSource,
                Does.Not.Contain("result.current_information"),
                "Result rows must not repeat the current-information heading.");
            Assert.That(
                panelSource,
                Does.Not.Contain("FactOriginLabel("),
                "Result rows must not repeat the authored/inferred origin prefix.");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Ontology/UI/WorldEditHUD.prefab");
            Assert.That(prefab, Is.Not.Null);
            var resultText = prefab.transform.Find(
                    "TripleScrollView/Viewport/Content/" +
                    "ResultRowTemplate/ResultText")
                ?.GetComponent<TextMeshProUGUI>();
            Assert.That(resultText, Is.Not.Null);
            Assert.That(resultText.fontSize, Is.EqualTo(21f));
        }

        [Test]
        public void DataPanelTabsUseTheAuthoredProductionOrder()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Ontology/UI/WorldEditHUD.prefab");
            Assert.That(prefab, Is.Not.Null);
            var modeTabs = prefab.transform.Find("ModeTabs");
            Assert.That(modeTabs, Is.Not.Null);

            var expectedOrder = new[]
            {
                "RuleTabButton",
                "PhysicalTabButton",
                "TripleTabButton",
                "ResultTabButton"
            };
            Assert.That(modeTabs.childCount, Is.EqualTo(expectedOrder.Length));
            for (var index = 0; index < expectedOrder.Length; index++)
                Assert.That(
                    modeTabs.GetChild(index).name,
                    Is.EqualTo(expectedOrder[index]));
        }

        [Test]
        public void ReopeningDataPanelSelectsRuleBlocksWithoutChangingData()
        {
            var controllerObject = new GameObject("Controller");
            var panelObject = new GameObject("Panel");
            try
            {
                var controller = controllerObject.AddComponent<
                    OntologyRuntimeWorldEditorController>();
                var panel = panelObject.AddComponent<
                    OntologyRuntimeWorldFactEditorPanel>();
                panel.Configure(controller);

                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var openField = typeof(OntologyRuntimeWorldEditorController)
                    .GetField("<IsOntologyOpen>k__BackingField", flags);
                var modeField = typeof(OntologyRuntimeWorldFactEditorPanel)
                    .GetField("mode", flags);
                var refresh = typeof(OntologyRuntimeWorldFactEditorPanel)
                    .GetMethod("Refresh", flags);
                Assert.That(openField, Is.Not.Null);
                Assert.That(modeField, Is.Not.Null);
                Assert.That(refresh, Is.Not.Null);

                var resultsMode = System.Enum.Parse(
                    modeField.FieldType,
                    "Results");
                modeField.SetValue(panel, resultsMode);
                openField.SetValue(controller, false);
                refresh.Invoke(panel, null);
                openField.SetValue(controller, true);
                refresh.Invoke(panel, null);

                Assert.That(
                    modeField.GetValue(panel).ToString(),
                    Is.EqualTo("RuleBlocks"));
            }
            finally
            {
                typeof(OntologyRuntimeWorldFactEditorPanel)
                    .GetMethod(
                        "Unsubscribe",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(
                        panelObject.GetComponent<
                            OntologyRuntimeWorldFactEditorPanel>(),
                        null);
                Object.DestroyImmediate(panelObject);
                Object.DestroyImmediate(controllerObject);
            }
        }
    }
}
