using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public readonly struct OntologyStoredAuthoritySession
    {
        public OntologyStoredAuthoritySession(string userId, string accessToken)
        {
            UserId = userId ?? string.Empty;
            AccessToken = accessToken ?? string.Empty;
        }

        public string UserId { get; }
        public string AccessToken { get; }
        public bool IsValid =>
            Guid.TryParse(UserId, out _) &&
            !string.IsNullOrWhiteSpace(AccessToken);
    }

    /// <summary>
    /// Owns local account-session preferences. The Authority client consumes the
    /// result but no longer constructs storage keys or mixes them with transport
    /// logic. Credentials are never written to world snapshots or command outboxes.
    /// </summary>
    public static class OntologyAuthoritySessionStore
    {
        private const string Prefix = "Tormia.WorldAuthority.";

        public static bool HasSession(string baseUrl) =>
            !string.IsNullOrWhiteSpace(
                PlayerPrefs.GetString(TokenKey(baseUrl), string.Empty));

        public static OntologyStoredAuthoritySession Load(string baseUrl) =>
            new(
                PlayerPrefs.GetString(UserKey(baseUrl), string.Empty),
                PlayerPrefs.GetString(TokenKey(baseUrl), string.Empty));

        public static void Save(
            string baseUrl,
            string userId,
            string accessToken)
        {
            PlayerPrefs.SetString(TokenKey(baseUrl), accessToken ?? string.Empty);
            PlayerPrefs.SetString(UserKey(baseUrl), userId ?? string.Empty);
            PlayerPrefs.Save();
        }

        public static void Clear(string baseUrl)
        {
            PlayerPrefs.DeleteKey(TokenKey(baseUrl));
            PlayerPrefs.DeleteKey(UserKey(baseUrl));
            PlayerPrefs.Save();
        }

        public static string LoadCharacterId(string baseUrl, string userId) =>
            PlayerPrefs.GetString(
                CharacterKey(baseUrl, userId),
                string.Empty);

        public static void SaveCharacterId(
            string baseUrl,
            string userId,
            string characterId)
        {
            PlayerPrefs.SetString(
                CharacterKey(baseUrl, userId),
                characterId ?? string.Empty);
            PlayerPrefs.Save();
        }

        public static string LoadWorldId(string baseUrl, string userId) =>
            PlayerPrefs.GetString(WorldKey(baseUrl, userId), string.Empty);

        public static void SaveWorldId(
            string baseUrl,
            string userId,
            string worldId)
        {
            PlayerPrefs.SetString(
                WorldKey(baseUrl, userId),
                worldId ?? string.Empty);
            PlayerPrefs.Save();
        }

        public static string TokenKey(string baseUrl) =>
            Prefix + Segment(NormalizeBaseUrl(baseUrl)) + ".accessToken";

        public static string UserKey(string baseUrl) =>
            Prefix + Segment(NormalizeBaseUrl(baseUrl)) + ".userId";

        public static string CharacterKey(string baseUrl, string userId) =>
            Prefix + Segment(NormalizeBaseUrl(baseUrl)) + "." +
            Segment(userId) + ".characterId";

        public static string WorldKey(string baseUrl, string userId) =>
            Prefix + Segment(NormalizeBaseUrl(baseUrl)) + "." +
            Segment(userId) + ".worldId";

        private static string NormalizeBaseUrl(string value) =>
            string.IsNullOrWhiteSpace(value)
                ? "default"
                : value.Trim().TrimEnd('/');

        private static string Segment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "default";
            }

            var characters = value.Trim().ToLowerInvariant().ToCharArray();
            for (var index = 0; index < characters.Length; index++)
            {
                if (!char.IsLetterOrDigit(characters[index]))
                {
                    characters[index] = '_';
                }
            }

            return new string(characters);
        }
    }
}
