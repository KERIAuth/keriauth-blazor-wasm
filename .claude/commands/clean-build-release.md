---
description: Perform a clean RELEASE build of the extension (production-optimized build)
---

# Clean Release Build

Perform a complete clean RELEASE build of the KERI Auth browser extension (production-optimized):

1. Clean all build artifacts:
   - Run `dotnet clean --configuration Release` to remove C# release build outputs
   - Run `cd Extension && npm run clean` to remove TypeScript outputs and caches

2. Restore dependencies:
   - Run `npm install` in the Extension directory to ensure all packages are current

3. Build TypeScript FIRST (critical for BackgroundWorker.js generation):
   - Run `npm run build` in the Extension directory
   - Verify compilation succeeds before proceeding

4. Build the C# project in Release mode (TypeScript already built):
   - Run `dotnet build --configuration Release -p:Quick=true` from project root
   - This creates an optimized production build with Quick flag to skip redundant TypeScript compilation

5. Run tests to verify the build:
   - Run `dotnet test --configuration Release` from project root
   - Verify all tests pass before proceeding

6. Verify the build output:
   - Check that `Extension/bin/Release/net9.0/browserextension/manifest.json` exists
   - Verify BackgroundWorker.js includes signifyClient and demo1 imports:

     ```bash
     grep "signifyClient" Extension/bin/Release/net9.0/browserextension/content/BackgroundWorker.js
     ```

7. Report success with the extension path (use relative path from project root):
   - Extension ready to load: `Extension/bin/Release/net9.0/browserextension/`
   - This is the production-optimized build ready for distribution

**Note**: This workflow avoids the "two-build problem" by building TypeScript before C#. See CLAUDE.md "Common Build Issues" section for details.

**Release Build Differences:**
- Optimized and minified code
- Production configuration settings
- No debug symbols (smaller package size)
- Ready for Chrome Web Store submission

**If build fails**:

- On Windows: Close Visual Studio and Windows Explorer windows, then retry
- Check for file permission errors in the build output
- Verify Node.js >= 22.13.0 and .NET 9.0 SDK are installed
