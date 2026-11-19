param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$source=Join-Path $root 'audio/native'
$output=Join-Path $source 'bin/x64/AutoTyperAudio.dll'
New-Item -ItemType Directory -Path (Split-Path $output) -Force | Out-Null
$clang=Join-Path $root '.tools/llvm-mingw/llvm-mingw-20261104-ucrt-x86_64/bin/x86_64-w64-mingw32-clang++.exe'
if(Test-Path -LiteralPath $clang) {
    & $clang -std=c++17 -O3 -shared -static -static-libgcc -static-libstdc++ (Join-Path $source 'processor.cpp') -o $output
    if($LASTEXITCODE -ne 0) { throw 'Local audio processor compilation failed.' }
} else {
    & cmake -S $source -B (Join-Path $source 'build') -A x64
    if($LASTEXITCODE -ne 0) { throw 'Install Visual Studio C++ Build Tools and CMake to build local audio.' }
    & cmake --build (Join-Path $source 'build') --config Release
    if($LASTEXITCODE -ne 0) { throw 'Local audio processor compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $source 'build/Release/AutoTyperAudio.dll') -Destination $output
}
Write-Output "Built local x64 audio processor: $output"
