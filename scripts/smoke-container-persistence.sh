#!/usr/bin/env bash
# Isolated, disposable Linux CI deployment. Never use this script against an existing deployment.
set -euo pipefail
export COMPOSE_PROJECT_NAME="drmc-security-smoke-${GITHUB_RUN_ID:-$$}"
export DRMC_SQL_PORT=14330
export MSSQL_SA_PASSWORD="${SA_PASSWORD:?Supply a throwaway SQL password}"
temporary="$(mktemp -d)"
export DRMC_PORTAL_ENV_FILE="$temporary/portal.env"
export DRMC_KEYRING_CERTIFICATE_FILE="$temporary/keyring.pfx"
export DRMC_KEYRING_PASSWORD_FILE="$temporary/keyring.password"
cleanup() {
  docker compose --profile portal down --volumes --remove-orphans
  rm -rf -- "$temporary"
}
trap cleanup EXIT
# SQL-only startup must work while the optional application secrets/env file are absent.
docker compose up -d --wait sqlserver
openssl rand -hex 32 > "$DRMC_KEYRING_PASSWORD_FILE"
openssl req -x509 -newkey rsa:2048 -nodes -days 2 -subj /CN=DRMC-CI \
  -keyout "$temporary/certificate.key" -out "$temporary/certificate.crt" >/dev/null 2>&1
openssl pkcs12 -export -inkey "$temporary/certificate.key" -in "$temporary/certificate.crt" \
  -out "$DRMC_KEYRING_CERTIFICATE_FILE" -passout "file:$DRMC_KEYRING_PASSWORD_FILE"
# Local Compose secrets retain host file ownership. Only the runtime UID can read these files.
sudo chown 1654:1654 "$DRMC_KEYRING_CERTIFICATE_FILE" "$DRMC_KEYRING_PASSWORD_FILE"
sudo chmod 0400 "$DRMC_KEYRING_CERTIFICATE_FILE" "$DRMC_KEYRING_PASSWORD_FILE"
cat > "$DRMC_PORTAL_ENV_FILE" <<EOF
ConnectionStrings__DefaultConnection=Server=sqlserver;Database=DrmcContainerSmoke;User Id=sa;Password=$MSSQL_SA_PASSWORD;Encrypt=True;TrustServerCertificate=True
Portal__PublicOrigin=https://localhost
AllowedHosts=localhost
Notifications__Email__Host=smtp.invalid
Notifications__Email__FromAddress=portal@example.org
Notifications__Sms__Endpoint=https://sms.invalid/send
Notifications__Sms__ApiKey=synthetic-ci-key
AccountEmail__WorkerEnabled=false
Notifications__Outbox__WorkerEnabled=false
RedStar__WorkerEnabled=false
WebsiteUsage__Enabled=false
EOF
docker compose --profile portal config --quiet
docker compose --profile portal build portal
docker compose --profile portal run --rm portal redstar-protection upgrade
docker compose --profile portal up -d --scale portal=2 portal
wait_for_portal() {
  for index in 1 2; do
    address="$(docker compose port --index "$index" portal 8080)"
    ready=false
    for attempt in $(seq 1 60); do
      if curl --silent --fail --header 'Host: localhost' --output /dev/null --max-time 6 "http://$address/health/ready"; then ready=true; break; fi
      sleep 1
    done
    if [ "$ready" != true ]; then
      docker compose logs --tail 50 portal
      return 1
    fi
  done
}
wait_for_portal
docker compose exec -T --index 1 portal sh -ec '
  test "$(id -u)" = 1654
  test -w /var/lib/drmc/keys
  test -w /var/lib/drmc/storage/patient-documents
  test -w /var/lib/drmc/storage/temporary-uploads
  test -w /var/lib/drmc/storage/lab-reports
  test -w /var/lib/drmc/storage/imports
  grep -q encryptedSecret /var/lib/drmc/keys/key-*.xml
  printf synthetic-persistence-fixture > /var/lib/drmc/storage/smoke-marker
  sha256sum /var/lib/drmc/keys/key-*.xml > /var/lib/drmc/storage/key-checksums
'
docker compose exec -T --index 2 portal sh -ec 'test "$(cat /var/lib/drmc/storage/smoke-marker)" = synthetic-persistence-fixture'
docker compose --profile portal up -d --force-recreate --scale portal=2 portal
wait_for_portal
for index in 1 2; do
  docker compose exec -T --index "$index" portal sh -ec '
    test "$(id -u)" = 1654
    test "$(cat /var/lib/drmc/storage/smoke-marker)" = synthetic-persistence-fixture
    sha256sum --check --status /var/lib/drmc/storage/key-checksums
  '
done
