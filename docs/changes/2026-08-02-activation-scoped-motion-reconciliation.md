# Activation-scoped motion reconciliation

## Summary

- **Date:** 2026-08-02
- **Status:** Implemented and verified
- **Issue:** A delayed Authority snapshot could pull a stopped player backward, and an in-flight Tick from an older activation could overwrite a newly activated runtime state.

## Ownership and decision

Player input and collision-resolved pose samples are ephemeral observations. Authority owns shared motion state. Unity may predict presentation locally, but it consumes correction only from the active runtime session after Authority has processed the latest accepted input sequence.

Each activation now creates a server-owned `RuntimeSessionId`. Intent, runtime Action intent, pose observation, and motion state carry it. Scheduler publication uses session-and-Tick compare-and-set, while Unity reconciliation uses session, processed input sequence, and Tick together.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Move N, then stop N+1 | Old residual is removed immediately | Unity EditMode reconciliation tests |
| New Tick still processed N | Snapshot is rejected | Unity EditMode reconciliation tests |
| Snapshot processed N+1 | Correction may be accepted | Unity EditMode reconciliation tests |
| New activation races old scheduler | Old session write is rejected | `PlayerMotionRuntimeSessionTests` |
| Reconnect starts sequence 1 | New session accepts it independently | `PlayerMotionRuntimeSessionTests` |

## Performance and multiplayer impact

The checks are fixed-size identity and integer comparisons. They persist no per-frame observation and add no world Fact or Rule evaluation.

## Updated documents

- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
