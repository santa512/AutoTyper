param([Parameter(Mandatory)][ValidateSet('x64','arm64')][string]$Architecture,
    [ValidateSet('both','winui','compact')][string]$Edition='both')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root "dist/release-$Architecture"
$suffix = if ($Architecture -eq 'x64') { '' } else { '-arm64' }
$machine = if ($Architecture -eq 'x64') { 0x8664 } else { 0xaa64 }
foreach ($name in @('AutoTyper','AutoTyperCompact')) {
    if(($name -eq 'AutoTyper' -and $Edition -eq 'compact') -or ($name -eq 'AutoTyperCompact' -and $Edition -eq 'winui')) { continue }
    $exe = Join-Path $assets "$name$suffix.exe"
    $stream = [IO.File]::OpenRead($exe)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        $stream.Position = 0x3c
        $header = $reader.ReadInt32()
        $stream.Position = $header
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne $machine) { throw "Wrong architecture: $exe" }
    } finally { $reader.Dispose() }
    if ($name -eq 'AutoTyper') {
        $probe = Start-Process -FilePath $exe -ArgumentList '--audio-self-test' -Wait -PassThru -WindowStyle Hidden
        if ($probe.ExitCode -ne 0) { throw 'Packaged local audio processor self-test failed.' }
        $probe = Start-Process -FilePath $exe -ArgumentList '--caption-self-test' -Wait -PassThru -WindowStyle Hidden
        if ($probe.ExitCode -ne 0) { throw 'Packaged local caption engine self-test failed.' }
    }
    $msi = Join-Path $assets "$name-Setup-$Architecture.msi"
    $log = Join-Path $env:RUNNER_TEMP "$name-install.log"
    $installed = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "Programs/$name/$name.exe"
    $shortcutName = if ($name -eq 'AutoTyper') { 'Auto Typer.lnk' } else { 'Auto Typer Compact.lnk' }
    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) $shortcutName
    $process = Start-Process -FilePath msiexec.exe -ArgumentList @('/i',"`"$msi`"",'/qn','/norestart','/l*v',"`"$log`"") -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -notin @(0,3010)) { Get-Content $log -Tail 60; throw "Installer failed: $($process.ExitCode)" }
    try {
        if (-not (Test-Path -LiteralPath $shortcut) -or -not (Test-Path -LiteralPath $installed)) { throw 'Installed EXE or Start menu shortcut missing.' }
        if ((Get-FileHash -LiteralPath $installed).Hash -ne (Get-FileHash -LiteralPath $exe).Hash) { throw 'Installed EXE differs from portable download.' }
        if ($name -eq 'AutoTyper') {
            $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
            New-Item -Path $runKey -Force | Out-Null
            New-ItemProperty -Path $runKey -Name AutoTyper -Value "`"$installed`" --startup" -PropertyType String -Force | Out-Null
        }
    } finally {
        $process = Start-Process -FilePath msiexec.exe -ArgumentList @('/x',"`"$msi`"",'/qn','/norestart') -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -notin @(0,3010)) { throw "Uninstall failed: $($process.ExitCode)" }
    }
    if ((Test-Path -LiteralPath $installed) -or (Test-Path -LiteralPath $shortcut)) { throw 'Uninstall left its executable or shortcut behind.' }
    if ($name -eq 'AutoTyper' -and (Get-Item -LiteralPath $runKey).GetValue('AutoTyper')) { throw 'Uninstall left its startup entry behind.' }
    Write-Output "PASS: $name $Architecture PE architecture, install, shortcut, file integrity and uninstall."
}
foreach ($asset in Get-ChildItem -LiteralPath $assets -File | Where-Object { $_.Extension -in @('.exe','.msi') }) {
    $expected = ((Get-Content -LiteralPath "$($asset.FullName).sha256" -Raw).Trim() -split '\s+')[0]
    if ((Get-FileHash -LiteralPath $asset.FullName).Hash.ToLowerInvariant() -ne $expected) { throw "Checksum mismatch: $($asset.Name)" }
}
