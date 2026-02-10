---
description: Perform a clean build of the extension using Windows (clean + full TypeScript and C# build)
---

# Clean Build (Windows)

Run the full clean build script. This cleans all artifacts, reinstalls npm dependencies, rebuilds TypeScript and C#, and runs tests.

```bash
cmd //c "c:\s\k\kbw\clean-build.cmd"
```

To skip npm reinstall (faster, use when dependencies haven't changed):

```bash
cmd //c "c:\s\k\kbw\clean-build.cmd --skip-install"
```

To skip tests:

```bash
cmd //c "c:\s\k\kbw\clean-build.cmd --skip-test"
```

## When to Use

- After changing `package.json` or `package-lock.json`
- After switching between Windows and WSL builds
- When incremental build (`/build-windows`) fails mysteriously
- After `dotnet clean` or manual artifact deletion

## Troubleshooting

If NU1403 (hash validation) errors persist after clean build, ensure Visual Studio and Windows Explorer are closed, then retry.
