#!/bin/bash

MIN_DELAY=""
MAX_DELAY=""
QUORUM=""
DETACHED=false
NAIVE=false
REBUILD=false

while [[ $# -gt 0 ]]; do
    case $1 in
        -m|--min-delay)
            MIN_DELAY="$2"
            shift 2
            ;;
        -M|--max-delay)
            MAX_DELAY="$2"
            shift 2
            ;;
        -q|--quorum)
            QUORUM="$2"
            shift 2
            ;;
        -d|--detached)
            DETACHED=true
            shift
            ;;
        -n|--naive)
            NAIVE=true
            shift
            ;;
        -h|--help)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  -m, --min-delay <ms>    Set minimum delay in milliseconds"
            echo "  -M, --max-delay <ms>    Set maximum delay in milliseconds"
            echo "  -q, --quorum <n>        Set write quorum value"
            echo "  -d, --detached          Run containers in detached mode"
            echo "  -n, --naive             Disable versioning (naive mode)"
            echo "  -h, --help              Show this help message"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            echo "Use -h or --help for usage information"
            exit 1
            ;;
    esac
done

echo "=== Stopping existing containers ==="
docker-compose down

echo "=== Building containers ==="
docker-compose build

if [[ -n "$MIN_DELAY" ]]; then
    export MIN_DELAY_MS="$MIN_DELAY"
    echo "Override: MIN_DELAY_MS=$MIN_DELAY"
fi
if [[ -n "$MAX_DELAY" ]]; then
    export MAX_DELAY_MS="$MAX_DELAY"
    echo "Override: MAX_DELAY_MS=$MAX_DELAY"
fi
if [[ -n "$QUORUM" ]]; then
    export WRITE_QUORUM="$QUORUM"
    echo "Override: WRITE_QUORUM=$QUORUM"
fi
if [[ "$NAIVE" == true ]]; then
    export USE_VERSIONING="false"
    echo "Override: USE_VERSIONING=false (naive mode)"
fi

echo "=== Starting containers ==="
if [[ "$DETACHED" == true ]]; then
    docker-compose up -d
    echo "Containers started in detached mode"
else
    docker-compose up
fi
