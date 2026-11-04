# CAP-RPT-001 — Apply trip scheduled

- Capability: CAP-RPT-001
- Canonical spec: `.product-specs/capabilities/reporting/features/reporting-read-model.md` (v0.1.1)
- Area: ReadModel
- Slice: `Features/ReadModel/ApplyTripScheduled/`

## Contract

`kind: worker` — no HTTP operation. Queue `reporting-q`, event type `TripScheduled` (`ApplyTripScheduledHandler : IEventWorker`).

- Event data (Fleet's wire): `tripId`, `operatorId`, `source.cityName`, `destination.cityName`, `departureUtc`, `totalSeats`. The rest is ignored.
- Writes: `reporting.trips` (upsert), `reporting.bookings.operator_id` (held bookings of the trip).
- Worker alerts: `invalidEventData`.

## Acceptance criteria

- [x] AC-6: Held bookings count once the trip arrives — `HandleAsync_TripArrivingAfterItsBooking_AttributesTheBookingToItsOperator`
- [x] AC-7: Latest announcement wins — `HandleAsync_LaterAnnouncement_ReplacesDetails`, `HandleAsync_OlderAnnouncementAfterNewer_ChangesNothing`
- [x] AC-2: Repeat changes nothing — `HandleAsync_SameEventTwice_HoldsOneTrip`
- [x] AC-10: Unusable data is rejected — `HandleAsync_TripWithoutSeats_IsRejected`

## Invariants touched

- Ordering is by the envelope's `occurredAt`, compared inside the upsert's `WHERE`, so it holds under concurrent workers.
