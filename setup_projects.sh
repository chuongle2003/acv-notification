#!/usr/bin/env bash
set -euo pipefail

# Backward-compatible entry point. The old version overwrote the repository
# scaffold; this version only runs the safe, reproducible Linux verification.
export LOCAL_UID="${LOCAL_UID:-$(id -u)}"
export LOCAL_GID="${LOCAL_GID:-$(id -g)}"
if (($#)); then
  docker --context "${DOCKER_CONTEXT:-default}" compose run --rm dotnet-sdk "$@"
else
  docker --context "${DOCKER_CONTEXT:-default}" compose run --rm dotnet-sdk
fi
