using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Pools and presents combat VFX selected by canonical intent. Third-party
    /// prefab names stay inside the presentation catalog.
    /// </summary>
    public sealed class OntologyCombatVfxAdapter : MonoBehaviour
    {
        [SerializeField] private OntologyCombatCatalog catalog;
        [SerializeField] private Transform poolRoot;

        private readonly Dictionary<string, Queue<GameObject>> pools = new();
        private readonly List<ActiveVfx> active = new();

        public void Configure(OntologyCombatCatalog value)
        {
            catalog = value;
        }

        private void Awake()
        {
            if (poolRoot == null)
            {
                var root = new GameObject("CombatVfxPool");
                root.transform.SetParent(transform, false);
                poolRoot = root.transform;
            }
            WarmPools();
        }

        private void OnEnable()
        {
            OntologyCombatPresentationBus.VfxRequested += HandleVfxRequested;
        }

        private void OnDisable()
        {
            OntologyCombatPresentationBus.VfxRequested -= HandleVfxRequested;
            for (var index = active.Count - 1; index >= 0; index--)
            {
                Return(active[index].IntentId, active[index].Instance);
            }
            active.Clear();
        }

        private void LateUpdate()
        {
            for (var index = active.Count - 1; index >= 0; index--)
            {
                var item = active[index];
                if (item.Instance == null || Time.unscaledTime < item.ReleaseAt) continue;
                Return(item.IntentId, item.Instance);
                active.RemoveAt(index);
            }
        }

        private void WarmPools()
        {
            if (catalog == null || catalog.Vfx == null) return;
            foreach (var definition in catalog.Vfx)
            {
                if (!IsValid(definition)) continue;
                EnsurePool(definition.intentId);
                for (var index = pools[definition.intentId].Count;
                     index < Mathf.Max(1, definition.initialPoolSize);
                     index++)
                {
                    pools[definition.intentId].Enqueue(Create(definition));
                }
            }
        }

        private void HandleVfxRequested(OntologyCombatVfxSignal signal)
        {
            var definition = catalog != null ? catalog.FindVfx(signal.IntentId) : null;
            if (!IsValid(definition)) return;

            EnsurePool(definition.intentId);
            var instance = pools[definition.intentId].Count > 0
                ? pools[definition.intentId].Dequeue()
                : Create(definition);
            if (instance == null) return;

            var targetParent = definition.followAnchor && signal.Anchor != null
                ? signal.Anchor
                : null;
            instance.transform.SetParent(targetParent, false);
            if (targetParent != null)
            {
                instance.transform.localPosition = definition.localPosition;
                instance.transform.localRotation =
                    Quaternion.Euler(definition.localEulerAngles);
            }
            else
            {
                instance.transform.SetPositionAndRotation(
                    signal.WorldPosition + signal.WorldRotation * definition.localPosition,
                    signal.WorldRotation * Quaternion.Euler(definition.localEulerAngles));
            }
            instance.transform.localScale = definition.localScale;
            instance.SetActive(true);
            RestartParticles(instance);
            active.Add(new ActiveVfx(
                definition.intentId,
                instance,
                Time.unscaledTime + Mathf.Max(0.05f, definition.lifetimeSeconds)));
        }

        private GameObject Create(OntologyCombatVfxDefinition definition)
        {
            var instance = Instantiate(definition.prefab, poolRoot);
            instance.name = definition.intentId + "_Pooled";
            instance.SetActive(false);
            return instance;
        }

        private void Return(string intentId, GameObject instance)
        {
            if (instance == null) return;
            instance.SetActive(false);
            instance.transform.SetParent(poolRoot, false);
            EnsurePool(intentId);
            pools[intentId].Enqueue(instance);
        }

        private void EnsurePool(string intentId)
        {
            if (!pools.ContainsKey(intentId))
                pools.Add(intentId, new Queue<GameObject>());
        }

        private static void RestartParticles(GameObject instance)
        {
            foreach (var particles in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Clear(true);
                particles.Play(true);
            }
        }

        private static bool IsValid(OntologyCombatVfxDefinition definition) =>
            definition != null &&
            !string.IsNullOrWhiteSpace(definition.intentId) &&
            definition.prefab != null;

        private readonly struct ActiveVfx
        {
            public ActiveVfx(string intentId, GameObject instance, float releaseAt)
            {
                IntentId = intentId;
                Instance = instance;
                ReleaseAt = releaseAt;
            }

            public string IntentId { get; }
            public GameObject Instance { get; }
            public float ReleaseAt { get; }
        }
    }
}
