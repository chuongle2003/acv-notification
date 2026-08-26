#!/usr/bin/env bash
set -euo pipefail

configuration="${1:-Release}"

dotnet restore TaskTracker.sln -p:EnableWindowsTargeting=true

dotnet test tests/TaskTracker.Domain.Tests/TaskTracker.Domain.Tests.csproj \
  --configuration "$configuration" --no-restore
dotnet test tests/TaskTracker.Application.Tests/TaskTracker.Application.Tests.csproj \
  --configuration "$configuration" --no-restore
dotnet test tests/TaskTracker.Infrastructure.Tests/TaskTracker.Infrastructure.Tests.csproj \
  --configuration "$configuration" --no-restore

dotnet build src/TaskTracker.Windows/TaskTracker.Windows.csproj \
  --configuration "$configuration" --no-restore \
  -p:EnableWindowsTargeting=true -p:AppxGeneratePriEnabled=false
