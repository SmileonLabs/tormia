# Motion-oriented weapon sweep

## Summary

- **Date:** 2026-07-29
- **Owner:** TOV development
- **Status:** Implemented and automated Unity verification passed
- **Request:** Align the sword swing trail with the direction of the animated weapon.

## Decision

The existing catalog-selected slash VFX remains a presentation asset, but its
anchor orientation is derived from the authored blade axis and the actual
frame-to-frame movement of the weapon's editable slash anchor. The effect
starts only inside the animation manifest contact window.

## Ontology boundary

- Triple and Rule Block: the existing Authority-approved swing contract
- Animation manifest: owns the permitted presentation/contact window
- Unity adapter: observes blade motion and orients a dynamic VFX anchor
- World Authority: continues to own all damage, cooldown, death and loot

Removing the swing Rule Block or its approved animation intent removes the
trail. There is no timer, weapon-name, prefab-name or animation-name fallback.

## Verification

- `WeaponSweepOrientationFollowsAuthoredBladeAndMotionAxes`
- Relevant Unity EditMode suites: 69/69 passed
- `scripts/verify-development.ps1 -RequireUnityMcp`
