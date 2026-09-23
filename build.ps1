# Compila Teclado Flotante con el compilador de C# de .NET Framework 4.8 (incluido en Windows 10/11, sin SDK).
# Resultado: dist\TecladoFlotante-Setup.exe (instalador) y dist\TecladoFlotante.exe (portable).
#
#   .\build.ps1                                   version del archivo VERSION, repo deducido de 'git remote'
#   .\build.ps1 -Version 1.2.0 -Repo usuario/repo  (lo usa GitHub Actions al publicar)
param(
    [string]$Version,
    [string]$Repo
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "No se encuentra el compilador de .NET Framework 4.x: $csc" }
$obj = Join-Path $root 'obj'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $obj, $dist | Out-Null

if (-not $Version) { $Version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim() }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version no valida: '$Version' (formato X.Y.Z)" }
# Canal: 'beta' (se publica como Pre-release y busca actualizaciones tambien entre betas) o 'stable'
$channelFile = Join-Path $root 'CHANNEL'
$Channel = if (Test-Path $channelFile) { (Get-Content $channelFile -Raw).Trim() } else { 'stable' }
if ($Channel -notin @('beta', 'stable')) { throw "CHANNEL no valido: '$Channel' (beta o stable)" }

if (-not $Repo) {
    $ErrorActionPreference = 'Continue'   # en PowerShell 5.1 el stderr de git se convertiria en error
    $url = git -C $root remote get-url origin 2>$null
    $ErrorActionPreference = 'Stop'
    if ($url -match 'github\.com[:/]([^/]+/[^/]+?)(\.git)?$') { $Repo = $Matches[1] } else { $Repo = '' }
}
$Author = if ($Repo) { $Repo.Split('/')[0] } else { '' }
Write-Host "Version $Version ($Channel)  Repo '$Repo'"

# Informacion de compilacion (version, repositorio para las actualizaciones y autor = usuario de GitHub)
$buildInfo = @"
using System.Reflection;
[assembly: AssemblyTitle("Teclado Flotante")]
[assembly: AssemblyDescription("Teclado en pantalla flotante para Windows")]
[assembly: AssemblyProduct("Teclado Flotante")]
[assembly: AssemblyCompany("$Author")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion("$Version.0")]
[assembly: AssemblyFileVersion("$Version.0")]
[assembly: AssemblyInformationalVersion("$Version")]
namespace TecladoFlotante
{
    public static class BuildInfo
    {
        public const string Version = "$Version";
        public const string Repo = "$Repo";
        public const string Author = "$Author";
        public const string Channel = "$Channel";
        public static bool IsBeta { get { return Channel == "beta"; } }
        public static string DisplayVersion { get { return IsBeta ? Version + " beta" : Version; } }
        public static string ProjectUrl { get { return Repo.Length == 0 ? "" : "https://github.com/" + Repo; } }
    }
}
"@
$buildInfoPath = Join-Path $obj 'BuildInfo.cs'
[IO.File]::WriteAllText($buildInfoPath, $buildInfo, (New-Object Text.UTF8Encoding $false))

$sources = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object FullName) + $buildInfoPath
$common = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001', '/warnaserror+',
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll',
    "/lib:$(Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF')", '/r:UIAutomationClient.dll', '/r:UIAutomationTypes.dll', '/r:WindowsBase.dll',
    "/win32manifest:$(Join-Path $root 'app.manifest')")

function Invoke-Csc([string[]]$csArgs) {
    & $csc @csArgs
    if ($LASTEXITCODE -ne 0) { throw "csc fallo ($LASTEXITCODE)" }
}

# 1) Compilacion previa para generar el icono con el mismo codigo de dibujo que usa la app
$stage = Join-Path $obj 'stage.exe'
Invoke-Csc ($common + "/out:$stage" + $sources)
$ico = Join-Path $obj 'app.ico'
& $stage --write-icon $ico | Out-Null
if (-not (Test-Path $ico)) { throw 'No se genero el icono' }

# 2) Compilacion final con icono
$exe = Join-Path $dist 'TecladoFlotante.exe'
Invoke-Csc ($common + "/win32icon:$ico" + "/out:$exe" + $sources)
Copy-Item $exe (Join-Path $dist 'TecladoFlotante-Setup.exe') -Force
Write-Host "OK -> $dist"
