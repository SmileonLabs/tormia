using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEditor;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyAccountWorldCardSerializationTests
    {
        [Test]
        public void WorldPanelAndCardUseDistinctMonoScriptAssets()
        {
            const string panelPath =
                "Assets/Scripts/Ontology/UI/OntologyAccountWorldSelectionPanel.cs";
            const string cardPath =
                "Assets/Scripts/Ontology/UI/OntologyAccountWorldCard.cs";

            var panelScript = AssetDatabase.LoadAssetAtPath<MonoScript>(panelPath);
            var cardScript = AssetDatabase.LoadAssetAtPath<MonoScript>(cardPath);

            Assert.That(panelScript, Is.Not.Null);
            Assert.That(cardScript, Is.Not.Null);
            Assert.That(panelScript.GetClass(),
                Is.EqualTo(typeof(OntologyAccountWorldSelectionPanel)));
            Assert.That(cardScript.GetClass(),
                Is.EqualTo(typeof(OntologyAccountWorldCard)));
            Assert.That(AssetDatabase.AssetPathToGUID(panelPath),
                Is.Not.EqualTo(AssetDatabase.AssetPathToGUID(cardPath)));
        }
    }
}
