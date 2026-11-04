-- =============================================================================
-- V0004__create_reporting_bookings.sql
-- Reporting's copy of one booking: trip, operator, outcome, amount, seats, key times and refund.
-- =============================================================================
-- Implements: data-model/reporting/reported-booking.md v0.1.0
-- Change tracking: excluded - see the audit.fn_exclude_from_audit reason below.
-- Identifier: keyed by the entity's identity attribute booking_id, an external reference (Booking's): uuid.
-- trip_id (Fleet's) and operator_id (Identity's) are external references: no foreign keys, so a booking
--   whose trip is not yet known is held and counted once the trip arrives.
-- Money: whole Indian rupees, as the events carry them.

BEGIN;

CREATE TABLE reporting.bookings (
    booking_id          uuid        NOT NULL,
    trip_id             uuid        NULL,
    operator_id         text        NULL,
    status              text        NOT NULL,
    amount              bigint      NULL,
    seats_count         integer     NULL,
    confirmed_at        timestamptz NULL,
    cancelled_at        timestamptz NULL,
    refund_amount       bigint      NULL,
    refund_completed_at timestamptz NULL,
    status_changed_at   timestamptz NOT NULL,
    CONSTRAINT pk_bookings PRIMARY KEY (booking_id),
    CONSTRAINT ck_bookings_operator_id_len CHECK (operator_id IS NULL OR length(operator_id) <= 64),
    -- Attribute status: exactly the values the entity file lists.
    CONSTRAINT ck_bookings_status CHECK (status IN ('confirmed', 'cancelled', 'failed', 'expired')),
    CONSTRAINT ck_bookings_amount_not_negative CHECK (amount IS NULL OR amount >= 0),
    CONSTRAINT ck_bookings_seats_count_positive CHECK (seats_count IS NULL OR seats_count > 0),
    CONSTRAINT ck_bookings_refund_amount_not_negative CHECK (refund_amount IS NULL OR refund_amount >= 0),
    -- A refund is an amount and the moment it completed, together or not at all.
    CONSTRAINT ck_bookings_refund_complete CHECK ((refund_amount IS NULL) = (refund_completed_at IS NULL))
);

-- Gross by confirmation day and refunds by completion day are range scans per operator.
CREATE INDEX ix_bookings_operator_id_confirmed_at ON reporting.bookings (operator_id, confirmed_at) WHERE confirmed_at IS NOT NULL;
CREATE INDEX ix_bookings_operator_id_refund_completed_at ON reporting.bookings (operator_id, refund_completed_at) WHERE refund_completed_at IS NOT NULL;
-- Seats sold per trip, and attributing held bookings when their trip arrives.
CREATE INDEX ix_bookings_trip_id ON reporting.bookings (trip_id) WHERE trip_id IS NOT NULL;

SELECT audit.fn_exclude_from_audit(
    'reporting.bookings',
    'projection rebuilt from Booking and Payment events: written only by the event worker, replayable, carries its own status_changed_at');

COMMENT ON TABLE reporting.bookings IS 'data-model/reporting/reported-booking.md';

COMMIT;
