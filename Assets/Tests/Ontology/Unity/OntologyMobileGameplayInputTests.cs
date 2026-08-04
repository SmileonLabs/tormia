using System.Linq;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.OnScreen;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyMobileGameplayInputTests
    {
        private const string ActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string PrefabPath =
            "Assets/Data/Ontology/UI/MobileGameplayControls.prefab";
        private const string EquipReferencePath =
            "Assets/Data/Ontology/Input/PlayerEquipActionReference.asset";

        [Test]
        public void ProjectActionsExposeMobileGameplayBindings()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
            Assert.That(asset, Is.Not.Null);
            AssertBinding(asset, "Player/Move", "<Gamepad>/leftStick");
            AssertBinding(asset, "Player/Jump", "<Gamepad>/buttonSouth");
            AssertBinding(asset, "Player/Equip", "<Gamepad>/rightShoulder");
            AssertBinding(asset, "UI/Click", "<Touchscreen>/touch*/press");
            AssertBinding(asset, "UI/Point", "<Touchscreen>/touch*/position");

            var equipReference =
                AssetDatabase.LoadAssetAtPath<InputActionReference>(
                    EquipReferencePath);
            Assert.That(equipReference, Is.Not.Null);
            Assert.That(equipReference.action, Is.Not.Null);
            Assert.That(
                equipReference.action.id,
                Is.EqualTo(asset.FindAction("Player/Equip", true).id));
        }

        [Test]
        public void MobilePrefabUsesOnlyVirtualDeviceInputs()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(
                prefab.GetComponent<OntologyMobileGameplayControls>(),
                Is.Not.Null);
            var stick = prefab.GetComponentInChildren<OnScreenStick>(true);
            Assert.That(stick, Is.Not.Null);
            Assert.That(stick.controlPath, Is.EqualTo("<Gamepad>/leftStick"));
            var buttons = prefab.GetComponentsInChildren<OnScreenButton>(true);
            Assert.That(
                buttons.Select(value => value.controlPath),
                Is.EquivalentTo(new[]
                {
                    "<Gamepad>/buttonSouth",
                    "<Gamepad>/rightShoulder"
                }));
        }

        [Test]
        public void SafeAreaConversionPreservesLandscapeInsets()
        {
            OntologyMobileGameplayControls.CalculateSafeAreaAnchors(
                new Rect(80f, 0f, 2240f, 1080f),
                new Vector2Int(2400, 1080),
                out var minimum,
                out var maximum);

            Assert.That(minimum.x, Is.EqualTo(80f / 2400f).Within(0.0001f));
            Assert.That(minimum.y, Is.Zero);
            Assert.That(maximum.x, Is.EqualTo(2320f / 2400f).Within(0.0001f));
            Assert.That(maximum.y, Is.EqualTo(1f));
        }

        private static void AssertBinding(
            InputActionAsset asset,
            string actionPath,
            string controlPath)
        {
            var action = asset.FindAction(actionPath, true);
            Assert.That(
                action.bindings.Any(binding => binding.path == controlPath),
                Is.True,
                actionPath + " lacks " + controlPath);
        }
    }
}
