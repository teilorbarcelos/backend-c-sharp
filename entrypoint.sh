#!/bin/sh
set -e

if [ "$MIGRATE_ONLY" = "true" ] || [ "$MIGRATE_ONLY" = "1" ]; then
    echo "[entrypoint] MIGRATE_ONLY=true — applying migrations and exiting."
    exec dotnet MageBackend.dll
fi

echo "[entrypoint] Starting MageBackend API..."
exec dotnet MageBackend.dll
