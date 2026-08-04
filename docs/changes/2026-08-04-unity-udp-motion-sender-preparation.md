# Unity UDP motion sender preparation

## Decision

Prepare the Unity HTTPS ticket client, fixed UDP bootstrap and authenticated
motion sender while keeping the adapter disabled and HTTP as the only active
gameplay writer.

## Safety boundary

- Admission is bound to lifecycle, runtime session, world, avatar and Zone.
- Late responses, disable, timeout and disconnect invalidate the operation.
- Retained byte-array credentials are cleared on every stop path.
- UDP does not own input meaning, gameplay evaluation or durable state.

## Verification

- Unity refreshed with no new C# compile error.
- Bootstrap shape, credential rejection, packet authentication and HTTP
  fallback tests were added.
- The official Unity MCP test job stalled before starting; execution remains a
  required stage-13 platform gate rather than being reported as passed.
