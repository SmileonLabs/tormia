using System.Collections;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologySupportObservationSensorTests
    {
        [UnityTest]
        public IEnumerator CollisionSupportIsObservedAndRetracted()
        {
            var bootstrapObject = new GameObject("Bootstrap");
            var bootstrap = bootstrapObject.AddComponent<OntologyWorldBootstrap>();

            var platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.name = "Platform";
            platform.transform.position = new Vector3(0f, -0.5f, 0f);
            platform.transform.localScale = new Vector3(4f, 1f, 4f);
            var platformOntology = platform.AddComponent<OntologyObject>();
            platformOntology.ConfigureOntologyData(
                "Platform",
                new[] { "SupportSurface" },
                new OntologyFactEntry[0]);

            var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = "Crate";
            crate.transform.position = new Vector3(0f, 1.5f, 0f);
            crate.AddComponent<Rigidbody>();
            var crateOntology = crate.AddComponent<OntologyObject>();
            crateOntology.ConfigureOntologyData(
                "Crate",
                new[] { "PhysicalObject" },
                new OntologyFactEntry[0]);
            var sensor = crate.AddComponent<OntologySupportObservationSensor>();
            sensor.Configure(bootstrap);

            bootstrap.ResetWorld(false);
            Physics.SyncTransforms();
            for (var index = 0; index < 90; index++)
            {
                yield return new WaitForFixedUpdate();
                if (bootstrap.World.HasFact(
                        "Crate",
                        OntologyPredicates.SupportedBy,
                        "Platform"))
                {
                    break;
                }
            }

            Assert.That(
                bootstrap.World.HasFact(
                    "Crate",
                    OntologyPredicates.SupportedBy,
                    "Platform"),
                Is.True);

            crate.transform.position = new Vector3(8f, 4f, 0f);
            crate.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(
                bootstrap.World.HasFact(
                    "Crate",
                    OntologyPredicates.SupportedBy,
                    "Platform"),
                Is.False);

            Object.Destroy(crate);
            Object.Destroy(platform);
            Object.Destroy(bootstrapObject);
            yield return null;
        }
    }
}
