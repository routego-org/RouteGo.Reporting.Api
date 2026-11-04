# CAP-RPT-001 — Apply booking event

- Capability: CAP-RPT-001
- Canonical spec: `.product-specs/capabilities/reporting/features/reporting-read-model.md` (v0.1.1)
- Area: ReadModel
- Slice: `Features/ReadModel/ApplyBookingEvent/`

## Contract

`kind: worker` — no HTTP operation. Queue `reporting-q`, event types `BookingConfirmed`, `BookingCancelled`, `BookingFailed`, `BookingExpired` (`ApplyBookingEventHandler : IEventWorker`).

- Event data (Booking's wire): `bookingId`; confirmed also `trip.tripId`, `operator.operatorId`, `totalAmount` (whole rupees), `seats[]`, `confirmedAt`; the others `tripId`. A cancellation's time is the envelope's `occurredAt`.
- Writes: `reporting.bookings` (merge by booking).
- Worker alerts: `invalidEventData`.

## Acceptance criteria

- [x] AC-1: Confirmed booking is held for its operator — `HandleAsync_Confirmation_HoldsTheBookingForItsOperator`
- [x] AC-2: Repeat changes nothing — `HandleAsync_ConfirmationTwice_HoldsOneBooking`
- [x] AC-3: Early cancellation is not undone — `HandleAsync_ConfirmationAfterALaterCancellation_KeepsCancelledAndTakesTheFacts`
- [x] AC-5: Failed and expired sell nothing — `HandleAsync_FailedAndExpired_HoldNoAmountOrSeats`
- [x] AC-6: Held until the trip is known — `HandleAsync_CancellationForAKnownTrip_TakesTheTripsOperator`
- [x] AC-10: Unusable data is rejected — `HandleAsync_ConfirmationWithoutAmount_IsRejected`

## Invariants touched

- Later state wins by `status_changed_at`, evaluated inside the upsert so it holds under concurrent workers.
- Every fact column is `COALESCE(existing, incoming)`: an event adds, never erases.
