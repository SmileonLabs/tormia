using System.Collections;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyBuoyancyAdapterTests
    {
        [UnityTest]
        public IEnumerator InferredFloatingStateEnablesBuoyancyAndRemovalDisablesIt()
        {
            var testOrigin = new Vector3(20000f, 0f, 20000f);
            var testScene = SceneManager.CreateScene("OntologyBuoyancyAdapterTest");
            SceneManager.SetActiveScene(testScene);

            var bootstrapObject = new GameObject("Bootstrap");
            var bootstrap = bootstrapObject.AddComponent<OntologyWorldBootstrap>();

            var water = new GameObject("TestWater");
            water.AddComponent<BoxCollider>().isTrigger = true;
            water.GetComponent<BoxCollider>().size = new Vector3(20f, 2f, 20f);
            water.transform.position = testOrigin;
            var waterOntology = water.AddComponent<OntologyObject>();
            waterOntology.ConfigureOntologyData(
                "TestWater",
                new[] { OntologyConcepts.WaterRegion },
                new OntologyFactEntry[0]);
            water.AddComponent<OntologyWaterRegionVolume>();

            var floatingObject = new GameObject("TestFloatable");
            floatingObject.transform.position = testOrigin + new Vector3(0f, -0.8f, 0f);
            floatingObject.AddComponent<BoxCollider>();
            var objectOntology = floatingObject.AddComponent<OntologyObject>();
            objectOntology.ConfigureOntologyData(
                "TestFloatable",
                new[] { OntologyConcepts.FloatableObject },
                new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.PhysicalProfile,
                        obj = "TestFloatable"
                    }
                });
            var sensor = floatingObject.AddComponent<OntologyWaterOccupancySensor>();
            sensor.Configure(bootstrap);

            var profile = ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
            profile.profileId = "TestFloatable";
            profile.mass = 1f;
            profile.supportsBuoyancy = true;
            profile.buoyancyStrength = 25f;
            profile.submergedDamping = 1f;
            profile.surfaceOffset = 0f;
            profile.uprightStability = 2f;
            profile.buoyancySampleCount = 4;

            var adapter = floatingObject.AddComponent<OntologyBuoyancyAdapter>();
            adapter.Configure(profile, bootstrap);

            yield return null;
            bootstrap.ResetWorld(logReport: false);
            bootstrap.World.AddFact(
                profile.profileId,
                OntologyPredicates.SupportsBehavior,
                OntologyObjects.Buoyancy);
            bootstrap.World.AddFact(
                "TestFloatable",
                OntologyPredicates.PhysicalState,
                OntologyObjects.Floating);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(adapter.IsBuoyancyActive, Is.True);
            Assert.That(adapter.TargetBody.linearVelocity.y, Is.GreaterThan(0f));

            bootstrap.World.RemoveFact(
                "TestFloatable",
                OntologyPredicates.PhysicalState,
                OntologyObjects.Floating);
            yield return new WaitForFixedUpdate();

            Assert.That(adapter.IsBuoyancyActive, Is.False);

            Object.Destroy(profile);
            Object.Destroy(floatingObject);
            Object.Destroy(water);
            Object.Destroy(bootstrapObject);
            yield return null;
            yield return SceneManager.UnloadSceneAsync(testScene);
        }
    }
}
