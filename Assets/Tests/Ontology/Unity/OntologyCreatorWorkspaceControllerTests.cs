using NUnit.Framework;
using Tormia.Ontology.Unity.Networking;

namespace Tormia.Ontology.Tests.Unity
{
    public sealed class OntologyCreatorWorkspaceControllerTests
    {
        [TestCase(true, true, true, "owner", true)]
        [TestCase(false, true, true, "owner", false)]
        [TestCase(true, false, true, "owner", false)]
        [TestCase(true, true, false, "owner", false)]
        [TestCase(true, true, true, "editor", false)]
        [TestCase(true, true, true, "viewer", false)]
        [TestCase(true, true, true, "", false)]
        public void WorkspaceVisibilityRequiresEnteredOwnerCreatorSession(
            bool creatorMode,
            bool isInWorld,
            bool runtimeReady,
            string role,
            bool expected)
        {
            Assert.That(
                OntologyCreatorWorkspaceController.ShouldShowWorkspace(
                    creatorMode,
                    isInWorld,
                    runtimeReady,
                    role),
                Is.EqualTo(expected));
        }
    }
}
