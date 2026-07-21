using System.Collections;
using System.Linq;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Tormia.Ontology.Tests
{
    public sealed class TormiaMainSceneSmokeTests
    {
        [UnityTest]
        public IEnumerator MainSceneCreatesOntologyWorldFromSceneObjects()
        {
            var operation = SceneManager.LoadSceneAsync("Assets/Scenes/TormiaMain.unity", LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            yield return operation;
            yield return null;

            var bootstrap = Object.FindFirstObjectByType<OntologyWorldBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);

            bootstrap.ResetWorld(logReport: false);
            Assert.That(bootstrap.World, Is.Not.Null);
            Assert.That(bootstrap.Session, Is.Not.Null);
            Assert.That(bootstrap.World.Facts.Count(), Is.GreaterThan(0));
            Assert.That(bootstrap.GetGeneratedQuests(), Is.Not.Null);
            Assert.That(bootstrap.GetActionCandidates(), Is.Not.Null);
        }
    }
}
