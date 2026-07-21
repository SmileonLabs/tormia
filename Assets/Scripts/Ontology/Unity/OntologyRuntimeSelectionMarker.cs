using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>Non-persistent visual marker; it never changes the selected world object.</summary>
    public sealed class OntologyRuntimeSelectionMarker : MonoBehaviour
    {
        private Transform target;
        private LineRenderer line;

        public static OntologyRuntimeSelectionMarker Create()
        {
            var root = new GameObject("RuntimeSelectionMarker");
            var marker = root.AddComponent<OntologyRuntimeSelectionMarker>();
            marker.line = root.AddComponent<LineRenderer>();
            marker.line.material = new Material(Shader.Find("Sprites/Default"));
            marker.line.widthMultiplier = 0.035f; marker.line.loop = true; marker.line.positionCount = 5;
            marker.line.startColor = marker.line.endColor = new Color(1f, 0.78f, 0.08f, 1f);
            marker.line.useWorldSpace = true; return marker;
        }

        public void SetTarget(Transform value) { target = value; gameObject.SetActive(value != null); }

        private void LateUpdate()
        {
            if (target == null) return;
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            var y = bounds.min.y + 0.03f; var min = bounds.min; var max = bounds.max;
            line.SetPositions(new[] { new Vector3(min.x, y, min.z), new Vector3(min.x, y, max.z), new Vector3(max.x, y, max.z), new Vector3(max.x, y, min.z), new Vector3(min.x, y, min.z) });
        }

        private void OnDestroy() { if (line != null && line.material != null) Destroy(line.material); }
    }
}
