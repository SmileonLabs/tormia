using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tormia.EditorTools
{
    public static class CharacterCreatorThumbnailGenerator
    {
        private const string BasePrefabPath = "Assets/ithappy/Creative_Characters_FREE/Prefabs/Base_Mesh.prefab";
        private const string ColorMaterialPath = "Assets/ithappy/Creative_Characters_FREE/Materials/Color.mat";
        private const string OutputFolder = "Assets/UI/CharacterCreatorThumbnails";
        private const int Size = 256;

        private static readonly PartDefinition[] Parts =
        {
            new("Body", "Body", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Body/Body_010.prefab", Frame.FullBody),

            new("Face", "Faces", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Faces/Male_emotion_usual_001.prefab", Frame.Head),
            new("Face", "Faces", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Faces/Male_emotion_happy_002.prefab", Frame.Head),
            new("Face", "Faces", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Faces/Male_emotion_angry_003.prefab", Frame.Head),

            new("Hair", "Hairstyle", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Hairstyle/Hairstyle_Male_001.prefab", Frame.Head),
            new("Hair", "Hairstyle", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Hairstyle/Hairstyle_Male_005.prefab", Frame.Head),
            new("Hair", "Hairstyle", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Hairstyle Single/Hairstyle_Male_Single_006.prefab", Frame.Head),

            new("Hat", "Hat", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Hat/Hat_010.prefab", Frame.Head),
            new("Hat", "Hat", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Hat Single/Hat_Single_008.prefab", Frame.Head),
            new("Hat", "Hat", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Hat Single/Hat_Single_013.prefab", Frame.Head),
            new("Hat", "Hat", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Hat Single/Hat_Single_016.prefab", Frame.Head),

            new("Glasses", "Glasses", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Glasses/Glasses_004.prefab", Frame.Head),
            new("Glasses", "Glasses", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Glasses/Glasses_006.prefab", Frame.Head),

            new("Outfit", "Outerwear", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Outfit/Outfit_010.prefab", Frame.FullBody),
            new("Outfit", "Outerwear", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Outwear/Outwear_004.prefab", Frame.UpperBody),
            new("Outfit", "Outerwear", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Outwear/Outwear_043.prefab", Frame.UpperBody),
            new("Outfit", "Outerwear", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Outwear/Outwear_050.prefab", Frame.UpperBody),
            new("Outfit", "Outerwear", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Mascots/Mascot_002.prefab", Frame.UpperBody),

            new("Pants", "Pants", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Pants/Pants_009.prefab", Frame.LowerBody),
            new("Pants", "Pants", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Pants/Pants_010.prefab", Frame.LowerBody),
            new("Pants", "Pants", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Shorts/Shorts_003.prefab", Frame.LowerBody),

            new("Gloves", "Gloves", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Gloves/Gloves_006.prefab", Frame.UpperBody),
            new("Gloves", "Gloves", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Gloves/Gloves_014.prefab", Frame.UpperBody),

            new("Costume", "Full_body", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Costumes/Costume_13_001.prefab", Frame.FullBody),
            new("Costume", "Full_body", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Costumes/Costume_13_002.prefab", Frame.FullBody),

            new("Shoes", "Shoes", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Shoes/Shoe_Slippers_002.prefab", Frame.Feet),
            new("Shoes", "Shoes", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Shoes/Shoe_Slippers_005.prefab", Frame.Feet),
            new("Shoes", "Shoes", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Shoes/Shoe_Sneakers_009.prefab", Frame.Feet),
            new("Shoes", "Shoes", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Socks/Socks_008.prefab", Frame.Feet),

            new("Accessories", "Accessories", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Face Accessories/Clown_nose_001.prefab", Frame.Head),
            new("Accessories", "Accessories", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Face Accessories/Headphones_002.prefab", Frame.Head),
            new("Accessories", "Accessories", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Face Accessories/Mustache_003.prefab", Frame.Head),
            new("Accessories", "Accessories", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Face Accessories/Mustache_011.prefab", Frame.Head),
            new("Accessories", "Accessories", "Assets/ithappy/Creative_Characters_FREE/Prefabs/Face Accessories/Pacifier_001.prefab", Frame.Head),
        };

        [MenuItem("Tools/Tormia/Generate Character Creator Thumbnails")]
        public static void GenerateThumbnails()
        {
            EnsureFolders();

            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(ColorMaterialPath);
            if (basePrefab == null)
            {
                Debug.LogError($"Missing base prefab: {BasePrefabPath}");
                return;
            }

            var rig = new GameObject("ThumbnailCaptureRig") { hideFlags = HideFlags.HideAndDontSave };
            var camera = CreateCamera(rig.transform);
            CreateLight(rig.transform);

            var generated = new List<string>();
            try
            {
                foreach (var part in Parts)
                {
                    var character = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
                    character.hideFlags = HideFlags.HideAndDontSave;
                    character.transform.SetParent(rig.transform, false);
                    character.transform.localPosition = Vector3.zero;
                    character.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

                    AssignMaterial(character, material);
                    var targetRenderer = ApplyPart(character, part);
                    ConfigureCamera(camera, targetRenderer, part.Frame);

                    var path = Render(camera, Sanitize(Path.GetFileNameWithoutExtension(part.PrefabPath)));
                    generated.Add(path);
                    Object.DestroyImmediate(character);
                }
            }
            finally
            {
                Object.DestroyImmediate(rig);
            }

            AssetDatabase.Refresh();
            ImportAsSprites(generated);
            Debug.Log($"Generated {generated.Count} character creator thumbnails.");
        }

        public static IReadOnlyList<string> PartPrefabPaths => Parts.Select(part => part.PrefabPath).ToArray();

        private static Camera CreateCamera(Transform parent)
        {
            var cameraObject = new GameObject("ThumbnailCamera") { hideFlags = HideFlags.HideAndDontSave };
            cameraObject.transform.SetParent(parent, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.11f, 0.16f, 1f);
            camera.orthographic = true;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 30f;
            camera.enabled = false;
            return camera;
        }

        private static void CreateLight(Transform parent)
        {
            var lightObject = new GameObject("ThumbnailLight") { hideFlags = HideFlags.HideAndDontSave };
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.localRotation = Quaternion.Euler(45f, 25f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.7f;
        }

        private static void AssignMaterial(GameObject character, Material material)
        {
            if (material == null)
            {
                return;
            }

            foreach (var renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.sharedMaterial = material;
                renderer.enabled = true;
            }
        }

        private static SkinnedMeshRenderer ApplyPart(GameObject character, PartDefinition part)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(part.PrefabPath);
            var source = prefab != null ? prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            var target = character.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault(renderer => renderer.name == part.TargetRendererName);

            if (source == null || source.sharedMesh == null || target == null)
            {
                Debug.LogWarning($"Thumbnail part mapping failed: {part.PrefabPath}");
                return target;
            }

            target.sharedMesh = source.sharedMesh;
            target.localBounds = source.sharedMesh.bounds;
            target.updateWhenOffscreen = true;
            return target;
        }

        private static void ConfigureCamera(Camera camera, SkinnedMeshRenderer targetRenderer, Frame frame)
        {
            if (targetRenderer != null)
            {
                var bounds = targetRenderer.bounds;
                var center = bounds.center;
                var radius = Mathf.Max(bounds.extents.x, bounds.extents.y);
                if (radius > 0.01f)
                {
                    camera.transform.position = center + new Vector3(0f, 0f, -6f);
                    camera.transform.LookAt(center);
                    camera.orthographicSize = Mathf.Max(radius * 1.28f, 0.22f);
                    return;
                }
            }

            var target = frame switch
            {
                Frame.Head => new Vector3(0f, 1.53f, 0f),
                Frame.UpperBody => new Vector3(0f, 1.12f, 0f),
                Frame.LowerBody => new Vector3(0f, 0.58f, 0f),
                Frame.Feet => new Vector3(0f, 0.18f, 0f),
                _ => new Vector3(0f, 0.9f, 0f),
            };

            camera.transform.position = target + new Vector3(0f, 0f, -6f);
            camera.transform.LookAt(target);
            camera.orthographicSize = frame switch
            {
                Frame.Head => 0.52f,
                Frame.UpperBody => 0.78f,
                Frame.LowerBody => 0.72f,
                Frame.Feet => 0.38f,
                _ => 1.35f,
            };
        }

        private static string Render(Camera camera, string fileName)
        {
            var renderTexture = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            camera.targetTexture = renderTexture;
            camera.Render();

            RenderTexture.active = renderTexture;
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            texture.Apply();

            var path = $"{OutputFolder}/{fileName}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());

            Object.DestroyImmediate(texture);
            camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTexture);
            return path;
        }

        private static void ImportAsSprites(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/UI"))
            {
                AssetDatabase.CreateFolder("Assets", "UI");
            }

            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                AssetDatabase.CreateFolder("Assets/UI", "CharacterCreatorThumbnails");
            }
        }

        private static string Sanitize(string value)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value.Replace(' ', '_');
        }

        private readonly struct PartDefinition
        {
            public readonly string Category;
            public readonly string TargetRendererName;
            public readonly string PrefabPath;
            public readonly Frame Frame;

            public PartDefinition(string category, string targetRendererName, string prefabPath, Frame frame)
            {
                Category = category;
                TargetRendererName = targetRendererName;
                PrefabPath = prefabPath;
                Frame = frame;
            }
        }

        private enum Frame
        {
            FullBody,
            UpperBody,
            LowerBody,
            Head,
            Feet
        }
    }
}
