# CAP-RPT-004 — Get admin summary

- Capability: CAP-RPT-004
- Canonical spec: `.product-specs/capabilities/reporting/features/admin-summary.md`
- Area: Reports
- Slice: `Features/Reports/GetAdminSummary/`

## Contract

- Operation: `getAdminSummary` (`contracts/reporting/admin-summary.yaml`)
- Route / verb: `GET /reports/admin/summary?from=&to=`
- Response: 200 `AdminSummary` (same shape as the operator summary)
- Problem codes: `VALIDATION_FAILED` (400), `UNAUTHORIZED` (401), `FORBIDDEN` (403)

## Acceptance criteria

- [x] AC-1: Totals across all operators — `GetAdminSummary_TwoOperators_SumsBoth`
- [x] AC-3: A quiet day appears as zero — `GetAdminSummary_QuietDayInTheRange_AppearsAsZero`
- [x] AC-4: Operator refused — `GetAdminSummary_Operator_IsRefusedForbidden`
- [x] AC-5: Reversed range refused — `GetAdminSummary_RangeEndingBeforeItStarts_IsRefusedValidationFailed`

AC-2 (agrees with the operators' own figures) holds by construction: both slices read through `Common/Sales/SalesSummaryReader.cs`.

## Invariants touched

- Admin only: Identity issues no Finance role yet, recorded as a deviation from the spec's Finance access.
