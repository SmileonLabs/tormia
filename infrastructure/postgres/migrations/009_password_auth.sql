BEGIN;

CREATE TABLE IF NOT EXISTS account_credentials
(
    user_id           uuid PRIMARY KEY REFERENCES app_users(user_id) ON DELETE CASCADE,
    email             citext NOT NULL UNIQUE,
    password_hash     text NOT NULL,
    password_salt     text NOT NULL,
    password_version  integer NOT NULL DEFAULT 1,
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS account_sessions
(
    session_id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id            uuid NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    token_hash         text NOT NULL UNIQUE,
    expires_at         timestamptz NOT NULL,
    revoked_at         timestamptz,
    created_at         timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_account_sessions_active_token
    ON account_sessions (token_hash, expires_at)
    WHERE revoked_at IS NULL;

COMMIT;
