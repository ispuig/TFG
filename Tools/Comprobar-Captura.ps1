param([string]$UnityEditorData)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if (!$UnityEditorData) {
        $versionLine = Get-Content ProjectSettings/ProjectVersion.txt | Select-Object -First 1
        $version = $versionLine.Split(':')[1].Trim()
        $UnityEditorData = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor/Data"
    }
    $compiler = Join-Path $UnityEditorData 'DotNetSdkRoslyn/csc.dll'
    $runtime = Join-Path $UnityEditorData 'NetCoreRuntime/dotnet.exe'
    $mono = Join-Path $UnityEditorData 'MonoBleedingEdge/bin/mono.exe'
    $response = Get-ChildItem Library/Bee/artifacts -Recurse -Filter Assembly-CSharp.rsp |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (!$response) { throw 'Abre el proyecto en Unity para generar las referencias de compilación.' }
    New-Item -ItemType Directory -Path .utmp/captura -Force | Out-Null
    $base = Get-Content $response.FullName | Where-Object {
        $_ -notmatch '^(-out:|-refout:|-analyzer:|/additionalfile:)' -and $_ -notmatch '^"Assets[\\/].*\.cs"$'
    }
    $sources = Get-ChildItem Assets/Scripts -Filter '*.cs' | ForEach-Object { '"Assets/Scripts/' + $_.Name + '"' }
    $editor = $base + $sources + '-out:".utmp/captura/Captura.Editor.dll"'
    $editor | Set-Content .utmp/captura/Captura.Editor.rsp
    & $runtime $compiler '@.utmp/captura/Captura.Editor.rsp'
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación C# del Editor.' }

    # Compila las ramas Android, pero NO sustituye un build APK/IL2CPP ni valida el SDK Android.
    $android = $base | Where-Object { $_ -notmatch '^-define:(UNITY_EDITOR|UNITY_STANDALONE|PLATFORM_STANDALONE|ENABLE_MONO)' }
    $android += @('-define:UNITY_ANDROID', '-define:ENABLE_IL2CPP') + $sources + '-out:".utmp/captura/Captura.AndroidBranches.dll"'
    $android | Set-Content .utmp/captura/Captura.AndroidBranches.rsp
    & $runtime $compiler '@.utmp/captura/Captura.AndroidBranches.rsp'
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación C# de las ramas Android.' }

    $tests = $base | Where-Object { $_ -notmatch '^-target:' }
    $tests += @('-target:exe', '-out:".utmp/captura/PruebasCaptura.exe"',
        '"Assets/Scripts/ControlCaptura.cs"', '"Assets/Scripts/AlmacenCapturas.cs"', '"Assets/Scripts/DatosCaptura.cs"', '"Assets/Scripts/SesionVideo.cs"', '"Tools/PruebasCaptura.cs"')
    $tests | Set-Content .utmp/captura/PruebasCaptura.rsp
    & $runtime $compiler '@.utmp/captura/PruebasCaptura.rsp'
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron compilar las pruebas.' }
    & $mono .utmp/captura/PruebasCaptura.exe .utmp/captura
    if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas de captura.' }
}
finally { Pop-Location }
