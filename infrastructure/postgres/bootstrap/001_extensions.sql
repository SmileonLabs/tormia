-- Bootstrap runs only when the local PostgreSQL volume is first created.
-- Schema changes must be added to postgres/migrations instead.
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE EXTENSION IF NOT EXISTS citext;
