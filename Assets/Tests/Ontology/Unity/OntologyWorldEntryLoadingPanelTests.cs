using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core.Tests
{
    public sealed class OntologyWorldEntryLoadingPanelTests
    {
        [TestCase(0.25f, 50f)]
        [TestCase(1f, 200f)]
        public void ProgressRevealChangesWidthWithoutScalingTheSlicedFill(
            float progress,
            float expectedRevealWidth)
        {
            var root = new GameObject("LoadingPanel");
            root.SetActive(false);
            root.AddComponent<CanvasGroup>();
            var panel = root.AddComponent<OntologyWorldEntryLoadingPanel>();

            var bounds = NewRect("Bounds", root.transform, new Vector2(200f, 20f));
            var reveal = NewRect("Reveal", bounds, Vector2.zero);
            reveal.gameObject.AddComponent<RectMask2D>();
            var fillRect = NewRect("Fill", reveal, Vector2.zero);
            var fill = fillRect.gameObject.AddComponent<Image>();
            fill.type = Image.Type.Sliced;
            var marker = NewRect("Marker", bounds, new Vector2(20f, 20f));

            SetField(panel, "progressFill", fill);
            SetField(panel, "progressFillBounds", bounds);
            SetField(panel, "progressReveal", reveal);
            SetField(panel, "progressMarker", marker);
            SetField(panel, "displayedProgress", progress);
            Invoke(panel, "ApplyProgress");

            Assert.That(reveal.sizeDelta.x, Is.EqualTo(expectedRevealWidth).Within(0.01f));
            Assert.That(fillRect.sizeDelta.x, Is.EqualTo(200f).Within(0.01f));
            Assert.That(fill.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(marker.anchorMin.x, Is.EqualTo(progress).Within(0.001f));

            Object.DestroyImmediate(root);
        }

        private static RectTransform NewRect(string name, Transform parent, Vector2 size)
        {
            var value = new GameObject(name, typeof(RectTransform));
            value.transform.SetParent(parent, false);
            var rect = value.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            return rect;
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);

        private static void Invoke(object target, string name) =>
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(target, null);
    }
}
