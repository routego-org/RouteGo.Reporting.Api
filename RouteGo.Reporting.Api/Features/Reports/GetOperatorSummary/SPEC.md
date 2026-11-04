# CAP-RPT-002 — Get operator summary

- Capability: CAP-RPT-002
- Canonical spec: `.product-specs/capabilities/reporting/features/operator-dashboard.md` (v0.1.0)
- Area: Reports
- Slice: `Features/Reports/GetOperatorSummary/`

## Contract

- Operation: `getOperatorSummary` (`contracts/reporting/operator-summary.yaml`)
- Route / verb: `GET /reports/operator/summary?from=&to=`
- Response: 200 `OperatorSummary` (`from`, `to`, `currency`, `totals`, `dailySeries`)
- Problem codes: `VALIDATION_FAILED` (400), `UNAUTHORIZED` (401), `FORBIDDEN` (403)

## Acceptance criteria

- [x] AC-1: Totals and daily series — `GetOperatorSummary_TwoDaysOfActivity_ReturnsTotalsAndSeries`
- [x] AC-2: A quiet day appears as zero — `GetOperatorSummary_QuietDayInTheRange_AppearsAsZero`
- [x] AC-3: No data is zeros, not an error — `GetOperatorSummary_NoActivityAtAll_ReturnsZeroTotals`
- [x] AC-4: Never another operator's figures — `GetOperatorSummary_TwoOperators_SeesOnlyItsOwn`
- [x] AC-5: Reversed range refused — `GetOperatorSummary_RangeEndingBeforeItStarts_IsRefusedValidationFailed`
- [x] AC-6: Token without an operator refused — `GetOperatorSummary_TokenNamingNoOperator_IsRefusedForbidden`
- [x] AC-7: Admin refused — `GetOperatorSummary_Admin_IsRefusedForbidden`

## Invariants touched

- The operator comes from the token only; the query's `WHERE operator_id` is the authorization.
- Business days are India Standard Time days; gross by confirmation day, refunds by completion day (`Common/Sales/SalesSummaryReader.cs`, shared with `GetAdminSummary`).
