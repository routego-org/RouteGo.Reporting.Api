# Migrations

Raw, versioned SQL for `routego_reporting`, applied by the pipeline's DbUp step — never by the application.

| What | How |
|---|---|
| Order | `V####__*.sql` by sequence, then `R__*.sql` (run-always routing: every run, after every V####, not journaled) |
| Journal | `public.schema_versions` |
| Transactions | each script wraps itself in `BEGIN; ... COMMIT;`; the runner adds no wrapping transaction |
| Variables | disabled |
| Empty folder | fails loudly |
| Run by hand | `deploy/ec2/apply-migrations.sh` (the same rules, for the EC2 deployment); locally `docker compose run --rm migrate` |

Schemas: `reporting` — `trips`, `bookings` and `payments` (the read model built from events) and `settlements`
(one final row per operator per business day). None of them is audited: the read model is rebuilt from events and
a settlement is written once by the nightly run, and each script records why. Tables carry the entity file they
implement in their `COMMENT`.

Money is whole Indian rupees (`bigint`), as the events carry it.
