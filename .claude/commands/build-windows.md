---
description: Incremental build of the extension using Windows (TypeScript + C# build without cleaning)
---

# Incremental Build (Windows)

Perform an incremental build of the KERI Auth browser extension using Windows toolchain. Use this for day-to-day development when you've made TypeScript or C# changes.

**When to use this**: After making code changes (TypeScript or C#) when dependencies haven't changed.

**When to use clean-build-windows instead**: After changing package.json, switching from WSL, or when builds fail mysteriously.

**Environment**: This command uses the Windows .NET SDK and npm. Do not mix with WSL builds.

---

## Build Steps

### 1. Build TypeScript

Build all TypeScript projects (types, modules, bundles) and app.ts:

```bash
pushd scripts
npm run build
popd

pushd Extension
npm run build:app
popd
