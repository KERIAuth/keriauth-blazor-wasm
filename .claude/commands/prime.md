# CLAUDE.md

<!-- 
## Architecture Overview
-->

## Typescript Coding Guidelines

- No guidelines yet

## C# Coding Guidelines

### Build & Test Commands

<!-- TODO add linters and formatting ? -->
- Build project: `dotnet build`

<!-- - Build main project (startup project core application) `dotnet build Dave.Agents/Dave.Agents.ImprintFetcher.csproj`
-->

### Code Style Guidelines

- **Naming**: Use PascalCase for classes, methods, properties, enums; camelCase with underscore prefix for private fields
- **Imports**: Order by System namespaces first, then project namespaces; sort alphabetically within groups
- **Types**: Use explicit types; prefer `var` only when type is obvious or for complex LINQ expressions
- **Error Handling**: Use FluentResults pattern with `Result<T>` for method returns; include descriptive error messages. Don't use exceptions for control flow, only if absolutely necessary. For example, if a file is not found, return a Result with an error message instead of throwing an exception. Use Result.Ok() for successful operations and Result.Fail("some message") for errors. Try to avoid explicit types for `Result<T>` unless necessary e.g. avoid `return Result.Ok<string>(someMessage)` and just `return someMessage` or the implicit `return Result.Ok(someMessage)`. In the end this means all method that can fail in any way should return a `Result<T>`.
<!--
- **Organization**: Follow CQRS pattern with Commands/Handlers; In the main project there should be a "Commands" folder. Inside that, there should be folders for each command e.g. "CreateUser". Inside that there should be two files " CreateUserRequest.cs" and "CreateUserHandler.cs". The request file should contain the request class and the handler file should contain the handler class. The handler class should implement the IRequestHandler interface from MediatR. The request class should implement the IRequest interface from MediatR. The request class should also contain a constructor that takes in all the required parameters for the command. The handler class should contain a constructor that takes in all the required dependencies for the command. The handler should implement IRequestHandler.
-->
- **Async**: Use async/await consistently; avoid blocking calls and sync-over-async patterns
- **Comments**: Use XML docs for public APIs; inline comments for implementation details; avoid regions
- **Brackets**: Use braces for all control structures, even single-line statements. Do not use IF-statements without braces. For example, do not use "if (condition) DoSomething();" instead use "if (condition) { DoSomething(); }".
- **Nullability**: Use nullable reference types; default to non-nullable; use `?` for nullable types
- **Constructors**: Use constructors for setting properties; Avoid using public setters for properties. For example, do not use "public string Name { get; set; }" instead use "public string Name { get; }" and set the value in the constructor.
- **Dates and Locale**: Use UTC for all date/time values in the backend.

### Common libraries

- **FluentResults**: Use for error handling and result management
<!-- - **MediatR**: Use for CQRS pattern implementation.

- **Sentry**: Use for error tracking and monitoring
- **Moq**: Use for unit testing; create mocks in test classes
- **Humanizer**: Use for string manipulation and formatting 
-->

### Application Setup

<!-- - **Program.cs**: Do NOT use the new minimal hosting model; configure services and middleware in a single `Program.cs` file
-->
- **Dependency Injection**: Use built-in DI container; register services in `Program.cs` using `AddScoped`, `AddTransient`, or `AddSingleton` as appropriate
- **Configuration**: Use `IConfiguration` for app settings; bind to strongly-typed classes for complex settings. Create a `AppSettings.cs` and bind all common settings to that class. Avoid hard-coded values in the codebase. Use `IOptions<AppSettings>` to access the settings in the codebase.
<!-- - **Logging**: Use built-in logging framework; configure logging in `Program.cs`; Log everything to the console in development. Only log warning and errors in production
-->
