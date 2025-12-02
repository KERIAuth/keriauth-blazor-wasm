---
description: Perform a clean build of the extension (clean + full TypeScript and C# build)
---

# Clean Build

Perform a complete clean build of the KERI Auth browser extension.

**IMPORTANT - WSL Required**: This project must be built from WSL (Windows Subsystem for Linux), not Windows. The `.csproj` blocks Windows builds to prevent NuGet cache conflicts. When running from Git Bash or other Windows environments, prefix commands with `wsl -e bash -ic` to execute them in WSL:

```bash
# Example: running dotnet build from Git Bash
wsl -e bash -ic "cd /mnt/c/s/k/kbw && dotnet build -p:Quick=true"
```

## Build Steps

### 1. Verify Prerequisites (WSL environment)

Check that required tools are installed in WSL with correct versions:

```bash
wsl -e bash -ic "node --version && npm --version && dotnet --version"
```

- Node.js >= 22.13.0
- npm >= 10.0.0
- .NET SDK 9.0.x

If Node.js version is wrong, use nvm to install the correct version:

```bash
wsl -e bash -ic "nvm install 22 && nvm use 22"
```

### 2. Clean and Reinstall npm Dependencies

**Always reinstall npm packages from WSL** to ensure Linux-native binaries (esbuild, tsc) are installed. Windows-native binaries will cause "esbuild platform mismatch" or "Permission denied" errors.

```bash
wsl -e bash -ic "cd /mnt/c/s/k/kbw/scripts && rm -rf node_modules && npm install"
wsl -e bash -ic "cd /mnt/c/s/k/kbw/Extension && rm -rf node_modules && npm install"
```

### 3. Clean All Build Artifacts

Remove all TypeScript outputs, tsbuildinfo files, and .NET build outputs:

```bash
wsl -e bash -ic "cd /mnt/c/s/k/kbw/scripts && npm run clean"
wsl -e bash -ic "cd /mnt/c/s/k/kbw/Extension && npm run clean"
wsl -e bash -ic "cd /mnt/c/s/k/kbw && dotnet clean"
```

Also delete the browserextension output directory to ensure a completely fresh build:

```bash
rm -rf Extension/bin/Debug/net9.0/browserextension
```

### 4. Restore NuGet Dependencies

```bash
wsl -e bash -ic "cd /mnt/c/s/k/kbw && dotnet restore"
```

### 5. Build TypeScript FIRST (Critical)

TypeScript must be built BEFORE the C# build. The BackgroundWorker.js generator scans for JavaScript files during MSBuild and will miss them if they don't exist yet.

```bash
wsl -e bash -ic "cd /mnt/c/s/k/kbw/scripts && npm run build"
wsl -e bash -ic "cd /mnt/c/s/k/kbw/Extension && npm run build:app"
```

### 6. Verify TypeScript Build Output

Check that all expected JavaScript files were generated:

```bash
ls -la Extension/wwwroot/scripts/esbuild/
ls -la Extension/wwwroot/scripts/es6/
ls -la Extension/wwwroot/app.js
```

Expected files in `scripts/esbuild/`:
- signifyClient.js
- ContentScript.js
- demo1.js
- utils.js

Expected files in `scripts/es6/`:
- index.js
- ExCsInterfaces.js
- storage-models.js
- webauthnCredentialWithPRF.js
- libsodium-polyfill.js

If any files are missing, the TypeScript build failed silently. Check for errors and rebuild.

### 7. Build the C# Project

Use Quick mode since TypeScript was already built in step 5:

```bash
wsl -e bash -ic "cd /mnt/c/s/k/kbw && dotnet build -p:Quick=true"
```

### 8. Run Tests

```bash
wsl -e bash -ic "cd /mnt/c/s/k/kbw && dotnet test"
```

Verify all tests pass before proceeding.

### 9. Verify Final Build Output

Check that the extension package is complete:

```bash
# Manifest exists
ls -la Extension/bin/Debug/net9.0/browserextension/manifest.json

# BackgroundWorker.js includes all required imports
grep -c "signifyClient" Extension/bin/Debug/net9.0/browserextension/content/BackgroundWorker.js
# Should return 2 (one import statement, one in allImports array)

# Key JavaScript files exist in output
ls -la Extension/bin/Debug/net9.0/browserextension/scripts/esbuild/
ls -la Extension/bin/Debug/net9.0/browserextension/scripts/es6/
```

### 10. Report Success

Extension ready to load: `Extension/bin/Debug/net9.0/browserextension/`

## Troubleshooting

### Service Worker Registration Failed (Status code: 3)

This error means the service worker JavaScript failed to load. Common causes:

1. **Missing JavaScript files**: TypeScript build was skipped or failed
   - Verify files exist in `Extension/bin/Debug/net9.0/browserextension/scripts/`
   - Rebuild TypeScript: `wsl -e bash -ic "cd /mnt/c/s/k/kbw/scripts && npm run build"`

2. **BackgroundWorker.js missing imports**: The "two-build problem"
   - Delete browserextension folder and rebuild from scratch
   - Ensure TypeScript is built BEFORE C# build

3. **Stale browser cache**: Chrome cached old extension files
   - Remove extension from chrome://extensions
   - Clear browser cache (Ctrl+Shift+Delete)
   - Re-add extension with "Load unpacked"

### esbuild Platform Mismatch

npm packages contain Windows binaries but build runs in WSL:

```bash
wsl -e bash -ic "cd /mnt/c/s/k/kbw/scripts && rm -rf node_modules && npm install"
```

### Permission Denied (code 126)

npm binaries lack execute permission in WSL:

```bash
wsl -e bash -ic "chmod +x /mnt/c/s/k/kbw/scripts/node_modules/.bin/* /mnt/c/s/k/kbw/Extension/node_modules/.bin/*"
```

### Windows Build Blocked

The `.csproj` intentionally blocks Windows builds. Use WSL:

```bash
wsl -e bash -ic "cd /mnt/c/s/k/kbw && dotnet build -p:Quick=true"
```

### SRI Hash Mismatch (integrity attribute error)

Browser has cached old wasm files:
- Remove extension from chrome://extensions
- Clear browser cache
- Re-add extension

## Notes

- This workflow avoids the "two-build problem" by building TypeScript before C#
- The clean scripts remove tsbuildinfo files to ensure TypeScript rebuilds correctly
- Always use `wsl -e bash -ic` when running from Git Bash or Windows terminals
- See CLAUDE.md "Common Build Issues" section for more details
