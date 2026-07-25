#!/usr/bin/env bash
# Restore the most recent AnkiLearner DB dump from restic into the running Postgres container.
# Run as the `anki` user.
#
# ORDER MATTERS: bring up only `postgres` and `db-setup`, restore, then start `api`. The API runs
# EF migrations at startup, which would race a `pg_restore --clean`.
#
#   docker compose --env-file app.env up -d postgres db-setup
#   ./restore-db-latest.sh
#   docker compose --env-file app.env up -d

set -euo pipefail

DEPLOY_DIR="/srv/ankilearner/deploy"
BACKUP_DIR="/srv/ankilearner/backup"

export XDG_RUNTIME_DIR="/run/user/$(id -u)"
export DOCKER_HOST="unix://${XDG_RUNTIME_DIR}/docker.sock"

set -a
# shellcheck disable=SC1091
source "${BACKUP_DIR}/backup.env"
set +a

RESTIC_REPO="s3:${OVH_S3_ENDPOINT}/${OVH_S3_BUCKET}/restic"
export RESTIC_CACHE_DIR="${BACKUP_DIR}/restic-cache"

cd "${DEPLOY_DIR}"

echo "Listing DB dump files in the latest db snapshot..."
restic -r "${RESTIC_REPO}" ls latest --tag db

read -r -p "Restore this dump over database 'ankilearner'? [y/N] " confirm
[[ "${confirm}" == "y" || "${confirm}" == "Y" ]] || { echo "Aborted."; exit 1; }

echo "Restoring..."
restic -r "${RESTIC_REPO}" dump latest --tag db "db/ankilearner_*.dump" \
  | docker compose --env-file "${DEPLOY_DIR}/app.env" exec -T postgres \
      pg_restore -U postgres -d ankilearner --clean --if-exists

echo "Done. Now start the API:  docker compose --env-file app.env up -d"
