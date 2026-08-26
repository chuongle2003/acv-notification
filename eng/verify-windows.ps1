param(
    [string]$Configuration = "Release",
    [string]$Version = "0.1.0",
    [switch]$BuildInstaller
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $repositoryRoot "artifacts\publish\win-x64"

function Assert-NativeSuccess([string]$step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$step failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repositoryRoot
try {
    dotnet restore TaskTracker.sln
    Assert-NativeSuccess "Restore"

    dotnet build TaskTracker.sln --configuration $Configuration --no-restore
    Assert-NativeSuccess "Build"

    dotnet test TaskTracker.sln --configuration $Configuration --no-build
    Assert-NativeSuccess "Tests"

    dotnet publish src/TaskTracker.Windows/TaskTracker.Windows.csproj `
        --configuration $Configuration `
        -p:PublishProfile=win-x64 `
        "-p:PublishDir=$publishDirectory\"
    Assert-NativeSuccess "Publish"

    if ($BuildInstaller) {
        $isccCandidates = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
        )
        $iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $iscc) {
            throw "Inno Setup 6 was not found. Install it or omit -BuildInstaller."
        }

        & $iscc "/DPublishDir=$publishDirectory" "/DAppVersion=$Version" `
            "installer\TaskTracker.iss"
        Assert-NativeSuccess "Installer"
    }

    Write-Host "Windows verification passed. Publish: $publishDirectory"
}
finally {
    Pop-Location
}
