#!/usr/bin/env bash
# Places an order on a brand-new broker before the fulfilment worker has ever started, checks that the event waits
# in the order-placed queue, then starts the worker and checks that the order becomes Fulfilled.
#
# Run from the repo root with the stack up except the worker:
#   docker compose up -d --build sqlserver ravendb rabbitmq api web
#   bash e2e/cold-start-check.sh
# CI runs exactly this before the Playwright tests.
set -euo pipefail

API=${API:-http://localhost:5080}
MQ=${MQ:-http://localhost:15672}
QUEUE_URL="$MQ/api/queues/%2F/order-placed"

say() { printf '\n== %s\n' "$*"; }
fail() {
  printf 'FAIL: %s\n' "$*" >&2
  echo "--- queues"; curl -s -u guest:guest "$MQ/api/queues" | python3 -m json.tool | grep -E '"name"|"messages"|"consumers"' || true
  echo "--- bindings"; curl -s -u guest:guest "$MQ/api/bindings" | python3 -m json.tool | grep -E '"source"|"destination"' || true
  exit 1
}

say "The fulfilment worker must not be running"
if docker compose ps --status running --services | grep -qx fulfilment; then
  fail "fulfilment is running; start the stack without it"
fi
echo "ok, not running"

say "Waiting for the API health check (SQL Server, RavenDB, RabbitMQ)"
for i in $(seq 1 60); do
  if curl -fsS "$API/health" >/dev/null 2>&1; then break; fi
  [ "$i" = 60 ] && fail "API never became healthy"
  sleep 3
done
curl -fsS "$API/health"; echo

say "The API declared the worker's queue at startup; nothing consumes it yet"
queue=$(curl -fsS -u guest:guest "$QUEUE_URL") || fail "the order-placed queue does not exist"
consumers=$(printf '%s' "$queue" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("consumers", 0))')
echo "order-placed exists, consumers=$consumers"
[ "$consumers" = 0 ] || fail "expected no consumers before the worker starts"
curl -fsS -u guest:guest "$MQ/api/exchanges/%2F/Shelf.Contracts%3AOrderPlaced/bindings/source" \
  | python3 -c 'import json,sys; [print("binding:", b["source"], "->", b["destination_type"], b["destination"]) for b in json.load(sys.stdin)]'

say "Placing an order"
cart=$(python3 -c 'import uuid; print(uuid.uuid4())')
curl -fsS -X POST "$API/api/cart/items" -H "X-Cart-Id: $cart" -H 'Content-Type: application/json' \
  -d '{"bookId":"dracula","quantity":1}' >/dev/null
checkout=$(curl -fsS -X POST "$API/api/checkout" -H "X-Cart-Id: $cart" -H 'Content-Type: application/json' \
  -d '{"email":"cold-start@example.com"}')
echo "$checkout"
order=$(printf '%s' "$checkout" | python3 -c 'import json,sys; print(json.load(sys.stdin)["orderId"])')

say "The OrderPlaced event is waiting in the order-placed queue"
for i in $(seq 1 30); do
  ready=$(curl -s -u guest:guest "$QUEUE_URL" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("messages_ready", 0))' 2>/dev/null || echo 0)
  if [ "${ready:-0}" -ge 1 ]; then break; fi
  [ "$i" = 30 ] && fail "no message reached the order-placed queue"
  sleep 2
done
echo "messages_ready=$ready"
status=$(curl -fsS "$API/api/orders/$order" | python3 -c 'import json,sys; print(json.load(sys.stdin)["status"])')
echo "order $order is $status"
[ "$status" = Placed ] || fail "expected Placed while the worker is down"

say "Starting the fulfilment worker for the first time"
docker compose up -d fulfilment

say "Waiting for the order to become Fulfilled"
for i in $(seq 1 45); do
  status=$(curl -fsS "$API/api/orders/$order" | python3 -c 'import json,sys; print(json.load(sys.stdin)["status"])')
  if [ "$status" = Fulfilled ]; then break; fi
  [ "$i" = 45 ] && { docker compose logs fulfilment; fail "order stayed $status"; }
  sleep 2
done
echo "order $order is $status"
echo "PASS: an order placed before the worker ever ran was kept and fulfilled"
