#!/usr/bin/env bash
set -e

MASTER_HOST=pg-master
REPLICA_USER=replicator
REPLICA_PASSWORD=replica_pass

echo "Waiting for master..."
until pg_isready -h $MASTER_HOST -p 5432 -U $REPLICA_USER; do
  echo "Master not ready yet..."
  sleep 2
done

if [ ! -s "$PGDATA/postgresql.conf" ]; then
  echo "Initializing replica from master..."
  
  rm -rf "$PGDATA"/*
  
  PGPASSWORD=$REPLICA_PASSWORD pg_basebackup \
    -h $MASTER_HOST -D "$PGDATA" \
    -U $REPLICA_USER -v -P -R -X stream

fi

echo "Starting replica..."
exec postgres -c hot_standby=on -c listen_addresses='*'
