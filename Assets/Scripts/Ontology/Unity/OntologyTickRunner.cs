using System.Collections;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyTickRunner : MonoBehaviour
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private float tickInterval = 1.0f;
        [SerializeField] private bool isRunning = true;

        private int currentTickId;
        private Coroutine tickRoutine;

        private void Start()
        {
            if (bootstrap == null)
            {
                bootstrap = FindFirstObjectByType<OntologyWorldBootstrap>();
            }

            if (isRunning)
            {
                tickRoutine = StartCoroutine(TickRoutine());
            }
        }

        private void OnDisable()
        {
            if (tickRoutine != null)
            {
                StopCoroutine(tickRoutine);
                tickRoutine = null;
            }
        }

        private IEnumerator TickRoutine()
        {
            while (isRunning)
            {
                yield return new WaitForSeconds(Mathf.Max(0.05f, tickInterval));
                RunNextTick();
            }
        }

        public void RunNextTick()
        {
            if (bootstrap == null)
            {
                bootstrap = FindFirstObjectByType<OntologyWorldBootstrap>();
            }

            if (bootstrap == null)
            {
                return;
            }

            if (bootstrap.World == null || bootstrap.Session == null)
            {
                bootstrap.ResetWorld(logReport: false);
            }

            if (bootstrap.World == null)
            {
                return;
            }

            currentTickId++;
            bootstrap.World.RemoveFacts("Simulation", "current_tick");
            bootstrap.World.AddFact("Simulation", "current_tick", "Tick_" + currentTickId);
            bootstrap.RunSimulation();
        }
    }
}
