#!/bin/sh
set -eux

DATA_DIR="/var/lib/postgresql/data"

# Ensure the directory exists before chown
mkdir -p "$DATA_DIR"
# Optional: Ensure the script doesn't run if the mount failed
if [ ! -d "$DATA_DIR" ]; then
  echo "Error: DATA_DIR $DATA_DIR does not exist"
  exit 1
fi

# 1. Fix permissions (Must be root)
chown -R postgres:postgres "$DATA_DIR"
chmod 700 "$DATA_DIR"

echo "=== [replica] waiting for master ==="
until PGPASSWORD=$MASTER_PASSWORD pg_isready -h pg-master -p 5432 -U admin -d appdb; do
  sleep 2
done

if [ ! -s "$DATA_DIR/PG_VERSION" ]; then
  echo "=== [replica] running pg_basebackup ==="
  rm -rf ${DATA_DIR:?}/*
  su postgres -c "pg_basebackup -h pg-master -U replicator -D $DATA_DIR -Fp -Xs -P -R"
fi

echo "=== [replica] starting postgres ==="
exec su postgres -c "postgres -D $DATA_DIR -c hot_standby=on -c listen_addresses='*'"