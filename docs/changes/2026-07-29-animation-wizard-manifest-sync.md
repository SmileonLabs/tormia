# Manifest-First Animation Wizard Synchronization

## Summary

- **Date:** 2026-07-29
- **Owner:** Unity ontology animation content pipeline
- **Status:** Implemented
- **Observed issue:** The character wizard wrote directly to
  `AnimationDatabase.asset`, optionally edited one ActorProfile, and assigned
  unrelated semantic properties to every new clip.

## Cause

The original convenience workflow predated the animation content manifest.
That left two authoring sources. A later manifest synchronization could remove
a wizard-created Database entry, while a removed manifest entry could be
silently reintroduced through direct Database editing.

## Decision

The wizard now performs `Manifest registration -> validation -> generated
Database/Profile synchronization`. It creates a canonical manifest entry,
validates the complete candidate manifest, and only then generates runtime
definitions and profile repertoire membership. Failed synchronization restores
the previous manifest. Direct Database-only registration and hardcoded
animation semantic properties are removed.

Current animation assets and World Authority action intents are checked against
the same manifest projection.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Valid wizard entry | Manifest entry generates matching Database definition and Profile membership | `WizardManifestRegistrationSynchronizesDatabaseAndProfile` |
| Manifest entry removed | Generated Database definition and Profile membership disappear | `WizardManifestRegistrationSynchronizesDatabaseAndProfile` |
| Current project content | Manifest, Database, profiles, sword animations, and Authority intents agree | `CurrentAnimationAssetsMatchManifestProjection` |
