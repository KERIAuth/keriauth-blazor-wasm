# Build Guide for KERI Auth Browser Extension

This guide ensures reliable builds across different development environments: Claude Code, VS Code, and Visual Studio Community.

## Quick Start

### For Most Developers (Recommended)
```bash
# Full clean build (TypeScript + C#)
dotnet build -p:FullBuild=true

# Quick rebuild (C# only, skips TypeScript)
dotnet build -p:Quick=true

# Production build
dotnet build -p:Production=true

# Clean all build outputs (including TypeScript dist/ and stale files)
dotnet clean
```

### For Extension Development with Live Reloading
```bash
# Terminal 1: Watch TypeScript changes
cd Extension && npm run watch

# Terminal 2: Watch C# changes
dotnet watch build -p:Quick=true
```

## Understanding the Build System

### Build Architecture

The extension has **two separate build systems** that must work together:

1. **TypeScript/JavaScript Build** (via npm/esbuild)
   - Compiles TypeScript to JavaScript
   - Bundles modules with dependencies
   - Output: `Extension/dist/wwwroot/scripts/`

2. **C# Build** (via dotnet)
   - Compiles Blazor WebAssembly
   - Copies JavaScript from `dist/` to final output
   - Output: `Extension/bin/Debug/net9.0/browserextension/`

### Build Flow Diagram

```
┌─────────────────────────────────────────────────────────┐
│ TypeScript Source Files                                 │
│ wwwroot/scripts/esbuild/*.ts                           │
└─────────────────┬───────────────────────────────────────┘
                  │
                  ▼
┌─────────────────────────────────────────────────────────┐
│ npm run build                                           │
│ ├── TypeScript compilation (tsc)                       │
│ ├── esbuild bundling (with signify-ts)                 │
│ └── Output: Extension/dist/wwwroot/scripts/            │
└─────────────────┬───────────────────────────────────────┘
                  │
                  ▼
┌─────────────────────────────────────────────────────────┐
│ dotnet build                                            │
│ ├── Compile C# Blazor WASM                             │
│ ├── Copy JS from dist/ to bin/                         │
│ ├── Package as browser extension                       │
│ └── Output: bin/Debug/net9.0/browserextension/        │
└─────────────────────────────────────────────────────────┘
```

## Build Commands Reference

### Canonical Build Commands (Work Everywhere)

| Command | What It Does | When To Use |
|---------|-------------|-------------|
| `dotnet build -p:FullBuild=true` | Complete build (npm + dotnet) | **Default choice** |
| `dotnet build -p:Quick=true` | C# only, skip TypeScript | When JS hasn't changed |
| `dotnet build -p:Production=true` | Release build, optimized | Before publishing |
| `dotnet clean && dotnet build -p:FullBuild=true` | Clean rebuild | After major changes |

### npm Commands (TypeScript Only)

Run these from `Extension/` directory:

```bash
# Install dependencies (first time or after package.json changes)
npm install

# Full TypeScript build
npm run build

# Watch mode (auto-rebuild on changes)
npm run watch

# Type checking only (no output)
npm run typecheck

# Lint TypeScript
npm run lint
npm run lint:fix
```

### dotnet Commands (C# + Packaging)

```bash
# Build with TypeScript (recommended)
dotnet build -p:FullBuild=true

# Build C# only
dotnet build

# Clean build artifacts
dotnet clean

# Run tests
dotnet test

# Restore NuGet packages
dotnet restore

# Format C# code
dotnet format
```

## Environment-Specific Instructions

### Claude Code (WSL/Linux)

Claude Code operates in WSL and handles builds automatically. No special configuration needed.

**If you see build failures:**
1. Close Visual Studio (Windows)
2. Close Windows Explorer if browsing project
3. Run: `dotnet clean && dotnet build -p:FullBuild=true`

### VS Code

#### Setup (One-Time)

1. **Install Recommended Extensions** (will prompt automatically):
   - C# DevKit
   - ESLint
   - Path IntelliSense
   - Code Spell Checker

2. **Configure Terminal** (already done in `.vscode/settings.json`):
   - Default terminal: Ubuntu (WSL) on Windows
   - Or bash on Linux

#### Build in VS Code

**Using Tasks (Keyboard Shortcuts):**
```
Ctrl+Shift+B  →  Shows build menu
Select: "dotnet build"
```

**Using Integrated Terminal:**
```bash
# Full build
dotnet build -p:FullBuild=true

# Watch mode (recommended for development)
cd Extension && npm run watch
```

**Build Tasks Available:**
- `dotnet build` - C# only build
- `npm build` - TypeScript only build
- `web dev` - Development server (not needed for extension)

#### Debugging in VS Code

1. Set breakpoints in C# or TypeScript
2. Press F5 or use Debug panel
3. Select "Launch Extension" configuration
4. Extension launches in new browser window

### Visual Studio Community

#### Setup (One-Time)

1. **Install Workloads**:
   - ASP.NET and web development
   - .NET desktop development

2. **Configure Build**:
   - Visual Studio can lock files, preventing WSL builds
   - Use Visual Studio **OR** WSL/Claude Code, not both simultaneously

#### Build in Visual Studio

**Before Building:**
1. Close VS Code if running
2. Ensure no WSL terminals are running `npm watch`

**Build Methods:**

1. **Solution Build** (Recommended):
   - Right-click solution → Build Solution
   - Uses MSBuild with proper targets

2. **Project Build**:
   - Right-click Extension project → Build
   - Faster but may skip TypeScript

3. **Rebuild Solution**:
   - Clean + Build
   - Use when switching between Release/Debug

**TypeScript Changes:**
- Visual Studio does NOT auto-rebuild TypeScript
- After changing .ts files:
  ```powershell
  # In Package Manager Console or Terminal
  cd Extension
  npm run build
  # Then rebuild solution
  ```

#### Debugging in Visual Studio

1. Set Configuration to "Debug"
2. Set Extension project as Startup Project
3. Press F5
4. Visual Studio will:
   - Build the project
   - Launch browser with extension
   - Attach debugger

**Note:** Blazor WASM debugging in browser DevTools works better than VS debugger for extension code.

## Common Issues and Solutions

### Issue: BackgroundWorker.js Missing TypeScript Modules (Two-Build Problem)

**Symptom:** After running `npm run clean`, the first `dotnet build` succeeds but BackgroundWorker.js doesn't include signifyClient.js or demo1.js imports. The second build includes them correctly.

**Root Cause:** The Blazor.BrowserExtension BackgroundWorker.js generator runs during `StaticWebAssetsPrepareForRun`, which executes BEFORE the `BuildExtensionScripts` MSBuild target. On a clean build, the generator scans for static assets before TypeScript compilation completes, so it doesn't find the JS files.

**Why Second Build Works:** The JS files from the first build persist (not cleaned by `dotnet clean`), so the second build's BackgroundWorker generator finds them.

**Solution Option 1: Recommended Workflow**
```bash
# Clean TypeScript outputs (only when needed for full rebuild)
cd Extension && npm run clean

# Build TypeScript FIRST
npm run build

# Then build C# (Quick mode since TypeScript already built)
cd .. && dotnet build -p:Quick=true
```

**Solution Option 2: Accept Two-Build After Deep Clean**
```bash
# After npm run clean, run build twice
dotnet build -p:FullBuild=true  # First build: creates JS files
dotnet build -p:FullBuild=true  # Second build: includes them in BackgroundWorker.js
```

**Solution Option 3: Don't Clean TypeScript Between Builds**
```bash
# Normal workflow (JS files persist across builds)
dotnet build -p:FullBuild=true  # Works correctly

# Deep clean only when absolutely necessary
cd Extension && npm run clean  # Then use Solution 1 or 2
```

**Note:** The csproj is configured to preserve `wwwroot/scripts/**/*.js` files during `dotnet clean` specifically to avoid this issue. Only use `npm run clean` when you need to regenerate TypeScript outputs from scratch.

**Verification:**
```bash
# Check if BackgroundWorker.js includes your modules
grep -c "signifyClient" Extension/bin/Debug/net9.0/browserextension/content/BackgroundWorker.js
# Should return 2 (one import, one in allImports array)
```

### Issue: "Package Blazor.BrowserExtension.Build not found"

**Cause:** NuGet cache corruption or WSL/Windows path mismatch

**Solution:**
```bash
# Close Visual Studio first!
# Restore packages first (dotnet clean requires packages)
dotnet restore --force-evaluate
dotnet clean
dotnet build -p:FullBuild=true
```

### Issue: "SignifyClient not connected" at Runtime

**Cause:** TypeScript changes not compiled or extension not reloaded

**Solution:**
```bash
# 1. Rebuild TypeScript
cd Extension && npm run build

# 2. Rebuild extension package
cd .. && dotnet build -p:Quick=true

# 3. Reload extension in browser
# Go to chrome://extensions → Click reload button
```

### Issue: npm Build Fails with "Permission Denied"

**Cause:** Windows process (Visual Studio/Explorer) has files locked

**Solution:**
```bash
# 1. Close Visual Studio
# 2. Close Windows Explorer windows showing project
# 3. Try build again
cd Extension && npm run build
```

### Issue: Changes Not Appearing in Extension

**Root Cause:** Browser is caching old extension code

**Solution:**
```bash
# 1. Rebuild
dotnet build -p:FullBuild=true

# 2. Hard reload extension
# Chrome: chrome://extensions → Reload button (NOT just refresh)
# Or: Remove and reload extension
```

### Issue: Build Works in VS Code but Not Visual Studio

**Cause:** Different build targets triggered

**Solution in Visual Studio:**
```powershell
# Use Developer PowerShell or Package Manager Console
dotnet build -p:FullBuild=true
```

Then use "Rebuild Solution" (not just "Build").

### Issue: "NuGet restore loop detected"

**Cause:** Build targets running during restore operation

**Solution:**
```bash
# 1. Close Visual Studio
# 2. Delete obj directories
rm -rf Extension/obj Extension.Tests/obj

# 3. Restore and build separately
dotnet restore
dotnet build -p:FullBuild=true
```

## Advanced Build Scenarios

### CI/CD Build (GitHub Actions)

```bash
cd Extension
npm ci  # Clean install (faster, reproducible)
npm run build:prod
cd ..
dotnet restore
dotnet build --configuration Release -p:FullBuild=true
dotnet test --no-build
```

### Cross-Platform Considerations

#### Windows (PowerShell/CMD)
```powershell
# Use PowerShell or CMD
dotnet build -p:FullBuild=true
```

#### WSL/Linux
```bash
# Standard Unix commands work
dotnet build -p:FullBuild=true
```

#### macOS
```bash
# Same as Linux
dotnet build -p:FullBuild=true
```

### Development Workflow Recommendations

**For TypeScript-Heavy Work:**
```bash
# Terminal 1: Auto-rebuild TypeScript
cd Extension && npm run watch

# Terminal 2: Manual C# builds as needed
dotnet build -p:Quick=true

# After changes: Reload extension in browser
```

**For C#-Heavy Work:**
```bash
# Terminal 1: Watch C# changes
dotnet watch build -p:Quick=true

# Occasionally rebuild TypeScript if needed
cd Extension && npm run build && cd ..
```

**For Full-Stack Work:**
```bash
# Build both on every change
dotnet build -p:FullBuild=true

# Or use watch mode for TypeScript only
cd Extension && npm run watch
# And rebuild C# manually
```

## Build System Architecture (For Maintainers)

### Key Files

1. **Extension.csproj** - MSBuild targets and properties
   - `BuildExtensionScripts` target runs `npm run build`
   - `CopyEsBuildJavascript` target copies from dist/
   - Conditional builds based on properties

2. **package.json** - npm scripts for TypeScript
   - `build` - Full TypeScript compilation
   - `build:es6` - TypeScript to ES6 modules
   - `bundle:esbuild` - Bundle with dependencies

3. **esbuild.config.js** - JavaScript bundler configuration
   - Bundles signify-ts dependencies
   - Platform-specific polyfills
   - Source maps for debugging

4. **tsconfig.json** - TypeScript compiler options
   - ES6 module output
   - Strict type checking
   - Declaration files

### Build Flags

| Flag | Effect | Set By |
|------|--------|--------|
| `BuildingProject=true` | Enable TypeScript build | `-p:FullBuild=true` |
| `SkipJavaScriptBuild=true` | Skip TypeScript | `-p:Quick=true` |
| `Configuration=Release` | Release mode | `-p:Production=true` |
| `DesignTimeBuild=true` | Skip npm (IDE intellisense) | Visual Studio |

### Output Directories

```
Extension/
├── dist/                          # Intermediate TypeScript output
│   └── wwwroot/scripts/
│       ├── es6/                   # TypeScript → ES6 modules
│       └── esbuild/               # Bundled with dependencies
├── bin/
│   └── Debug/net9.0/
│       ├── wwwroot/               # Blazor WASM output
│       └── browserextension/      # Final extension package ✓
│           ├── manifest.json
│           ├── _framework/        # Blazor runtime
│           └── scripts/           # Copied from dist/
└── node_modules/                  # npm dependencies
```

## Testing After Build

### Manual Testing Checklist

1. **Load Extension:**
   ```
   chrome://extensions
   → Enable Developer Mode
   → Load Unpacked
   → Select: Extension/bin/Debug/net9.0/browserextension/
   ```

2. **Verify Build:**
   - Check manifest.json has timestamp
   - Open extension popup (should load UI)
   - Check browser console for errors
   - Open service worker DevTools

3. **Test Functionality:**
   - Create/unlock identifier
   - Sign request from web page
   - Check service worker logs

### Automated Testing

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test class
dotnet test --filter "ClassName=SignifyServiceTests"

# Watch mode (auto-run on changes)
dotnet watch test
```

## Getting Help

### Check Build Logs

**npm build:**
```bash
cd Extension
npm run build
# Look for TypeScript errors or esbuild failures
```

**dotnet build:**
```bash
dotnet build -p:FullBuild=true -v:detailed
# Verbose output shows all targets
```

### Check File Timestamps

```bash
# Verify TypeScript output is fresh
ls -lrt Extension/dist/wwwroot/scripts/esbuild/

# Verify final output is fresh
ls -lrt Extension/bin/Debug/net9.0/browserextension/scripts/esbuild/
```

### Common Verification Commands

```bash
# Is npm installed?
npm --version  # Should be >= 11.0.0

# Is .NET SDK installed?
dotnet --version  # Should be 9.0.x

# Are dependencies installed?
cd Extension && npm list --depth=0

# Are NuGet packages restored?
dotnet list package
```

## Additional Resources

- [CLAUDE.md](./CLAUDE.md) - Full architecture documentation
- [Extension.csproj](./Extension/Extension.csproj) - Build targets
- [package.json](./Extension/package.json) - npm scripts
- [esbuild.config.js](./Extension/esbuild.config.js) - Bundler config
- [Blazor.BrowserExtension Docs](https://mingyaulee.github.io/Blazor.BrowserExtension/)
