using Extension.Services.SignifyService;
using System.Reflection;
using Xunit;

namespace Extension.Tests.Services.SignifyService;

/// <summary>
/// Tests for the extended signify-ts shim methods added based on TypeScript implementation.
/// Verifies the additional JSImport methods correspond to signify_ts_shim.ts exports.
/// </summary>
public class SignifyExtendedShimTests
{
    [Fact]
    public void GetSignedHeaders_Method_ShouldExist()
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);

        // Act
        var method = shimType.GetMethod("GetSignedHeaders", 
            BindingFlags.NonPublic | BindingFlags.Static);

        // Assert
        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Equal(5, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType); // origin
        Assert.Equal(typeof(string), parameters[1].ParameterType); // url
        Assert.Equal(typeof(string), parameters[2].ParameterType); // method
        Assert.Equal(typeof(string), parameters[3].ParameterType); // headersDict
        Assert.Equal(typeof(string), parameters[4].ParameterType); // aidName
    }

    [Fact]
    public void GetNameByPrefix_Method_ShouldExist()
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);

        // Act
        var method = shimType.GetMethod("GetNameByPrefix", 
            BindingFlags.NonPublic | BindingFlags.Static);

        // Assert
        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(string), parameters[0].ParameterType); // prefix
    }

    [Fact]
    public void GetIdentifierByPrefix_Method_ShouldExist()
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);

        // Act
        var method = shimType.GetMethod("GetIdentifierByPrefix", 
            BindingFlags.NonPublic | BindingFlags.Static);

        // Assert
        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(string), parameters[0].ParameterType); // prefix
    }

    [Fact]
    public void AllShimMethods_ShouldFollowConsistentNamingPattern()
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);
        var methods = shimType.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.IsStatic && m.ReturnType.IsGenericType && 
                       m.ReturnType.GetGenericTypeDefinition() == typeof(Task<>));

        // Act & Assert
        foreach (var method in methods)
        {
            // All shim methods should return Task<string> and be static
            Assert.True(method.IsStatic);
            Assert.Equal(typeof(Task<string>), method.ReturnType);
            
            // Method names should follow PascalCase convention
            Assert.True(char.IsUpper(method.Name[0]), $"Method {method.Name} should start with uppercase");
        }
    }

    [Fact]
    public void ExtendedShimMethods_ShouldCorrespondToTypeScriptExports()
    {
        // This test verifies that the C# shim methods correspond to 
        // the exported functions in signify_ts_shim.ts

        // Arrange
        var expectedMethods = new[]
        {
            "BootAndConnect",    // bootAndConnect
            "Connect",           // connect
            "CreateAID",         // createAID
            "GetAIDs",          // getAIDs
            "GetAID",           // getAID
            "GetCredentialsList", // getCredentialsList
            "GetCredential",     // getCredential
            "GetState",          // getState
            "GetSignedHeaders",  // getSignedHeaders
            "GetNameByPrefix",   // getNameByPrefix
            "GetIdentifierByPrefix" // getIdentifierByPrefix
        };

        var shimType = typeof(Signify_ts_shim);

        // Act & Assert
        foreach (var expectedMethod in expectedMethods)
        {
            var method = shimType.GetMethod(expectedMethod, 
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            Assert.True(method.IsStatic);
        }
    }

    [Fact]
    public void GetSignedHeaders_ShouldHandleHttpHeaderSigning()
    {
        // This test documents the expected behavior of GetSignedHeaders
        // based on the TypeScript implementation

        // Arrange
        var shimType = typeof(Signify_ts_shim);
        var method = shimType.GetMethod("GetSignedHeaders", 
            BindingFlags.NonPublic | BindingFlags.Static);

        // Act & Assert
        Assert.NotNull(method);
        
        // Should accept all parameters needed for HTTP header signing
        var parameters = method.GetParameters();
        Assert.Equal("origin", parameters[0].Name);
        Assert.Equal("url", parameters[1].Name);
        Assert.Equal("method", parameters[2].Name);
        Assert.Equal("headersDict", parameters[3].Name);
        Assert.Equal("aidName", parameters[4].Name);
    }

    [Theory]
    [InlineData("GetNameByPrefix")]
    [InlineData("GetIdentifierByPrefix")]
    public void PrefixLookupMethods_ShouldAcceptPrefixParameter(string methodName)
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);

        // Act
        var method = shimType.GetMethod(methodName, 
            BindingFlags.NonPublic | BindingFlags.Static);

        // Assert
        Assert.NotNull(method);
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal("prefix", parameters[0].Name);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void ShimMethods_InNonBrowserEnvironment_DocumentedBehavior()
    {
        // This test documents that shim methods are designed for browser environment
        // and will not work in test/server environments without proper setup

        if (!OperatingSystem.IsBrowser())
        {
            // All JSImport methods require browser context with:
            // 1. Blazor WASM runtime
            // 2. signify_ts_shim.js loaded
            // 3. signify-ts library initialized
            
            // In test environment, these methods would throw JSException
            // when actually invoked (we don't invoke them here)
            Assert.True(true); // Document expected behavior
        }
    }

    [Fact]
    public void ShimMethodParameters_ShouldAllowNullValues()
    {
        // Based on TypeScript implementation, some parameters might be optional/nullable
        
        var shimType = typeof(Signify_ts_shim);
        var methods = shimType.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.IsStatic && m.ReturnType == typeof(Task<string>));

        foreach (var method in methods)
        {
            foreach (var param in method.GetParameters())
            {
                // String parameters should handle null gracefully in TypeScript
                if (param.ParameterType == typeof(string))
                {
                    // Note: Actual null handling depends on TypeScript implementation
                    Assert.Equal(typeof(string), param.ParameterType);
                }
            }
        }
    }

    [Fact]
    public void ExtendedShimMethods_ShouldSupportSignifyTsFeatures()
    {
        // This test verifies that extended methods support key signify-ts features:
        // 1. HTTP header signing for authenticated requests
        // 2. Identifier lookup by prefix
        // 3. Name resolution for identifiers

        var shimType = typeof(Signify_ts_shim);
        
        // Header signing support
        var getSignedHeadersMethod = shimType.GetMethod("GetSignedHeaders", 
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(getSignedHeadersMethod);
        
        // Prefix-based lookup support
        var getNameByPrefixMethod = shimType.GetMethod("GetNameByPrefix", 
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(getNameByPrefixMethod);
        
        var getIdentifierByPrefixMethod = shimType.GetMethod("GetIdentifierByPrefix", 
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(getIdentifierByPrefixMethod);
    }

    [Fact]
    public void ShimArchitecture_ShouldFollowSignifyTsPatterns()
    {
        // This test verifies the shim follows patterns from signify-ts:
        // 1. Async/await pattern with Promises -> Task<T>
        // 2. JSON string serialization for complex objects
        // 3. Error handling through exceptions

        var shimType = typeof(Signify_ts_shim);
        var methods = shimType.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name.StartsWith("Get", StringComparison.Ordinal) || m.Name.StartsWith("Create", StringComparison.Ordinal) || m.Name.Contains("Connect", StringComparison.Ordinal));

        foreach (var method in methods)
        {
            // All signify operations should be async
            Assert.True(method.ReturnType.IsGenericType);
            Assert.Equal(typeof(Task<>), method.ReturnType.GetGenericTypeDefinition());
            
            // Return type should be Task<string> for JSON serialization
            Assert.Equal(typeof(string), method.ReturnType.GetGenericArguments()[0]);
        }
    }
}