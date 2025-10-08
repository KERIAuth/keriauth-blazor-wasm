---
description: Perform a clean build of the extension (clean + full TypeScript and C# build)
---

Perform a clean build of the KERI Auth browser extension:

1. Clean all build artifacts (dotnet clean + npm clean)
2. Run full build including TypeScript compilation (dotnet build -p:FullBuild=true)
3. Report the build status and output location

If the build succeeds, remind me that the extension is ready to test at:
`C:\s\k\kbw\Extension\bin\Debug\net9.0\browserextension\`
