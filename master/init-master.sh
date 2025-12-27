#!/usr/bin/env bash

set -e

echo "Configuring PostgreSQL Master for replication..."

# Custom pg_hba
cat /master-pg_hba.conf >> "$PGDATA/pg_hba.conf"

# Criar usuário de replicação:
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" <<-EOSQL
    CREATE ROLE replicator WITH REPLICATION LOGIN PASSWORD 'replica_pass';
EOSQL

echo "Master configured for replication."
