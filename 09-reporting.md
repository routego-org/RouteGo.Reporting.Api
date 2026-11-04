# 09. Reporting Service

| | |
|---|---|
| **Type** | API + worker |
| **Build phase** | 9 |
| **Port** | 5008 |
| **Data** | PostgreSQL `routego_reporting` (own read model); S3 bucket `routego-reports-<account>` |
| **Instance** | `routego-core` |
| **Repo path** | `src/Reporting` |

---

## 1. BRD: why it exists
- Operators need dashboards of bookings, revenue and occupancy; Finance needs a daily settlement report (FR-H1, H3, BO-5).
- If dashboards query the live booking database, a manager refreshing a report slows down customers.

**Business outcome:** trustworthy reporting that cannot hurt the booking path.

## 2. PRD: what it offers
| Capability | Behavior |
|---|---|
| Operator dashboard | Bookings, revenue, refunds, occupancy per trip for a date range |
| Trip occupancy | Seats sold over total seats |
| Daily settlement | Per operator: gross, refunds, net for the previous day, as a CSV in S3 and a table in the UI |

Rules:
- Operators see only their own data; Finance and Admin see all.
- Dashboards are near-real-time (seconds behind), settlement is final for a day after 01:00 IST the next day.
- Settlement is idempotent: running it again for the same date gives the same result.

## 3. FRD: how it behaves

### 3.1 Endpoints
| Method and path | Auth | Purpose |
|---|---|---|
| `GET /reports/operator/summary?from=&to=` | Operator (own) | Totals and daily series |
| `GET /reports/operator/trips?date=` | Operator | Occupancy per trip |
| `GET /reports/admin/summary?from=&to=` | Admin | System totals |
| `GET /reports/settlements?date=` | Admin or Finance | List per operator |
| `GET /reports/settlements/{date}/download` | Admin or Finance | Presigned URL to the CSV |
| `GET /health`, `/health/ready` | none | Probes |

### 3.2 Data model
```
trip_dim(trip_id pk, operator_id, source, destination, departure_utc, seats_total)
booking_facts(booking_id pk, trip_id, operator_id, status, amount, seats, confirmed_at, cancelled_at, refund_amount)
payment_facts(payment_id pk, booking_id, amount, status, at)
settlements(settlement_date, operator_id, gross, refunds, net, csv_s3_key, generated_at, pk(settlement_date, operator_id))
processed_messages(message_id pk, processed_at)
```
Facts are updated by events, with upserts keyed on natural ids, so duplicates and out-of-order delivery are safe (later state wins by timestamp).

### 3.3 Events consumed (`reporting-q`)
`TripScheduled`; `BookingConfirmed`, `BookingFailed`, `BookingExpired`, `BookingCancelled`; `PaymentSucceeded`, `PaymentFailed`, `RefundCompleted`.
Also `reporting-schedule-q` for the nightly job. Publishes nothing.

### 3.4 Nightly settlement
- **EventBridge Scheduler** recurring schedule `reporting-nightly-settlement`: `cron(0 1 * * ? *)` with timezone `Asia/Kolkata`, target `reporting-schedule-q`, message `{"job":"settlement","date":"<yesterday>"}` (use a templated or computed date inside the worker if the schedule payload is static: the worker computes "yesterday in IST").
- The worker aggregates facts for that IST day per operator, writes rows to `settlements` (upsert), writes a CSV to `reports/settlement/<date>/<operator>.csv` in S3 (SSE-KMS).
- S3 **lifecycle rule**: move to Standard-IA after 30 days, Glacier-class after 180, expire after 7 years (adjust to finance policy).

### 3.5 Config
| Where | Key |
|---|---|
| SSM `/routego/prod/reporting/` | `reports-bucket`, `settlement-timezone` (Asia/Kolkata), `presign-minutes` (5) |
| Secrets | `routego/reporting/db` |

## 4. Dependencies
**Depends on (sync):** none.
**Consumes:** Booking, Payment, Fleet events; nightly schedule.

**Depended on by:** Web Portal (operator dashboard, admin and finance screens).

## 5. AWS services used and why
| AWS service | Use | Without it |
|---|---|---|
| **SQS (+ DLQ)** | Receive events at its own pace | Reporting outage would block booking or lose data |
| **SNS filter policies** | Receive only relevant events | Wasted processing |
| **EventBridge Scheduler** | Nightly job | A cron entry on one instance that dies with the instance, or a scan loop |
| **S3 + lifecycle** | Keep reports cheaply for years | Disk fills; manual cleanup |
| **RDS** | Reporting database | n/a |
| **IAM** | Read and write only the reports bucket | n/a |
| **CloudWatch** | Alarm if no settlement row is written by 02:00 | A silent missed report |

**What students learn:** separating OLTP from reporting, event-built read models, scheduled recurring jobs, S3 lifecycle policies, and idempotent batch jobs.

## 6. Before building: AWS setup
1. DB `routego_reporting`, user, secret.
2. S3 bucket `routego-reports-<account>`: block public access, SSE-KMS, versioning, lifecycle rule above.
3. Queues `reporting-q` and `reporting-schedule-q` (+ DLQs); `reporting-q` subscribed to booking, payment (all listed types) and fleet (`TripScheduled`).
4. Scheduler: `routego-scheduler-role` allowed to send to `reporting-schedule-q`; create the recurring schedule.
5. SSM parameters; ECR repo `routego/reporting`; log group.
6. Core role: S3 on the reports bucket; SQS on the two queues.
7. Alarms: DLQs; custom metric `SettlementRowsWritten` equals 0 after 02:00 IST.

## 7. Local development
1. `docker compose up -d`; run Booking, Payment flow once so that facts exist (or run the seed script that replays sample events).
2. Migrate and run: `dotnet run --project src/Reporting -- --migrate-only`, then `dotnet run --project src/Reporting`.
3. Query the dashboard endpoints with an operator token.
4. **Scheduler fallback:** if EventBridge Scheduler is not available locally, run `curl -X POST http://localhost:5008/internal/run-settlement?date=2026-10-01` (internal key) or send the message to `reporting-schedule-q` by hand. The worker code path is identical.
5. Inspect the CSV in LocalStack S3.

## 8. Deploying to AWS
1. Prerequisites above.
2. DB, user, secret. Build and push `routego/reporting`.
3. SSM parameters; migrate.
4. Compose block on core (`5008:8080`, log group `/routego/reporting`).
5. Nginx: not needed (Portal uses the private address).
6. **Backfill:** if Reporting is deployed after bookings exist, re-publish events (Booking and Payment each have an `internal/republish` admin tool) or accept starting fresh for the class.
7. Verify:
   - Dashboards match the numbers in the Booking database.
   - Trigger the schedule manually from the Scheduler console ("run now" by creating a one-time schedule) and see the CSV in S3.
8. **Break it on purpose:**
   - Run a heavy dashboard query repeatedly during a booking load test: booking latency does not change (own database).
   - Stop Reporting for an hour of bookings, restart: it catches up.
   - Run the settlement twice for one date: the same numbers (idempotent).
   - Show the lifecycle rule and storage class transitions in the S3 console.

## 9. Lab checklist
- [ ] Dashboard load does not slow booking.
- [ ] Settlement rerun gives identical results.
- [ ] Operator A cannot read operator B's numbers.
