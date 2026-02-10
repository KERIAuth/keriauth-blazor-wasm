@echo off
REM Incremental build of KERI Auth browser extension (Windows)
REM Usage: build.cmd [--skip-ts] [--test]
REM   --skip-ts   Skip TypeScript build (C# only)
REM   --test      Run tests after build

setlocal enabledelayedexpansion
cd /d "%~dp0"

set SKIP_TS=0
set RUN_TESTS=0
for %%a in (%*) do (
    if "%%a"=="--skip-ts" set SKIP_TS=1
    if "%%a"=="--test" set RUN_TESTS=1
)

if !SKIP_TS!==1 (
    echo === Skipping TypeScript build ===
) else (
    echo === Building TypeScript ===
    cd scripts && call npm run build && cd ..
    if errorlevel 1 ( echo FAILED: TypeScript scripts build && exit /b 1 )

    cd Extension && call npm run build:app && cd ..
    if errorlevel 1 ( echo FAILED: app.ts build && exit /b 1 )
)

echo === Building C# (Release, Quick) ===
dotnet build --configuration Release -p:Quick=true
if errorlevel 1 ( echo FAILED: dotnet build && exit /b 1 )

if !RUN_TESTS!==1 (
    echo === Running Tests ===
    dotnet test --configuration Release --no-build
    if errorlevel 1 ( echo FAILED: dotnet test && exit /b 1 )
)

echo === Build succeeded ===
