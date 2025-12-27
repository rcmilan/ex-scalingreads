#!/usr/bin/env bash
set -e

# Use 0.0.0.0/0 to allow any container within the docker network to attempt auth
echo "host replication replicator 0.0.0.0/0 scram-sha-256" >> "$PGDATA/pg_hba.conf"
echo "host all all 0.0.0.0/0 scram-sha-256" >> "$PGDATA/pg_hba.conf"