using System.Linq;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyWorldCollisionSemanticAssetTests
    {
        [Test]
        public void WorldEnvironmentCollidersHaveExplicitOntologyRoles()
        {
            const string scenePath = "Assets/Scenes/TormiaWorld.unity";
            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    scenePath,
                    OpenSceneMode.Additive);
            }
            try
            {
                var environment = scene
                    .GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<Transform>(true))
                    .FirstOrDefault(candidate =>
                        candidate.name == "Poly Style Map Environment");
                Assert.That(environment, Is.Not.Null);

                var colliders = environment
                    .GetComponentsInChildren<Collider>(true);
                Assert.That(colliders, Is.Not.Empty);
                foreach (var collider in colliders)
                {
                    var adapter = collider.GetComponentInParent<
                        OntologyCollisionRoleAdapter>(true);
                    Assert.That(
                        adapter,
                        Is.Not.Null,
                        collider.transform.GetHierarchyPath() +
                        " must author a collision role; Unity collider state " +
                        "alone cannot create WalkableSupport meaning.");
                    Assert.That(
                        adapter.Role ==
                            OntologyCollisionRole.WalkableSupport ||
                        adapter.Role ==
                            OntologyCollisionRole.WaterVolume,
                        Is.True,
                        collider.transform.GetHierarchyPath());
                }

                Assert.That(
                    colliders.Any(collider =>
                        collider.GetComponentInParent<
                                OntologyCollisionRoleAdapter>(true)?.Role ==
                            OntologyCollisionRole.WalkableSupport),
                    Is.True);
                Assert.That(
                    colliders.Any(collider =>
                        collider.GetComponentInParent<
                                OntologyCollisionRoleAdapter>(true)?.Role ==
                            OntologyCollisionRole.WaterVolume),
                    Is.True);
            }
            finally
            {
                if (openedForTest)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    internal static class OntologyTransformTestExtensions
    {
        public static string GetHierarchyPath(this Transform transform)
        {
            var path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }
    }
}
