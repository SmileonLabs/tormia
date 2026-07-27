# Password-authenticated account entry

## Decision

The scene-authored entry flow now starts with **Sign In** or **Create Account**.
Registration and login call the World Authority authentication endpoints and
return an opaque Bearer session. The old automatic development identity,
`/v1/dev/users`, and caller-supplied `X-Tormia-User-Id` authority path are
removed.

## Boundaries

- Passwords belong to authentication data, not account profile triples or world Facts.
- Passwords are PBKDF2-hashed by the Authority; raw session tokens are returned once and only their hashes are stored server-side.
- Unity attaches the Bearer session to REST and SignalR requests. PlayerPrefs token storage is acceptable only for this desktop development slice; production requires platform secure storage.
- Successful registration proceeds to account-owned character creation. Existing accounts proceed to character selection.

## Enabled and disabled evidence

- Enabled: register, read the account dashboard, create a character and world, reconnect with login, and negotiate SignalR with the Bearer session.
- Disabled: a missing/revoked token is unauthorized, a wrong password is rejected, duplicate registration is rejected, and `/v1/dev/users` is absent.
- Unity UI smoke registered a new account, received its Bearer session, loaded an empty dashboard, and navigated to character creation; logout cleared the remembered session.
- Unity compiled with zero Console errors. EditMode passed 53/53 and PlayMode passed 34/34 after the hierarchy-authored panels were saved in `TormiaMain`.
