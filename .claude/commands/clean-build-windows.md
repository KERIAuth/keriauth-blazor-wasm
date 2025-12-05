---
description: Perform a clean build of the extension using Windows (clean + full TypeScript and C# build)
---

# Clean Build (Windows)

Perform a complete clean build of the KERI Auth browser extension using Windows toolchain.

**Environment**: This command uses the Windows .NET SDK and npm. Do not mix with WSL builds.

**Note for Claude Code**: Commands use bash syntax (since Claude Code runs in a bash shell) but execute Windows-native tools (node, npm, dotnet).

## Build Steps

### 1. Verify Prerequisites

Check that required tools are installed with correct versions:

```bash
node --version
npm --version
dotnet --version
```

* Node.js >= 22.13.0
* npm >= 10.0.0
* .NET SDK 9.0.x

### 2. Clean and Reinstall npm Dependencies

Reinstall npm packages to ensure consistent binaries:

```bash
cd scripts && rm -rf node_modules && npm install
cd ../Extension && rm -rf node_modules && npm install
cd ..
```

### 3. Clean All Build Artifacts

Remove all TypeScript outputs, tsbuildinfo files, .NET build outputs, and NuGet cache (to avoid NU1403 errors when switching from WSL):

```bash
cd scripts && npm run clean && cd ../Extension && npm run clean && cd ..
rm -rf Extension/bin Extension/obj Extension.Tests/obj
dotnet nuget locals all --clear
```

### 4. Restore NuGet Dependencies

```bash
dotnet restore --force-evaluate
```

### 5. Build TypeScript FIRST (Critical)

TypeScript must be built BEFORE the C# build. The BackgroundWorker.js generator scans for JavaScript files during MSBuild and will miss them if they don't exist yet.

```bash
cd scripts && npm run build
cd ../Extension && npm run build:app
cd ..
```

### 6. Verify TypeScript Build Output

Check that all expected JavaScript files were generated:

```bash
ls Extension/wwwroot/scripts/esbuild/
ls Extension/wwwroot/scripts/es6/
ls Extension/wwwroot/app.js
```

Expected files in `scripts/esbuild/`:

* signifyClient.js
* ContentScript.js
* demo1.js
* utils.js

Expected files in `scripts/es6/`:

* index.js
* ExCsInterfaces.js
* storage-models.js
* webauthnCredentialWithPRF.js
* libsodium-polyfill.js

If any files are missing, the TypeScript build failed silently. Check for errors and rebuild.

### 7. Build the C# Project

Use Quick mode since TypeScript was already built in step 5:

```bash
dotnet build -p:Quick=true
```

### 8. Run Tests

```bash
dotnet test
```

Verify all tests pass before proceeding.

### 9. Verify Final Build Output

Check that the extension package is complete:

```bash
# Manifest exists
ls Extension/bin/Debug/net9.0/browserextension/manifest.json

# BackgroundWorker.js includes all required imports
grep -c "signifyClient" Extension/bin/Debug/net9.0/browserextension/content/BackgroundWorker.js
# Should return 2 (one import statement, one in allImports array)

# Key JavaScript files exist in output
ls Extension/bin/Debug/net9.0/browserextension/scripts/esbuild/
ls Extension/bin/Debug/net9.0/browserextension/scripts/es6/
```

### 10. Report Success

Extension ready to load: `Extension/bin/Debug/net9.0/browserextension/`

## Troubleshooting

### Service Worker Registration Failed (Status code: 3)

This error means the service worker JavaScript failed to load. Common causes:

1. **Missing JavaScript files**: TypeScript build was skipped or failed
   * Verify files exist in `Extension/bin/Debug/net9.0/browserextension/scripts/`
   * Rebuild TypeScript: `cd scripts && npm run build`

2. **BackgroundWorker.js missing imports**: The "two-build problem"
   * Delete browserextension folder and rebuild from scratch
   * Ensure TypeScript is built BEFORE C# build

3. **Stale browser cache**: Chrome cached old extension files
   * Remove extension from chrome://extensions
   * Clear browser cache (Ctrl+Shift+Delete)
   * Re-add extension with "Load unpacked"

### NU1403: Package Content Hash Validation Failed

Mixed builds between Windows and WSL. Pick one environment and stick with it:

```bash
# Clean and restore in Windows
dotnet nuget locals all --clear
rm -rf Extension/obj Extension.Tests/obj
dotnet restore --force-evaluate
```

### SRI Hash Mismatch (integrity attribute error)

Browser has cached old wasm files:

* Remove extension from chrome://extensions
* Clear browser cache
* Re-add extension

## Notes

* This workflow avoids the "two-build problem" by building TypeScript before C#
* The clean scripts remove tsbuildinfo files to ensure TypeScript rebuilds correctly
* Pick either Windows OR WSL for all builds - don't mix environments
* See CLAUDE.md "Common Build Issues" section for more details
