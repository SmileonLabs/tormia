# Isolated Remote Multiplayer Test Authority

## Summary

- **Date:** 2026-08-03
- **Owner:** TOV team
- **Status:** Verified
- **Related issue or request:** Deploy a multiplayer test Authority without affecting existing services.

## Intent

Provide a safe server endpoint for Android multiplayer verification while
preserving all existing PunkArena services.

## Data classification

- [ ] Account profile
- [ ] Durable authored world data
- [ ] Runtime observation
- [ ] Inferred state
- [ ] Unity presentation
- [x] Transport / authority / infrastructure

## Decision and boundary

The test deployment owns only the `tormia_test` database, the
`tormia_test_app` login, the `tormia-test-authority` container, and the
`tov-api.punkarena.app` virtual host. It must not modify or reuse an existing
service database, process, port, or virtual host. The RDS administrator secret
is bootstrap-only and is removed after provisioning.

## Ontology expression

No gameplay relation changes. The deployed World Authority remains the source
of truth for authored Triples, Rule Blocks, shared results, and runtime session
contracts. Unity receives only the public Authority endpoint.

## Verification

| Case | Expected result | Evidence |
| --- | --- | --- |
| Enabled / added | Isolated API connects to PostgreSQL and Redis | `/health`, `/health/realtime`, and `/health/scheduler` return healthy |
| Disabled / removed | Existing services remain independent | New container binds only to `127.0.0.1:5272`; separate Nginx host |
| Regression / edge case | Disabling the Editor switch restores local Authority while Android remains remote | Targeted Unity EditMode endpoint-selection test passes |
| Editor remote development | Editor play mode and authoring use remote HTTPS while local URL remains available | Authored endpoint switch enabled and tested |
| TLS renewal | Certificate renews without manual Nginx changes | Certbot renewal dry-run succeeds |

## Performance and multiplayer impact

The service uses the existing Redis backplane and a dedicated PostgreSQL login.
No per-frame durable-write policy changes are introduced.

## Documentation updated

- [x] `PROJECT_CONTEXT.md`
- [x] `PROJECT_CONTEXT.ko.md`
- [ ] Localization CSV / migration, if needed
- [ ] Test scenario manifest, if needed
