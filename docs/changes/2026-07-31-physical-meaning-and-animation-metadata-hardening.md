# Physical Meaning and animation metadata hardening

## Summary

- **Date:** 2026-07-31
- **Scope:** Unity physical presentation, animation manifest/runtime projection
- **Status:** Implemented; Unity MCP verification pending an active Editor instance

## Intent

Remove permissive presentation fallbacks and code-owned animation lifecycle
classification discovered while diagnosing player landing presentation.

## Decision and boundaries

- A Physical Meaning motion lease is required before a Unity adapter may move
  an entity. Missing meaning fails closed.
- Character step height is numeric Physical Meaning data, not a coordinator
  default.
- The animation manifest explicitly owns presentation lifecycle metadata and
  compatibility aliases. Unity does not classify lifecycle ownership from
  intent or source clip names.
- `Anim_Collapse_Reaction` is the canonical collapse reaction ID.
  `Anim_Fall` remains read-compatible only through an explicit legacy alias.
- Removing Physical Meaning clears the motion driver and its derived step
  tuning. Removing or changing lifecycle metadata removes that routing; no
  hidden normalization recreates it.

## Verification

| Case | Expected evidence |
| --- | --- |
| Complete Physical Meaning | Motion lease allows only the configured driver and supplies authored step height. |
| Removed Physical Meaning | Motion lease fails closed and step tuning clears to zero. |
| Manifest-owned jump lifecycle | Motion-state-owned entries route to the resolver and explicitly disable root motion. |
| Removed/conflicting metadata | Missing metadata does not route; conflicting intent owners fail manifest validation. |
| Legacy collapse ID | `Anim_Fall` resolves to `Anim_Collapse_Reaction`, while synchronized profiles author only the canonical ID. |

## Updated documentation

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `tests/harness/core-regression-scenarios.json`
