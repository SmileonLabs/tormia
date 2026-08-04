# Rule Block and Meaning-Package Lifecycle

## Decision

Rule assignment and removal use Authority `bindingId` identity. Unity no longer mutates online semantic state optimistically. Authority owns atomic apply, partial binding removal, final package closure, restoration, and revisioned provenance.

## Production line

`Quick Setup -> declarative meaning package -> Authority validation -> owned Fact/Binding rows -> projection with provenance -> Unity authoring UI`

## Enabled and removed evidence

- Adding a behavior package requires at least one published, owned Rule Block and creates its declared authored Triples atomically.
- Removing one binding retracts only that identity and its RuleBound results.
- Removing the final owned binding closes the package and retracts owned canonical/typed Triples while independent contributions survive.
- Failed commands leave the prior Unity projection unchanged.

## Adoption ordering correction

Exact unowned catalog contributions are identified and reserved for adoption before replace-predicate retraction. They become package-owned rows and are not written to the displaced baseline ledger. A one-time provenance repair retracts only historical rows that were demonstrably restored from this former ordering defect; durable action state and independent contributions remain.
