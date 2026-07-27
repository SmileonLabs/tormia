using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core.Tests
{
    public sealed class OntologyRuntimeSelectionMarkerTests
    {
        [Test]
        public void SelectedMesh_UsesSilhouetteOutlineWithoutGroundRectangle()
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            OntologyRuntimeSelectionMarker marker = null;
            try
            {
                marker = OntologyRuntimeSelectionMarker.Create();
                marker.SetTarget(target.transform);

                Assert.That(marker.GetComponent<LineRenderer>(), Is.Null);
                var proxy = marker.GetComponentInChildren<MeshRenderer>();
                Assert.That(proxy, Is.Not.Null);
                Assert.That(proxy.sharedMaterial, Is.Not.Null);
                Assert.That(
                    proxy.sharedMaterial.shader.name,
                    Is.EqualTo("Tormia/Ontology/SelectionOutline"));
                Assert.That(
                    proxy.transform.position,
                    Is.EqualTo(target.transform.position));
            }
            finally
            {
                if (marker != null)
                    Object.DestroyImmediate(marker.gameObject);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ClearingSelection_RemovesOutlineProxies()
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            OntologyRuntimeSelectionMarker marker = null;
            try
            {
                marker = OntologyRuntimeSelectionMarker.Create();
                marker.SetTarget(target.transform);
                Assert.That(
                    marker.GetComponentsInChildren<MeshRenderer>(true),
                    Is.Not.Empty);

                marker.SetTarget(null);
                Assert.That(
                    marker.GetComponentsInChildren<MeshRenderer>(true),
                    Is.Empty);
            }
            finally
            {
                if (marker != null)
                    Object.DestroyImmediate(marker.gameObject);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void WorldEditHandle_UsesCenteredIconOnlyRequestedOrder()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Ontology/UI/WorldEditContextHandle.prefab");
            Assert.That(prefab, Is.Not.Null);

            var panel = prefab.transform.Find("HandlePanel");
            Assert.That(panel, Is.Not.Null);
            var expected = new[]
            {
                "MoveButton",
                "RotateLeftButton",
                "RotateRightButton",
                "ScaleUpButton",
                "ScaleDownButton",
                "DuplicateButton",
                "OntologyButton",
                "DeleteButton"
            };

            for (var index = 0; index < expected.Length; index++)
            {
                var button = panel.GetChild(index);
                Assert.That(button.name, Is.EqualTo(expected[index]));

                var icon = button.Find("Icon") as RectTransform;
                Assert.That(icon, Is.Not.Null);
                Assert.That(icon.anchoredPosition, Is.EqualTo(Vector2.zero));
                Assert.That(icon.anchorMin, Is.EqualTo(Vector2.zero));
                Assert.That(icon.anchorMax, Is.EqualTo(Vector2.one));

                var image = icon.GetComponent<Image>();
                Assert.That(image, Is.Not.Null);
                Assert.That(image.sprite, Is.Not.Null);
                Assert.That(image.preserveAspect, Is.True);
            }
        }
    }
}
