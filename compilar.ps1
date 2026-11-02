# Compila todos los programas con el compilador que ya trae Windows.
$ErrorActionPreference = 'Stop'
foreach ($app in 'ChequeadorPC', 'RespaldoPC', 'PosformateoPC', 'ReparadorPC', 'FichaServicio') {
    & (Join-Path $PSScriptRoot "$app\src\build.ps1")
}
