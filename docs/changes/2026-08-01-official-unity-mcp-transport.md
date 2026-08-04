# Official Unity MCP Transport

## Summary

- **Date:** 2026-08-01
- **Owner:** TOV development infrastructure
- **Status:** Migration validation
- **Related request:** Replace the repeatedly disconnecting third-party Unity MCP transport

## Intent

Make Unity's official Assistant MCP relay the primary Codex-to-Editor transport
without losing the existing connection until restart and tool-coverage checks
prove the replacement works for TOV.

## Decision and boundary

Codex launches the official Windows relay with `--mcp` and pins it to the TOV
project with `--project-path`. The relay communicates with the Editor bridge by
named pipe. The legacy Streamable HTTP endpoint remains a temporary fallback
only during validation and is removed together with `com.coplaydev.unity-mcp`
after the official path passes the migration gate.

This changes development infrastructure only. It does not own or alter world
Facts, Rule Blocks, Physical Meaning, Authority state, or gameplay behavior.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Official transport enabled | Relay, project-pinned Codex config, active Editor connection record, and named pipe are present | `scripts/verify-unity-mcp.ps1` |
| Approved interactive client | MCP initialize and tool discovery expose scene, GameObject, and console tools | `scripts/verify-unity-mcp.ps1 -LiveToolProbe` or the restarted Codex session |
| Wrong/missing setup | Missing relay, Assistant package, project pin, Editor bridge, or required tool fails explicitly | `scripts/verify-unity-mcp.ps1` negative checks |
| Temporary fallback | Legacy HTTP health is reported only when explicitly requested and cannot satisfy the official check | `-CheckLegacyFallback` |

## Removal gate

Remove the legacy HTTP MCP package and configuration only after Codex restart,
Unity restart, assembly reload, read-console, scene query, GameObject mutation
and undo, save, compile, and Unity test probes pass through the official tools.

## Updated documents

- `AGENTS.md`
- `docs/PROJECT_CONTEXT.md`
- `docs/PROJECT_CONTEXT.ko.md`
- `scripts/verify-unity-mcp.ps1`
- `scripts/verify-development.ps1`
