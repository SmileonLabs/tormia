using System;
using System.Reflection;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyWorldAuthorityRoleTests
    {
        [TestCase("owner", true)]
        [TestCase("editor", true)]
        [TestCase("viewer", false)]
        public void SelectedWorldRoleControlsDurableAuthoring(
            string role,
            bool expectedCanEdit)
        {
            var gameObject = new GameObject("AuthorityRoleTest");
            try
            {
                var client = gameObject.AddComponent<OntologyWorldAuthorityClient>();
                var userId = Guid.NewGuid().ToString();
                var worldId = Guid.NewGuid().ToString();
                SetField(client, "currentUserId", userId);
                SetField(client, "currentWorldId", worldId);
                SetField(client, "currentAccount", new OntologyAuthorityAccountDashboard
                {
                    worlds = new[]
                    {
                        new OntologyAuthorityAccountWorld
                        {
                            worldId = worldId,
                            role = role
                        }
                    }
                });

                Assert.That(client.CurrentWorldRole, Is.EqualTo(role));
                Assert.That(client.CanEditCurrentWorld, Is.EqualTo(expectedCanEdit));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
