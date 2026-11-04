# CAP-RPT-005 — Run daily settlement

- Capability: CAP-RPT-005
- Canonical spec: `.product-specs/capabilities/reporting/features/daily-settlement.md`
- Area: Settlements
- Slice: `Features/Settlements/RunDailySettlement/`

## Contract

`kind: worker` — no HTTP operation. Trigger: a message on `reporting-schedule-q` (the nightly schedule, 01:00 India Standard Time). The body is optional; `{"settlementDate":"yyyy-MM-dd"}` settles that day (manual re-run), otherwise the business day before the one the message arrived on (`RunDailySettlementHandler : IEventWorker`, read by `SettlementScheduleQueueService`).

- Reads: `reporting.bookings` (gross by confirmation day, refunds by completion day, IST days).
- Writes: `reporting.settlements` (insert once per day and operator), one CSV per settlement at `settlement/<date>/<operator>.csv` in the reports bucket.
- Worker alerts: `SETTLEMENT_INCOMPLETE` (logged per failed operator), `invalidEventData` (a trigger naming a non-date).

## Acceptance criteria

- [x] AC-1, AC-4: Each operator with activity gets a settlement and report — `HandleAsync_NightlyRun_SettlesEachOperatorWithActivityForThePreviousDay`
- [x] AC-2: The day is a business day — `HandleAsync_BookingConfirmedAfterMidnightInIndia_BelongsToTheNextDay`
- [x] AC-3: A refund counts on its completion day — `HandleAsync_RefundCompletedLater_CountsOnItsCompletionDayOnly`
- [x] AC-5, AC-6: Final once written — `HandleAsync_SecondRunAfterALateConfirmation_ChangesNothing`
- [x] AC-7: One failure does not stop the rest — `HandleAsync_OneOperatorFailing_SettlesTheOthersAndPostponesTheRun`
- [x] AC-8: A rerun writes only the missing — `HandleAsync_RerunAfterAFailure_WritesOnlyTheMissingSettlement`
- [x] AC-9: No activity, no settlement — covered by the first test (the dormant operator has none)
- [x] A trigger naming a non-date is rejected — `HandleAsync_TriggerNamingANonDate_IsRejected`

## Invariants touched

- The settlement row is inserted first and its report written before the commit, so no settlement exists without its report and only the run that inserted the row writes it.
- Postponing on a partial failure makes the queue redeliver; the redelivered run settles only operators without a row.
- Reports are encrypted at rest; the 7-year retention is a bucket lifecycle rule owned by the deployment.
