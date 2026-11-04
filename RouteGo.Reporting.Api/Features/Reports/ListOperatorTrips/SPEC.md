# CAP-RPT-003 — List operator trips

- Capability: CAP-RPT-003
- Canonical spec: `.product-specs/capabilities/reporting/features/operator-dashboard.md` (v0.1.0)
- Area: Reports
- Slice: `Features/Reports/ListOperatorTrips/`

## Contract

- Operation: `listOperatorTrips` (`contracts/reporting/operator-trip.yaml`)
- Route / verb: `GET /reports/operator/trips?date=`
- Response: 200 `OperatorTripListResponse` (`items[]` of `TripOccupancy`, `totalCount`)
- Problem codes: `VALIDATION_FAILED` (400), `UNAUTHORIZED` (401), `FORBIDDEN` (403)

## Acceptance criteria

- [x] AC-8: Occupancy per trip — `ListOperatorTrips_TripsWithBookings_ReturnsSeatsSoldAndTotal`
- [x] AC-9: A cancelled booking frees its seats — `ListOperatorTrips_CancelledBooking_IsNotCountedAsSold`
- [x] AC-10: A day with no trips is empty — `ListOperatorTrips_DayWithNoTrips_ReturnsEmptyList`
- [x] AC-11: Never another operator's trips — `ListOperatorTrips_TwoOperators_SeesOnlyItsOwn`
- [x] AC-12: Missing day refused — `ListOperatorTrips_MissingDay_IsRefusedValidationFailed`
- [x] Token without an operator refused — `ListOperatorTrips_TokenNamingNoOperator_IsRefusedForbidden`

## Invariants touched

- The operator comes from the token only; the query's `WHERE operator_id` is the authorization.
- A trip belongs to the India Standard Time day it departs on.
