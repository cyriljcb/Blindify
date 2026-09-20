# Build de l'APK Android + déploiement vers host/ (V2 — remplace la procédure manuelle
# copier-coller documentée jusqu'ici dans docs/architecture.md "Mettre à jour l'app Android sans
# câble"). Lit version/buildNumber directement depuis pubspec.yaml plutôt que de les redemander,
# pour ne plus jamais désynchroniser host/apk_version.json du build réellement copié (retour
# utilisateur : la détection de mise à jour ne se déclenchait pas quand cette étape était oubliée).
#
# Usage : depuis app/, ./scripts/build_release.ps1

$ErrorActionPreference = "Stop"

$appDir = Split-Path -Parent $PSScriptRoot
$hostDir = Join-Path (Split-Path -Parent $appDir) "host"

$pubspecPath = Join-Path $appDir "pubspec.yaml"
$versionLine = Select-String -Path $pubspecPath -Pattern '^version:\s*(\S+)' | Select-Object -First 1
if (-not $versionLine) { throw "Impossible de trouver la ligne 'version:' dans $pubspecPath" }

$versionComplete = $versionLine.Matches[0].Groups[1].Value  # ex. "1.0.3+4"
$parts = $versionComplete -split '\+'
if ($parts.Count -ne 2) { throw "Format de version inattendu (attendu X.Y.Z+N) : $versionComplete" }
$versionName = $parts[0]
$buildNumber = [int]$parts[1]

Write-Host "Build release $versionName (build $buildNumber)..."

Push-Location $appDir
try {
    flutter build apk --release
    if ($LASTEXITCODE -ne 0) { throw "flutter build apk a échoué (code $LASTEXITCODE)" }
} finally {
    Pop-Location
}

$apkSource = Join-Path $appDir "build\app\outputs\flutter-apk\app-release.apk"
if (-not (Test-Path $apkSource)) { throw "APK introuvable après build : $apkSource" }

Copy-Item -Path $apkSource -Destination (Join-Path $hostDir "blindify.apk") -Force

$versionJson = @{ versionName = $versionName; buildNumber = $buildNumber } | ConvertTo-Json -Compress
Set-Content -Path (Join-Path $hostDir "apk_version.json") -Value $versionJson -Encoding utf8NoBOM

Write-Host "OK — host/blindify.apk et host/apk_version.json à jour ($versionName+$buildNumber)."
Write-Host "Rappel : ni blindify.apk ni apk_version.json ne sont commités (état de déploiement, voir .gitignore)."
