# Compila el banco de pruebas contra dist\TecladoFlotante.exe (ejecuta antes build.ps1).
#   .\test.ps1          -> pruebas unitarias + prueba real (mueve el raton y escribe en una ventana de prueba)
#   .\test.ps1 unit     -> solo pruebas sin raton (las que corre GitHub Actions)
#   .\test.ps1 render   -> genera capturas en obj\test\img
# Las pruebas usan una carpeta de ajustes temporal: nunca tocan la configuracion real.
param([string]$Mode = '')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$t = Join-Path $root 'obj\test'
New-Item -ItemType Directory -Force $t, "$t\img" | Out-Null
Copy-Item "$root\dist\TecladoFlotante.exe" $t -Force
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /codepage:65001 `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/r:$t\TecladoFlotante.exe" `
    "/win32manifest:$root\app.manifest" "/out:$t\Harness.exe" "$root\tests\Harness.cs"
if ($LASTEXITCODE -ne 0) { throw 'csc fallo' }
switch ($Mode) {
    'render' { & "$t\Harness.exe" render "$t\img" }
    'unit'   {
        & "$t\Harness.exe" unit; $u = $LASTEXITCODE
        & "$t\Harness.exe" tray;   $u += $LASTEXITCODE   # arranque completo de la app (sin raton)
        & "$t\Harness.exe" dialog; exit ($u + $LASTEXITCODE) # avisos siempre por encima del teclado
    }
    default  { & "$t\Harness.exe" }
}
exit $LASTEXITCODE
