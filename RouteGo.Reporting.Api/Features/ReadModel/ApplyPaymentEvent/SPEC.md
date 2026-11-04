# CAP-RPT-001 — Apply payment event

- Capability: CAP-RPT-001
- Canonical spec: `.product-specs/capabilities/reporting/features/reporting-read-model.md` (v0.1.1)
- Area: ReadModel
- Slice: `Features/ReadModel/ApplyPaymentEvent/`

## Contract

`kind: worker` — no HTTP operation. Queue `reporting-q`, event types `PaymentSucceeded`, `PaymentFailed`, `RefundCompleted` (`ApplyPaymentEventHandler : IEventWorker`).

- Event data (Payment's wire): `bookingId`; `paymentId` (payment events); `amount` (whole rupees; required for PaymentSucceeded and RefundCompleted; PaymentFailed carries none). A refund's completion time is the envelope's `occurredAt`.
- Writes: `reporting.payments` (merge by payment), `reporting.bookings.refund_*`.
- Worker alerts: `invalidEventData`.

## Acceptance criteria

- [x] AC-4: Completed refund recorded when it completes — `HandleAsync_RefundCompleted_RecordsAmountAndTimeOnTheBooking`
- [x] AC-8: Payments held by outcome — `HandleAsync_SucceededAndFailedPayments_AreHeldByOutcome`
- [x] AC-2: Repeat changes nothing — `HandleAsync_RefundTwice_KeepsTheFirst`
- [x] Nil refund records nothing — `HandleAsync_RefundOfNothing_RecordsNothing`
- [x] AC-10: Unusable data is rejected — `HandleAsync_SucceededWithoutAmount_IsRejected`

## Invariants touched

- Payments never change `bookings.status` or `bookings.amount`: revenue comes from confirmations only.
- A refund arriving before any booking event starts the booking as cancelled; its `status_changed_at` is the refund time, so older booking events fill facts without moving status.

## Drift

- ReportedPayment.amount is required in the data model but PaymentFailed carries no amount; V0005 makes it nullable.
