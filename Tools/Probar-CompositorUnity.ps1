param([string]$UnityEditor)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (!$UnityEditor) {
    $version = (Get-Content (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') | Select-Object -First 1).Split(':')[1].Trim()
    $UnityEditor = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor/Unity.exe"
}
$testRoot = Join-Path $projectRoot ('.utmp/captura/unity_' + [guid]::NewGuid().ToString('N'))
foreach($folder in @('Assets/Scripts','Assets/Editor','Assets/CurrentMaterialOutput','Packages','ProjectSettings')) {
    New-Item -ItemType Directory -Path (Join-Path $testRoot $folder) -Force | Out-Null
}
foreach($file in @('ParStereo.cs','CompositorStereo.cs','DatosCaptura.cs','RepositorioCapturas.cs','AlmacenCapturas.cs',
    'NewUnlitUniversalRenderPipelineShader.shader','StereoViewer.shader')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "Assets/Scripts/$file") -Destination (Join-Path $testRoot 'Assets/Scripts')
    $meta = Join-Path $projectRoot "Assets/Scripts/$file.meta"
    if(Test-Path -LiteralPath $meta) { Copy-Item -LiteralPath $meta -Destination (Join-Path $testRoot 'Assets/Scripts') }
}
foreach($file in @('StereoComposite.mat','StereoComposite.mat.meta')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "Assets/CurrentMaterialOutput/$file") -Destination (Join-Path $testRoot 'Assets/CurrentMaterialOutput')
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PruebasCapturaUnity.cs') -Destination (Join-Path $testRoot 'Assets/Editor')
Copy-Item -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $testRoot 'ProjectSettings')
'{"dependencies":{"com.unity.modules.imageconversion":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}' | Set-Content (Join-Path $testRoot 'Packages/manifest.json')
$testLog = Join-Path $testRoot 'unity.log'
Write-Output "Proyecto de prueba: $testRoot"
# Sin -nographics: esta prueba necesita ejecutar el shader real. No abre una ventana interactiva.
$unityProcess = Start-Process -FilePath $UnityEditor -WindowStyle Hidden -PassThru -ArgumentList @(
    '-batchmode','-force-d3d11','-projectPath', ('"'+$testRoot+'"'),
    '-executeMethod','PruebasCapturaUnity.Ejecutar','-logFile',('"'+$testLog+'"'))
if (!$unityProcess.WaitForExit(180000)) {
    $unityProcess.Kill()
    throw "Unity no terminó en tres minutos. Proceso $($unityProcess.Id); diagnóstico: $testLog"
}
if ($unityProcess.ExitCode -ne 0 -or !(Test-Path (Join-Path $testRoot 'resultado.txt'))) {
    Get-Content -LiteralPath $testLog -Tail 65
    throw "Falló la prueba Unity. Diagnóstico: $testLog"
}
Get-Content (Join-Path $testRoot 'resultado.txt')
