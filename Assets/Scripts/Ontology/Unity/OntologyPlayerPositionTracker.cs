using UnityEngine;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyPlayerPositionTracker : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private Transform player;
        [SerializeField] private Transform gridOrigin;
        [SerializeField] private float tileSize = 1f;
        [SerializeField] private string actorId = "Player";
        [SerializeField] private string tileIdPrefix = "Tile";
        [SerializeField] private float updateInterval = 0.1f;
        [SerializeField] private bool runSimulationOnTileChanged = true;
        [SerializeField] private OntologyFactEntry[] transientFactsToCleanup =
        {
            new OntologyFactEntry { predicate = "status", obj = "Wet" },
            new OntologyFactEntry { predicate = "movement_state", obj = "Slowed" },
            new OntologyFactEntry { predicate = "exposed_to", obj = "ColdEnvironment" },
            new OntologyFactEntry { predicate = "animation_intent", obj = "ImpairedMovement" },
            new OntologyFactEntry { predicate = "animation_intent", obj = "Discomfort" }
        };

        private string currentTileId;
        private float nextUpdateTime;

        private void Awake()
        {
            EnsureReferences();
        }

        private void Start()
        {
            EnsureWorldReady();
            UpdateStandingTile(force: true);
        }

        private void Update()
        {
            if (Time.time < nextUpdateTime)
            {
                return;
            }

            nextUpdateTime = Time.time + Mathf.Max(0.01f, updateInterval);
            UpdateStandingTile(force: false);
        }

        public void ForceRefresh()
        {
            EnsureReferences();
            EnsureWorldReady();
            UpdateStandingTile(force: true);
        }

        private void UpdateStandingTile(bool force)
        {
            EnsureReferences();
            if (!EnsureWorldReady())
            {
                return;
            }

            var nextTileId = GetCurrentTileId();
            if (string.IsNullOrWhiteSpace(nextTileId))
            {
                return;
            }

            if (!force && nextTileId == currentTileId)
            {
                return;
            }

            var previousTileId = currentTileId;
            if (!string.IsNullOrWhiteSpace(previousTileId))
            {
                bootstrap.World.RemoveFact(actorId, "standing_on", previousTileId);
            }

            CleanupTransientFacts();

            if (bootstrap.World.AddFact(actorId, "standing_on", nextTileId) || force)
            {
                currentTileId = nextTileId;
                RecordStandingOnChanged(previousTileId, nextTileId);
                if (runSimulationOnTileChanged)
                {
                    bootstrap.RunSimulation();
                }
            }
        }

        private void CleanupTransientFacts()
        {
            if (transientFactsToCleanup == null)
            {
                return;
            }

            foreach (var fact in transientFactsToCleanup)
            {
                if (fact == null || string.IsNullOrWhiteSpace(fact.predicate) || string.IsNullOrWhiteSpace(fact.obj))
                {
                    continue;
                }

                bootstrap.World.RemoveFact(actorId, fact.predicate, fact.obj);
            }
        }

        private string GetCurrentTileId()
        {
            var trackedPlayer = player != null ? player : transform;
            var origin = gridOrigin != null ? gridOrigin.position : Vector3.zero;
            var safeTileSize = tileSize <= 0f ? 1f : tileSize;
            var local = trackedPlayer.position - origin;
            var x = Mathf.FloorToInt(local.x / safeTileSize);
            var y = Mathf.FloorToInt(local.z / safeTileSize);
            return tileIdPrefix + "_" + x + "_" + y;
        }

        private void RecordStandingOnChanged(string previousTileId, string nextTileId)
        {
            if (bootstrap == null || bootstrap.Session == null)
            {
                return;
            }

            var from = string.IsNullOrWhiteSpace(previousTileId) ? "None" : previousTileId;
            var ontologyEvent = new OntologyEvent(
                "PlayerStandingOnChanged",
                actorId + " moved from " + from + " to " + nextTileId);

            if (!string.IsNullOrWhiteSpace(previousTileId))
            {
                ontologyEvent.RemovedFacts.Add(new OntologyFact(actorId, "standing_on", previousTileId));
            }

            ontologyEvent.AddedFacts.Add(new OntologyFact(actorId, "standing_on", nextTileId));
            bootstrap.Session.EventHistory.Add(ontologyEvent);
        }

        private void EnsureReferences()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            if (player == null)
            {
                player = transform;
            }
        }

        private bool EnsureWorldReady()
        {
            if (bootstrap == null)
            {
                return false;
            }

            if (bootstrap.World == null || bootstrap.Session == null)
            {
                bootstrap.ResetWorld(logReport: false);
            }

            return bootstrap.World != null && bootstrap.Session != null;
        }
    }
}
