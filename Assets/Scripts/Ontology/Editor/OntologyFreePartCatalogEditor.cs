using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public static class OntologyFreePartCatalogEditor
    {
        private const string DatabasePath = "Assets/Data/Ontology/CharacterPartDatabase.asset";
        private const string PrefabRoot = "Assets/ithappy/Creative_Characters_FREE/Prefabs";
        private const string ThumbnailRoot = "Assets/UI/CharacterCreatorThumbnails";

        [MenuItem("Tools/Ontology/Rebuild Free Character Part Catalog")]
        public static void RebuildCatalog()
        {
            var database = AssetDatabase.LoadAssetAtPath<OntologyCharacterPartDatabase>(DatabasePath);
            if (database == null)
            {
                Debug.LogError("[OntologyPartCatalog] CharacterPartDatabase was not found.");
                return;
            }

            var specs = CreateSpecs();
            var serialized = new SerializedObject(database);
            var definitions = serialized.FindProperty("definitions");
            Undo.RecordObject(database, "Rebuild Free Character Part Catalog");
            definitions.arraySize = specs.Count;

            for (var i = 0; i < specs.Count; i++)
            {
                WriteDefinition(definitions.GetArrayElementAtIndex(i), specs[i]);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
            Debug.Log($"[OntologyPartCatalog] Rebuilt {specs.Count} definitions for 34 prefab variants and one base-mesh part.");
        }

        private static List<PartSpec> CreateSpecs()
        {
            var specs = new List<PartSpec>
            {
                Part("Part_Body_Base", "Body 010", "Body", "Body", "Body/Body_010", true,
                    Fact("covers", "Body"), Fact("provides", "SkinSurface")),

                Part("Part_Face_Base", "Usual Expression", "Face", "Faces", "Faces/Male_emotion_usual_001", true,
                    Fact("covers", "Face"), Fact("style", "NeutralExpression")),
                Part("Part_Male_Emotion_Happy_002", "Happy Expression", "Face", "Faces", "Faces/Male_emotion_happy_002", false,
                    Fact("covers", "Face"), Fact("style", "HappyExpression")),
                Part("Part_Male_Emotion_Angry_003", "Angry Expression", "Face", "Faces", "Faces/Male_emotion_angry_003", false,
                    Fact("covers", "Face"), Fact("style", "AngryExpression")),

                Part("Part_Hairstyle_Base", "Hairstyle Male 001", "Hair", "Hairstyle", "Hairstyle/Hairstyle_Male_001", true,
                    Fact("covers", "Head"), Fact("style", "Hair")),
                Part("Part_Hairstyle_Male_005", "Hairstyle Male 005", "Hair", "Hairstyle", "Hairstyle/Hairstyle_Male_005", false,
                    Fact("covers", "Head"), Fact("style", "Hair")),
                Part("Part_Hairstyle_Male_Single_006", "Hairstyle Male Single 006", "Hair", "Hairstyle", "Hairstyle Single/Hairstyle_Male_Single_006", false,
                    Fact("covers", "Head"), Fact("style", "Hair")),

                BasePart("Part_TShirt_Base", "T-Shirt Base", "UpperBody", "T_Shirt", true,
                    Fact("covers", "Torso"), Fact("provides", "Warmth")),

                Part("Part_Pants_Base", "Pants 009", "LowerBody", "Pants", "Pants/Pants_009", true,
                    Fact("covers", "LowerBody")),
                Part("Part_Pants_010", "Pants 010", "LowerBody", "Pants", "Pants/Pants_010", false,
                    Fact("covers", "LowerBody")),
                Part("Part_Shorts_003", "Shorts 003", "LowerBody", "Pants", "Shorts/Shorts_003", false,
                    Fact("covers", "LowerBody"), Fact("style", "Shorts")),

                Part("Part_Shoes_Base", "Sneakers 009", "Footwear", "Shoes", "Shoes/Shoe_Sneakers_009", true,
                    Fact("covers", "Feet"), Fact(OntologyPredicates.GrantsCapability, OntologyObjects.SwampResistance)),
                Part("Part_Shoe_Slippers_002", "Slippers 002", "Footwear", "Shoes", "Shoes/Shoe_Slippers_002", false,
                    Fact("covers", "Feet"), Fact("style", "Slippers")),
                Part("Part_Shoe_Slippers_005", "Slippers 005", "Footwear", "Shoes", "Shoes/Shoe_Slippers_005", false,
                    Fact("covers", "Feet"), Fact("style", "Slippers")),
                Part("Part_Socks_008", "Socks 008", "Footwear", "Shoes", "Socks/Socks_008", false,
                    Fact("covers", "Feet"), Fact("style", "Socks")),

                Part("Part_Outerwear_Base", "Outwear 004", "Outerwear", "Outerwear", "Outwear/Outwear_004", false,
                    Fact("covers", "Torso"), Fact("provides", "Warmth"), Fact(OntologyPredicates.GrantsCapability, OntologyObjects.ColdProtection)),
                Part("Part_Outwear_043", "Outwear 043", "Outerwear", "Outerwear", "Outwear/Outwear_043", false,
                    Fact("covers", "Torso"), Fact("provides", "Warmth"), Fact(OntologyPredicates.GrantsCapability, OntologyObjects.ColdProtection)),
                Part("Part_Outwear_050", "Outwear 050", "Outerwear", "Outerwear", "Outwear/Outwear_050", false,
                    Fact("covers", "Torso"), Fact("provides", "Warmth"), Fact(OntologyPredicates.GrantsCapability, OntologyObjects.ColdProtection)),
                Part("Part_Mascot_002", "Mascot 002", "Outerwear", "Outerwear", "Mascots/Mascot_002", false,
                    Fact("covers", "Torso"), Fact("style", "Mascot")),

                Part("Part_Outfit_010", "Outfit 010", "FullBody", "Outerwear", "Outfit/Outfit_010", false,
                    Fact("covers", "Torso"), Fact("covers", "LowerBody"),
                    Fact(OntologyPredicates.ConflictsWithSlot, "UpperBody"),
                    Fact(OntologyPredicates.ConflictsWithSlot, "LowerBody"),
                    Fact(OntologyPredicates.ConflictsWithSlot, "Outerwear"), Fact("style", "Outfit")),
                LinkedPart("Part_FullBody_Base", "Costume 13", "FullBody", "Outerwear", "Costumes/Costume_13_001", "Part_Costume_13_002", true,
                    Fact("covers", "Torso"), Fact("covers", "LowerBody"),
                    Fact(OntologyPredicates.ConflictsWithSlot, "UpperBody"),
                    Fact(OntologyPredicates.ConflictsWithSlot, "LowerBody"),
                    Fact(OntologyPredicates.ConflictsWithSlot, "Outerwear"),
                    Fact(OntologyPredicates.ConflictsWithSlot, "Headwear"), Fact("style", "Costume")),

                Part("Part_Hat_Base", "Hat 010", "Headwear", "Hat", "Hat/Hat_010", false,
                    Fact("covers", "Head"), Fact("style", "Headwear")),
                Part("Part_Hat_Single_008", "Hat Single 008", "Headwear", "Hat", "Hat Single/Hat_Single_008", false,
                    Fact("covers", "Head"), Fact("style", "Headwear")),
                Part("Part_Hat_Single_013", "Hat Single 013", "Headwear", "Hat", "Hat Single/Hat_Single_013", false,
                    Fact("covers", "Head"), Fact("style", "Headwear")),
                Part("Part_Hat_Single_016", "Hat Single 016", "Headwear", "Hat", "Hat Single/Hat_Single_016", false,
                    Fact("covers", "Head"), Fact("style", "Headwear")),
                LinkedSupport("Part_Costume_13_002", "Costume 13 Hat", "Headwear", "Hat", "Costumes/Costume_13_002",
                    Fact("covers", "Head"), Fact("style", "Costume")),

                Part("Part_Glasses_Base", "Glasses 006", "Eyewear", "Glasses", "Glasses/Glasses_006", false,
                    Fact("covers", "Eyes"), Fact("style", "Eyewear")),
                Part("Part_Glasses_004", "Glasses 004", "Eyewear", "Glasses", "Glasses/Glasses_004", false,
                    Fact("covers", "Eyes"), Fact("style", "Eyewear")),

                Part("Part_Gloves_Base", "Gloves 006", "Handwear", "Gloves", "Gloves/Gloves_006", false,
                    Fact("covers", "Hands"), Fact("provides", "HandProtection")),
                Part("Part_Gloves_014", "Gloves 014", "Handwear", "Gloves", "Gloves/Gloves_014", false,
                    Fact("covers", "Hands"), Fact("provides", "HandProtection")),

                Part("Part_Mustache_Base", "Mustache 011", "FacialHair", "Mustache", "Face Accessories/Mustache_011", false,
                    Fact("covers", "Face"), Fact("style", "FacialHair")),
                Part("Part_Mustache_003", "Mustache 003", "FacialHair", "Mustache", "Face Accessories/Mustache_003", false,
                    Fact("covers", "Face"), Fact("style", "FacialHair")),

                Part("Part_Accessories_Base", "Headphones 002", "Accessory", "Accessories", "Face Accessories/Headphones_002", false,
                    Fact("covers", "Ears"), Fact("style", "AudioGear")),
                Part("Part_Clown_Nose_001", "Clown Nose 001", "Accessory", "Accessories", "Face Accessories/Clown_nose_001", false,
                    Fact("covers", "Face"), Fact("style", "ClownNose")),
                Part("Part_Pacifier_001", "Pacifier 001", "Accessory", "Accessories", "Face Accessories/Pacifier_001", false,
                    Fact("covers", "Face"), Fact("style", "Pacifier"))
            };

            return specs;
        }

        private static PartSpec Part(
            string id, string displayName, string slot, string rendererPath, string assetStem, bool enabledByDefault,
            params OntologyFactEntry[] facts)
        {
            return new PartSpec(id, displayName, slot, rendererPath, assetStem, enabledByDefault, true, false, Array.Empty<string>(), facts);
        }

        private static PartSpec BasePart(
            string id, string displayName, string slot, string rendererPath, bool enabledByDefault,
            params OntologyFactEntry[] facts)
        {
            return new PartSpec(id, displayName, slot, rendererPath, null, enabledByDefault, true, true, Array.Empty<string>(), facts);
        }

        private static PartSpec LinkedPart(
            string id, string displayName, string slot, string rendererPath, string assetStem, string linkedPartId, bool visible,
            params OntologyFactEntry[] facts)
        {
            return new PartSpec(id, displayName, slot, rendererPath, assetStem, false, visible, false, new[] { linkedPartId }, facts);
        }

        private static PartSpec LinkedSupport(
            string id, string displayName, string slot, string rendererPath, string assetStem,
            params OntologyFactEntry[] facts)
        {
            return new PartSpec(id, displayName, slot, rendererPath, assetStem, false, false, false, Array.Empty<string>(), facts);
        }

        private static OntologyFactEntry Fact(string predicate, string obj)
        {
            return new OntologyFactEntry { predicate = predicate, obj = obj };
        }

        private static void WriteDefinition(SerializedProperty element, PartSpec spec)
        {
            element.FindPropertyRelative("partId").stringValue = spec.Id;
            element.FindPropertyRelative("displayName").stringValue = spec.DisplayName;
            element.FindPropertyRelative("slot").stringValue = spec.Slot;
            element.FindPropertyRelative("rendererPath").stringValue = spec.RendererPath;
            element.FindPropertyRelative("variantPrefab").objectReferenceValue = LoadPrefab(spec.AssetStem);
            element.FindPropertyRelative("useBaseRendererMesh").boolValue = spec.UseBaseRendererMesh;
            element.FindPropertyRelative("icon").objectReferenceValue = LoadIcon(spec);
            element.FindPropertyRelative("material").objectReferenceValue = null;
            element.FindPropertyRelative("enabledByDefault").boolValue = spec.EnabledByDefault;
            element.FindPropertyRelative("visibleInCustomization").boolValue = spec.Visible;

            var linkedPartIds = element.FindPropertyRelative("linkedPartIds");
            linkedPartIds.arraySize = spec.LinkedPartIds.Length;
            for (var i = 0; i < spec.LinkedPartIds.Length; i++)
            {
                linkedPartIds.GetArrayElementAtIndex(i).stringValue = spec.LinkedPartIds[i];
            }

            var facts = element.FindPropertyRelative("facts");
            facts.arraySize = spec.Facts.Length;
            for (var i = 0; i < spec.Facts.Length; i++)
            {
                var fact = facts.GetArrayElementAtIndex(i);
                fact.FindPropertyRelative("predicate").stringValue = spec.Facts[i].predicate;
                fact.FindPropertyRelative("obj").stringValue = spec.Facts[i].obj;
            }
        }

        private static GameObject LoadPrefab(string assetStem)
        {
            return string.IsNullOrWhiteSpace(assetStem)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/{assetStem}.prefab");
        }

        private static Sprite LoadIcon(PartSpec spec)
        {
            if (string.IsNullOrWhiteSpace(spec.AssetStem))
            {
                return AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Data/Ontology/CharacterPartThumbnails/Part_TShirt_Base.png");
            }

            var fileName = spec.AssetStem.Substring(spec.AssetStem.LastIndexOf('/') + 1);
            return AssetDatabase.LoadAssetAtPath<Sprite>($"{ThumbnailRoot}/{fileName}.png");
        }

        private readonly struct PartSpec
        {
            public PartSpec(
                string id, string displayName, string slot, string rendererPath, string assetStem,
                bool enabledByDefault, bool visible, bool useBaseRendererMesh, string[] linkedPartIds, OntologyFactEntry[] facts)
            {
                Id = id;
                DisplayName = displayName;
                Slot = slot;
                RendererPath = rendererPath;
                AssetStem = assetStem;
                EnabledByDefault = enabledByDefault;
                Visible = visible;
                UseBaseRendererMesh = useBaseRendererMesh;
                LinkedPartIds = linkedPartIds;
                Facts = facts;
            }

            public string Id { get; }
            public string DisplayName { get; }
            public string Slot { get; }
            public string RendererPath { get; }
            public string AssetStem { get; }
            public bool EnabledByDefault { get; }
            public bool Visible { get; }
            public bool UseBaseRendererMesh { get; }
            public string[] LinkedPartIds { get; }
            public OntologyFactEntry[] Facts { get; }
        }
    }
}
