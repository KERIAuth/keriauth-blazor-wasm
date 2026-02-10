@echo off
REM Full clean build of KERI Auth browser extension (Windows)
REM Usage: clean-build.cmd [--skip-install] [--skip-test]
REM   --skip-install   Skip npm install (use existing node_modules)
REM   --skip-test      Skip running tests

setlocal enabledelayedexpansion
cd /d "%~dp0"

set SKIP_INSTALL=0
set SKIP_TEST=0
for %%a in (%*) do (
    if "%%a"=="--skip-install" set SKIP_INSTALL=1
    if "%%a"=="--skip-test" set SKIP_TEST=1
)

echo === Verifying Prerequisites ===
node --version
npm --version
dotnet --version

if !SKIP_INSTALL!==1 (
    echo === Skipping npm install ===
) else (
    echo === Installing npm dependencies ===
    cd scripts && rmdir /s /q node_modules 2>nul & call npm install && cd ..
    if errorlevel 1 ( echo FAILED: npm install scripts && exit /b 1 )

    cd Extension && rmdir /s /q node_modules 2>nul & call npm install && cd ..
    if errorlevel 1 ( echo FAILED: npm install Extension && exit /b 1 )
)

echo === Cleaning build artifacts ===
cd scripts && call npm run clean && cd ..
cd Extension && call npm run clean && cd ..
rmdir /s /q Extension\bin Extension\obj Extension.Tests\obj 2>nul
dotnet nuget locals all --clear

echo === Restoring NuGet packages ===
dotnet restore -p:Configuration=Release --force-evaluate
if errorlevel 1 ( echo FAILED: dotnet restore && exit /b 1 )

echo === Building TypeScript ===
cd scripts && call npm run build && cd ..
if errorlevel 1 ( echo FAILED: TypeScript scripts build && exit /b 1 )

cd Extension && call npm run build:app && cd ..
if errorlevel 1 ( echo FAILED: app.ts build && exit /b 1 )

echo === Verifying TypeScript output ===
if not exist Extension\wwwroot\scripts\esbuild\signifyClient.js (
    echo FAILED: Missing esbuild output
    exit /b 1
)
if not exist Extension\wwwroot\app.js (
    echo FAILED: Missing app.js
    exit /b 1
)

echo === Building C# (Release, Quick) ===
dotnet build --configuration Release -p:Quick=true --no-restore
if errorlevel 1 ( echo FAILED: dotnet build && exit /b 1 )

if !SKIP_TEST!==1 (
    echo === Skipping tests ===
) else (
    echo === Running Tests ===
    dotnet test --configuration Release --no-build
    if errorlevel 1 ( echo FAILED: dotnet test && exit /b 1 )
)

echo === Verifying final output ===
if not exist Extension\bin\Release\net9.0\browserextension\manifest.json (
    echo FAILED: Missing manifest.json in build output
    exit /b 1
)

echo === Clean build succeeded ===
echo Extension ready at: Extension\bin\Release\net9.0\browserextension\
