param(
    [Parameter(Mandatory)][ValidateSet('x64','arm64')][string]$Architecture,
    [ValidateSet('both','winui','compact')][string]$Edition = 'both',
    [string]$WixPath = '.tools/wix/wix.exe'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $platform = if ($Architecture -eq 'arm64') { 'ARM64' } else { 'x64' }
    $work = Join-Path $root "dist/build-$Architecture"
    $assets = Join-Path $root "dist/release-$Architecture"
    New-Item -ItemType Directory -Path $assets -Force | Out-Null
    [xml]$project = Get-Content AutoTyper.csproj
    $version = $project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not (Test-Path -LiteralPath $WixPath)) { throw "Install WiX 6.0.2 with: dotnet tool install --tool-path .tools/wix wix --version 6.0.2" }
    $wix = (Resolve-Path -LiteralPath $WixPath).Path
    $suffix = if ($Architecture -eq 'x64') { '' } else { '-arm64' }
    foreach ($app in @('main','compact')) {
        if (($app -eq 'main' -and $Edition -eq 'compact') -or ($app -eq 'compact' -and $Edition -eq 'winui')) { continue }
        if ($app -eq 'main') {
            $publish = Join-Path $work 'main'
            & dotnet publish AutoTyper.csproj -c Release -r "win-$Architecture" "-p:Platform=$platform" -p:PublishProfile=SingleFile "-p:PublishDir=$publish/"
            if ($LASTEXITCODE -ne 0) { throw "Main $Architecture publish failed." }
            $exe = Join-Path $publish 'AutoTyper.exe'
            $name = 'Auto Typer'; $folder = 'AutoTyper'; $exeName = 'AutoTyper.exe'
            $upgrade = '4F714E6A-7A62-4B64-95B6-BA107B3E880B'
            $component = '4EFBF128-94A4-411E-A189-070C52E87BDC'
            $icon = Join-Path $root 'Assets/AutoTyper.ico'
        } else {
            $nativeBuild = Join-Path $work 'compact'
            & cmake -S native -B $nativeBuild -A $platform -DBUILD_TESTING=ON
            if ($LASTEXITCODE -ne 0) { throw "Compact $Architecture configuration failed." }
            & cmake --build $nativeBuild --config Release
            if ($LASTEXITCODE -ne 0) { throw "Compact $Architecture build failed." }
            & ctest --test-dir $nativeBuild -C Release --output-on-failure
            if ($LASTEXITCODE -ne 0) { throw "Compact $Architecture tests failed." }
            $exe = Join-Path $nativeBuild 'Release/AutoTyperCompact.exe'
            $name = 'Auto Typer Compact'; $folder = 'AutoTyperCompact'; $exeName = 'AutoTyperCompact.exe'
            $upgrade = 'B6CFF61F-07D6-4C24-8AF4-0317B31EAB7C'
            $component = '738EFB4D-C3F6-4917-A57F-155D976276F0'
            $icon = Join-Path $root 'native/resources/AutoTyperCompact.ico'
        }
        $base = [IO.Path]::GetFileNameWithoutExtension($exeName)
        Copy-Item -LiteralPath $exe -Destination (Join-Path $assets "$base$suffix.exe")
        $msi = Join-Path $assets "$base-Setup-$Architecture.msi"
        & $wix build installer/AutoTyper.wxs -arch $Architecture -d "AppName=$name" -d "AppVersion=$version" -d "UpgradeCode=$upgrade" -d "ComponentGuid=$component" -d "AppIcon=$icon" -d "AppExe=$exe" -d "ExeName=$exeName" -d "InstallFolder=$folder" -out $msi
        if ($LASTEXITCODE -ne 0) { throw "$name $Architecture installer failed." }
    }
    foreach ($asset in Get-ChildItem -LiteralPath $assets -File | Where-Object { $_.Extension -in @('.exe','.msi') }) {
        $hash = (Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        [IO.File]::WriteAllText("$($asset.FullName).sha256", "$hash  $($asset.Name)`n", [Text.Encoding]::ASCII)
    }
} finally { Pop-Location }
