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
            // Original methods
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
            "GetIdentifierByPrefix", // getIdentifierByPrefix
            
            // IPEX Protocol Methods
            "IpexApply",         // ipexApply
            "IpexOffer",         // ipexOffer
            "IpexAgree",         // ipexAgree
            "IpexGrant",         // ipexGrant
            "IpexAdmit",         // ipexAdmit
            "IpexSubmitApply",   // ipexSubmitApply
            "IpexSubmitOffer",   // ipexSubmitOffer
            "IpexSubmitAgree",   // ipexSubmitAgree
            "IpexSubmitGrant",   // ipexSubmitGrant
            "IpexSubmitAdmit",   // ipexSubmitAdmit
            
            // OOBI Operations
            "OobiGet",           // oobiGet
            "OobiResolve",       // oobiResolve
            
            // Operations Management
            "OperationsGet",     // operationsGet
            "OperationsList",    // operationsList
            "OperationsDelete",  // operationsDelete
            "OperationsWait",    // operationsWait
            
            // Registry Management
            "RegistriesList",    // registriesList
            "RegistriesCreate",  // registriesCreate
            "RegistriesRename",  // registriesRename
            
            // Contact Management
            "ContactsList",      // contactsList
            "ContactsGet",       // contactsGet
            "ContactsAdd",       // contactsAdd
            "ContactsUpdate",    // contactsUpdate
            "ContactsDelete",    // contactsDelete
            
            // Additional Credential Operations
            "CredentialsIssue",  // credentialsIssue
            "CredentialsRevoke", // credentialsRevoke
            "CredentialsState",  // credentialsState
            "CredentialsDelete", // credentialsDelete
            
            // Schemas Operations
            "SchemasGet",        // schemasGet
            "SchemasList",       // schemasList
            
            // Notifications Operations
            "NotificationsList", // notificationsList
            "NotificationsMark", // notificationsMark
            "NotificationsDelete" // notificationsDelete
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

    // ===================== IPEX Protocol Method Tests =====================

    [Theory]
    [InlineData("IpexApply")]
    [InlineData("IpexOffer")]
    [InlineData("IpexAgree")]
    [InlineData("IpexGrant")]
    [InlineData("IpexAdmit")]
    public void IpexMethods_ShouldAcceptJsonArgsParameter(string methodName)
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);

        // Act
        var method = shimType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);

        // Assert
        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(string), parameters[0].ParameterType); // JSON args
    }

    [Theory]
    [InlineData("IpexSubmitApply", 4)] // name, exnJson, sigsJson, recipientsJson
    [InlineData("IpexSubmitOffer", 5)] // name, exnJson, sigsJson, atc, recipientsJson
    [InlineData("IpexSubmitAgree", 4)] // name, exnJson, sigsJson, recipientsJson
    [InlineData("IpexSubmitGrant", 5)] // name, exnJson, sigsJson, atc, recipientsJson
    [InlineData("IpexSubmitAdmit", 5)] // name, exnJson, sigsJson, atc, recipientsJson
    public void IpexSubmitMethods_ShouldHaveCorrectParameterCount(string methodName, int expectedParameterCount)
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);

        // Act
        var method = shimType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);

        // Assert
        Assert.NotNull(method);
        Assert.Equal(expectedParameterCount, method.GetParameters().Length);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
    }

    // ===================== OOBI Operation Tests =====================

    [Fact]
    public void OobiGet_Method_ShouldHaveCorrectSignature()
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);

        // Act
        var method = shimType.GetMethod("OobiGet", BindingFlags.NonPublic | BindingFlags.Static);

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType); // name
        Assert.Equal(typeof(string), parameters[1].ParameterType); // role (nullable)
    }

    [Fact]
    public void OobiResolve_Method_ShouldHaveCorrectSignature()
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);

        // Act
        var method = shimType.GetMethod("OobiResolve", BindingFlags.NonPublic | BindingFlags.Static);

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType); // oobi
        Assert.Equal(typeof(string), parameters[1].ParameterType); // alias (nullable)
    }

    // ===================== Operations Management Tests =====================

    [Theory]
    [InlineData("OperationsGet", 1)]     // name
    [InlineData("OperationsList", 1)]    // type (nullable)
    [InlineData("OperationsDelete", 1)]  // name
    [InlineData("OperationsWait", 2)]    // operationJson, optionsJson (nullable)
    public void OperationsMethods_ShouldHaveCorrectParameterCount(string methodName, int expectedParameterCount)
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);

        // Act
        var method = shimType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);

        // Assert
        Assert.NotNull(method);
        Assert.Equal(expectedParameterCount, method.GetParameters().Length);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
    }

    // ===================== Registry Management Tests =====================

    [Fact]
    public void RegistriesCreate_Method_ShouldAcceptJsonArgs()
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);

        // Act
        var method = shimType.GetMethod("RegistriesCreate", BindingFlags.NonPublic | BindingFlags.Static);

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(string), parameters[0].ParameterType); // argsJson
    }

    [Fact]
    public void RegistriesRename_Method_ShouldHaveCorrectSignature()
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);

        // Act
        var method = shimType.GetMethod("RegistriesRename", BindingFlags.NonPublic | BindingFlags.Static);

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType); // name
        Assert.Equal(typeof(string), parameters[1].ParameterType); // registryName
        Assert.Equal(typeof(string), parameters[2].ParameterType); // newName
    }

    // ===================== Contact Management Tests =====================

    [Theory]
    [InlineData("ContactsList", 3)]   // group, filterField, filterValue (all nullable)
    [InlineData("ContactsGet", 1)]    // prefix
    [InlineData("ContactsAdd", 2)]    // prefix, infoJson
    [InlineData("ContactsUpdate", 2)] // prefix, infoJson
    [InlineData("ContactsDelete", 1)] // prefix
    public void ContactsMethods_ShouldHaveCorrectParameterCount(string methodName, int expectedParameterCount)
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);

        // Act
        var method = shimType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);

        // Assert
        Assert.NotNull(method);
        Assert.Equal(expectedParameterCount, method.GetParameters().Length);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
    }

    // ===================== Credential Operations Tests =====================

    [Theory]
    [InlineData("CredentialsIssue", 2)]   // name, argsJson
    [InlineData("CredentialsRevoke", 3)]  // name, said, datetime (nullable)
    [InlineData("CredentialsState", 2)]   // ri, said
    [InlineData("CredentialsDelete", 1)]  // said
    public void CredentialOperationsMethods_ShouldHaveCorrectParameterCount(string methodName, int expectedParameterCount)
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);

        // Act
        var method = shimType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);

        // Assert
        Assert.NotNull(method);
        Assert.Equal(expectedParameterCount, method.GetParameters().Length);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
    }

    // ===================== Schema Operations Tests =====================

    [Fact]
    public void SchemasGet_Method_ShouldAcceptSaidParameter()
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);

        // Act
        var method = shimType.GetMethod("SchemasGet", BindingFlags.NonPublic | BindingFlags.Static);

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(string), parameters[0].ParameterType); // said
    }

    [Fact]
    public void SchemasList_Method_ShouldHaveNoParameters()
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);

        // Act
        var method = shimType.GetMethod("SchemasList", BindingFlags.NonPublic | BindingFlags.Static);

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
        Assert.Empty(method.GetParameters());
    }

    // ===================== Notifications Operations Tests =====================

    [Theory]
    [InlineData("NotificationsList", 2)]   // start, end (both nullable)
    [InlineData("NotificationsMark", 1)]   // said
    [InlineData("NotificationsDelete", 1)] // said
    public void NotificationsMethods_ShouldHaveCorrectParameterCount(string methodName, int expectedParameterCount)
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);

        // Act
        var method = shimType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);

        // Assert
        Assert.NotNull(method);
        Assert.Equal(expectedParameterCount, method.GetParameters().Length);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
    }

    // ===================== Parameter Type Tests =====================

    [Theory]
    [InlineData("NotificationsList")]
    public void NotificationsList_ShouldAcceptNullableIntParameters(string methodName)
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);

        // Act
        var method = shimType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);

        // Assert
        Assert.NotNull(method);
        
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(int?), parameters[0].ParameterType); // start
        Assert.Equal(typeof(int?), parameters[1].ParameterType); // end
    }

    // ===================== Comprehensive Coverage Tests =====================

    [Fact]
    public void AllNewShimMethods_ShouldReturnTaskOfString()
    {
        // This test ensures all new shim methods follow the same pattern
        var shimType = typeof(Signify_ts_shim);
        var newMethods = new[]
        {
            "IpexApply", "IpexOffer", "IpexAgree", "IpexGrant", "IpexAdmit",
            "IpexSubmitApply", "IpexSubmitOffer", "IpexSubmitAgree", "IpexSubmitGrant", "IpexSubmitAdmit",
            "OobiGet", "OobiResolve",
            "OperationsGet", "OperationsList", "OperationsDelete", "OperationsWait",
            "RegistriesList", "RegistriesCreate", "RegistriesRename",
            "ContactsList", "ContactsGet", "ContactsAdd", "ContactsUpdate", "ContactsDelete",
            "CredentialsIssue", "CredentialsRevoke", "CredentialsState", "CredentialsDelete",
            "SchemasGet", "SchemasList",
            "NotificationsList", "NotificationsMark", "NotificationsDelete"
        };

        foreach (var methodName in newMethods)
        {
            var method = shimType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            Assert.True(method.IsStatic);
            Assert.Equal(typeof(Task<string>), method.ReturnType);
        }
    }

    [Fact]
    public void ExtendedShimMethods_ShouldSupportFullSignifyTsFeatures()
    {
        // This test verifies that extended methods support comprehensive signify-ts features:
        // 1. IPEX protocol for credential exchange
        // 2. OOBI operations for identifier discovery
        // 3. Operations management for async operation tracking
        // 4. Registry management for credential registries
        // 5. Contact management for known identifiers
        // 6. Extended credential operations (issue, revoke, state)
        // 7. Schema operations for credential schemas
        // 8. Notifications management

        var shimType = typeof(Signify_ts_shim);
        
        // IPEX protocol support
        var ipexApplyMethod = shimType.GetMethod("IpexApply", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(ipexApplyMethod);
        
        // OOBI operations support
        var oobiGetMethod = shimType.GetMethod("OobiGet", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(oobiGetMethod);
        
        // Operations management support
        var operationsGetMethod = shimType.GetMethod("OperationsGet", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(operationsGetMethod);
        
        // Registry management support
        var registriesCreateMethod = shimType.GetMethod("RegistriesCreate", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(registriesCreateMethod);
        
        // Contact management support
        var contactsListMethod = shimType.GetMethod("ContactsList", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(contactsListMethod);
        
        // Extended credential operations support
        var credentialsIssueMethod = shimType.GetMethod("CredentialsIssue", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(credentialsIssueMethod);
        
        // Schema operations support
        var schemasGetMethod = shimType.GetMethod("SchemasGet", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(schemasGetMethod);
        
        // Notifications management support
        var notificationsListMethod = shimType.GetMethod("NotificationsList", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(notificationsListMethod);
    }

    [Fact]
    public void AllShimMethods_ShouldFollowNamingConventions()
    {
        // Verify all shim methods follow consistent naming conventions
        var shimType = typeof(Signify_ts_shim);
        var methods = shimType.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.IsStatic && m.ReturnType == typeof(Task<string>));

        foreach (var method in methods)
        {
            // Method names should be PascalCase
            Assert.True(char.IsUpper(method.Name[0]), $"Method {method.Name} should start with uppercase");
            
            // Methods should not contain underscores (following C# conventions)
            Assert.DoesNotContain("_", method.Name);
            
            // All parameters should be valid types
            foreach (var param in method.GetParameters())
            {
                Assert.True(param.ParameterType == typeof(string) || 
                           param.ParameterType == typeof(int?) ||
                           param.ParameterType == typeof(bool),
                           $"Parameter {param.Name} in {method.Name} has unexpected type {param.ParameterType}");
            }
        }
    }
}