using NUnit.Framework;
using Tormia.Ontology.Core;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyCreatorWorkDraftTests
    {
        [Test]
        public void DraftUsesCatalogStageOrderWithoutMutatingWorldState()
        {
            var world = new OntologyWorldState();
            var services = new[]
            {
                Service("RuleEngineer", 30),
                Service("WorldArchitect", 10, true),
                Service("ResourceMaker", 20)
            };

            Assert.That(
                OntologyCreatorWorkDraftFactory.TryCreate(
                    "Create a small cooperative farming adventure.",
                    services,
                    out var draft,
                    out var error),
                Is.True,
                error.ToString());
            Assert.That(
                draft.ServiceRoute,
                Is.EqualTo(new[]
                {
                    "WorldArchitect",
                    "ResourceMaker",
                    "RuleEngineer"
                }));
            Assert.That(world.Facts, Is.Empty);
        }

        [Test]
        public void DraftRejectsShortPromptAndMissingArchitect()
        {
            Assert.That(
                OntologyCreatorWorkDraftFactory.TryCreate(
                    "short",
                    new[] { Service("RuleEngineer", 10) },
                    out _,
                    out _),
                Is.False);
            Assert.That(
                OntologyCreatorWorkDraftFactory.TryCreate(
                    "Create a game with enough detail.",
                    new[] { Service("RuleEngineer", 10) },
                    out _,
                    out _),
                Is.False);
        }

        [Test]
        public void RouteRejectsDuplicateCanonicalServiceIds()
        {
            Assert.That(
                OntologyCreatorRoutePlanner.TryBuild(
                    new[]
                    {
                        Service("WorldArchitect", 10, true),
                        Service("WorldArchitect", 20)
                    },
                    out _,
                    out var error),
                Is.False);
            Assert.That(
                error,
                Is.EqualTo(OntologyCreatorDraftError.DuplicateServiceId));
        }

        private static OntologyCreatorServiceDefinition Service(
            string serviceId,
            int order,
            bool acceptsInitialPrompt = false) =>
            new()
            {
                serviceId = serviceId,
                stageOrder = order,
                acceptsInitialPrompt = acceptsInitialPrompt
            };
    }
}
