---
description: Perform a clean build of the extension (clean + full TypeScript and C# build)
---

Perform a clean build of the KERI Auth browser extension:

1. Clean all build artifacts (dotnet clean + npm clean)
2. Run `npm install` to install all dependencies
2. Build the TypeScript code (npm run build)
2. Run dotnet build with quick build (dotnet build -p:Quick=true)

If the build succeeds, remind me that the extension is ready to test at:
`C:\s\k\kbw\Extension\bin\Debug\net9.0\browserextension\`
