using NUnit.Framework;
using Tormia.Ontology.Core;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyAuthoritySessionStoreTests
    {
        [Test]
        public void AccountSelectionsAreScopedByAuthorityAndUser()
        {
            var firstUserWorld = OntologyAuthoritySessionStore.WorldKey(
                "http://127.0.0.1:5272/",
                "USER-A");
            var secondUserWorld = OntologyAuthoritySessionStore.WorldKey(
                "http://127.0.0.1:5272",
                "USER-B");
            var otherAuthorityWorld = OntologyAuthoritySessionStore.WorldKey(
                "https://authority.example",
                "USER-A");

            Assert.That(firstUserWorld, Is.Not.EqualTo(secondUserWorld));
            Assert.That(firstUserWorld, Is.Not.EqualTo(otherAuthorityWorld));
            Assert.That(
                firstUserWorld,
                Is.EqualTo(
                    OntologyAuthoritySessionStore.WorldKey(
                        "http://127.0.0.1:5272",
                        "user-a")));
        }

        [Test]
        public void SessionCredentialKeyIsNotAWorldOrCharacterKey()
        {
            var baseUrl = "http://127.0.0.1:5272";
            var tokenKey = OntologyAuthoritySessionStore.TokenKey(baseUrl);

            Assert.That(
                tokenKey,
                Is.Not.EqualTo(
                    OntologyAuthoritySessionStore.WorldKey(baseUrl, "user")));
            Assert.That(
                tokenKey,
                Is.Not.EqualTo(
                    OntologyAuthoritySessionStore.CharacterKey(
                        baseUrl,
                        "user")));
        }
    }
}
