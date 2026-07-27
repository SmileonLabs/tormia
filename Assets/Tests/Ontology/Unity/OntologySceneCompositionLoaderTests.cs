using NUnit.Framework;
using Tormia.Ontology.Core;

namespace Tormia.Ontology.Tests.Unity
{
    public sealed class OntologySceneCompositionLoaderTests
    {
        [Test]
        public void CanonicalCompositionSceneNamesRemainStable()
        {
            Assert.That(OntologySceneCompositionLoader.DefaultBootstrapSceneName, Is.EqualTo("TormiaBootstrap"));
            Assert.That(OntologySceneCompositionLoader.DefaultWorldSceneName, Is.EqualTo("TormiaWorld"));
            Assert.That(OntologySceneCompositionLoader.DefaultUiSceneName, Is.EqualTo("TormiaUI"));
        }

        [Test]
        public void MissingSceneIsNotReportedAsLoaded()
        {
            Assert.That(
                OntologySceneCompositionLoader.IsSceneLoaded("TormiaSceneThatDoesNotExist"),
                Is.False);
        }
    }
}
