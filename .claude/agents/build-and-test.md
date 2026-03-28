---
name: build-and-test
description: Build the project and run tests after implementation changes. Use this after completing each implementation step.
tools: Bash, Read, Grep, Glob
model: haiku
maxTurns: 10
permissionMode: dontAsk
---

Build the project and run tests. Report concisely.

1. Run `make build` from the project root.
2. If build succeeds, run `make test`.
3. Report:
   - Build: pass/fail (if fail, show only the first error)
   - Tests: pass/fail counts (if failures, show only failing test names and error messages)
4. If build fails, do NOT run tests. Focus on diagnosing the build error.
