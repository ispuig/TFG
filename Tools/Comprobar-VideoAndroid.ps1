param([string]$AndroidPlayer)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (!$AndroidPlayer) {
    $version = (Get-Content (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') | Select-Object -First 1).Split(':')[1].Trim()
    $AndroidPlayer = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor/Data/PlaybackEngines/AndroidPlayer"
}
$androidJar = Get-ChildItem (Join-Path $AndroidPlayer 'SDK/platforms') -Filter android.jar -Recurse | Sort-Object FullName | Select-Object -First 1
if (!$androidJar) { throw 'No está instalado el SDK Android de Unity.' }
$salida = Join-Path $projectRoot '.utmp/captura/java'
New-Item -ItemType Directory -Path $salida -Force | Out-Null
$fuentes = Get-ChildItem (Join-Path $projectRoot 'Assets/AndroidPlugins') -Filter '*.java' | Select-Object -ExpandProperty FullName
& (Join-Path $AndroidPlayer 'OpenJDK/bin/javac.exe') -encoding UTF-8 --release 8 -cp $androidJar.FullName -d $salida $fuentes (Join-Path $PSScriptRoot 'PruebasRgbaYuv.java')
if ($LASTEXITCODE -ne 0) { throw 'No compila el plugin Java.' }
& (Join-Path $AndroidPlayer 'OpenJDK/bin/java.exe') -cp $salida PruebasRgbaYuv
if ($LASTEXITCODE -ne 0) { throw 'Falló la conversión RGBA/YUV.' }
Write-Output 'Plugin compilado contra Android SDK. MediaCodec/MediaMuxer aún requieren prueba en Quest.'
