---
description: Incremental build of the extension using Windows (TypeScript + C# build without cleaning)
---

# Incremental Build (Windows)

Run the incremental build script. This builds TypeScript (types, modules, bundles, app.ts) then C# in Release configuration.

```bash
cmd //c "c:\s\k\kbw\build.cmd"
```

To skip TypeScript (C# only changes):

```bash
cmd //c "c:\s\k\kbw\build.cmd --skip-ts"
```

To also run tests after building:

```bash
cmd //c "c:\s\k\kbw\build.cmd --test"
```

If the build fails, try the clean build: `/clean-build-windows`
