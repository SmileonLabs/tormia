using System.Collections;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyWaterOccupancySensorTests
    {
        [UnityTest]
        public IEnumerator GenericObjectAddsAndRemovesWaterOccupancyObservation()
        {
            var testOrigin = new Vector3(10000f, 0f, 10000f);
            var testScene = SceneManager.CreateScene("OntologyWaterOccupancySensorTest");
            SceneManager.SetActiveScene(testScene);

            var bootstrapObject = new GameObject("TestBootstrap");
            var bootstrap = bootstrapObject.AddComponent<OntologyWorldBootstrap>();

            var waterObject = new GameObject("TestWater");
            waterObject.transform.position = testOrigin;
            var waterOntology = waterObject.AddComponent<OntologyObject>();
            waterOntology.ConfigureOntologyData(
                "TestWater",
                new[] { OntologyConcepts.WaterRegion },
                new OntologyFactEntry[0]);
            var waterCollider = waterObject.AddComponent<BoxCollider>();
            waterCollider.isTrigger = true;
            var waterVolume = waterObject.AddComponent<OntologyWaterRegionVolume>();

            var observedObject = new GameObject("TestFloatable");
            observedObject.transform.position = testOrigin;
            var observedOntology = observedObject.AddComponent<OntologyObject>();
            observedOntology.ConfigureOntologyData(
                "TestFloatable",
                new[] { OntologyConcepts.FloatableObject },
                new OntologyFactEntry[0]);
            observedObject.AddComponent<BoxCollider>();
            var sensor = observedObject.AddComponent<OntologyWaterOccupancySensor>();
            sensor.Configure(bootstrap);

            yield return null;
            bootstrap.ResetWorld(logReport: false);

            yield return new WaitForFixedUpdate();
            Assert.That(
                bootstrap.World.HasFact("TestFloatable", OntologyPredicates.Occupies, "TestWater"),
                Is.True);

            // A reset replaces the runtime world and removes observation facts.
            // The sensor must republish on the next physics step rather than
            // waiting for its normal throttled observation interval.
            bootstrap.ResetWorld(logReport: false);
            yield return new WaitForFixedUpdate();
            Assert.That(
                bootstrap.World.HasFact("TestFloatable", OntologyPredicates.Occupies, "TestWater"),
                Is.True,
                "Water occupancy must be restored immediately after a world reset.");

            observedObject.transform.position = testOrigin + new Vector3(100f, 0f, 0f);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.That(
                bootstrap.World.HasFact("TestFloatable", OntologyPredicates.Occupies, "TestWater"),
                Is.False);

            Object.Destroy(bootstrapObject);
            Object.Destroy(waterObject);
            Object.Destroy(observedObject);
            yield return null;
            yield return SceneManager.UnloadSceneAsync(testScene);
        }

        [UnityTest]
        public IEnumerator ObjectOnDryLandInsideLargeWaterVolumeDoesNotOccupyWater()
        {
            var testScene = SceneManager.CreateScene(
                "OntologyDryLandInsideWaterVolumeTest");
            SceneManager.SetActiveScene(testScene);

            var bootstrapObject = new GameObject("TestBootstrap");
            var bootstrap = bootstrapObject.AddComponent<OntologyWorldBootstrap>();

            var waterObject = new GameObject("TestWater");
            waterObject.transform.position = new Vector3(0f, -1f, 0f);
            var waterOntology = waterObject.AddComponent<OntologyObject>();
            waterOntology.ConfigureOntologyData(
                "TestWater",
                new[] { OntologyConcepts.WaterRegion },
                new OntologyFactEntry[0]);
            var waterCollider = waterObject.AddComponent<BoxCollider>();
            waterCollider.isTrigger = true;
            waterCollider.size = new Vector3(20f, 4f, 20f);
            waterObject.AddComponent<OntologyWaterRegionVolume>();

            var dryLand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            dryLand.name = "DryLand";
            dryLand.transform.position = new Vector3(0f, 0.95f, 0f);
            dryLand.transform.localScale = new Vector3(5f, 0.1f, 5f);

            var observedObject = new GameObject("TestFloatable");
            observedObject.transform.position = new Vector3(0f, 0.55f, 0f);
            var observedOntology = observedObject.AddComponent<OntologyObject>();
            observedOntology.ConfigureOntologyData(
                "TestFloatable",
                new[] { OntologyConcepts.FloatableObject },
                new OntologyFactEntry[0]);
            observedObject.AddComponent<BoxCollider>();
            var sensor = observedObject.AddComponent<OntologyWaterOccupancySensor>();
            sensor.Configure(bootstrap);

            yield return null;
            bootstrap.ResetWorld(logReport: false);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            Assert.That(
                bootstrap.World.HasFact(
                    "TestFloatable",
                    OntologyPredicates.Occupies,
                    "TestWater"),
                Is.False,
                "Dry land inside a broad WaterRegion must not infer water occupancy.");

            Object.Destroy(bootstrapObject);
            Object.Destroy(waterObject);
            Object.Destroy(dryLand);
            Object.Destroy(observedObject);
            yield return null;
            yield return SceneManager.UnloadSceneAsync(testScene);
        }

        [UnityTest]
        public IEnumerator AttachedPresentationWithoutSolidColliderStillObservesWater()
        {
            var testScene = SceneManager.CreateScene(
                "OntologyAttachedWaterOccupancyTest");
            SceneManager.SetActiveScene(testScene);

            var bootstrapObject = new GameObject("TestBootstrap");
            var bootstrap = bootstrapObject.AddComponent<OntologyWorldBootstrap>();

            var waterObject = new GameObject("TestWater");
            var waterOntology = waterObject.AddComponent<OntologyObject>();
            waterOntology.ConfigureOntologyData(
                "TestWater",
                new[] { OntologyConcepts.WaterRegion },
                new OntologyFactEntry[0]);
            var waterCollider = waterObject.AddComponent<BoxCollider>();
            waterCollider.isTrigger = true;
            waterCollider.size = new Vector3(10f, 4f, 10f);
            waterObject.AddComponent<OntologyWaterRegionVolume>();

            var observedObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            observedObject.name = "AttachedFloatable";
            var observedOntology = observedObject.AddComponent<OntologyObject>();
            observedOntology.ConfigureOntologyData(
                "AttachedFloatable",
                new[] { OntologyConcepts.FloatableObject },
                new OntologyFactEntry[0]);
            var solidCollider = observedObject.GetComponent<Collider>();
            solidCollider.enabled = false;
            var sensor = observedObject.AddComponent<OntologyWaterOccupancySensor>();
            sensor.Configure(bootstrap);

            yield return null;
            bootstrap.ResetWorld(logReport: false);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.That(
                bootstrap.World.HasFact(
                    "AttachedFloatable",
                    OntologyPredicates.Occupies,
                    "TestWater"),
                Is.True,
                "A visible attached item must keep observing water after its collision body is disabled.");

            observedObject.transform.position = new Vector3(100f, 0f, 0f);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.That(
                bootstrap.World.HasFact(
                    "AttachedFloatable",
                    OntologyPredicates.Occupies,
                    "TestWater"),
                Is.False);

            Object.Destroy(bootstrapObject);
            Object.Destroy(waterObject);
            Object.Destroy(observedObject);
            yield return null;
            yield return SceneManager.UnloadSceneAsync(testScene);
        }
    }
}
