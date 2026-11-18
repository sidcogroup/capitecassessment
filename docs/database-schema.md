CREATE SCHEMA IF NOT EXISTS cadb;

CREATE FUNCTION cadb.generate_user_id() RETURNS BIGINT AS $$
DECLARE new_id BIGINT;
BEGIN
  LOOP
    new_id := 1000000000 + (('x' || substr(replace(gen_random_uuid()::text, '-', ''), 1, 12))::bit(48)::bigint % 9000000000);
    EXIT WHEN NOT EXISTS (SELECT 1 FROM cadb.users WHERE id = new_id);
  END LOOP;
  RETURN new_id;
END;
$$ LANGUAGE plpgsql;

-- Login identity only. Personal details live in user_profiles.
-- id needs BIGINT: a 10-digit number is bigger than INT's max.
CREATE TABLE cadb.users (
  id          BIGINT PRIMARY KEY DEFAULT cadb.generate_user_id()
              CHECK (id BETWEEN 1000000000 AND 9999999999),
  email       VARCHAR(254) NOT NULL UNIQUE,   -- 254 is the maximum length of a valid email address
  username    VARCHAR(30)  NOT NULL UNIQUE,
  role        VARCHAR(20)  NOT NULL DEFAULT 'Customer' CHECK (role IN ('Customer','Consultant')),
  created_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE TABLE cadb.user_profiles (
  user_id     BIGINT PRIMARY KEY REFERENCES cadb.users(id),
  first_name  VARCHAR(50) NOT NULL,
  last_name   VARCHAR(50) NOT NULL,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE cadb.user_contact (
  user_id     BIGINT PRIMARY KEY REFERENCES cadb.users(id),
  contact_number  VARCHAR(13) NOT NULL,
  contact_email   VARCHAR(254) NOT NULL,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE cadb.otp_codes (
  id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id      BIGINT NOT NULL REFERENCES cadb.users(id),
  code_hash    VARCHAR(100) NOT NULL,   -- fits a bcrypt (60) or SHA-256 hex (64) hash
  expires_at   TIMESTAMPTZ NOT NULL,
  attempts     SMALLINT NOT NULL DEFAULT 0,
  consumed_at  TIMESTAMPTZ,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX ix_otp_user ON cadb.otp_codes (user_id, created_at DESC);

CREATE TABLE cadb.accounts (
  id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id         BIGINT NOT NULL REFERENCES cadb.users(id),
  account_number  VARCHAR(20) NOT NULL UNIQUE,
  account_type    VARCHAR(20) NOT NULL CHECK (account_type IN ('Transact','Savings','Credit')),
  name            VARCHAR(50) NOT NULL,    
  currency        CHAR(3)     NOT NULL DEFAULT 'ZAR',
  balance         NUMERIC(12,2) NOT NULL DEFAULT 0,   
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX ix_accounts_user ON cadb.accounts (user_id);

CREATE TABLE cadb.transactions (
  id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  account_id        UUID NOT NULL REFERENCES cadb.accounts(id),
  transaction_date  TIMESTAMPTZ NOT NULL,
  description       VARCHAR(200) NOT NULL,
  merchant          VARCHAR(100),
  amount            NUMERIC(12,2) NOT NULL CHECK (amount > 0),
  type              VARCHAR(10) NOT NULL CHECK (type IN ('Debit','Credit')),
  reference         VARCHAR(50)
);
CREATE INDEX ix_txn_account_date ON cadb.transactions (account_id, transaction_date DESC);

CREATE TABLE cadb.disputes (
  id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  dispute_reference VARCHAR (50),
  transaction_id    UUID NOT NULL REFERENCES cadb.transactions(id),
  reason            VARCHAR(100) NOT NULL,
  details           VARCHAR(1000),
  status            VARCHAR(20) NOT NULL DEFAULT 'Submitted'
                    CHECK (status IN ('Submitted','Under Review','Resolved','Rejected')),
  created_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at        TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX ux_dispute_open_per_txn
  ON cadb.disputes (transaction_id) WHERE status IN ('Submitted','Under Review');

CREATE TABLE cadb.dispute_status_history (
  id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  dispute_id   UUID NOT NULL REFERENCES cadb.disputes(id),
  status       VARCHAR(20) NOT NULL CHECK (status IN ('Submitted','Under Review','Resolved','Rejected')),
  note         VARCHAR(500),
  changed_by   BIGINT NOT NULL REFERENCES cadb.users(id),
  changed_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX ix_history_dispute ON cadb.dispute_status_history (dispute_id, changed_at);

CREATE TABLE cadb.notifications (
  id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id     BIGINT NOT NULL REFERENCES cadb.users(id),
  type        VARCHAR(30) NOT NULL CHECK (type IN ('Otp','DisputeReceived', 'DisputeProgress', 'DisputeFinalised')),
  channel     VARCHAR(20) NOT NULL DEFAULT 'Email',
  recipient   VARCHAR(254) NOT NULL,
  subject     VARCHAR(200),
  body        VARCHAR(2000),
  status      VARCHAR(10) NOT NULL CHECK (status IN ('Sent','Failed')),
  dispute_id  UUID REFERENCES cadb.disputes(id),
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);