# build_wasm.ps1
Write-Host "🌲 Compiling Pino to WebAssembly..." -ForegroundColor Green
dotnet publish pino-csharp/pino-csharp.csproj -c Release -r browser-wasm

if ($LASTEXITCODE -eq 0) {
    Write-Host "🧹 Cleaning up old WASM assets..." -ForegroundColor Green
    if (Test-Path pino.site/wasm) {
        Remove-Item -Recurse -Force pino.site/wasm
    }
    New-Item -ItemType Directory -Path pino.site/wasm -Force | Out-Null

    Write-Host "📦 Copying WebAssembly bundle to website..." -ForegroundColor Green
    Copy-Item -Path pino-csharp/bin/Release/net10.0/browser-wasm/AppBundle/* -Destination pino.site/wasm/ -Recurse -Container -Force

    Write-Host "✨ WebAssembly build complete and synced!" -ForegroundColor Green
} else {
    Write-Error "❌ WASM Compilation failed!"
}
