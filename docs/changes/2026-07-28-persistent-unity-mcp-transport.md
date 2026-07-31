# Persistent Unity MCP Transport

## Summary

- **Date:** 2026-07-28
- **Owner:** TOV development harness
- **Status:** Verified
- **Related issue or request:** Unity MCP repeatedly appears disconnected during continuous editor work

## Intent

Keep Codex connected across Unity script compilation and assembly reloads
without requiring the user to repeatedly restart Unity or click Start Session.

## Data classification

- [ ] Account profile
- [ ] Durable authored world data
- [ ] Runtime observation
- [ ] Inferred state
- [ ] Unity presentation
- [x] Transport / authority / infrastructure

## Decision and boundary

Local development uses one MCP for Unity Streamable HTTP server at
`http://127.0.0.1:8080/mcp`. The server remains alive outside the Unity editor
domain, and Unity reconnects through its HTTP/WebSocket bridge after reload.
Codex no longer starts a separate stdio Unity server for each task.

This transport must not own ontology meaning, account state, durable world
commands, gameplay rules, physics, or presentation decisions.

## Ontology expression

None. This is development-tool transport and does not add canonical terms,
Triples, profiles, Rule Blocks, or rule outcomes.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled / added | HTTP server is healthy and the `tormia` Unity instance is connected | `scripts/verify-unity-mcp.ps1` |
| Disabled / removed | A missing server, missing Unity instance, or stdio Codex config fails explicitly | `scripts/verify-unity-mcp.ps1` negative checks |
| Regression / edge case | Forced Unity script reload temporarily removes the instance and reconnects automatically | 2026-07-28 measured reload: restored in 10.7 seconds without a manual click |

## Performance and multiplayer impact

The local HTTP server is one persistent development process. It creates no
database writes, network fan-out to players, world events, or multiplayer
authority changes.

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] Localization CSV / migration, if needed
- [ ] Test scenario manifest, if needed
