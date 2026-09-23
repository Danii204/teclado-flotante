# Publica una nueva versión: actualiza VERSION, crea el commit y la etiqueta vX.Y.Z y lo sube a GitHub.
# GitHub Actions compila, prueba y crea la Release; las copias instaladas avisan de la actualización.
#
#   .\release.ps1 1.2.0
#
# Antes: añade en CHANGELOG.md una sección "## [1.2.0] - AAAA-MM-DD" con los cambios.
param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Formato de versión no válido: $Version (usa X.Y.Z)" }
$current = (Get-Content VERSION -Raw).Trim()
if ([version]$Version -le [version]$current -and $Version -ne $current) { throw "La versión $Version no es mayor que la actual ($current)" }
if (git tag --list "v$Version") { throw "La etiqueta v$Version ya existe" }
if (-not (Select-String -Path CHANGELOG.md -Pattern "^## \[?$([regex]::Escape($Version))\]?" -Quiet)) {
    throw "Falta la sección '## [$Version]' en CHANGELOG.md"
}
$branch = git rev-parse --abbrev-ref HEAD
if ($branch -ne 'main') { throw "Publica desde la rama main (estás en $branch)" }

# Compilar y probar en local antes de publicar
& "$PSScriptRoot\build.ps1" -Version $Version
& "$PSScriptRoot\test.ps1" unit
if ($LASTEXITCODE -ne 0) { throw 'Las pruebas han fallado: no se publica' }

Set-Content VERSION $Version -Encoding ascii -NoNewline
git add -A
if (git status --porcelain) { git commit -m "Versión $Version" }
git tag -a "v$Version" -m "Teclado Flotante $Version"
git push origin main --follow-tags
Write-Host "Publicada la etiqueta v$Version. GitHub Actions creará la Release en unos minutos."
