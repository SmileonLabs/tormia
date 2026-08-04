internal static class AuthorityCompiledContractMemoryEstimator
{
    // This is deliberately a conservative retained-size estimate rather than
    // a GC heap measurement. It accounts for every index owned by the compiled
    // contract and saturates instead of wrapping when hostile/invalid input is
    // presented to cache admission logic.
    internal static long Estimate(IReadOnlyList<AuthorityFactSnapshot> facts)
        => Estimate(facts, CancellationToken.None);

    internal static long Estimate(
        IReadOnlyList<AuthorityFactSnapshot> facts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var semanticFacts = new HashSet<(Guid Subject, string Predicate, string Value)>();
        var subjects = new HashSet<Guid>();
        var predicates = new HashSet<string>(StringComparer.Ordinal);
        var rowKeys = new HashSet<(Guid Subject, string Predicate)>();
        var conceptSubjects = new HashSet<Guid>();
        long bytes = 0;

        // Contract, source array and its read-only collection wrapper.
        Add(ref bytes, 160);
        AddArray(ref bytes, facts.Count, 8);
        Add(ref bytes, 32);

        for (var index = 0; index < facts.Count; index++)
        {
            if ((index & 255) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            var fact = facts[index];
            subjects.Add(fact.SubjectEntityId);
            predicates.Add(fact.PredicateId ?? string.Empty);
            rowKeys.Add((fact.SubjectEntityId, fact.PredicateId ?? string.Empty));
            semanticFacts.Add((
                fact.SubjectEntityId,
                fact.PredicateId ?? string.Empty,
                fact.ObjectValue ?? string.Empty));
            if (string.Equals(fact.PredicateId, "has_concept", StringComparison.Ordinal))
                conceptSubjects.Add(fact.SubjectEntityId);

            // Snapshot record plus its retained strings. Counting strings per
            // row is intentionally conservative: database materialization is
            // not required to intern equal values.
            Add(ref bytes, 72);
            AddString(ref bytes, fact.PredicateId);
            AddString(ref bytes, fact.ObjectKind);
            AddString(ref bytes, fact.ObjectValue);
        }

        // Raw provenance row lookup: dictionary buckets/entries and one List
        // plus backing array per (subject,predicate) key.
        AddDictionary(ref bytes, rowKeys.Count, 64);
        var rowCounts = new Dictionary<(Guid Subject, string Predicate), int>();
        for (var index = 0; index < facts.Count; index++)
        {
            if ((index & 255) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            var fact = facts[index];
            var key = (fact.SubjectEntityId, fact.PredicateId ?? string.Empty);
            rowCounts.TryGetValue(key, out var count);
            rowCounts[key] = count + 1;
        }
        var groupIndex = 0;
        foreach (var count in rowCounts.Values)
        {
            if ((groupIndex++ & 255) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            Add(ref bytes, 32);
            AddArray(ref bytes, count, 8);
        }

        // OntologyWorldState owns entity, semantic-fact, origin, predicate and
        // changed-predicate indexes. OntologyFact holds three OntologyId/string
        // references and is duplicated as keys/elements across these indexes.
        Add(ref bytes, 192);
        AddDictionary(ref bytes, subjects.Count, 48);
        MultiplyAdd(ref bytes, subjects.Count, 32); // OntologyEntityState
        AddHashSet(ref bytes, semanticFacts.Count, 48); // facts
        AddDictionary(ref bytes, semanticFacts.Count, 64); // factOrigins
        MultiplyAdd(ref bytes, semanticFacts.Count, 80); // one origin HashSet each
        AddDictionary(ref bytes, predicates.Count, 48); // factsByPredicate
        MultiplyAdd(ref bytes, predicates.Count, 56); // indexed HashSet headers
        MultiplyAdd(ref bytes, semanticFacts.Count, 48); // predicate set entries
        AddHashSet(ref bytes, predicates.Count, 32); // changedPredicates

        // World compilation creates/retains OntologyId strings independently
        // of the source snapshot. Count all three per semantic fact so shared
        // references or runtime string reuse only make the estimate safer.
        var semanticIndex = 0;
        foreach (var fact in semanticFacts)
        {
            if ((semanticIndex++ & 255) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            AddString(ref bytes, fact.Subject.ToString("D"));
            AddString(ref bytes, fact.Predicate);
            AddString(ref bytes, fact.Value);
        }

        // has_concept additionally retains per-entity concept sets.
        MultiplyAdd(ref bytes, conceptSubjects.Count, 56);
        var conceptCount = 0;
        semanticIndex = 0;
        foreach (var fact in semanticFacts)
        {
            if ((semanticIndex++ & 255) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(
                    fact.Predicate, "has_concept", StringComparison.Ordinal))
                conceptCount++;
        }
        MultiplyAdd(ref bytes, conceptCount, 32);

        return Math.Max(1, bytes);
    }

    private static void AddDictionary(ref long bytes, int count, long entryBytes)
    {
        Add(ref bytes, 80);
        AddArray(ref bytes, count, 4);
        MultiplyAdd(ref bytes, count, entryBytes);
    }

    private static void AddHashSet(ref long bytes, int count, long entryBytes)
    {
        Add(ref bytes, 72);
        AddArray(ref bytes, count, 4);
        MultiplyAdd(ref bytes, count, entryBytes);
    }

    private static void AddArray(ref long bytes, int count, long elementBytes)
    {
        Add(ref bytes, 24);
        MultiplyAdd(ref bytes, count, elementBytes);
    }

    private static void AddString(ref long bytes, string? value)
    {
        // Object header + length/terminator, aligned to 8 bytes.
        var raw = SaturatingAdd(26, SaturatingMultiply(value?.Length ?? 0, 2));
        Add(ref bytes, Align8(raw));
    }

    private static void MultiplyAdd(ref long total, long count, long size) =>
        Add(ref total, SaturatingMultiply(count, size));

    private static void Add(ref long total, long value) =>
        total = SaturatingAdd(total, value);

    private static long SaturatingAdd(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;

    private static long SaturatingMultiply(long left, long right) =>
        left == 0 || right == 0
            ? 0
            : left > long.MaxValue / right ? long.MaxValue : left * right;

    private static long Align8(long value) => value >= long.MaxValue - 7
        ? long.MaxValue
        : (value + 7) & ~7L;
}
