@echo off
rem ===== HorseRace one-click installer (Windows) =====
rem Double-click this file. (Or drag your Unity project folder onto it.)
rem The rest of this file after the marker line is PowerShell (UTF-8).
setlocal
set "HR_SELF=%~f0"
set "HR_ARG=%~1"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=[IO.File]::ReadAllText($env:HR_SELF,[Text.Encoding]::UTF8); $i=$s.IndexOf('#'+'#PS-START'); Invoke-Expression $s.Substring($i)"
echo.
pause
exit /b
##PS-START
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$Branch = 'claude/vrc-steve-ballrun-game-wfuuq3'
$ZipUrl = "https://codeload.github.com/sigenmai/wadoPTR/zip/refs/heads/$Branch"
$SaveDir = Join-Path $env:LOCALAPPDATA 'HorseRace'
$SaveFile = Join-Path $SaveDir 'project.txt'

function Say($m) { Write-Host $m }
function IsUnityProject($p) { $p -and (Test-Path (Join-Path $p 'Assets')) -and (Test-Path (Join-Path $p 'ProjectSettings')) }

try {
    Say '=== 荒野横断ホースレース ワンボタンインストーラー ==='

    # 1. Unity プロジェクトを決める：ドラッグされたフォルダ → bat の置き場所 → 前回の場所 → 選んでもらう
    $proj = $null
    $here = Split-Path -Parent $env:HR_SELF
    foreach ($c in @($env:HR_ARG, $here, (Split-Path -Parent $here))) { if (-not $proj -and (IsUnityProject $c)) { $proj = $c } }
    if (-not $proj -and (Test-Path $SaveFile)) { $c = (Get-Content $SaveFile -Raw).Trim(); if (IsUnityProject $c) { $proj = $c } }
    if (-not $proj) {
        Add-Type -AssemblyName System.Windows.Forms
        $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
        $dlg.Description = 'VRChat ワールドの Unity プロジェクトのフォルダ（Assets が入っているフォルダ）を選んでください'
        if ($dlg.ShowDialog() -ne 'OK') { Say 'キャンセルしました。'; return }
        $proj = $dlg.SelectedPath
    }
    if (-not (IsUnityProject $proj)) { throw "Unity プロジェクトではありません（Assets と ProjectSettings がありません）: $proj" }
    New-Item -ItemType Directory -Force $SaveDir | Out-Null
    Set-Content -Path $SaveFile -Value $proj -Encoding UTF8
    Say "プロジェクト: $proj"

    # 2. VRChat SDK（Worlds）が入っているか
    $pk = ''
    foreach ($f in @('Packages\vpm-manifest.json', 'Packages\manifest.json')) { $fp = Join-Path $proj $f; if (Test-Path $fp) { $pk += (Get-Content $fp -Raw) } }
    if ($pk -notmatch 'com\.vrchat\.worlds') {
        Say ''
        Say '!! このプロジェクトには VRChat SDK - Worlds が入っていないようです。'
        Say '   VRChat Creator Companion で「Worlds」テンプレートのプロジェクトを作るか、Manage Project で VRChat SDK - Worlds を追加してから、もう一度実行してください。'
        return
    }

    # 3. 最新のファイルを GitHub から取ってくる（Assets/HorseRace だけ取り出す）
    $tmp = Join-Path $env:TEMP 'HorseRace'
    New-Item -ItemType Directory -Force $tmp | Out-Null
    $zip = Join-Path $tmp 'horserace.zip'
    Say '最新版をダウンロードしています（数百 MB、数分かかることがあります）…'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $ZipUrl -OutFile $zip -UseBasicParsing

    Say 'Assets/HorseRace をプロジェクトにコピーしています…'
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $dst = Join-Path $proj 'Assets\HorseRace'
    $za = [IO.Compression.ZipFile]::OpenRead($zip)
    $n = 0
    try {
        foreach ($e in $za.Entries) {
            if ($e.FullName -notmatch '^[^/]+/Assets/HorseRace/(.+)$') { continue }
            $rel = $Matches[1]
            if ($rel.EndsWith('/')) { continue }
            $out = Join-Path $dst ($rel -replace '/', '\')
            # 自分で直した配置（Save Layout の結果）は上書きしない
            if ($rel -eq 'Layout/LayoutOverrides.json' -and (Test-Path $out)) { continue }
            New-Item -ItemType Directory -Force (Split-Path -Parent $out) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $out, $true)
            $n++
        }
    } finally { $za.Dispose() }
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
    if ($n -eq 0) { throw 'ダウンロードしたファイルに Assets/HorseRace が見つかりませんでした。' }
    Say "$n 個のファイルをコピーしました。"

    # 4. Unity 側で自動セットアップを始める目印（Unity は「.」で始まるファイルを取り込まない）
    Set-Content -Path (Join-Path $dst '.oneclick') -Value 'run' -Encoding ASCII

    # 5. Unity を起動（もう開いていれば、Unity の画面に切り替えるだけで始まる）
    $running = Get-Process -Name 'Unity' -ErrorAction SilentlyContinue
    if ($running) {
        Say ''
        Say '>> Unity が開いています。Unity の画面に切り替えてください。読み込みのあと自動で組み立てが始まります。'
    } else {
        $ver = ''
        $pv = Join-Path $proj 'ProjectSettings\ProjectVersion.txt'
        if (Test-Path $pv) { $m = Select-String -Path $pv -Pattern 'm_EditorVersion:\s*(\S+)'; if ($m) { $ver = $m.Matches[0].Groups[1].Value } }
        $exe = $null
        foreach ($root in @("$env:ProgramFiles\Unity\Hub\Editor", "${env:ProgramFiles(x86)}\Unity\Hub\Editor")) {
            $c = Join-Path $root "$ver\Editor\Unity.exe"
            if ($ver -and (Test-Path $c)) { $exe = $c; break }
        }
        if ($exe) {
            Say "Unity $ver を起動します。開いたら自動で組み立てが始まります（初回は数分〜十数分）。"
            Start-Process -FilePath $exe -ArgumentList @('-projectPath', "`"$proj`"")
        } else {
            Say ''
            Say ">> Unity $ver が見つからなかったので、VRChat Creator Companion（または Unity Hub）からこのプロジェクトを開いてください。開くと自動で組み立てが始まります。"
        }
    }
    Say ''
    Say '完了ダイアログ「ワールドができました」が出たら終わりです。エラーが出たら Unity の Console の赤い行を Claude に送ってください。'
}
catch {
    Say ''
    Say ('エラー: ' + $_.Exception.Message)
    Say 'この画面の文をそのまま Claude に送ってください。'
}
