# Build the Inkhound Docker image and publish it to Docker Hub.
# Tags pushed: geekyreaper/inkhound:<yyyy.MM.dd> and geekyreaper/inkhound:latest
# Requires: `docker login` already done for an account with push rights on geekyreaper/inkhound.

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$image    = 'geekyreaper/inkhound'
$tag      = Get-Date -Format 'yyyy.MM.dd'

Push-Location $repoRoot
try {
    Write-Host "==> Building $image`:$tag (also tagged latest)"
    docker build -t "$image`:$tag" -t "$image`:latest" .
    if ($LASTEXITCODE -ne 0) { throw "docker build failed with exit code $LASTEXITCODE" }

    Write-Host "==> Pushing $image`:$tag"
    docker push "$image`:$tag"
    if ($LASTEXITCODE -ne 0) { throw "docker push failed for tag $tag (exit code $LASTEXITCODE)" }

    Write-Host "==> Pushing $image`:latest"
    docker push "$image`:latest"
    if ($LASTEXITCODE -ne 0) { throw "docker push failed for tag latest (exit code $LASTEXITCODE)" }

    Write-Host "==> Done. Published $image`:$tag and $image`:latest"
}
finally {
    Pop-Location
}
