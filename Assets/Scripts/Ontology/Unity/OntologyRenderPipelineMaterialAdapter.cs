using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presentation-only bridge for imported Built-in Render Pipeline materials.
    /// Imported packages stay untouched; project-owned ontology wrappers receive
    /// cached URP-compatible material instances at presentation time.
    /// </summary>
    public static class OntologyRenderPipelineMaterialAdapter
    {
        private const string UrpLitShaderName = "Universal Render Pipeline/Lit";
        private static readonly Dictionary<Material, Material> ConvertedMaterials =
            new Dictionary<Material, Material>();

        public static void ApplyTo(GameObject presentationRoot)
        {
            if (presentationRoot == null ||
                GraphicsSettings.currentRenderPipeline == null)
            {
                return;
            }

            var targetShader = Shader.Find(UrpLitShaderName);
            if (targetShader == null || !targetShader.isSupported)
            {
                return;
            }

            foreach (var renderer in
                     presentationRoot.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var index = 0; index < materials.Length; index++)
                {
                    var source = materials[index];
                    if (!RequiresUrpBridge(source))
                        continue;

                    materials[index] = GetOrCreateCompatibleMaterial(
                        source,
                        targetShader);
                    changed = true;
                }

                if (changed)
                    renderer.sharedMaterials = materials;
            }
        }

        public static Material CreateCompatibleMaterial(
            Material source,
            Shader targetShader)
        {
            if (source == null || targetShader == null)
                return null;

            var baseTexture = ReadTexture(source, "_BaseMap", "_MainTex");
            var baseScale = ReadTextureScale(source, "_BaseMap", "_MainTex");
            var baseOffset = ReadTextureOffset(source, "_BaseMap", "_MainTex");
            var baseColor = ReadColor(source, Color.white, "_BaseColor", "_Color");
            var normalTexture = ReadTexture(source, "_BumpMap");
            var metallicTexture = ReadTexture(source, "_MetallicGlossMap");
            var emissionTexture = ReadTexture(source, "_EmissionMap");
            var emissionColor = ReadColor(
                source,
                Color.black,
                "_EmissionColor");
            var metallic = ReadFloat(source, 0f, "_Metallic");
            var smoothness = ReadFloat(
                source,
                0.5f,
                "_Smoothness",
                "_Glossiness");

            var converted = new Material(targetShader)
            {
                name = source.name + " (TOV URP)",
                enableInstancing = source.enableInstancing,
                hideFlags = HideFlags.DontSave
            };
            SetTexture(converted, "_BaseMap", baseTexture, baseScale, baseOffset);
            SetTexture(converted, "_MainTex", baseTexture, baseScale, baseOffset);
            SetColor(converted, "_BaseColor", baseColor);
            SetColor(converted, "_Color", baseColor);
            SetFloat(converted, "_Metallic", metallic);
            SetFloat(converted, "_Smoothness", smoothness);
            SetFloat(converted, "_Glossiness", smoothness);

            if (normalTexture != null)
            {
                SetTexture(
                    converted,
                    "_BumpMap",
                    normalTexture,
                    Vector2.one,
                    Vector2.zero);
                converted.EnableKeyword("_NORMALMAP");
            }

            if (metallicTexture != null)
            {
                SetTexture(
                    converted,
                    "_MetallicGlossMap",
                    metallicTexture,
                    Vector2.one,
                    Vector2.zero);
                converted.EnableKeyword("_METALLICSPECGLOSSMAP");
            }

            // Some imported Standard materials retain stale serialized emission
            // values even though the source keyword is disabled. Enabling those
            // values in URP washes the albedo out to solid white.
            if (source.IsKeywordEnabled("_EMISSION") &&
                (emissionTexture != null ||
                 emissionColor.maxColorComponent > 0f))
            {
                SetTexture(
                    converted,
                    "_EmissionMap",
                    emissionTexture,
                    Vector2.one,
                    Vector2.zero);
                SetColor(converted, "_EmissionColor", emissionColor);
                converted.EnableKeyword("_EMISSION");
                converted.globalIlluminationFlags =
                    MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            return converted;
        }

        private static bool RequiresUrpBridge(Material source)
        {
            if (source == null || source.shader == null)
                return false;

            var shaderName = source.shader.name;
            return !source.shader.isSupported ||
                   shaderName == "Standard" ||
                   shaderName == "Standard (Specular setup)" ||
                   shaderName == "Hidden/InternalErrorShader";
        }

        private static Material GetOrCreateCompatibleMaterial(
            Material source,
            Shader targetShader)
        {
            if (ConvertedMaterials.TryGetValue(source, out var converted) &&
                converted != null)
            {
                return converted;
            }

            converted = CreateCompatibleMaterial(source, targetShader);
            ConvertedMaterials[source] = converted;
            return converted;
        }

        private static Texture ReadTexture(
            Material source,
            params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                if (source.HasProperty(propertyName))
                    return source.GetTexture(propertyName);
            }

            return null;
        }

        private static Vector2 ReadTextureScale(
            Material source,
            params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                if (source.HasProperty(propertyName))
                    return source.GetTextureScale(propertyName);
            }

            return Vector2.one;
        }

        private static Vector2 ReadTextureOffset(
            Material source,
            params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                if (source.HasProperty(propertyName))
                    return source.GetTextureOffset(propertyName);
            }

            return Vector2.zero;
        }

        private static Color ReadColor(
            Material source,
            Color fallback,
            params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                if (source.HasProperty(propertyName))
                    return source.GetColor(propertyName);
            }

            return fallback;
        }

        private static float ReadFloat(
            Material source,
            float fallback,
            params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                if (source.HasProperty(propertyName))
                    return source.GetFloat(propertyName);
            }

            return fallback;
        }

        private static void SetTexture(
            Material target,
            string propertyName,
            Texture texture,
            Vector2 scale,
            Vector2 offset)
        {
            if (!target.HasProperty(propertyName))
                return;

            target.SetTexture(propertyName, texture);
            target.SetTextureScale(propertyName, scale);
            target.SetTextureOffset(propertyName, offset);
        }

        private static void SetColor(
            Material target,
            string propertyName,
            Color color)
        {
            if (target.HasProperty(propertyName))
                target.SetColor(propertyName, color);
        }

        private static void SetFloat(
            Material target,
            string propertyName,
            float value)
        {
            if (target.HasProperty(propertyName))
                target.SetFloat(propertyName, value);
        }
    }
}
