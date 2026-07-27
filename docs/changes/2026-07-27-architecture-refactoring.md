# Architecture Refactoring

## Summary

- **Date:** 2026-07-27
- **Owner:** TOV development
- **Status:** Implemented; verification recorded below
- **Request:** Refactor the current account, world, ontology, creator, and
  persistence foundation without overwriting designer-authored UI.

## Data classification

- [x] Account profile
- [x] Durable authored world data
- [x] Runtime observation
- [x] Inferred state
- [x] Unity presentation
- [x] Transport / permission / infrastructure

## Decisions

- Preserved the 734-entry working tree and edited no third-party hierarchy or
  unrelated user asset as a cleanup shortcut.
- Replaced name-based runtime UI discovery with explicit scene/component
  bindings.
- Removed the account-character preview's duplicate appearance implementation.
- Extracted stable placed-object Fact projection and legacy subject migration.
- Extracted creator route planning and typed validation errors from the UI.
- Extracted local Authority session/selection storage from the HTTP client.
- Snapshot pending appearance writes by character, restrict checkpoint retry to
  revision conflicts, and share atomic file replacement.
- Removed an accidental nested Unity project copy after confirming every nested
  asset already existed in the authoritative root project. Generated screenshots,
  recovery output, image-generation scratch files, and future nested copies are
  excluded without deleting designer evidence.
- Made character-creation preview clones presentation-only: the presenter owns
  its clone explicitly, cloned gameplay adapters are disabled, and renderer
  state is synchronized from the selected avatar without object-name lookup.

## Enabled and removed cases

| Area | Enabled case | Removed/disabled case |
| --- | --- | --- |
| Runtime UI | Explicit roots/components follow `InWorld` | Renaming a UI object does not bypass the gate |
| Appearance | One projector applies linked parts and variants | Account preview no longer owns a duplicate implementation |
| Appearance preview | Presenter-owned render clone follows mesh/material changes | Cloned gameplay adapters do not run a second appearance/default pipeline |
| Placed entities | Stable `entityId` owns durable Facts | Editable instance name is not a new Fact subject |
| Creator draft | Catalog builds one deterministic route | Duplicate service IDs and missing prompt entry are rejected |
| Appearance save | Captured character and parts are saved together | Changing selection cannot redirect a pending write |
| Checkpoint | `stale_revision` reloads and retries | Forbidden, invalid, and transport failures do not retry |

## Verification

- Relevant Unity, UI, Editor, and Unity test assemblies compiled with no
  errors.
- The open Unity Editor ran the complete EditMode suite: 112 passed, 0 failed,
  0 skipped, and the complete PlayMode suite: 40 passed, 0 failed, 0 skipped.
- `scripts/verify-development.ps1 -RequireServices
  -RunAccountWorldLoopSmoke` passed, including the World Authority build and
  PostgreSQL/Redis/Authority health.
- The account/world loop smoke passed registration, character/profile, world
  entry, checkpoint autosave, logout, login, and resume.
- Added tests cover stable placed-object Fact subjects, legacy migration,
  session key isolation, duplicate creator services, and checkpoint retry
  policy.
