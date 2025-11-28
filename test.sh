#!/bin/bash

./run.sh -d "$@"

echo "=== Waiting for services to be ready ==="
MAX_RETRIES=30
RETRY_COUNT=0

while [[ $RETRY_COUNT -lt $MAX_RETRIES ]]; do
    if curl -s http://localhost:8080/health > /dev/null 2>&1; then
        echo "Leader is ready!"
        break
    fi
    echo "Waiting for leader to be ready... ($((RETRY_COUNT + 1))/$MAX_RETRIES)"
    sleep 2
    RETRY_COUNT=$((RETRY_COUNT + 1))
done

if [[ $RETRY_COUNT -eq $MAX_RETRIES ]]; then
    echo "Error: Services did not become ready in time"
    docker-compose logs
    docker-compose down
    exit 1
fi

sleep 3

echo "=== Running integration tests ==="
dotnet test LeadersAndFollowers.IntegrationTests/LeadersAndFollowers.IntegrationTests.csproj --logger "console;verbosity=detailed"
TEST_EXIT_CODE=$?

echo "=== Stopping containers ==="
docker-compose down

exit $TEST_EXIT_CODE
