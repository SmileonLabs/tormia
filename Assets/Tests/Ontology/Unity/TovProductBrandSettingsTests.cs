using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;

namespace Tormia.Ontology.Tests.Unity
{
    public sealed class TovProductBrandSettingsTests
    {
        [Test]
        public void PlayerFacingBrandUsesTovWithoutRenamingTechnicalIdentifiers()
        {
            Assert.That(PlayerSettings.productName, Is.EqualTo("TOV"));
            Assert.That(PlayerSettings.companyName, Is.EqualTo("Smileon Labs"));
            Assert.That(
                PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone),
                Is.EqualTo("com.smileonlabs.tov"));
            Assert.That(
                PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android),
                Is.EqualTo("com.smileonlabs.tov"));
            Assert.That(
                PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS),
                Is.EqualTo("com.smileonlabs.tov"));
        }
    }
}
