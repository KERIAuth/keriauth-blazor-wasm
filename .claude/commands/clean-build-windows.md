---

## description: Perform a clean build of the extension using Windows (clean + full TypeScript and C# build)

# Clean Build (Windows)

Perform a complete clean build of the KERI Auth browser extension using Windows toolchain.

**Environment**: This command uses the Windows .NET SDK and npm. Do not mix with WSL builds.

**Note for Claude Code**: Commands use bash syntax (since Claude Code runs in a bash shell) but execute Windows-native tools (node, npm, dotnet).

---

## Security Awareness

This workflow executes code from:

* npm scripts defined in `package.json`
* Installed npm dependencies
* MSBuild targets and NuGet packages

Before running a clean build, review unexpected changes to:

* `package.json`
* `package-lock.json`
* `*.csproj`
* `Directory.Build.*`
* `scripts/*`

Optional safety pre-check:

```bash
git status --porcelain
```

Expected output: **none**
If files are modified unexpectedly, review them before continuing.

---

## Build Steps

### 1. Verify Prerequisites

Check that required tools are installed with correct versions:

```bash
node --version
npm --version
dotnet --version
```

Required:

* Node.js >= 22.13.0
* npm >= 10.0.0
* .NET SDK 9.0.x

---

### 2. Clean and Reinstall npm Dependencies

Reinstall npm packages to ensure consistent binaries:

```bash
pushd scripts
rm -rf node_modules
npm install
popd

pushd Extension
rm -rf node_modules
npm install
popd
```

> ⚠️ npm install executes lifecycle scripts.
> Review dependency or lockfile changes before running.

#### Future Reproducibility / Security Probe (Optional)

Occasionally you may run builds in offline mode:

```bash
npm --offline run build
```

This forces npm to use only locally cached packages and fails if network access is required.
Useful for detecting:

* dependency drift
* unexpected downloads
* non-reproducible builds

Not intended for routine development.

---

### 3. Clean All Build Artifacts

Remove TypeScript outputs, tsbuildinfo files, .NET build outputs, and NuGet cache:

```bash
pushd scripts
npm run clean
popd

pushd Extension
npm run clean
popd

rm -rf Extension/bin Extension/obj Extension.Tests/obj
dotnet nuget locals all --clear
```

---

### 4. Restore NuGet Dependencies

**Note:** Release configuration only.

```bash
dotnet restore -p:Configuration=Release --force-evaluate
```

---

### 5. Build TypeScript FIRST (Critical)

TypeScript must exist before MSBuild scans JavaScript files.

```bash
pushd scripts
npm run build
popd

pushd Extension
npm run build:app
popd
```

---

### 6. Verify TypeScript Build Output

```bash
ls Extension/wwwroot/scripts/esbuild/
ls Extension/wwwroot/scripts/es6/
test -f Extension/wwwroot/app.js
```

Expected files:

#### `scripts/esbuild/`

* signifyClient.js
* ContentScript.js
* demo1.js
* utils.js

#### `scripts/es6/`

* index.js
* ExCsInterfaces.js
* storage-models.js
* libsodium-polyfill.js
* navigatorCredentialsShim.js

If missing — rebuild and check logs.

---

### 7. Build the C# Project

```bash
dotnet build --configuration Release -p:Quick=true --no-restore
```

---

### 8. Run Tests

```bash
dotnet test --configuration Release
```

All tests must pass before proceeding.

---

### 9. Verify Final Build Output

```bash
test -f Extension/bin/Release/net9.0/browserextension/manifest.json
```

Verify BackgroundWorker imports:

```bash
grep -c "signifyClient" Extension/bin/Release/net9.0/browserextension/content/BackgroundWorker.js
```

Expected result:

```
2
```

Verify output JS presence:

```bash
ls Extension/bin/Release/net9.0/browserextension/scripts/esbuild/
ls Extension/bin/Release/net9.0/browserextension/scripts/es6/
```

Optional integrity snapshot:

```bash
sha256sum Extension/bin/Release/net9.0/browserextension/manifest.json
```

---

### 10. Report Success

Extension ready to load:

```
Extension/bin/Release/net9.0/browserextension/
```

Reload in `chrome://extensions`.

---

## Troubleshooting

### Service Worker Registration Failed (Status code: 3)

**Cause:** JS failed to load.

1️⃣ Missing JS

* Verify build outputs exist
* Rebuild TypeScript

2️⃣ BackgroundWorker imports missing

* Delete browserextension output
* Rebuild clean

3️⃣ Browser cache stale

* Remove extension
* Clear cache
* Reload unpacked

---

### NU1403 Package Hash Validation Failed

Mixing Windows/WSL artifacts.

```bash
dotnet nuget locals all --clear
rm -rf Extension/obj Extension.Tests/obj
dotnet restore -p:Configuration=Release --force-evaluate
```

---

### SRI Hash Mismatch

Old wasm cached:

* Remove extension
* Clear cache
* Reload

---

## Notes

* TypeScript must build before C#
* Clean scripts remove tsbuildinfo caches
* Use Windows OR WSL exclusively
* See `CLAUDE.md → Common Build Issues`
