# Build Instructions

For architecture overview, see [ARCHITECTURE.md](ARCHITECTURE.md). For coding standards, see [CODING.md](CODING.md).

> **Future direction**: The intent is to move all builds to WSL (Ubuntu), eliminating Windows-specific builds. Some related repositories maintained by others require Linux builds.

## Prerequisites

- Node.js >= 22.13.0
- npm >= 11.0.0
- .NET 9.0 SDK
- Chromium-based browser version 127+ (Chrome, Edge, or Brave)

Check versions:
```bash
node --version
npm --version
dotnet --version
```

## Build Configuration

This project uses **Release configuration only**. Debug configuration is intentionally disabled — browser extension debugging happens through Chrome DevTools, not Visual Studio debuggers.

## Quick Reference

### Batch Scripts (Windows)

| Script | Purpose |
|--------|---------|
| `build.cmd` | Incremental build (TypeScript + C#) |
| `build.cmd --skip-ts` | C#-only build (skip TypeScript) |
| `build.cmd --test` | Build + run tests |
| `build.cmd --skip-ts --test` | C#-only build + tests |
| `clean-build.cmd` | Full clean build |
| `clean-build.cmd --skip-install` | Clean build, skip npm install |
| `clean-build.cmd --skip-test` | Clean build, skip tests |

### Claude Code Bash Shell

Use `cmd //c` (double slash) — bash interprets single `/c` as a path:

```bash
cmd //c "c:\s\k\kbw\build.cmd --test"          # Incremental build + test
cmd //c "c:\s\k\kbw\build.cmd --skip-ts --test" # C#-only + test
cmd //c "c:\s\k\kbw\clean-build.cmd"            # Clean build
```

### Slash Commands (Claude Code)

- `/build-windows` — incremental build
- `/clean-build-windows` — full clean build

### What NOT to Run Directly

These will fail or produce incomplete results:
- `npm run build`
- `dotnet build`
- `pushd scripts && npm run build && popd`

Always use the batch scripts or slash commands.

## First Time Setup

```bash
# 1. Install npm dependencies (required once after clone)
cd scripts
npm install

cd ../Extension
npm install
cd ..

# 2. Build TypeScript
cd scripts
npm run build

cd ../Extension
npm run build:app
cd ..

# 3. Build and test C#
dotnet build --configuration Release -p:Quick=true
dotnet test --configuration Release
```

The `npm install` steps are only needed:
- After first clone
- When package.json or package-lock.json files change
- After running `npm clean` or deleting node_modules/

## Build System Architecture

The extension requires **two independent build systems** that must run in order:

```
TypeScript sources -> npm build -> JS in wwwroot/ -> dotnet build -> Extension package
```

### Why This Order Matters

The Blazor.BrowserExtension `BackgroundWorker.js` generator runs during MSBuild's `StaticWebAssetsPrepareForRun` phase, which scans for static assets. JavaScript files must already exist in `wwwroot/scripts/` before the C# build starts, or they won't be included in the generated BackgroundWorker.js.

### Build Flags

| Flag | Effect | Shortcut |
|------|--------|----------|
| `BuildingProject=true` | Enable TypeScript build | `-p:FullBuild=true` |
| `SkipJavaScriptBuild=true` | Skip TypeScript | `-p:Quick=true` |
| `Configuration=Release` | Release mode | `-p:Production=true` |
| `DesignTimeBuild=true` | Skip npm (IDE IntelliSense) | Set by Visual Studio |

### Key Build Files

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
- Use watch mode (`npm run watch` in Extension/) or manual rebuild
- Always reload extension in browser after changes

### C#-only changes
- Build with `--skip-ts` flag for faster iteration
- Always reload extension in browser after changes

### Mixed TypeScript + C# changes
- Full rebuild required (both build systems)

## Build Environments

### Supported Environments

| Environment | Status | Notes |
|-------------|--------|-------|
| Windows PowerShell | Supported | Primary development environment (transitioning to WSL) |
| Windows Git Bash | Supported | Works with Windows dotnet CLI |
| VS Code | Supported | Uses Windows dotnet CLI |
| Visual Studio | Supported | Uses Windows NuGet cache |
| WSL (Ubuntu) | Supported | Uses separate cache — don't mix with Windows |
| GitHub Actions (Ubuntu) | CI/CD | Isolated Linux environment |

### Windows / WSL Cache Incompatibility

Windows and WSL maintain **separate, incompatible NuGet caches**:
- Windows: `C:\Users\<user>\.nuget\packages`
- WSL: `/home/<user>/.nuget/packages`

**Rule**: Pick either Windows OR WSL for all local builds on a given machine. Don't mix environments. Mixing causes `NU1403: Package content hash validation failed` errors.

### Switching Between Environments

If you must switch, clean caches first:

```powershell
# Windows PowerShell
dotnet nuget locals all --clear
Remove-Item -Recurse -Force Extension\obj, Extension.Tests\obj
dotnet restore -p:Configuration=Release --force-evaluate
```

```bash
# WSL/Linux bash
dotnet nuget locals all --clear
rm -rf Extension/obj Extension.Tests/obj
dotnet restore -p:Configuration=Release --force-evaluate
```

## Troubleshooting

### BackgroundWorker.js Missing TypeScript Modules (Two-Build Problem)

**Symptom**: First `dotnet build` after `npm run clean` succeeds but BackgroundWorker.js doesn't include signifyClient.js imports. Second build works.

**Cause**: BackgroundWorker.js generator runs before MSBuild's TypeScript build target. On clean build, JS files don't exist yet when generator scans.

**Solutions**:
1. **Recommended**: Build TypeScript first, then C#:
   ```bash
   cd scripts && npm run build
   cd .. && dotnet build -p:Quick=true
   ```
2. Run `dotnet build` twice after deep clean
3. Avoid `npm run clean` unless necessary (JS files persist across `dotnet clean`)

### TypeScript Files Not Rebuilding After Clean

**Symptom**: `tsc` completes instantly but no `.js` files generated.

**Cause**: `.tsbuildinfo` files remain after output deleted. TypeScript thinks everything is up-to-date.

**Solution**: `npm run clean` from scripts workspace root removes both output and tsbuildinfo files.

### NU1403: Package Content Hash Validation Failed

**Cause**: Mixed builds between Windows and WSL.

**Solution**: Pick one environment, clean caches (see "Switching Between Environments" above).

### NuGet Restore Loop

**Cause**: npm build targets running during restore.

**Solution**:
1. Close Visual Studio
2. Delete obj directories: `rm -rf Extension/obj Extension.Tests/obj`
3. Restore and build separately: `dotnet restore && dotnet build -p:FullBuild=true`

### npm Build "Permission Denied"

**Cause**: Windows process (Visual Studio/Explorer) has files locked.

**Solution**: Close Visual Studio and Windows Explorer, then retry.

### Extension Won't Load

- Verify build output directory has manifest.json
- Check for JavaScript bundles in scripts/esbuild/
- Enable verbose build logging

### Changes Don't Appear

- Rebuild TypeScript if .ts files changed
- Hard reload extension in chrome://extensions (reload button)
- Browser caches extension files — refreshing popup is not sufficient

### Emergency Recovery

```bash
# Full cleanup and rebuild (WSL/Linux)
rm -rf Extension/bin Extension/obj Extension.Tests/obj
rm -rf scripts/types/dist scripts/modules/dist scripts/bundles/node_modules/.cache
cd scripts && npm run clean && cd ../Extension && npm run clean && cd ..
dotnet nuget locals all --clear
dotnet restore -p:Configuration=Release --force-evaluate
cd scripts && npm run build && cd ..
dotnet build --configuration Release -p:Quick=true
```

```powershell
# Full cleanup and rebuild (Windows PowerShell)
Remove-Item -Recurse -Force Extension\bin, Extension\obj, Extension.Tests\obj -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force scripts\types\dist, scripts\modules\dist, scripts\bundles\node_modules\.cache -ErrorAction SilentlyContinue
cd scripts; npm run clean; cd ..\Extension; npm run clean; cd ..
dotnet nuget locals all --clear
dotnet restore -p:Configuration=Release --force-evaluate
cd scripts; npm run build; cd ..
dotnet build --configuration Release -p:Quick=true
```

## Debugging

- **Browser extension logs**: F12 console and extension service worker logs
- **Blazor WASM**: Enable detailed errors in `Program.cs`
- **signify-ts**: `console.log`/`console.debug` in TypeScript, visible in browser console
- **Advanced**: See [Blazor.BrowserExtension docs](https://mingyaulee.github.io/Blazor.BrowserExtension/running-and-debugging)

## GitHub Actions CI

The CI pipeline runs on `ubuntu-latest`:
1. Installs Node.js and npm dependencies
2. Builds TypeScript
3. Restores and builds C# (Release configuration)
4. Runs tests
5. Uploads build artifacts

See [.github/workflows/dotnet.yml](.github/workflows/dotnet.yml).
