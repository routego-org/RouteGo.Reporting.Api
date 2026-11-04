-- =============================================================================
-- V0003__create_reporting_trips.sql
-- Reporting's copy of one scheduled departure: operator, route, departure and seat count.
-- =============================================================================
-- Implements: data-model/reporting/reported-trip.md v0.1.0
-- Change tracking: excluded - see the audit.fn_exclude_from_audit reason below.
-- Identifier: keyed by the entity's identity attribute trip_id, an external reference (Fleet's): uuid, no foreign key.
-- operator_id is an external reference (Identity's): text, no foreign key.

BEGIN;

CREATE SCHEMA IF NOT EXISTS reporting;

CREATE TABLE reporting.trips (
    trip_id           uuid        NOT NULL,
    operator_id       text        NOT NULL,
    source            text        NOT NULL,
    destination       text        NOT NULL,
    departure_at      timestamptz NOT NULL,
    seats_total       integer     NOT NULL,
    last_announced_at timestamptz NOT NULL,
    CONSTRAINT pk_trips PRIMARY KEY (trip_id),
    CONSTRAINT ck_trips_operator_id_len CHECK (length(operator_id) <= 64),
    CONSTRAINT ck_trips_source_len CHECK (length(source) <= 200),
    CONSTRAINT ck_trips_destination_len CHECK (length(destination) <= 200),
    -- Attribute seats_total: "a whole number above zero".
    CONSTRAINT ck_trips_seats_total_positive CHECK (seats_total > 0)
);

-- The operator's trips on a business day: the occupancy list reads by operator and departure.
CREATE INDEX ix_trips_operator_id_departure_at ON reporting.trips (operator_id, departure_at);

SELECT audit.fn_exclude_from_audit(
    'reporting.trips',
    'projection rebuilt from Fleet TripScheduled events: written only by the event worker, replayable, carries its own last_announced_at');

COMMENT ON TABLE reporting.trips IS 'data-model/reporting/reported-trip.md';

COMMIT;
