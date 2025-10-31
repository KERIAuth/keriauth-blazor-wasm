---
description: Perform a clean build of the extension (clean + full TypeScript and C# build)
---

# Clean Build

Perform a complete clean build of the KERI Auth browser extension:

1. Clean all build artifacts:
   - Run `dotnet clean` to remove C# build outputs
   - Run `cd Extension && npm run clean` to remove TypeScript outputs and caches

2. Restore dependencies:
   - Run `npm install` in the Extension directory to ensure all packages are current

3. Build TypeScript FIRST (critical for BackgroundWorker.js generation):
   - Run `npm run build` in the Extension directory
   - Verify compilation succeeds before proceeding

4. Build the C# project with quick mode (TypeScript already built):
   - Run `dotnet build -p:Quick=true` from project root
   - This skips redundant TypeScript compilation since step 3 already built it

5. Run tests to verify the build:
   - Run `dotnet test` from project root
   - Verify all tests pass before proceeding

6. Verify the build output:
   - Check that `Extension/bin/Debug/net9.0/browserextension/manifest.json` exists
   - Verify BackgroundWorker.js includes signifyClient and demo1 imports:

     ```bash
     grep "signifyClient" Extension/bin/Debug/net9.0/browserextension/content/BackgroundWorker.js
     ```

7. Report success with the extension path (use relative path from project root):
   - Extension ready to load: `Extension/bin/Debug/net9.0/browserextension/`

**Note**: This workflow avoids the "two-build problem" by building TypeScript before C#. See CLAUDE.md "Common Build Issues" section for details.

**If build fails**:

- On Windows: Close Visual Studio and Windows Explorer windows, then retry
- Check for file permission errors in the build output
- Verify Node.js >= 22.13.0 and .NET 9.0 SDK are installed
