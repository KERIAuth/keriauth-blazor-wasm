# Build Instructions

For architecture overview, see [ARCHITECTURE.md](ARCHITECTURE.md). For coding standards, see [CODING.md](CODING.md).

> This project builds exclusively in WSL (Ubuntu). Windows builds are not supported.

## Prerequisites

- WSL with Ubuntu
- Node.js >= 22.13.0
- npm >= 11.0.0
- .NET 9.0 SDK
- GNU Make
- Chromium-based browser version 127+ (Chrome, Edge, or Brave)

Check versions:
```bash
make prereqs
```

## Build Configuration

This project uses **Release configuration only**. Debug configuration is intentionally disabled — browser extension debugging happens through Chrome DevTools, not VS Code debuggers.

## Quick Reference

| Command | Purpose |
|---------|---------|
| `make build` | Full incremental build (TypeScript + C#) |
| `make build-ts` | TypeScript only (scripts + app.ts) |
| `make test` | Run C# tests |
| `make clean-build` | Full clean + install + build + test |
| `make install` | Install npm dependencies |
| `make clean` | Clean all build artifacts |
| `make watch` | TypeScript watch mode for development |
| `make typecheck` | TypeScript type checking (no emit) |
| `make verify` | Verify build output integrity |

## First Time Setup

```bash
make install
make build
make test
```

The `make install` step is only needed:
- After first clone
- When package.json or package-lock.json files change
- After running `make clean` or deleting node_modules/

## Build System Architecture

The extension requires **two independent build systems** that must run in order:

```
TypeScript sources -> npm build -> JS in wwwroot/ -> dotnet build -> Extension package
```

The Makefile enforces this ordering automatically.

### Why This Order Matters

The Blazor.BrowserExtension `BackgroundWorker.js` generator runs during MSBuild's `StaticWebAssetsPrepareForRun` phase, which scans for static assets. JavaScript files must already exist in `wwwroot/scripts/` before the C# build starts, or they won't be included in the generated BackgroundWorker.js.

### Build Flags

| Flag | Effect | Shortcut |
|------|--------|----------|
| `BuildingProject=true` | Enable TypeScript build | `-p:FullBuild=true` |
| `SkipJavaScriptBuild=true` | Skip TypeScript | `-p:Quick=true` |
| `Configuration=Release` | Release mode | `-p:Production=true` |
| `DesignTimeBuild=true` | Skip npm (IDE IntelliSense) | Set by VS Code |

### Key Build Files

- `Makefile` — Build entry point (all targets)
- `Extension/Extension.csproj` — MSBuild targets (`BuildExtensionScripts`, `BuildAppTs`)
- `scripts/package.json` — npm workspaces root (types, modules, bundles build order)
- `scripts/bundles/esbuild.config.js` — JavaScript bundler configuration
- `Extension/package.json` — npm scripts for app.ts and clean

### Output

Final extension package: `Extension/bin/Release/net9.0/browserextension/`

## Loading the Extension in Browser

1. Open Chrome/Edge/Brave
2. Navigate to `chrome://extensions`
3. Enable "Developer mode" (top right)
4. Click "Load unpacked"
5. Select: `Extension/bin/Release/net9.0/browserextension/`

**After rebuilding**: click the reload button in chrome://extensions. Browser caches extension files — refreshing the popup is not sufficient.

## Development Workflow

### TypeScript-only changes
- Use `make watch` for automatic rebuilds, or `make build-ts` for manual rebuild
- Always reload extension in browser after changes

### C#-only changes
- Run `dotnet build --configuration Release -p:Quick=true` directly for faster iteration
- Always reload extension in browser after changes

### Mixed TypeScript + C# changes
- Run `make build` (full rebuild)

## Migrating from Windows Builds

If you previously built on Windows, clean the NuGet cache before building in WSL:

```bash
dotnet nuget locals all --clear
rm -rf Extension/obj Extension.Tests/obj
dotnet restore -p:Configuration=Release --force-evaluate
```

Windows and WSL maintain **separate, incompatible NuGet caches**. Do not mix build environments. Mixing causes `NU1403: Package content hash validation failed` errors.

## Troubleshooting

### BackgroundWorker.js Missing TypeScript Modules (Two-Build Problem)

**Symptom**: First `dotnet build` after `make clean` succeeds but BackgroundWorker.js doesn't include signifyClient.js imports. Second build works.

**Cause**: BackgroundWorker.js generator runs before MSBuild's TypeScript build target. On clean build, JS files don't exist yet when generator scans.

**Solution**: Use `make build` which builds TypeScript before C# automatically. If running dotnet directly, build TypeScript first:
```bash
make build-ts
dotnet build --configuration Release -p:Quick=true
```

### TypeScript Files Not Rebuilding After Clean

**Symptom**: `tsc` completes instantly but no `.js` files generated.

**Cause**: `.tsbuildinfo` files remain after output deleted. TypeScript thinks everything is up-to-date.

**Solution**: `make clean` removes both output and tsbuildinfo files.

### NU1403: Package Content Hash Validation Failed

**Cause**: Mixed builds between Windows and WSL.

**Solution**: See "Migrating from Windows Builds" above.

### NuGet Restore Loop

**Cause**: npm build targets running during restore.

**Solution**:
1. Delete obj directories: `rm -rf Extension/obj Extension.Tests/obj`
2. Restore and build separately: `dotnet restore && dotnet build -p:FullBuild=true`

### Extension Won't Load

- Verify build output directory has manifest.json: `make verify`
- Check for JavaScript bundles in scripts/esbuild/
- Enable verbose build logging

### Changes Don't Appear

- Rebuild TypeScript if .ts files changed
- Hard reload extension in chrome://extensions (reload button)
- Browser caches extension files — refreshing popup is not sufficient

### Emergency Recovery

```bash
make clean-build
```

Or manually:
```bash
rm -rf Extension/bin Extension/obj Extension.Tests/obj
rm -rf scripts/types/dist scripts/modules/dist scripts/bundles/node_modules/.cache
cd scripts && npm run clean && cd ../Extension && npm run clean && cd ..
dotnet nuget locals all --clear
dotnet restore -p:Configuration=Release --force-evaluate
make install
make build
make test
```

## Debugging

- **Browser extension logs**: F12 console and extension service worker logs
- **Blazor WASM**: Enable detailed errors in `Program.cs`
- **signify-ts**: `console.log`/`console.debug` in TypeScript, visible in browser console
- **Advanced**: See [Blazor.BrowserExtension docs](https://mingyaulee.github.io/Blazor.BrowserExtension/running-and-debugging)

## GitHub Actions CI

The CI pipeline runs on `ubuntu-latest` and uses Makefile targets for common build steps. CI-specific steps (version stamping, artifact packaging) remain in the workflow YAML.

See [.github/workflows/dotnet.yml](.github/workflows/dotnet.yml).
