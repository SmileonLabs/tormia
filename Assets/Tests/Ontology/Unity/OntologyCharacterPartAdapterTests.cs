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
        public void EquippingHairKeepsUpperAndLowerBodyEnabled()
        {
            var setup = CreateSetup(
                Part("Part_Hairstyle_Base", "Hair", "Hair", false),
                Part("Part_TShirt_Base", "UpperBody", "Shirt", true),
                Part("Part_Pants_Base", "LowerBody", "Pants", true));

            setup.Adapter.InjectActivePartFacts();
            Assert.That(setup.Adapter.EquipPart("Part_Hairstyle_Base"), Is.True);

            Assert.That(setup.Renderers["Hair"].enabled, Is.True);
            Assert.That(setup.Renderers["Shirt"].enabled, Is.True);
            Assert.That(setup.Renderers["Pants"].enabled, Is.True);
        }

        [Test]
        public void EquippingFullBodyDisablesExplicitlyConflictingSlots()
        {
            var fullBody = Part("Part_FullBody_Base", "FullBody", "FullBody", false);
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
            Assert.That(setup.Adapter.EquipPart("Part_FullBody_Base"), Is.True);

            Assert.That(setup.Renderers["Shirt"].enabled, Is.False);
            Assert.That(setup.Renderers["Pants"].enabled, Is.False);
            Assert.That(setup.Renderers["Outerwear"].enabled, Is.False);
            Assert.That(setup.Renderers["Hair"].enabled, Is.True);
        }

        private Setup CreateSetup(params OntologyCharacterPartDefinition[] definitions)
        {
            var root = Track(new GameObject("PartAdapterTestRoot"));
            var bootstrap = root.AddComponent<OntologyWorldBootstrap>();
            bootstrap.ResetWorld(logReport: false);

            var visualRoot = Track(new GameObject("VisualRoot"));
            visualRoot.transform.SetParent(root.transform);
            var renderers = new Dictionary<string, Renderer>();
            foreach (var definition in definitions)
            {
                var partObject = Track(new GameObject(definition.rendererPath));
                partObject.transform.SetParent(visualRoot.transform);
                var renderer = partObject.AddComponent<MeshRenderer>();
                renderer.enabled = definition.enabledByDefault;
                renderers.Add(definition.rendererPath, renderer);
            }

            var database = Track(ScriptableObject.CreateInstance<OntologyCharacterPartDatabase>());
            SetField(database, "definitions", new List<OntologyCharacterPartDefinition>(definitions));

            var adapter = root.AddComponent<OntologyCharacterPartAdapter>();
            SetField(adapter, "bootstrap", bootstrap);
            SetField(adapter, "partDatabase", database);
            SetField(adapter, "visualRoot", visualRoot.transform);

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
