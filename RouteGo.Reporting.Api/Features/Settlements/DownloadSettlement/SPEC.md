# CAP-RPT-007 — Download settlement

- Capability: CAP-RPT-007
- Canonical spec: `.product-specs/capabilities/reporting/features/settlement-reports.md`
- Area: Settlements
- Slice: `Features/Settlements/DownloadSettlement/`

## Contract

- Operation: `downloadSettlement` (`contracts/reporting/settlement.yaml`)
- Route / verb: `GET /reports/settlements/{date}/download?operatorId=`
- Response: 200 `SettlementDownloadLink` (`url`, `expiresAt`)
- Problem codes: `VALIDATION_FAILED` (400), `UNAUTHORIZED` (401), `FORBIDDEN` (403), `NOT_FOUND` (404)

## Acceptance criteria

- [x] AC-6: Short-lived link — `DownloadSettlement_ExistingSettlement_ReturnsALinkExpiringAfterTheConfiguredMinutes`
- [x] AC-7: The link stops working — enforced by the signed expiry (`X-Amz-Expires`); asserted in the AC-6 test
- [x] AC-8: No settlement, no link — `DownloadSettlement_NoSettlement_IsRefusedNotFound`
- [x] AC-9: Operator refused — `DownloadSettlement_Operator_IsRefusedForbidden`
- [x] AC-10: Missing operator or malformed day refused — `DownloadSettlement_MissingOperatorOrMalformedDay_IsRefusedValidationFailed`

## Invariants touched

- The object key is derived from (date, operator) by `Common/Sales/SettlementReportKey.cs`, shared with `RunDailySettlement`; the operator id is URI-escaped so it cannot add path segments.
- Link lifetime is the `PresignMinutes` setting (spec default 5; deployments set it).
- Admin only until Identity issues a Finance role.
