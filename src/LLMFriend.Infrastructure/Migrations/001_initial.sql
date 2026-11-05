CREATE TABLE IF NOT EXISTS schema_migrations (
  version INT NOT NULL PRIMARY KEY,
  applied_at DATETIME(6) NOT NULL
);

CREATE TABLE IF NOT EXISTS app_users (
  id CHAR(36) NOT NULL PRIMARY KEY,
  username VARCHAR(100) NOT NULL UNIQUE,
  password_hash TEXT NOT NULL,
  is_admin BOOLEAN NOT NULL DEFAULT FALSE,
  must_change_password BOOLEAN NOT NULL DEFAULT TRUE,
  created_at DATETIME(6) NOT NULL
);

CREATE TABLE IF NOT EXISTS sessions (
  id CHAR(36) NOT NULL PRIMARY KEY,
  user_id CHAR(36) NOT NULL,
  token_hash CHAR(64) NOT NULL UNIQUE,
  expires_at DATETIME(6) NOT NULL,
  revoked_at DATETIME(6) NULL,
  CONSTRAINT fk_sessions_user FOREIGN KEY (user_id) REFERENCES app_users(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS accounts (
  id CHAR(36) NOT NULL PRIMARY KEY,
  owner_id CHAR(36) NOT NULL,
  provider VARCHAR(40) NOT NULL,
  external_id VARCHAR(100) NOT NULL,
  username VARCHAR(100) NOT NULL,
  display_name VARCHAR(200) NOT NULL,
  avatar_url TEXT NULL,
  encrypted_access_token TEXT NULL,
  encrypted_refresh_token TEXT NULL,
  token_expires_at DATETIME(6) NULL,
  snapshot_json LONGTEXT NULL,
  status VARCHAR(30) NOT NULL,
  last_error TEXT NULL,
  last_checked_at DATETIME(6) NULL,
  next_check_at DATETIME(6) NULL,
  created_at DATETIME(6) NOT NULL,
  UNIQUE KEY uq_owner_provider_external (owner_id, provider, external_id),
  KEY ix_account_due (status, next_check_at),
  CONSTRAINT fk_accounts_owner FOREIGN KEY (owner_id) REFERENCES app_users(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS account_shares (
  account_id CHAR(36) NOT NULL,
  user_id CHAR(36) NOT NULL,
  created_at DATETIME(6) NOT NULL,
  PRIMARY KEY (account_id, user_id),
  CONSTRAINT fk_shares_account FOREIGN KEY (account_id) REFERENCES accounts(id) ON DELETE CASCADE,
  CONSTRAINT fk_shares_user FOREIGN KEY (user_id) REFERENCES app_users(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS account_events (
  id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  account_id CHAR(36) NOT NULL,
  occurred_at DATETIME(6) NOT NULL,
  kind VARCHAR(40) NOT NULL,
  description VARCHAR(500) NOT NULL,
  KEY ix_events_account (account_id, occurred_at),
  CONSTRAINT fk_events_account FOREIGN KEY (account_id) REFERENCES accounts(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS oauth_pending (
  id CHAR(36) NOT NULL PRIMARY KEY,
  user_id CHAR(36) NOT NULL,
  state_hash CHAR(64) NOT NULL UNIQUE,
  encrypted_verifier TEXT NOT NULL,
  expires_at DATETIME(6) NOT NULL,
  completed_at DATETIME(6) NULL,
  account_id CHAR(36) NULL,
  error_message VARCHAR(500) NULL,
  CONSTRAINT fk_pending_user FOREIGN KEY (user_id) REFERENCES app_users(id) ON DELETE CASCADE
);
