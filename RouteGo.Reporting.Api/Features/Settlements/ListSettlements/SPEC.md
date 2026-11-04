# CAP-RPT-006 — List settlements

- Capability: CAP-RPT-006
- Canonical spec: `.product-specs/capabilities/reporting/features/settlement-reports.md`
- Area: Settlements
- Slice: `Features/Settlements/ListSettlements/`

## Contract

- Operation: `listSettlements` (`contracts/reporting/settlement.yaml`)
- Route / verb: `GET /reports/settlements?date=`
- Response: 200 `SettlementListResponse` (`items[]` of `Settlement`, `totalCount`)
- Problem codes: `VALIDATION_FAILED` (400), `UNAUTHORIZED` (401), `FORBIDDEN` (403)

## Acceptance criteria

- [x] AC-1: List the day's settlements — `ListSettlements_DayWithSettlements_ReturnsThemAllWithTotal`
- [x] AC-2: Unsettled day is empty — `ListSettlements_DayNotYetSettled_ReturnsEmptyList`
- [x] AC-3: Admin may list — covered by AC-1 (the caller is an admin)
- [x] AC-4: Operator refused — `ListSettlements_Operator_IsRefusedForbidden`
- [x] AC-5: Missing or malformed day refused — `ListSettlements_MissingOrMalformedDay_IsRefusedValidationFailed`

## Invariants touched

- Admin only: Identity issues no Finance role yet (deviation from the spec's Finance access, recorded in the build notes).
