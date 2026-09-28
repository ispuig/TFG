param([string]$UnityEditor, [switch]$Reutilizar, [switch]$CompilarAndroid)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (!$UnityEditor) {
    $version = (Get-Content (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') | Select-Object -First 1).Split(':')[1].Trim()
    $UnityEditor = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor/Unity.exe"
}
$testRoot = Join-Path $projectRoot ('.utmp/captura/proyecto_' + [guid]::NewGuid().ToString('N'))
if ($Reutilizar) {
    $anterior = Get-ChildItem (Join-Path $projectRoot '.utmp/captura') -Directory -Filter 'proyecto_*' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($anterior) { $testRoot = $anterior.FullName }
}
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
foreach ($folder in @('Assets','Packages','ProjectSettings')) {
    $destino = Join-Path $testRoot $folder
    New-Item -ItemType Directory -Path $destino -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $projectRoot $folder) | Copy-Item -Destination $destino -Recurse -Force
}
# Reutilizar exactamente los paquetes ya instalados, sin descargar ni alterar el proyecto abierto.
$manifest = Get-Content (Join-Path $testRoot 'Packages/manifest.json') -Raw | ConvertFrom-Json
$paquetesDelEditor = Join-Path (Split-Path $UnityEditor -Parent) 'Data/Resources/PackageManager/BuiltInPackages'
foreach ($package in Get-ChildItem (Join-Path $projectRoot 'Library/PackageCache') -Directory) {
    $jsonPath = Join-Path $package.FullName 'package.json'
    if (Test-Path -LiteralPath $jsonPath) {
        $json = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
        if ($json.name) {
            # SRP y otros paquetes incluidos con el Editor deben coincidir con sus APIs nativas.
            $incluido = Join-Path $paquetesDelEditor $json.name
            $origen = if (Test-Path -LiteralPath (Join-Path $incluido 'package.json')) { $incluido } else { $package.FullName }
            $manifest.dependencies | Add-Member -MemberType NoteProperty -Name $json.name -Value ('file:' + $origen.Replace('\','/')) -Force
        }
    }
}
$manifest | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $testRoot 'Packages/manifest.json')
New-Item -ItemType Directory -Path (Join-Path $testRoot 'Assets/Editor') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PruebasProyectoUnity.cs') -Destination (Join-Path $testRoot 'Assets/Editor')
$testLog = Join-Path $testRoot $(if ($CompilarAndroid) { 'android.log' } else { 'unity.log' })
Write-Output "Proyecto de prueba: $testRoot"
$inicio = Get-Date
$argumentos = @(
    '-batchmode','-force-d3d11','-projectPath', ('"'+$testRoot+'"'),
    '-executeMethod', $(if ($CompilarAndroid) { 'PruebasProyectoUnity.CompilarAndroid' } else { 'PruebasProyectoUnity.Ejecutar' }),
    '-logFile',('"'+$testLog+'"'))
if ($CompilarAndroid) { $argumentos += @('-buildTarget','Android') }
$unityProcess = Start-Process -FilePath $UnityEditor -WindowStyle Hidden -PassThru -ArgumentList $argumentos
$limiteMs = if ($CompilarAndroid) { 1800000 } else { 600000 }
if (!$unityProcess.WaitForExit($limiteMs)) { $unityProcess.Kill(); throw "Unity no terminó dentro del límite. Diagnóstico: $testLog" }
$archivoResultado = if ($CompilarAndroid) { 'resultado_android.txt' } else { 'resultado_proyecto.txt' }
$resultado = Get-Item (Join-Path $testRoot $archivoResultado) -ErrorAction SilentlyContinue
if ($unityProcess.ExitCode -ne 0 -or !$resultado -or $resultado.LastWriteTime -lt $inicio) {
    Get-Content -LiteralPath $testLog -Tail 65
    throw "Falló la comprobación del proyecto. Diagnóstico: $testLog"
}
Get-Content (Join-Path $testRoot $archivoResultado)
