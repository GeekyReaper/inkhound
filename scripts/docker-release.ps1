# Build the Inkhound Docker image and publish it to Docker Hub.
# Tags pushed: geekyreaper/inkhound:<yyyy.MM.dd>[a-z] and geekyreaper/inkhound:latest
# If a tag for today's date already exists on Docker Hub, a letter suffix is appended
# (2026.07.24 -> 2026.07.24a -> 2026.07.24b -> ...) so a second release the same day
# doesn't overwrite the first one.
# Requires: `docker login` already done for an account with push rights on geekyreaper/inkhound.

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$image    = 'geekyreaper/inkhound'
$baseTag  = Get-Date -Format 'yyyy.MM.dd'

# Retourne $true si le tag existe déjà sur le registre (utilise les identifiants Docker CLI
# déjà en place via `docker login`, donc fonctionne aussi bien pour un repo privé que public).
# "no such manifest" sur stderr est le résultat NORMAL pour un tag absent (ce n'est pas une
# erreur de script) : $ErrorActionPreference est mis à SilentlyContinue localement (scope de la
# fonction uniquement) pour empêcher ce stderr redirigé d'être promu en erreur terminante par le
# $ErrorActionPreference = 'Stop' global — sans ça, le script s'arrête dès le premier tag absent.
function Test-DockerTagExists {
    param([Parameter(Mandatory)][string]$ImageRef)
    $ErrorActionPreference = 'SilentlyContinue'
    docker manifest inspect $ImageRef *> $null
    return $LASTEXITCODE -eq 0
}

$tag = $baseTag
if (Test-DockerTagExists "$image`:$tag") {
    $suffixIndex = 0
    do {
        $tag = "$baseTag$([char](97 + $suffixIndex))"
        $suffixIndex++
    } while (Test-DockerTagExists "$image`:$tag")
    Write-Host "==> Tag $baseTag already exists, using $tag instead"
}

Push-Location $repoRoot
try {
    Write-Host "==> Building $image`:$tag (also tagged latest)"
    docker build --build-arg "APP_VERSION=$tag" -t "$image`:$tag" -t "$image`:latest" .
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
