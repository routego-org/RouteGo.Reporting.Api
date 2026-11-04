-- =============================================================================
-- V0006__create_reporting_settlements.sql
-- The final gross, refunds and net for one operator on one business day.
-- =============================================================================
-- Implements: data-model/reporting/settlement.md v0.1.0
-- Change tracking: excluded - see the audit.fn_exclude_from_audit reason below.
-- Identifier: the entity's identity is the pair (settlement_date, operator_id), so the table carries no id column.
--   operator_id is an external reference (Identity's): text, no foreign key.
-- Money: whole Indian rupees. The report file is not a column: its S3 key is derived from the pair
--   (settlement/<date>/<operator>.csv), because every settlement has exactly one report.

BEGIN;

CREATE TABLE reporting.settlements (
    settlement_date date        NOT NULL,
    operator_id     text        NOT NULL,
    gross           bigint      NOT NULL,
    refunds         bigint      NOT NULL,
    net             bigint      NOT NULL,
    generated_at    timestamptz NOT NULL,
    -- At most one settlement per operator per settlement date, and the primary key refuses a second:
    -- a settlement is final once written, so a rerun inserts only what is missing.
    CONSTRAINT pk_settlements PRIMARY KEY (settlement_date, operator_id),
    CONSTRAINT ck_settlements_operator_id_len CHECK (length(operator_id) <= 64),
    CONSTRAINT ck_settlements_gross_not_negative CHECK (gross >= 0),
    CONSTRAINT ck_settlements_refunds_not_negative CHECK (refunds >= 0),
    -- Rule: "Net always equals gross minus refunds."
    CONSTRAINT ck_settlements_net_is_gross_minus_refunds CHECK (net = gross - refunds)
);

SELECT audit.fn_exclude_from_audit(
    'reporting.settlements',
    'machine-written final record: inserted once by the nightly settlement run and never changed (entity rule), carries its own generated_at');

COMMENT ON TABLE reporting.settlements IS 'data-model/reporting/settlement.md';

COMMIT;
