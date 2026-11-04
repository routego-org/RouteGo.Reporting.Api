#!/bin/bash
# LOCAL ONLY. Runs once LocalStack is ready: creates what Reporting needs from the platform
# (00-overall-architecture.md section 12.4, 09-reporting.md section 6). Idempotent: Booking's, Payment's
# and Fleet's own local scripts create some of the same topics and queues.
set -euo pipefail
export AWS_DEFAULT_REGION=ap-south-1
ACCOUNT=000000000000
QUEUES=http://localhost:4566/$ACCOUNT

# Topics Reporting listens to (shared with the other services; creating one that exists is a no-op).
for topic in booking payment fleet; do
  awslocal sns create-topic --name "routego-${topic}-events" >/dev/null
done

# A queue with its dead-letter queue: five receives, then parked.
make_queue() { # name dlq-name visibility-seconds
  awslocal sqs create-queue --queue-name "$2" >/dev/null
  dlq_arn=$(awslocal sqs get-queue-attributes --queue-url "$QUEUES/$2" --attribute-names QueueArn --query Attributes.QueueArn --output text)
  attributes=$(python3 -c 'import json,sys; print(json.dumps({"VisibilityTimeout": sys.argv[1], "ReceiveMessageWaitTimeSeconds": "20", "RedrivePolicy": json.dumps({"deadLetterTargetArn": sys.argv[2], "maxReceiveCount": "5"})}))' "$3" "$dlq_arn")
  awslocal sqs create-queue --queue-name "$1" --attributes "$attributes" >/dev/null
}

subscribe() { # queue topic filter-json
  attributes=$(python3 -c 'import json,sys; print(json.dumps({"RawMessageDelivery": "true", "FilterPolicy": json.dumps({"eventType": json.loads(sys.argv[1])})}))' "$3")
  awslocal sns subscribe --topic-arn "arn:aws:sns:ap-south-1:$ACCOUNT:routego-$2-events" \
    --protocol sqs --notification-endpoint "arn:aws:sqs:ap-south-1:$ACCOUNT:$1" \
    --attributes "$attributes" >/dev/null
}

# What Reporting reads. A settlement run can take a while, so its queue's visibility is longer.
make_queue reporting-q reporting-dlq 60
subscribe reporting-q booking '["BookingConfirmed","BookingFailed","BookingExpired","BookingCancelled"]'
subscribe reporting-q payment '["PaymentSucceeded","PaymentFailed","RefundCompleted"]'
subscribe reporting-q fleet '["TripScheduled"]'
make_queue reporting-schedule-q reporting-schedule-dlq 300

# Settlement reports: private, encrypted, kept seven years (2555 days) as the spec proposes.
# Created last: the compose health check waits for it.
awslocal s3api create-bucket --bucket routego-reports \
  --create-bucket-configuration LocationConstraint=ap-south-1 >/dev/null 2>&1 || true
awslocal s3api put-bucket-lifecycle-configuration --bucket routego-reports --lifecycle-configuration \
  '{"Rules":[{"ID":"expire-reports","Status":"Enabled","Filter":{"Prefix":"settlement/"},"Expiration":{"Days":2555}}]}'

echo "Reporting queues, subscriptions and bucket are ready."
