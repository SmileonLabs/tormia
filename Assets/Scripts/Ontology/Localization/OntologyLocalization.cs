using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Localization
{
    public enum OntologyLanguage
    {
        Id,
        English,
        Korean,
        IdWithKorean,
        IdWithEnglish
    }

    [Serializable]
    public class OntologyTermLocalizationEntry
    {
        public string termId;
        public string english;
        public string korean;
        [TextArea] public string englishDescription;
        [TextArea] public string koreanDescription;
    }

    [CreateAssetMenu(menuName = "Tormia/Ontology/Term Localization", fileName = "OntologyTermLocalization")]
    public class OntologyTermLocalization : ScriptableObject
    {
        public List<OntologyTermLocalizationEntry> entries = new();

        private Dictionary<string, OntologyTermLocalizationEntry> lookup;

        public string Format(string termId, OntologyLanguage language)
        {
            if (string.IsNullOrWhiteSpace(termId))
            {
                return termId;
            }

            var entry = Find(termId);
            var english = !string.IsNullOrWhiteSpace(entry?.english) ? entry.english : Nicify(termId);
            var korean = !string.IsNullOrWhiteSpace(entry?.korean) ? entry.korean : english;

            return language switch
            {
                OntologyLanguage.English => english,
                OntologyLanguage.Korean => korean,
                OntologyLanguage.IdWithKorean => $"{termId} ({korean})",
                OntologyLanguage.IdWithEnglish => $"{termId} ({english})",
                _ => termId
            };
        }

        public OntologyTermLocalizationEntry Find(string termId)
        {
            if (lookup == null || lookup.Count != entries.Count)
            {
                RebuildLookup();
            }

            return lookup.TryGetValue(termId, out var entry) ? entry : null;
        }

        public void AddOrUpdate(string termId, string english, string korean, string englishDescription = "", string koreanDescription = "")
        {
            var entry = entries.Find(item => item.termId == termId);
            if (entry == null)
            {
                entry = new OntologyTermLocalizationEntry { termId = termId };
                entries.Add(entry);
            }

            entry.english = english;
            entry.korean = korean;
            entry.englishDescription = englishDescription;
            entry.koreanDescription = koreanDescription;
            RebuildLookup();
        }

        private void RebuildLookup()
        {
            lookup = new Dictionary<string, OntologyTermLocalizationEntry>();
            foreach (var entry in entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.termId))
                {
                    lookup[entry.termId] = entry;
                }
            }
        }

        private static string Nicify(string termId)
        {
            return termId.Replace('_', ' ');
        }
    }

    public static class OntologyLocalizer
    {
        private const string DefaultResourcePath = "Ontology/OntologyTermLocalization";
        private static OntologyTermLocalization cachedDefault;

        public static string Format(string termId, OntologyLanguage language, OntologyTermLocalization localization = null)
        {
            localization ??= GetDefault();
            return localization != null ? localization.Format(termId, language) : termId;
        }

        public static OntologyTermLocalization GetDefault()
        {
            if (cachedDefault == null)
            {
                cachedDefault = Resources.Load<OntologyTermLocalization>(DefaultResourcePath);
            }

            return cachedDefault;
        }
    }
}
