#!/usr/bin/env bash
set -euo pipefail

endpoint="$(terraform -chdir=infra output -raw api_endpoint)"
zone="$HOSTED_ZONE_ID"
name="$RECORD_NAME"
client=DdnsClient/src/DdnsClient/bin/Debug/net8.0/ddns-client.dll

fail() { echo "e2e: FAILED: $*" >&2; exit 1; }
report() { curl -fsS -X POST "$endpoint/hostname" -H 'Content-Type: application/json' -d "{\"hostname\":\"$name\",\"ip\":\"$1\"}"; }
record() {
  aws route53 list-resource-record-sets --hosted-zone-id "$zone" \
    --query "ResourceRecordSets[?Name=='$name.' && Type=='A'].ResourceRecords[0].Value" --output text
}

cleanup() {
  ip="$(record)"
  [ -n "$ip" ] || return 0
  aws route53 change-resource-record-sets --hosted-zone-id "$zone" --change-batch \
    "{\"Changes\":[{\"Action\":\"DELETE\",\"ResourceRecordSet\":{\"Name\":\"$name\",\"Type\":\"A\",\"TTL\":60,\"ResourceRecords\":[{\"Value\":\"$ip\"}]}}]}" >/dev/null
  echo "e2e: deleted A record $name"
}
trap cleanup EXIT

[ -z "$(record)" ] || fail "A record $name already exists"

body=""
for attempt in 1 2 3 4 5; do
  if body="$(report 203.0.113.10 2>&1)"; then break; fi
  echo "e2e: attempt $attempt failed: $body" >&2
  sleep 3
done
grep -q '"status":"updated"' <<<"$body" || fail "first report did not update: $body"
[ "$(record)" = "203.0.113.10" ] || fail "A record not created: $(record)"
echo "e2e: first report -> $body, A record 203.0.113.10"

body="$(report 203.0.113.10)"
grep -q '"status":"current"' <<<"$body" || fail "repeat report was not current: $body"
echo "e2e: same ip -> $body"

body="$(report 203.0.113.11)"
grep -q '"status":"updated"' <<<"$body" || fail "changed ip did not update: $body"
[ "$(record)" = "203.0.113.11" ] || fail "A record not changed: $(record)"
echo "e2e: new ip -> $body, A record 203.0.113.11"

body="$(curl -fsS "$endpoint/hostname/$name")"
grep -q '"ip":"203.0.113.11"' <<<"$body" || fail "lookup returned: $body"
echo "e2e: lookup -> $body"

out="$(dotnet "$client" "$name" "$endpoint/hostname")"
ip="$(cut -d' ' -f2 <<<"$out")"
[ "$out" = "$name $ip updated" ] || fail "client first run: $out"
[ "$(record)" = "$ip" ] || fail "A record not set to client ip: $(record)"
echo "e2e: client -> $out, A record $ip"

out="$(dotnet "$client" "$name" "$endpoint/hostname")"
[ "$out" = "$name $ip current" ] || fail "client second run: $out"
echo "e2e: client again -> $out"

echo "e2e: ALL CHECKS PASSED"
