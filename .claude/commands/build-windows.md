---
description: Incremental build of the extension using Windows (TypeScript + C# build without cleaning)
---

# Incremental Build (Windows)

Perform an incremental build of the KERI Auth browser extension using Windows toolchain. Use this for day-to-day development when you've made TypeScript or C# changes.

**When to use this**: After making code changes (TypeScript or C#) when dependencies haven't changed.

**When to use clean-build-windows instead**: After changing package.json, switching from WSL, or when builds fail mysteriously.

**Environment**: This command uses the Windows .NET SDK and npm. Do not mix with WSL builds.

## Build Steps

### 1. Build TypeScript

Build all TypeScript projects (types, modules, bundles) and app.ts:

```bash
cd scripts && npm run build
cd ../Extension && npm run build:app
cd ..
```

### 2. Verify TypeScript Build Output

Check that key JavaScript files were generated:

```bash
ls Extension/wwwroot/scripts/esbuild/signifyClient.js
ls Extension/wwwroot/scripts/es6/index.js
ls Extension/wwwroot/app.js
```

If any files are missing, check for TypeScript errors in the build output.

### 3. Build the C# Project

Use Quick mode since TypeScript was already built:

```bash
dotnet build --configuration Release -p:Quick=true
```

### 4. Verify Build Output

Check that the extension package is complete:

```bash
ls Extension/bin/Release/net9.0/browserextension/manifest.json
```

### 5. Report Success

Extension ready to load: `Extension/bin/Release/net9.0/browserextension/`

Remember to reload the extension in chrome://extensions after building.

## Optional: Run Tests

```bash
dotnet test --configuration Release
```

## Troubleshooting

If the incremental build fails or produces unexpected results:

1. **TypeScript changes not appearing**: The tsbuildinfo cache may be stale
   ```bash
   cd scripts && npm run clean && npm run build
   ```

2. **C# build errors**: Try restoring NuGet packages
   ```bash
   dotnet restore -p:Configuration=Release
   dotnet build --configuration Release -p:Quick=true
   ```

3. **Persistent issues**: Fall back to `/clean-build-windows` for a full clean build
