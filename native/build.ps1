param([string]$ToolchainRoot)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$buildPath = Join-Path $projectRoot 'build'
$outputPath = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $buildPath,$outputPath -Force | Out-Null

if ($ToolchainRoot) {
    $compiler = Join-Path $ToolchainRoot 'bin\x86_64-w64-mingw32-clang++.exe'
    $windres = Join-Path $ToolchainRoot 'bin\x86_64-w64-mingw32-windres.exe'
} else {
    $compiler = (Get-Command x86_64-w64-mingw32-clang++.exe -ErrorAction Stop).Source
    $windres = (Get-Command x86_64-w64-mingw32-windres.exe -ErrorAction Stop).Source
}
if (!(Test-Path -LiteralPath $compiler) -or !(Test-Path -LiteralPath $windres)) {
    throw 'Pass -ToolchainRoot with the extracted official LLVM-MinGW directory, or use CMake with Visual Studio.'
}

Push-Location $projectRoot
try {
    & $windres '-Iresources' 'resources/app.rc' '-O' 'coff' '-o' 'build/app.res.o'
    if ($LASTEXITCODE -ne 0) { throw 'Resource compilation failed.' }
    $commonFlags = @('-std=c++17','-O2','-Wall','-Wextra','-Wpedantic','-static','-Isrc')
    & $compiler @commonFlags 'src/TypingEngine.cpp' 'tests/engine_tests.cpp' '-o' 'build/engine_tests.exe'
    if ($LASTEXITCODE -ne 0) { throw 'Engine test build failed.' }
    & '.\build\engine_tests.exe'
    if ($LASTEXITCODE -ne 0) { throw 'Engine tests failed.' }
    & $compiler @commonFlags '-municode' '-mwindows' '-DUNICODE' '-D_UNICODE' 'tests/input_target.cpp' '-luser32' '-lgdi32' '-o' 'build/input_target.exe'
    if ($LASTEXITCODE -ne 0) { throw 'Integration target build failed.' }
    & $compiler @commonFlags '-Os' '-municode' '-mwindows' '-DUNICODE' '-D_UNICODE' '-DNOMINMAX' '-DWIN32_LEAN_AND_MEAN' '-D_WIN32_WINNT=0x0A00' '-DWINVER=0x0A00' 'src/main.cpp' 'src/TypingEngine.cpp' 'build/app.res.o' '-luser32' '-lgdi32' '-lcomctl32' '-ldwmapi' '-luxtheme' '-ladvapi32' '-Wl,--strip-all' '-o' 'dist/AutoTyperCompact.exe'
    if ($LASTEXITCODE -ne 0) { throw 'App compilation failed.' }
    $exePath = Join-Path $outputPath 'AutoTyperCompact.exe'
    $hash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  AutoTyperCompact.exe" | Set-Content -LiteralPath "$exePath.sha256" -Encoding ascii
    Get-Item -LiteralPath $exePath | Select-Object FullName,Length
} finally { Pop-Location }
