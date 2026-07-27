using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Non-persistent Unity presentation for the selected world object.
    /// It mirrors render meshes with an expanded inverted-hull material and never
    /// changes the selected object, its ontology data, or its original materials.
    /// </summary>
    public sealed class OntologyRuntimeSelectionMarker : MonoBehaviour
    {
        private const string OutlineShaderName = "Tormia/Ontology/SelectionOutline";
        private static readonly Color OutlineColor = new(1f, .72f, .08f, 1f);

        private sealed class OutlineProxy
        {
            public Renderer source;
            public Renderer proxy;
        }

        private Transform target;
        private Material outlineMaterial;
        private readonly List<OutlineProxy> proxies = new();

        public static OntologyRuntimeSelectionMarker Create()
        {
            var root = new GameObject("RuntimeSelectionMarker");
            var marker = root.AddComponent<OntologyRuntimeSelectionMarker>();
            marker.EnsureMaterial();
            return marker;
        }

        public void SetTarget(Transform value)
        {
            if (target == value)
            {
                gameObject.SetActive(value != null);
                return;
            }

            target = value;
            RebuildProxies();
            gameObject.SetActive(value != null);
        }

        private void LateUpdate()
        {
            if (target == null) return;
            for (var index = proxies.Count - 1; index >= 0; index--)
            {
                var entry = proxies[index];
                if (entry.source == null || entry.proxy == null)
                {
                    if (entry.proxy != null) DestroyRuntimeObject(entry.proxy.gameObject);
                    proxies.RemoveAt(index);
                    continue;
                }

                var sourceTransform = entry.source.transform;
                var proxyTransform = entry.proxy.transform;
                proxyTransform.SetPositionAndRotation(
                    sourceTransform.position,
                    sourceTransform.rotation);
                proxyTransform.localScale = sourceTransform.lossyScale;
                entry.proxy.enabled =
                    entry.source.enabled &&
                    entry.source.gameObject.activeInHierarchy;
            }
        }

        private void RebuildProxies()
        {
            ClearProxies();
            if (target == null || !EnsureMaterial()) return;

            foreach (var source in target.GetComponentsInChildren<Renderer>(true))
            {
                if (source is MeshRenderer meshRenderer)
                {
                    CreateMeshProxy(meshRenderer);
                }
                else if (source is SkinnedMeshRenderer skinnedRenderer)
                {
                    CreateSkinnedProxy(skinnedRenderer);
                }
            }
        }

        private void CreateMeshProxy(MeshRenderer source)
        {
            var sourceFilter = source.GetComponent<MeshFilter>();
            if (sourceFilter == null || sourceFilter.sharedMesh == null) return;

            var proxyObject = new GameObject(
                "Outline_" + source.gameObject.name,
                typeof(MeshFilter),
                typeof(MeshRenderer));
            proxyObject.transform.SetParent(transform, false);
            proxyObject.GetComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
            var proxyRenderer = proxyObject.GetComponent<MeshRenderer>();
            ConfigureRenderer(proxyRenderer, source, sourceFilter.sharedMesh.subMeshCount);
            proxies.Add(new OutlineProxy { source = source, proxy = proxyRenderer });
        }

        private void CreateSkinnedProxy(SkinnedMeshRenderer source)
        {
            if (source.sharedMesh == null) return;

            var proxyObject = new GameObject(
                "Outline_" + source.gameObject.name,
                typeof(SkinnedMeshRenderer));
            proxyObject.transform.SetParent(transform, false);
            var proxyRenderer = proxyObject.GetComponent<SkinnedMeshRenderer>();
            proxyRenderer.sharedMesh = source.sharedMesh;
            proxyRenderer.bones = source.bones;
            proxyRenderer.rootBone = source.rootBone;
            proxyRenderer.localBounds = source.localBounds;
            proxyRenderer.updateWhenOffscreen = true;
            ConfigureRenderer(proxyRenderer, source, source.sharedMesh.subMeshCount);
            proxies.Add(new OutlineProxy { source = source, proxy = proxyRenderer });
        }

        private void ConfigureRenderer(Renderer proxy, Renderer source, int subMeshCount)
        {
            var materialCount = Mathf.Max(1, subMeshCount);
            var materials = new Material[materialCount];
            for (var index = 0; index < materials.Length; index++)
                materials[index] = outlineMaterial;

            proxy.sharedMaterials = materials;
            proxy.shadowCastingMode = ShadowCastingMode.Off;
            proxy.receiveShadows = false;
            proxy.lightProbeUsage = LightProbeUsage.Off;
            proxy.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var sourceTransform = source.transform;
            proxy.transform.SetPositionAndRotation(
                sourceTransform.position,
                sourceTransform.rotation);
            proxy.transform.localScale = sourceTransform.lossyScale;
        }

        private bool EnsureMaterial()
        {
            if (outlineMaterial != null) return true;
            var shader = Shader.Find(OutlineShaderName);
            if (shader == null)
            {
                Debug.LogError(
                    $"[{nameof(OntologyRuntimeSelectionMarker)}] " +
                    $"Shader '{OutlineShaderName}' was not found.");
                return false;
            }

            outlineMaterial = new Material(shader)
            {
                name = "Runtime Selection Outline"
            };
            outlineMaterial.SetColor("_OutlineColor", OutlineColor);
            outlineMaterial.SetFloat("_OutlineWidth", .025f);
            return true;
        }

        private void ClearProxies()
        {
            foreach (var entry in proxies)
            {
                if (entry.proxy != null) DestroyRuntimeObject(entry.proxy.gameObject);
            }
            proxies.Clear();
        }

        private void OnDestroy()
        {
            ClearProxies();
            if (outlineMaterial != null) DestroyRuntimeObject(outlineMaterial);
        }

        private static void DestroyRuntimeObject(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying)
                Destroy(value);
            else
                DestroyImmediate(value);
        }
    }
}
