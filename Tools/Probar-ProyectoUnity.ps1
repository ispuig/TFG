param([string]$UnityEditor)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (!$UnityEditor) {
    $version = (Get-Content (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') | Select-Object -First 1).Split(':')[1].Trim()
    $UnityEditor = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor/Unity.exe"
}
$testRoot = Join-Path $projectRoot ('.utmp/captura/proyecto_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
foreach ($folder in @('Assets','Packages','ProjectSettings')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $folder) -Destination $testRoot -Recurse
}
# Reutilizar exactamente los paquetes ya instalados, sin descargar ni alterar el proyecto abierto.
$manifest = Get-Content (Join-Path $testRoot 'Packages/manifest.json') -Raw | ConvertFrom-Json
foreach ($package in Get-ChildItem (Join-Path $projectRoot 'Library/PackageCache') -Directory) {
    $jsonPath = Join-Path $package.FullName 'package.json'
    if (Test-Path -LiteralPath $jsonPath) {
        $json = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
        if ($json.name) { $manifest.dependencies | Add-Member -MemberType NoteProperty -Name $json.name -Value ('file:' + $package.FullName.Replace('\','/')) -Force }
    }
}
$manifest | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $testRoot 'Packages/manifest.json')
New-Item -ItemType Directory -Path (Join-Path $testRoot 'Assets/Editor') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PruebasProyectoUnity.cs') -Destination (Join-Path $testRoot 'Assets/Editor')
$testLog = Join-Path $testRoot 'unity.log'
Write-Output "Proyecto de prueba: $testRoot"
$unityProcess = Start-Process -FilePath $UnityEditor -WindowStyle Hidden -PassThru -ArgumentList @(
    '-batchmode','-force-d3d11','-projectPath', ('"'+$testRoot+'"'),
    '-executeMethod','PruebasProyectoUnity.Ejecutar','-logFile',('"'+$testLog+'"'))
if (!$unityProcess.WaitForExit(600000)) { $unityProcess.Kill(); throw "Unity no terminó. Diagnóstico: $testLog" }
if ($unityProcess.ExitCode -ne 0 -or !(Test-Path (Join-Path $testRoot 'resultado_proyecto.txt'))) {
    Get-Content -LiteralPath $testLog -Tail 65
    throw "Falló la comprobación del proyecto. Diagnóstico: $testLog"
}
Get-Content (Join-Path $testRoot 'resultado_proyecto.txt')
