-- =============================================================================
-- V0005__create_reporting_payments.sql
-- Reporting's copy of one payment attempt: booking, amount, outcome and time.
-- =============================================================================
-- Implements: data-model/reporting/reported-payment.md v0.1.0
-- Change tracking: excluded - see the audit.fn_exclude_from_audit reason below.
-- Identifier: keyed by the entity's identity attribute payment_id, an external reference (Payment's): uuid.
-- booking_id is an external reference (Booking's): uuid, no foreign key.
-- DRIFT: the entity file makes amount required, but Payment's PaymentFailed event carries no amount (the
--   reporting-read-model spec says so too). amount is therefore nullable, and required for a succeeded
--   payment by ck_payments_amount_succeeded. To be reported upstream; see specs/INDEX.md.

BEGIN;

CREATE TABLE reporting.payments (
    payment_id  uuid        NOT NULL,
    booking_id  uuid        NOT NULL,
    amount      bigint      NULL,
    status      text        NOT NULL,
    occurred_at timestamptz NOT NULL,
    CONSTRAINT pk_payments PRIMARY KEY (payment_id),
    -- Attribute status: exactly the values the entity file lists.
    CONSTRAINT ck_payments_status CHECK (status IN ('succeeded', 'failed')),
    CONSTRAINT ck_payments_amount_not_negative CHECK (amount IS NULL OR amount >= 0),
    CONSTRAINT ck_payments_amount_succeeded CHECK (status <> 'succeeded' OR amount IS NOT NULL)
);

CREATE INDEX ix_payments_booking_id ON reporting.payments (booking_id);

SELECT audit.fn_exclude_from_audit(
    'reporting.payments',
    'projection rebuilt from Payment events: written only by the event worker, replayable, carries its own occurred_at');

COMMENT ON TABLE reporting.payments IS 'data-model/reporting/reported-payment.md';

COMMIT;
