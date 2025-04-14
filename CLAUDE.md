# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build/Run Commands
- Build: `dotnet build`
- Build with frontend: `npm install && npm run build && dotnet build`
- Test: `dotnet test`
- Run single test: `dotnet test --filter "FullyQualifiedName=KeriAuth.BrowserExtension.Tests.Helper.UrlBuilderTests.CreateUrlWithEncodedQueryStrings_EncodesQueryParamsCorrectly"`
- Frontend build: `npm run build`
- ES6 TypeScript build: `npm run build:es6`
- Bundle with esbuild: `npm run bundle:esbuild`

## Code Style Guidelines
- .NET 8.0.404 SDK required (see global.json)
- 4-space indentation
- Opening braces on same line
- PascalCase for classes, interfaces (with 'I' prefix), public methods and properties
- Private fields: camelCase with underscore prefix (_fieldName)
- Constants: use readonly fields or const values (prefer const for immutable values)
- Nullable reference types enabled - use explicit null checks
- Use expression-bodied members for simple methods
- Try/catch with specific exception types
- Organize imports at top of file, outside namespace declarations
- Test pattern: Arrange-Act-Assert with descriptive test names
- EnforceCodeStyleInBuild enabled in project files
- Async methods suffixed with "Async" returning Task/Task<T>
- XML comments on public APIs