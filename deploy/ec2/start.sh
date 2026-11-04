#!/usr/bin/env bash
# Build the image, apply migrations, start the API, then wait for /health/ready.
# Usage on the one-server layout:  sudo bash ~/reporting/deploy/ec2/start.sh
# Usage (three-instance layout, on routego-core):
#   sudo IDENTITY_JWKS_URL=http://<identity-private-ip>:5001/.well-known/jwks.json \
#        IDENTITY_ISSUER=<issuer Identity writes into its tokens> \
#        bash ~/reporting/deploy/ec2/start.sh
set -euo pipefail

cd "$(dirname "$0")"
REGION=ap-south-1
DB_SECRET=database/postgresql/routego-reporting-api

SHARED_NETWORK=ec2_default
POSTGRES_CONTAINER=ec2-postgres-1
POSTGRES_SUPERUSER=notification_user

# One-server layout: Identity is reached by name on the shared ec2_default network. Set a variable to
# override (the three-instance layout uses private addresses).
IDENTITY_JWKS_URL="${IDENTITY_JWKS_URL:-http://identity:8080/.well-known/jwks.json}"
IDENTITY_ISSUER="${IDENTITY_ISSUER:-$(aws ssm get-parameter --name /routego/prod/identity/issuer --region "$REGION" \
  --query Parameter.Value --output text)}"

if ! docker network inspect "$SHARED_NETWORK" >/dev/null 2>&1; then
  echo "ERROR: Docker network $SHARED_NETWORK not found. Is the Postgres container ($POSTGRES_CONTAINER) running?" >&2
  exit 1
fi

# The migrate container needs the connection details; the instance role reads them from the same
# secret the API uses, so the password is never written to a file on this server.
echo ">> Reading the database connection from Secrets Manager"
secret_json="$(aws secretsmanager get-secret-value --secret-id "$DB_SECRET" --region "$REGION" \
  --query SecretString --output text)"
read_field() {
  printf '%s' "$secret_json" | python3 -c 'import json, sys; print(json.load(sys.stdin).get(sys.argv[1], sys.argv[2]))' "$1" "${2:-}"
}
REPORTING_DB_HOST="$(read_field host)"
REPORTING_DB_PORT="$(read_field port 5432)"
REPORTING_DB_NAME="$(read_field database)"
REPORTING_DB_USER="$(read_field username)"
REPORTING_DB_PASSWORD="$(read_field password)"
REPORTING_DB_SSLMODE="$(read_field sslmode require)"
AWS_ACCOUNT_ID="$(aws sts get-caller-identity --query Account --output text)"
export REPORTING_DB_HOST REPORTING_DB_PORT REPORTING_DB_NAME REPORTING_DB_USER REPORTING_DB_PASSWORD REPORTING_DB_SSLMODE
export AWS_ACCOUNT_ID IDENTITY_JWKS_URL IDENTITY_ISSUER

# Without its queues Reporting could not build its figures or run the nightly settlement, and without
# its bucket it could not write a report, so stop here with a clear message instead of a container
# that restarts forever.
echo ">> Checking the queues and the bucket the API needs"
for queue in reporting-q reporting-schedule-q; do
  if ! aws sqs get-queue-url --queue-name "$queue" --region "$REGION" >/dev/null 2>&1; then
    echo "ERROR: queue $queue is missing, or this instance role cannot reach it." >&2
    exit 1
  fi
done
if ! aws s3api head-bucket --bucket "routego-reports-${AWS_ACCOUNT_ID}" --region "$REGION" >/dev/null 2>&1; then
  echo "ERROR: bucket routego-reports-${AWS_ACCOUNT_ID} is missing, or this instance role cannot reach it." >&2
  exit 1
fi

echo ">> Creating the database login and database (skipped when they exist)"
docker exec -i -e DB_NAME="$REPORTING_DB_NAME" -e DB_USER="$REPORTING_DB_USER" -e DB_PASSWORD="$REPORTING_DB_PASSWORD" \
  "$POSTGRES_CONTAINER" psql --no-psqlrc --quiet -U "$POSTGRES_SUPERUSER" -d postgres < create-database.sql

echo ">> Building image (first time takes 5-10 minutes)"
docker build -t routego/reporting:latest ../..

echo ">> Applying migrations and starting the API"
if ! docker compose up -d; then
  echo "ERROR: migrations or API start failed. Last logs:" >&2
  docker compose ps -a
  docker compose logs --tail 50 migrate reporting
  exit 1
fi

echo ">> Waiting for the API to be ready"
for _ in $(seq 1 30); do
  if curl -fs http://localhost:5008/health/ready >/dev/null 2>&1; then
    docker compose ps -a
    echo ">> DEPLOY OK — Reporting API is ready on port 5008"
    exit 0
  fi
  sleep 2
done

echo "ERROR: Reporting API did not become ready. Last logs:" >&2
docker compose ps -a
docker compose logs --tail 50 migrate reporting
exit 1
