-- =====================================================================
-- Mock seed data for schema cadb (PostgreSQL)
-- Covers: users, user_profiles, user_contact, accounts, transactions
-- NOT seeded on purpose: otp_codes, notifications (created at login),
--                        disputes, dispute_status_history.
--
-- Run top to bottom in one go (psql -f or pgAdmin query tool).
-- Transactions span the LAST 6 MONTHS relative to when you run it, and
-- are generated with a fixed random seed so the shape is repeatable.
-- To re-run, execute the CLEANUP block at the bottom first.
-- =====================================================================

BEGIN;

SELECT setseed(0.42);

-- ---------------------------------------------------------------------
-- 1. USERS  (5 customers + 1 consultant; ids are fixed 10-digit numbers)
-- Users 1-2 are real inboxes. Users 3-5 are Gmail plus-aliases (they still
-- land in your inboxes) reserved for assessors - swap them later using the
-- ASSESSOR EMAIL SWAP block at the bottom.
-- ---------------------------------------------------------------------
INSERT INTO cadb.users (id, email, username, role, created_at) VALUES
  (1000000001, 'example1@gmail.com',                'testuser',     'Customer', now() - interval '7 months'),
  (1000000002, 'example2@gmail.com',                'nkosithibo',   'Customer', now() - interval '7 months'),
(1000000003, 'example3@gmail.com',                  'assessor1',    'Customer', now() - interval '7 months'),
(1000000004, 'example4@gmail.com',                  'assessor2',    'Customer', now() - interval '7 months'),
  (1000000005, 'simonsays+assessor3@gmail.com',     'assessor3',    'Customer', now() - interval '7 months'),
  -- Consultant: signs in by OTP using the username 'consultant1' (no account number needed) and can use /api/v1/consultant/*.
  -- Consultant usernames must contain a letter, so they can never be mistaken for a 10-digit account number.
  (1000000006, 'thebestconsultant+111@gmail.com',   'consultant1','Consultant', now() - interval '7 months');

-- ---------------------------------------------------------------------
-- 2. USER PROFILES
-- ---------------------------------------------------------------------
INSERT INTO cadb.user_profiles (user_id, first_name, last_name) VALUES
  (1000000001, 'Lerato', 'Dlamini'),
  (1000000002, 'Thabo',  'Nkosi'),
  (1000000003, 'Ayesha', 'Patel'),
  (1000000004, 'Pieter', 'van der Merwe'),
  (1000000005, 'Naledi', 'Khumalo'),
  (1000000006, 'touch', 'man');

-- ---------------------------------------------------------------------
-- 3. USER CONTACT (contact_email mirrors the login email)
-- ---------------------------------------------------------------------
INSERT INTO cadb.user_contact (user_id, contact_number, contact_email) VALUES
  (1000000001, '+27821234501', 'example1@gmail.com'),
  (1000000002, '+27821234502', 'example2@gmail.com'),
  (1000000003, '+27821234503', 'example3@gmail.com'),
  (1000000004, '+27821234504', 'example4@gmail.com'),
  (1000000005, '+27821234505', 'simonsays+assessor3@gmail.com'),
  (1000000006, '+27821234506', 'thebestconsultant+111@gmail.com');

-- ---------------------------------------------------------------------
-- 4. ACCOUNTS (balance starts at 0, recalculated from the ledger in step 7)
-- ---------------------------------------------------------------------
INSERT INTO cadb.accounts (id, user_id, account_number, account_type, name, currency, balance, created_at) VALUES
  ('aaaaaaaa-0000-0000-0000-000000000101', 1000000001, '1052000101', 'Transact', 'Global One Account', 'ZAR', 0, now() - interval '7 months'),
  ('aaaaaaaa-0000-0000-0000-000000000102', 1000000001, '1052000102', 'Savings',  'Flexi Savings',      'ZAR', 0, now() - interval '7 months'),
  ('aaaaaaaa-0000-0000-0000-000000000103', 1000000001, '1052000103', 'Credit',   'Credit Card',        'ZAR', 0, now() - interval '7 months'),
  ('aaaaaaaa-0000-0000-0000-000000000201', 1000000002, '1052000201', 'Transact', 'Global One Account', 'ZAR', 0, now() - interval '7 months'),
  ('aaaaaaaa-0000-0000-0000-000000000202', 1000000002, '1052000202', 'Savings',  'Flexi Savings',      'ZAR', 0, now() - interval '7 months'),
  ('aaaaaaaa-0000-0000-0000-000000000203', 1000000002, '1052000203', 'Credit',   'Credit Card',        'ZAR', 0, now() - interval '7 months'),
  ('aaaaaaaa-0000-0000-0000-000000000301', 1000000003, '1052000301', 'Transact', 'Global One Account', 'ZAR', 0, now() - interval '7 months'),
  ('aaaaaaaa-0000-0000-0000-000000000302', 1000000003, '1052000302', 'Credit',   'Credit Card',        'ZAR', 0, now() - interval '7 months'),
  ('aaaaaaaa-0000-0000-0000-000000000401', 1000000004, '1052000401', 'Transact', 'Global One Account', 'ZAR', 0, now() - interval '7 months'),
  ('aaaaaaaa-0000-0000-0000-000000000402', 1000000004, '1052000402', 'Savings',  'Flexi Savings',      'ZAR', 0, now() - interval '7 months'),
  ('aaaaaaaa-0000-0000-0000-000000000501', 1000000005, '1052000501', 'Transact', 'Global One Account', 'ZAR', 0, now() - interval '7 months'),
  ('aaaaaaaa-0000-0000-0000-000000000502', 1000000005, '1052000502', 'Credit',   'Credit Card',        'ZAR', 0, now() - interval '7 months');
-- The consultant (1000000006) has no account: they sign in with their username.

-- ---------------------------------------------------------------------
-- 5. TRANSACTION GENERATION HELPERS (temp tables, dropped on COMMIT)
-- ---------------------------------------------------------------------

-- Per-account behaviour: how many random card purchases, opening balance,
-- salary / rent (Transact), monthly repayment (Credit), monthly deposit (Savings).
CREATE TEMP TABLE _plan (
  account_id  uuid PRIMARY KEY,
  acct_type   text,
  card_count  int,
  opening     numeric(12,2),
  salary      numeric(12,2),
  rent        numeric(12,2),
  repayment   numeric(12,2),
  deposit     numeric(12,2)
) ON COMMIT DROP;

INSERT INTO _plan (account_id, acct_type, card_count, opening, salary, rent, repayment, deposit) VALUES
  ('aaaaaaaa-0000-0000-0000-000000000101', 'Transact', 420,  8500.00, 38500.00,  9500.00, NULL,    NULL),
  ('aaaaaaaa-0000-0000-0000-000000000102', 'Savings',    0, 25000.00,     NULL,     NULL, NULL, 3000.00),
  ('aaaaaaaa-0000-0000-0000-000000000103', 'Credit',   150,     0.00,     NULL,     NULL, 6000.00,  NULL),
  ('aaaaaaaa-0000-0000-0000-000000000201', 'Transact', 380,  5200.00, 32000.00,  8200.00, NULL,    NULL),
  ('aaaaaaaa-0000-0000-0000-000000000202', 'Savings',    0, 12000.00,     NULL,     NULL, NULL, 2000.00),
  ('aaaaaaaa-0000-0000-0000-000000000203', 'Credit',   120,     0.00,     NULL,     NULL, 4500.00,  NULL),
  ('aaaaaaaa-0000-0000-0000-000000000301', 'Transact', 300,  3000.00, 24500.00,  6500.00, NULL,    NULL),
  ('aaaaaaaa-0000-0000-0000-000000000302', 'Credit',   100,     0.00,     NULL,     NULL, 3500.00,  NULL),
  ('aaaaaaaa-0000-0000-0000-000000000401', 'Transact', 250, 12000.00, 45000.00, 12500.00, NULL,    NULL),
  ('aaaaaaaa-0000-0000-0000-000000000402', 'Savings',    0, 60000.00,     NULL,     NULL, NULL, 5000.00),
  ('aaaaaaaa-0000-0000-0000-000000000501', 'Transact', 330,  4100.00, 29000.00,  7000.00, NULL,    NULL),
  ('aaaaaaaa-0000-0000-0000-000000000502', 'Credit',    90,     0.00,     NULL,     NULL, 3000.00,  NULL);

-- Merchant catalogue for random card spend.
CREATE TEMP TABLE _merchants (
  id        serial PRIMARY KEY,
  prefix    text,
  merchant  text,
  location  text,
  min_amt   numeric,
  max_amt   numeric
) ON COMMIT DROP;

INSERT INTO _merchants (prefix, merchant, location, min_amt, max_amt) VALUES
  -- groceries (several entries so they dominate, like real life)
  ('POS PURCHASE', 'Woolworths Food',   'Sandton City',  45,  1200),
  ('POS PURCHASE', 'Checkers',          'Hyde Park',     35,  1500),
  ('POS PURCHASE', 'Checkers Hyper',    'Rosebank',      80,  2200),
  ('POS PURCHASE', 'Pick n Pay',        'Fourways',      30,   950),
  ('POS PURCHASE', 'Shoprite',          'Randburg',      25,   800),
  ('POS PURCHASE', 'Spar',              'Melville',      20,   600),
  ('POS PURCHASE', 'Food Lovers Market','Bryanston',     40,   900),
  -- fuel
  ('POS PURCHASE', 'Engen',             'Sunninghill',  250,  1100),
  ('POS PURCHASE', 'Shell Ultra City',  'N1 Midrand',   300,  1200),
  ('POS PURCHASE', 'Sasol',             'Randpark',     200,   950),
  ('POS PURCHASE', 'BP Express',        'Northgate',    220,   900),
  -- food & coffee
  ('POS PURCHASE', 'Vida e Caffe',      'Sandton',       35,    90),
  ('POS PURCHASE', 'Starbucks',         'Rosebank',      45,   110),
  ('POS PURCHASE', 'Steers',            'Randburg',      60,   260),
  ('POS PURCHASE', 'Nandos',            'Cresta',        90,   420),
  ('POS PURCHASE', 'KFC',               'Northcliff',    70,   300),
  ('POS PURCHASE', 'Spur',              'Fourways',     180,   900),
  ('POS PURCHASE', 'Mugg & Bean',       'Hyde Park',    110,   520),
  ('POS PURCHASE', 'Wimpy',             'Midrand',       80,   350),
  -- retail
  ('POS PURCHASE', 'Clicks',            'Sandton',       40,   780),
  ('POS PURCHASE', 'Dis-Chem',          'Fourways',      55,   950),
  ('POS PURCHASE', 'Woolworths Fashion','Sandton City', 200,  2400),
  ('POS PURCHASE', 'Mr Price',          'Cresta',        80,   900),
  ('POS PURCHASE', 'Pep',               'Randburg',      40,   450),
  ('POS PURCHASE', 'Builders Warehouse','Strijdom Park', 90,  3200),
  ('POS PURCHASE', 'Cotton On',         'Rosebank',     120,   800),
  ('POS PURCHASE', 'Edgars',            'Eastgate',     150,  1800),
  ('POS PURCHASE', 'Game',              'Fourways',     180,  4500),
  -- transport
  ('POS PURCHASE', 'Uber Trip',         NULL,            45,   380),
  ('POS PURCHASE', 'Bolt',              NULL,            40,   320),
  ('POS PURCHASE', 'Gautrain',          'Sandton',       30,   180),
  ('POS PURCHASE', 'E-Toll / SANRAL',   NULL,            25,    90),
  -- online
  ('ONLINE PURCHASE', 'Takealot',       NULL,            90,  4500),
  ('ONLINE PURCHASE', 'Amazon',         NULL,           120,  2500),
  ('ONLINE PURCHASE', 'Uber Eats',      NULL,            85,   480),
  ('ONLINE PURCHASE', 'Mr D Food',      NULL,            80,   450),
  ('ONLINE PURCHASE', 'Spotify',        NULL,            60,    80),
  ('ONLINE PURCHASE', 'Apple Services', NULL,            15,   400),
  ('ONLINE PURCHASE', 'Google Play',    NULL,            10,   300),
  ('ONLINE PURCHASE', 'Showmax',        NULL,            59,   139),
  ('ONLINE PURCHASE', 'Computicket',    NULL,           150,  1600),
  ('ONLINE PURCHASE', 'Airbnb',         NULL,           600,  4800);

-- Recurring debit orders applied to every Transact account.
CREATE TEMP TABLE _debit_orders (
  description text,
  merchant    text,
  amount      numeric(12,2),
  day         int
) ON COMMIT DROP;

INSERT INTO _debit_orders (description, merchant, amount, day) VALUES
  ('DEBIT ORDER VODACOM CONTRACT', 'Vodacom',          649.00,  1),
  ('DEBIT ORDER DISCOVERY HEALTH','Discovery Health', 2150.00,  2),
  ('DEBIT ORDER VIRGIN ACTIVE',   'Virgin Active',     799.00,  3),
  ('DEBIT ORDER NETFLIX.COM',     'Netflix',           199.00,  5),
  ('DEBIT ORDER DSTV',            'DStv',              459.00,  7),
  ('DEBIT ORDER OUTSURANCE',      'Outsurance',        585.00, 28);

-- Calendar months we generate scheduled items for (current month back 6).
CREATE TEMP TABLE _months (
  k           int PRIMARY KEY,
  month_start timestamptz
) ON COMMIT DROP;

INSERT INTO _months (k, month_start)
SELECT k, date_trunc('month', now()) - make_interval(months => k)
FROM generate_series(0, 6) AS k;

-- All scheduled (monthly) items, per account.
CREATE TEMP TABLE _sched (
  account_id  uuid,
  description text,
  merchant    text,
  amount      numeric(12,2),
  type        text,
  day         int,
  only_k      int[]            -- NULL = every month, otherwise only those month offsets
) ON COMMIT DROP;

-- Salary + rent (Transact)
INSERT INTO _sched (account_id, description, merchant, amount, type, day, only_k)
SELECT account_id, 'SALARY ACME HOLDINGS',  'Acme Holdings', salary, 'Credit', 25, NULL FROM _plan WHERE acct_type = 'Transact';
INSERT INTO _sched (account_id, description, merchant, amount, type, day, only_k)
SELECT account_id, 'DEBIT ORDER RENT',      'Landlord',      rent,   'Debit',   1, NULL FROM _plan WHERE acct_type = 'Transact';
-- Standard debit orders (Transact)
INSERT INTO _sched (account_id, description, merchant, amount, type, day, only_k)
SELECT p.account_id, d.description, d.merchant, d.amount, 'Debit', d.day, NULL
FROM _plan p CROSS JOIN _debit_orders d WHERE p.acct_type = 'Transact';
-- Credit card repayments
INSERT INTO _sched (account_id, description, merchant, amount, type, day, only_k)
SELECT account_id, 'PAYMENT RECEIVED - THANK YOU', 'Credit Card Payment', repayment, 'Credit', 20, NULL FROM _plan WHERE acct_type = 'Credit';
-- Savings: monthly deposit, monthly interest, two ad-hoc withdrawals
INSERT INTO _sched (account_id, description, merchant, amount, type, day, only_k)
SELECT account_id, 'TRANSFER FROM GLOBAL ONE ACCOUNT', 'Own Account',   deposit,                      'Credit', 26, NULL     FROM _plan WHERE acct_type = 'Savings';
INSERT INTO _sched (account_id, description, merchant, amount, type, day, only_k)
SELECT account_id, 'INTEREST CAPITALISED',             'Capitec Bank',  round(opening * 0.006, 2),    'Credit', 28, NULL     FROM _plan WHERE acct_type = 'Savings';
INSERT INTO _sched (account_id, description, merchant, amount, type, day, only_k)
SELECT account_id, 'TRANSFER TO GLOBAL ONE ACCOUNT',   'Own Account',   1500.00,                      'Debit',  12, ARRAY[1,3] FROM _plan WHERE acct_type = 'Savings';

-- ---------------------------------------------------------------------
-- 6. TRANSACTIONS
-- ---------------------------------------------------------------------

-- 6a. Opening balance (so balance == ledger sum exactly)
INSERT INTO cadb.transactions (account_id, transaction_date, description, merchant, amount, type, reference)
SELECT account_id, now() - interval '6 months', 'OPENING BALANCE', NULL, opening, 'Credit',
       'OPEN-' || upper(substr(md5(account_id::text), 1, 8))
FROM _plan
WHERE opening > 0;

-- 6b. Scheduled monthly items (only those that fall inside the last 6 months and not in the future)
INSERT INTO cadb.transactions (account_id, transaction_date, description, merchant, amount, type, reference)
SELECT s.account_id,
       m.month_start + make_interval(days => s.day - 1, hours => 8),
       s.description, s.merchant, s.amount, s.type,
       'SCH-' || upper(substr(md5(random()::text), 1, 10))
FROM _sched s
CROSS JOIN _months m
WHERE (s.only_k IS NULL OR m.k = ANY (s.only_k))
  AND m.month_start + make_interval(days => s.day - 1, hours => 8) BETWEEN now() - interval '6 months' AND now();

-- 6c. Random card purchases (Transact + Credit accounts)
INSERT INTO cadb.transactions (account_id, transaction_date, description, merchant, amount, type, reference)
SELECT g.account_id,
       (now() - interval '6 months') + g.r_date * interval '6 months',
       m.prefix || ' ' || upper(m.merchant) || COALESCE(' ' || upper(m.location), ''),
       m.merchant,
       round((m.min_amt + g.r_amt * g.r_amt * (m.max_amt - m.min_amt))::numeric, 2),
       'Debit',
       'CRD-' || upper(substr(md5(random()::text), 1, 10))
FROM (
  SELECT p.account_id, random() AS r_m, random() AS r_date, random() AS r_amt
  FROM _plan p
  CROSS JOIN LATERAL generate_series(1, p.card_count) AS n
  WHERE p.card_count > 0
) g
JOIN _merchants m ON m.id = 1 + floor(g.r_m * (SELECT count(*) FROM _merchants))::int;

-- 6d. ATM withdrawals (Transact, 14 each)
INSERT INTO cadb.transactions (account_id, transaction_date, description, merchant, amount, type, reference)
SELECT g.account_id,
       (now() - interval '6 months') + g.r_date * interval '6 months',
       'ATM WITHDRAWAL CAPITEC ATM',
       'Capitec ATM',
       ((2 + floor(g.r_amt * 8)) * 100)::numeric(12,2),   -- R200 - R900 in R100 steps
       'Debit',
       'ATM-' || upper(substr(md5(random()::text), 1, 10))
FROM (
  SELECT p.account_id, random() AS r_date, random() AS r_amt
  FROM _plan p
  CROSS JOIN LATERAL generate_series(1, 14) AS n
  WHERE p.acct_type = 'Transact'
) g;

-- 6e. Incoming EFTs from friends/family (Transact, 10 each)
INSERT INTO cadb.transactions (account_id, transaction_date, description, merchant, amount, type, reference)
SELECT g.account_id,
       (now() - interval '6 months') + g.r_date * interval '6 months',
       'EFT CREDIT ' || upper(f.name),
       f.name,
       round((150 + g.r_amt * 1850)::numeric, 2),
       'Credit',
       'EFT-' || upper(substr(md5(random()::text), 1, 10))
FROM (
  SELECT p.account_id, random() AS r_date, random() AS r_amt, random() AS r_name
  FROM _plan p
  CROSS JOIN LATERAL generate_series(1, 10) AS n
  WHERE p.acct_type = 'Transact'
) g
JOIN (VALUES (1,'S Mokoena'), (2,'A Naidoo'), (3,'J Botha'), (4,'T Mahlangu'), (5,'N Jacobs')) AS f(id, name)
  ON f.id = 1 + floor(g.r_name * 5)::int;

-- ---------------------------------------------------------------------
-- 7. RECALCULATE BALANCES FROM THE LEDGER
-- (Credit-card balances are negative = amount owed)
-- ---------------------------------------------------------------------
UPDATE cadb.accounts a
SET balance = COALESCE((
  SELECT SUM(CASE t.type WHEN 'Credit' THEN t.amount ELSE -t.amount END)
  FROM cadb.transactions t
  WHERE t.account_id = a.id
), 0)
WHERE a.user_id BETWEEN 1000000001 AND 1000000005;

COMMIT;

-- =====================================================================
-- VERIFICATION (run after the script)
-- =====================================================================
-- SELECT 'users' AS tbl, count(*) FROM cadb.users
-- UNION ALL SELECT 'user_profiles', count(*) FROM cadb.user_profiles
-- UNION ALL SELECT 'user_contact',  count(*) FROM cadb.user_contact
-- UNION ALL SELECT 'accounts',      count(*) FROM cadb.accounts
-- UNION ALL SELECT 'transactions',  count(*) FROM cadb.transactions;
--
-- -- Rows per account + date range (should span ~6 months)
-- SELECT a.account_number, a.account_type, count(*) AS txns,
--        min(t.transaction_date)::date AS first_txn, max(t.transaction_date)::date AS last_txn, a.balance
-- FROM cadb.accounts a JOIN cadb.transactions t ON t.account_id = a.id
-- GROUP BY a.account_number, a.account_type, a.balance ORDER BY a.account_number;
--
-- -- Balance must equal ledger sum (expect zero rows)
-- SELECT a.account_number FROM cadb.accounts a
-- WHERE a.balance <> (SELECT SUM(CASE type WHEN 'Credit' THEN amount ELSE -amount END)
--                     FROM cadb.transactions WHERE account_id = a.id);

-- =====================================================================
-- ASSESSOR EMAIL SWAP (uncomment and edit when assessor emails are known)
-- =====================================================================
-- UPDATE cadb.users        SET email = 'assessor.one@example.com'  WHERE id = 1000000003;
-- UPDATE cadb.user_contact SET contact_email = 'assessor.one@example.com', updated_at = now() WHERE user_id = 1000000003;
-- (repeat for 1000000004 and 1000000005)

-- =====================================================================
-- CLEANUP (uncomment to remove all seed data before re-running)
-- Order matters because of foreign keys. Also clears OTP/notification rows
-- the seeded users generated by logging in.
-- =====================================================================
-- BEGIN;
-- DELETE FROM cadb.dispute_status_history WHERE dispute_id IN (SELECT d.id FROM cadb.disputes d JOIN cadb.transactions t ON t.id = d.transaction_id JOIN cadb.accounts a ON a.id = t.account_id WHERE a.user_id BETWEEN 1000000001 AND 1000000006);
-- DELETE FROM cadb.notifications  WHERE user_id BETWEEN 1000000001 AND 1000000006;
-- DELETE FROM cadb.disputes       WHERE transaction_id IN (SELECT t.id FROM cadb.transactions t JOIN cadb.accounts a ON a.id = t.account_id WHERE a.user_id BETWEEN 1000000001 AND 1000000006);
-- DELETE FROM cadb.transactions   WHERE account_id IN (SELECT id FROM cadb.accounts WHERE user_id BETWEEN 1000000001 AND 1000000006);
-- DELETE FROM cadb.accounts       WHERE user_id BETWEEN 1000000001 AND 1000000006;
-- DELETE FROM cadb.otp_codes      WHERE user_id BETWEEN 1000000001 AND 1000000006;
-- DELETE FROM cadb.user_contact   WHERE user_id BETWEEN 1000000001 AND 1000000006;
-- DELETE FROM cadb.user_profiles  WHERE user_id BETWEEN 1000000001 AND 1000000006;
-- DELETE FROM cadb.users          WHERE id BETWEEN 1000000001 AND 1000000006;
-- COMMIT;
