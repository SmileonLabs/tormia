using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyCharacterPartAdapterTests
    {
        private readonly List<Object> objectsToDestroy = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var target in objectsToDestroy)
            {
                if (target != null)
                {
                    Object.DestroyImmediate(target);
                }
            }

            objectsToDestroy.Clear();
        }

        [Test]
        public void ActionDrivenUnequipRebuildsCapabilities()
        {
            var setup = CreateSetup(new OntologyCharacterPartDefinition
            {
                partId = "Part_Shoes_Base",
                slot = "Feet",
                rendererPath = "Shoes",
                enabledByDefault = true,
                facts = new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.GrantsCapability,
                        obj = OntologyObjects.SwampResistance
                    }
                }
            });

            setup.Adapter.InjectActivePartFacts();
            Assert.That(setup.Bootstrap.World.HasFact("Player", OntologyPredicates.HasCapability, OntologyObjects.SwampResistance), Is.True);

            setup.Bootstrap.World.AddFact("Player", OntologyPredicates.UnequipPart, "Part_Shoes_Base");
            setup.Adapter.SyncFromWorldFacts();

            Assert.That(setup.Renderers["Shoes"].enabled, Is.False);
            Assert.That(setup.Bootstrap.World.HasFact("Player", OntologyPredicates.HasCapability, OntologyObjects.SwampResistance), Is.False);
        }

        [Test]
        public void RebuildingPartsPreservesCapabilitiesOwnedByAnotherSource()
        {
            var setup = CreateSetup(new OntologyCharacterPartDefinition
            {
                partId = "Part_Shoes_Base",
                slot = "Feet",
                rendererPath = "Shoes",
                enabledByDefault = true,
                facts = new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.GrantsCapability,
                        obj = OntologyObjects.SwampResistance
                    }
                }
            });
            setup.Bootstrap.World.AddFactContribution(
                "Player",
                OntologyPredicates.HasCapability,
                "Interaction",
                OntologyFactOrigin.ActorProfile);

            setup.Adapter.InjectActivePartFacts();
            setup.Adapter.InjectActivePartFacts();

            Assert.That(
                setup.Bootstrap.World.HasFact(
                    "Player",
                    OntologyPredicates.HasCapability,
                    "Interaction"),
                Is.True);
            Assert.That(
                setup.Bootstrap.World.HasFact(
                    "Player",
                    OntologyPredicates.HasCapability,
                    OntologyObjects.SwampResistance),
                Is.True);
        }

        [Test]
        public void EquippingHairKeepsUpperAndLowerBodyEnabled()
        {
            var setup = CreateSetup(
                Part("Part_Hairstyle_Base", "Hair", "Hair", false),
                Part("Part_TShirt_Base", "UpperBody", "Shirt", true),
                Part("Part_Pants_Base", "LowerBody", "Pants", true));

            setup.Adapter.InjectActivePartFacts();
            Assert.That(setup.Adapter.CanEquipPart("Part_Hairstyle_Base", out var hairReason), Is.True, hairReason);
            Assert.That(setup.Adapter.EquipPart("Part_Hairstyle_Base"), Is.True);

            Assert.That(setup.Renderers["Hair"].enabled, Is.True);
            Assert.That(setup.Renderers["Shirt"].enabled, Is.True);
            Assert.That(setup.Renderers["Pants"].enabled, Is.True);
        }

        [Test]
        public void EquippingFullBodyDisablesExplicitlyConflictingSlots()
        {
            var fullBody = Part("Part_FullBody_Base", "FullBody", "Full_body", false);
            fullBody.facts = new[]
            {
                new OntologyFactEntry { predicate = OntologyPredicates.ConflictsWithSlot, obj = "UpperBody" },
                new OntologyFactEntry { predicate = OntologyPredicates.ConflictsWithSlot, obj = "LowerBody" },
                new OntologyFactEntry { predicate = OntologyPredicates.ConflictsWithSlot, obj = "Outerwear" }
            };

            var setup = CreateSetup(
                fullBody,
                Part("Part_TShirt_Base", "UpperBody", "Shirt", true),
                Part("Part_Pants_Base", "LowerBody", "Pants", true),
                Part("Part_Outerwear_Base", "Outerwear", "Outerwear", true),
                Part("Part_Hairstyle_Base", "Hair", "Hair", true));

            setup.Adapter.InjectActivePartFacts();
            Assert.That(setup.Adapter.CanEquipPart("Part_FullBody_Base", out var fullBodyReason), Is.True, fullBodyReason);
            Assert.That(setup.Adapter.EquipPart("Part_FullBody_Base"), Is.True);

            Assert.That(setup.Renderers["Shirt"].enabled, Is.False);
            Assert.That(setup.Renderers["Pants"].enabled, Is.False);
            Assert.That(setup.Renderers["Outerwear"].enabled, Is.False);
            Assert.That(setup.Renderers["Hair"].enabled, Is.True);
        }

        [Test]
        public void ReplacingFullBodyWithUpperBodyDoesNotRestorePreviousLowerBody()
        {
            var fullBody = Part("Part_FullBody_Base", "FullBody", "Full_body", false);
            fullBody.facts = new[]
            {
                new OntologyFactEntry { predicate = OntologyPredicates.ConflictsWithSlot, obj = "UpperBody" },
                new OntologyFactEntry { predicate = OntologyPredicates.ConflictsWithSlot, obj = "LowerBody" }
            };
            var shirt = Part("Part_Shirt", "UpperBody", "Shirt", true);
            var pants = Part("Part_Pants", "LowerBody", "Pants", true);
            var setup = CreateSetup(fullBody, shirt, pants);

            setup.Adapter.ApplyDefaultPreset();
            setup.Adapter.InjectActivePartFacts();
            Assert.That(setup.Adapter.EquipPart(fullBody.partId), Is.True);
            Assert.That(setup.Adapter.EquipPart(shirt.partId), Is.True);

            Assert.That(setup.Adapter.IsPartEquipped(fullBody.partId), Is.False);
            Assert.That(setup.Adapter.IsPartEquipped(shirt.partId), Is.True);
            Assert.That(setup.Adapter.IsPartEquipped(pants.partId), Is.False);
            Assert.That(setup.Renderers["Shirt"].enabled, Is.True);
            Assert.That(setup.Renderers["Pants"].enabled, Is.False);
        }

        [Test]
        public void ClothingAndFootwearCanBeRemovedWhileBodyAndFaceRemainRequired()
        {
            var body = Part("Part_Body", "Body", "Body", true);
            body.required = true;
            var face = Part("Part_Face", "Face", "Face", true);
            face.required = true;
            var shirt = Part("Part_Shirt", "UpperBody", "Shirt", true);
            var pants = Part("Part_Pants", "LowerBody", "Pants", true);
            var shoes = Part("Part_Shoes", "Footwear", "Shoes", true);
            var setup = CreateSetup(body, face, shirt, pants, shoes);

            setup.Adapter.ApplyDefaultPreset();
            setup.Adapter.InjectActivePartFacts();
            Assert.That(setup.Adapter.UnequipPart(shirt.partId), Is.True);
            Assert.That(setup.Adapter.UnequipPart(pants.partId), Is.True);
            Assert.That(setup.Adapter.UnequipPart(shoes.partId), Is.True);

            Assert.That(setup.Adapter.IsPartEquipped(body.partId), Is.True);
            Assert.That(setup.Adapter.IsPartEquipped(face.partId), Is.True);
            Assert.That(setup.Adapter.IsPartEquipped(shirt.partId), Is.False);
            Assert.That(setup.Adapter.IsPartEquipped(pants.partId), Is.False);
            Assert.That(setup.Adapter.IsPartEquipped(shoes.partId), Is.False);
            Assert.That(setup.Adapter.UnequipPart(body.partId), Is.False);
            Assert.That(setup.Adapter.UnequipPart(face.partId), Is.False);
        }

        [Test]
        public void BodyPartCannotBeRemovedByThumbnailToggle()
        {
            var body = Part("Part_Body", "Body", "Body", true);
            body.required = true;
            var setup = CreateSetup(body);
            setup.Adapter.ApplyDefaultPreset();
            setup.Adapter.InjectActivePartFacts();

            Assert.That(setup.Adapter.CanUnequipPart(body.partId, out var reason), Is.False);
            Assert.That(reason, Is.EqualTo(OntologyCharacterPartAdapter.FailureRequiredPart));
            Assert.That(setup.Adapter.UnequipPart(body.partId), Is.False);
            Assert.That(setup.Adapter.IsPartEquipped(body.partId), Is.True);
            Assert.That(setup.Renderers["Body"].enabled, Is.True);
        }

        [Test]
        public void EnsureAppearanceInitializedPublishesDefaultsEvenAfterRendererPresetRan()
        {
            var shirt = Part("Part_Shirt", "UpperBody", "Shirt", true);
            var pants = Part("Part_Pants", "LowerBody", "Pants", true);
            var setup = CreateSetup(shirt, pants);

            setup.Adapter.ApplyDefaultPreset();
            Assert.That(setup.Adapter.GetEquippedPartIds(), Is.Empty);
            setup.Adapter.EnsureAppearanceInitialized();

            Assert.That(setup.Adapter.IsPartEquipped(shirt.partId), Is.True);
            Assert.That(setup.Adapter.IsPartEquipped(pants.partId), Is.True);
        }

        [Test]
        public void EquippingAnotherVariantInSameSlotKeepsOnlyMatchingFact()
        {
            var first = Part("Part_Shoes_A", "Footwear", "Shoes", false);
            var second = Part("Part_Shoes_B", "Footwear", "Shoes", false);
            first.variantPrefab = CreateVariantPrefab("ShoesA");
            second.variantPrefab = CreateVariantPrefab("ShoesB");
            var setup = CreateSetup(first, second);

            Assert.That(setup.Adapter.EquipPart(first.partId), Is.True);
            Assert.That(setup.Adapter.EquipPart(second.partId), Is.True);

            Assert.That(setup.Bootstrap.World.HasFact("Player", OntologyPredicates.EquippedPart, first.partId), Is.False);
            Assert.That(setup.Bootstrap.World.HasFact("Player", OntologyPredicates.EquippedPart, second.partId), Is.True);
            Assert.That(setup.Adapter.IsPartEquipped(first.partId), Is.False);
            Assert.That(setup.Adapter.IsPartEquipped(second.partId), Is.True);
        }

        [Test]
        public void ReplacingPantsDoesNotDisableEquippedHatThroughUnequippedCostumeLink()
        {
            var pantsBase = Part("Part_Pants_Base", "LowerBody", "Pants", true);
            pantsBase.variantPrefab = CreateVariantPrefab("PantsBase");
            var pantsVariant = Part("Part_Pants_Variant", "LowerBody", "Pants", false);
            pantsVariant.variantPrefab = CreateVariantPrefab("PantsVariant");
            var regularHat = Part("Part_Hat", "Headwear", "Hat", false);
            regularHat.variantPrefab = CreateVariantPrefab("RegularHat");
            var costumeHat = Part("Part_Costume_Hat", "Headwear", "Hat", false);
            costumeHat.variantPrefab = CreateVariantPrefab("CostumeHat");
            costumeHat.visibleInCustomization = false;
            var costume = Part("Part_Costume", "FullBody", "Full_body", false);
            costume.linkedPartIds = new[] { costumeHat.partId };
            costume.facts = new[]
            {
                new OntologyFactEntry
                    { predicate = OntologyPredicates.ConflictsWithSlot, obj = "LowerBody" },
                new OntologyFactEntry
                    { predicate = OntologyPredicates.ConflictsWithSlot, obj = "Headwear" }
            };
            var setup = CreateSetup(
                pantsBase,
                pantsVariant,
                regularHat,
                costumeHat,
                costume);

            setup.Adapter.ApplyDefaultPreset();
            setup.Adapter.InjectActivePartFacts();
            Assert.That(setup.Adapter.EquipPart(regularHat.partId), Is.True);
            Assert.That(setup.Adapter.EquipPart(pantsVariant.partId), Is.True);

            Assert.That(setup.Adapter.IsPartEquipped(regularHat.partId), Is.True);
            Assert.That(setup.Adapter.IsPartEquipped(pantsVariant.partId), Is.True);
            Assert.That(setup.Renderers["Hat"].enabled, Is.True);
            Assert.That(
                ((SkinnedMeshRenderer)setup.Renderers["Hat"]).sharedMesh,
                Is.EqualTo(regularHat.variantPrefab
                    .GetComponentInChildren<SkinnedMeshRenderer>(true)
                    .sharedMesh));
        }

        [Test]
        public void VariantCopiesAuthoredLocalBoundsAndBaseSelectionRestoresOriginalMesh()
        {
            var basePart = Part("Part_Shirt_Base", "UpperBody", "Shirt", true);
            basePart.useBaseRendererMesh = true;
            var variant = Part("Part_Shirt_Variant", "UpperBody", "Shirt", false);
            variant.variantPrefab = CreateVariantPrefab("ShirtVariant");
            var sourceRenderer = variant.variantPrefab.GetComponent<SkinnedMeshRenderer>();
            sourceRenderer.localBounds = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f));

            var setup = CreateSetup(basePart, variant);
            var targetRenderer = (SkinnedMeshRenderer)setup.Renderers["Shirt"];
            var baseMesh = Track(new Mesh { name = "BaseShirtMesh" });
            targetRenderer.sharedMesh = baseMesh;

            setup.Adapter.ApplyDefaultPreset();
            setup.Adapter.InjectActivePartFacts();
            Assert.That(setup.Adapter.EquipPart(variant.partId), Is.True, "Variant should equip.");
            Assert.That(targetRenderer.sharedMesh, Is.EqualTo(sourceRenderer.sharedMesh));
            Assert.That(targetRenderer.localBounds, Is.EqualTo(sourceRenderer.localBounds));

            Assert.That(setup.Adapter.EquipPart(basePart.partId), Is.True, "Base part should replace the variant.");
            Assert.That(targetRenderer.sharedMesh, Is.EqualTo(baseMesh));
        }

        [Test]
        public void LinkedCostumePartsEquipAndUnequipAtomically()
        {
            var costume = Part("Part_Costume", "FullBody", "Outerwear", false);
            costume.linkedPartIds = new[] { "Part_Costume_Hat" };
            costume.facts = new[]
            {
                new OntologyFactEntry { predicate = OntologyPredicates.ConflictsWithSlot, obj = "UpperBody" },
                new OntologyFactEntry { predicate = OntologyPredicates.ConflictsWithSlot, obj = "LowerBody" },
                new OntologyFactEntry { predicate = OntologyPredicates.ConflictsWithSlot, obj = "Headwear" }
            };
            var costumeHat = Part("Part_Costume_Hat", "Headwear", "Hat", false);
            costumeHat.visibleInCustomization = false;
            costumeHat.variantPrefab = CreateVariantPrefab("CostumeHat");
            var regularHat = Part("Part_Hat", "Headwear", "Hat", true);
            regularHat.variantPrefab = CreateVariantPrefab("RegularHat");

            var setup = CreateSetup(
                costume,
                costumeHat,
                Part("Part_Shirt", "UpperBody", "Shirt", true),
                Part("Part_Pants", "LowerBody", "Pants", true),
                regularHat);

            setup.Adapter.ApplyDefaultPreset();
            setup.Adapter.InjectActivePartFacts();
            Assert.That(setup.Adapter.EquipPart(costume.partId), Is.True);

            Assert.That(setup.Renderers["Outerwear"].enabled, Is.True);
            Assert.That(setup.Renderers["Hat"].enabled, Is.True);
            Assert.That(setup.Renderers["Shirt"].enabled, Is.False);
            Assert.That(setup.Renderers["Pants"].enabled, Is.False);
            Assert.That(setup.Bootstrap.World.HasFact("Player", OntologyPredicates.EquippedPart, costume.partId), Is.True);
            Assert.That(setup.Bootstrap.World.HasFact("Player", OntologyPredicates.EquippedPart, costumeHat.partId), Is.True);

            Assert.That(setup.Adapter.UnequipPart(costume.partId), Is.True);
            Assert.That(setup.Renderers["Outerwear"].enabled, Is.False, "Costume renderer should be disabled.");
            Assert.That(setup.Renderers["Hat"].enabled, Is.False);
            Assert.That(setup.Renderers["Shirt"].enabled, Is.False);
            Assert.That(setup.Renderers["Pants"].enabled, Is.False);
            Assert.That(
                setup.Bootstrap.World.HasFact("Player", OntologyPredicates.EquippedPart, costumeHat.partId),
                Is.False,
                "Linked costume hat fact should be removed.");
            Assert.That(setup.Bootstrap.World.HasFact("Player", OntologyPredicates.EquippedPart, regularHat.partId), Is.False);
        }

        [Test]
        public void EquippingRegularHatOverLinkedCostumeHatKeepsCostumeBodyEquipped()
        {
            var costume = Part("Part_Costume", "FullBody", "Full_body", false);
            costume.linkedPartIds = new[] { "Part_Costume_Hat" };
            costume.facts = new[]
            {
                new OntologyFactEntry
                    { predicate = OntologyPredicates.ConflictsWithSlot, obj = "UpperBody" },
                new OntologyFactEntry
                    { predicate = OntologyPredicates.ConflictsWithSlot, obj = "LowerBody" }
            };
            var costumeHat = Part("Part_Costume_Hat", "Headwear", "Hat", false);
            costumeHat.visibleInCustomization = false;
            costumeHat.variantPrefab = CreateVariantPrefab("CostumeHat");
            var regularHat = Part("Part_Hat", "Headwear", "Hat", false);
            regularHat.variantPrefab = CreateVariantPrefab("RegularHat");
            var shirt = Part("Part_Shirt", "UpperBody", "Shirt", true);
            var pants = Part("Part_Pants", "LowerBody", "Pants", true);
            var setup = CreateSetup(costume, costumeHat, regularHat, shirt, pants);

            setup.Adapter.ApplyDefaultPreset();
            setup.Adapter.InjectActivePartFacts();
            Assert.That(setup.Adapter.EquipPart(costume.partId), Is.True);
            Assert.That(setup.Adapter.EquipPart(regularHat.partId), Is.True);

            Assert.That(setup.Adapter.IsPartEquipped(costume.partId), Is.True);
            Assert.That(setup.Adapter.IsPartEquipped(costumeHat.partId), Is.False);
            Assert.That(setup.Adapter.IsPartEquipped(regularHat.partId), Is.True);
            Assert.That(setup.Adapter.IsPartEquipped(shirt.partId), Is.False);
            Assert.That(setup.Adapter.IsPartEquipped(pants.partId), Is.False);
            Assert.That(setup.Renderers["Full_body"].enabled, Is.True);
            Assert.That(setup.Renderers["Hat"].enabled, Is.True);
        }

        private Setup CreateSetup(params OntologyCharacterPartDefinition[] definitions)
        {
            var root = Track(new GameObject("PartAdapterTestRoot"));
            var actorObject = root.AddComponent<OntologyObject>();
            actorObject.ConfigureOntologyData(
                "Player",
                new[] { OntologyConcepts.Actor },
                System.Array.Empty<OntologyFactEntry>());
            var bootstrap = root.AddComponent<OntologyWorldBootstrap>();
            bootstrap.ResetWorld(logReport: false);

            var visualRoot = Track(new GameObject("VisualRoot"));
            visualRoot.transform.SetParent(root.transform);
            var renderers = new Dictionary<string, Renderer>();
            foreach (var definition in definitions)
            {
                if (!renderers.TryGetValue(definition.rendererPath, out var renderer))
                {
                    var partObject = Track(new GameObject(definition.rendererPath));
                    partObject.transform.SetParent(visualRoot.transform);
                    renderer = partObject.AddComponent<SkinnedMeshRenderer>();
                    renderer.enabled = false;
                    renderers.Add(definition.rendererPath, renderer);
                }

                renderer.enabled |= definition.enabledByDefault;
            }

            var database = Track(ScriptableObject.CreateInstance<OntologyCharacterPartDatabase>());
            SetField(database, "definitions", new List<OntologyCharacterPartDefinition>(definitions));

            var adapter = root.AddComponent<OntologyCharacterPartAdapter>();
            SetField(adapter, "bootstrap", bootstrap);
            SetField(adapter, "partDatabase", database);
            SetField(adapter, "visualRoot", visualRoot.transform);
            SetField(adapter, "actorObject", actorObject);

            return new Setup(bootstrap, adapter, renderers);
        }

        private static OntologyCharacterPartDefinition Part(string id, string slot, string path, bool enabled)
        {
            return new OntologyCharacterPartDefinition
            {
                partId = id,
                slot = slot,
                rendererPath = path,
                enabledByDefault = enabled
            };
        }

        private GameObject CreateVariantPrefab(string name)
        {
            var prefab = Track(new GameObject(name));
            var renderer = prefab.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = Track(new Mesh { name = name + "Mesh" });
            return prefab;
        }

        private T Track<T>(T target) where T : Object
        {
            objectsToDestroy.Add(target);
            return target;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {fieldName}");
            field.SetValue(target, value);
        }

        private readonly struct Setup
        {
            public Setup(OntologyWorldBootstrap bootstrap, OntologyCharacterPartAdapter adapter, Dictionary<string, Renderer> renderers)
            {
                Bootstrap = bootstrap;
                Adapter = adapter;
                Renderers = renderers;
            }

            public OntologyWorldBootstrap Bootstrap { get; }
            public OntologyCharacterPartAdapter Adapter { get; }
            public Dictionary<string, Renderer> Renderers { get; }
        }
    }
}
