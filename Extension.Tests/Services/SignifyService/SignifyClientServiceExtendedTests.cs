using Extension.Helper;
using Extension.Services.SignifyService;
using Extension.Services.SignifyService.Models;
using FluentResults;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace Extension.Tests.Services.SignifyService;

/// <summary>
/// Tests for the extended SignifyClientService methods that wrap the new signify-ts functionality.
/// These tests verify the high-level service methods without requiring browser context.
/// </summary>
public class SignifyClientServiceExtendedTests
{
    private readonly Mock<ILogger<SignifyClientService>> _mockLogger;

    public SignifyClientServiceExtendedTests()
    {
        _mockLogger = new Mock<ILogger<SignifyClientService>>();
    }

    // ===================== IPEX Protocol Method Tests =====================

    [Fact]
    public void IpexApply_Method_ShouldExist()
    {
        // Arrange
        var service = new SignifyClientService(_mockLogger.Object);

        // Act & Assert
        var method = typeof(SignifyClientService).GetMethod("IpexApply");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Result<IpexExchangeResult>>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(IpexApplyArgs), parameters[0].ParameterType);
    }

    [Fact]
    public void IpexOffer_Method_ShouldExist()
    {
        // Arrange
        var service = new SignifyClientService(_mockLogger.Object);

        // Act & Assert
        var method = typeof(SignifyClientService).GetMethod("IpexOffer");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Result<IpexExchangeResult>>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(IpexOfferArgs), parameters[0].ParameterType);
    }

    [Theory]
    [InlineData("IpexApply")]
    [InlineData("IpexOffer")]
    [InlineData("IpexAgree")]
    [InlineData("IpexGrant")]
    [InlineData("IpexAdmit")]
    public void IpexMethods_ShouldReturnIpexExchangeResult(string methodName)
    {
        // Arrange
        var serviceType = typeof(SignifyClientService);

        // Act
        var method = serviceType.GetMethod(methodName);

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Result<IpexExchangeResult>>), method.ReturnType);
    }

    // ===================== OOBI Operations Tests =====================

    [Fact]
    public void GetOobi_Method_ShouldHaveCorrectSignature()
    {
        // Arrange
        var serviceType = typeof(SignifyClientService);

        // Act
        var method = serviceType.GetMethod("GetOobi");

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Result<RecursiveDictionary>>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType); // name
        Assert.Equal(typeof(string), parameters[1].ParameterType); // role (nullable)
    }

    [Fact]
    public void ResolveOobi_Method_ShouldHaveCorrectSignature()
    {
        // Arrange
        var serviceType = typeof(SignifyClientService);

        // Act
        var method = serviceType.GetMethod("ResolveOobi");

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Result<RecursiveDictionary>>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType); // oobi
        Assert.Equal(typeof(string), parameters[1].ParameterType); // alias (nullable)
    }

    // ===================== Operations Management Tests =====================

    [Theory]
    [InlineData("GetOperation", typeof(Result<Operation>))]
    [InlineData("ListOperations", typeof(Result<List<Operation>>))]
    [InlineData("DeleteOperation", typeof(Result))]
    [InlineData("WaitForOperation", typeof(Result<Operation>))]
    public void OperationsMethods_ShouldHaveCorrectReturnTypes(string methodName, Type expectedReturnType)
    {
        // Arrange
        var serviceType = typeof(SignifyClientService);

        // Act
        var method = serviceType.GetMethod(methodName);

        // Assert
        Assert.NotNull(method);
        
        // Check if return type is wrapped in Task<>
        if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var innerType = method.ReturnType.GetGenericArguments()[0];
            Assert.Equal(expectedReturnType, innerType);
        }
        else
        {
            Assert.Equal(typeof(Task<>).MakeGenericType(expectedReturnType), method.ReturnType);
        }
    }

    // ===================== Registry Management Tests =====================

    [Fact]
    public void ListRegistries_Method_ShouldReturnListOfDictionaries()
    {
        // Arrange
        var serviceType = typeof(SignifyClientService);

        // Act
        var method = serviceType.GetMethod("ListRegistries");

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Result<List<RecursiveDictionary>>>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(string), parameters[0].ParameterType); // name
    }

    [Fact]
    public void CreateRegistry_Method_ShouldHaveCorrectSignature()
    {
        // Arrange
        var serviceType = typeof(SignifyClientService);

        // Act
        var method = serviceType.GetMethod("CreateRegistry");

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Result<RecursiveDictionary>>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Equal(6, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType); // name
        Assert.Equal(typeof(string), parameters[1].ParameterType); // registryName
        Assert.Equal(typeof(int?), parameters[2].ParameterType);   // toad
        Assert.Equal(typeof(bool), parameters[3].ParameterType);   // noBackers
        Assert.Equal(typeof(List<string>), parameters[4].ParameterType); // baks
        Assert.Equal(typeof(string), parameters[5].ParameterType); // nonce
    }

    // ===================== Contact Management Tests =====================

    [Theory]
    [InlineData("ListContacts", typeof(Result<List<Contact>>))]
    [InlineData("GetContact", typeof(Result<Contact>))]
    [InlineData("AddContact", typeof(Result<Contact>))]
    [InlineData("UpdateContact", typeof(Result<Contact>))]
    [InlineData("DeleteContact", typeof(Result))]
    public void ContactsMethods_ShouldHaveCorrectReturnTypes(string methodName, Type expectedReturnType)
    {
        // Arrange
        var serviceType = typeof(SignifyClientService);

        // Act
        var method = serviceType.GetMethod(methodName);

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<>).MakeGenericType(expectedReturnType), method.ReturnType);
    }

    [Fact]
    public void AddContact_Method_ShouldAcceptContactInfo()
    {
        // Arrange
        var serviceType = typeof(SignifyClientService);

        // Act
        var method = serviceType.GetMethod("AddContact");

        // Assert
        Assert.NotNull(method);
        
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);     // prefix
        Assert.Equal(typeof(ContactInfo), parameters[1].ParameterType); // info
    }

    // ===================== Additional Credential Operations Tests =====================

    [Theory]
    [InlineData("IssueCredential")]
    [InlineData("RevokeCredential")]
    [InlineData("GetCredentialState")]
    public void ExtendedCredentialMethods_ShouldReturnDictionary(string methodName)
    {
        // Arrange
        var serviceType = typeof(SignifyClientService);

        // Act
        var method = serviceType.GetMethod(methodName);

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Result<RecursiveDictionary>>), method.ReturnType);
    }

    [Fact]
    public void DeleteCredential_Method_ShouldReturnResult()
    {
        // Arrange
        var serviceType = typeof(SignifyClientService);

        // Act
        var method = serviceType.GetMethod("DeleteCredential");

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Result>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(string), parameters[0].ParameterType); // said
    }

    [Fact]
    public void IssueCredential_Method_ShouldAcceptCredentialData()
    {
        // Arrange
        var serviceType = typeof(SignifyClientService);

        // Act
        var method = serviceType.GetMethod("IssueCredential");

        // Assert
        Assert.NotNull(method);
        
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);        // name
        Assert.Equal(typeof(CredentialData), parameters[1].ParameterType); // args
    }

    // ===================== Schema Operations Tests =====================

    [Fact]
    public void GetSchema_Method_ShouldAcceptSaidParameter()
    {
        // Arrange
        var serviceType = typeof(SignifyClientService);

        // Act
        var method = serviceType.GetMethod("GetSchema");

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Result<RecursiveDictionary>>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(string), parameters[0].ParameterType); // said
    }

    [Fact]
    public void ListSchemas_Method_ShouldHaveNoParameters()
    {
        // Arrange
        var serviceType = typeof(SignifyClientService);

        // Act
        var method = serviceType.GetMethod("ListSchemas");

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Result<List<RecursiveDictionary>>>), method.ReturnType);
        Assert.Empty(method.GetParameters());
    }

    // ===================== Notifications Operations Tests =====================

    [Fact]
    public void ListNotifications_Method_ShouldAcceptOptionalParameters()
    {
        // Arrange
        var serviceType = typeof(SignifyClientService);

        // Act
        var method = serviceType.GetMethod("ListNotifications");

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Result<List<RecursiveDictionary>>>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(int?), parameters[0].ParameterType); // start
        Assert.Equal(typeof(int?), parameters[1].ParameterType); // end
    }

    [Fact]
    public void MarkNotification_Method_ShouldReturnString()
    {
        // Arrange
        var serviceType = typeof(SignifyClientService);

        // Act
        var method = serviceType.GetMethod("MarkNotification");

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Result<string>>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(string), parameters[0].ParameterType); // said
    }

    [Fact]
    public void DeleteNotification_Method_ShouldReturnResult()
    {
        // Arrange
        var serviceType = typeof(SignifyClientService);

        // Act
        var method = serviceType.GetMethod("DeleteNotification");

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Result>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(string), parameters[0].ParameterType); // said
    }

    // ===================== Model Validation Tests =====================

    [Fact]
    public void IpexApplyArgs_ShouldHaveRequiredProperties()
    {
        // Arrange & Act
        var type = typeof(IpexApplyArgs);
        var properties = type.GetProperties();

        // Assert
        Assert.Contains(properties, p => p.Name == "SenderName");
        Assert.Contains(properties, p => p.Name == "Recipient");
        Assert.Contains(properties, p => p.Name == "SchemaSaid");
        Assert.Contains(properties, p => p.Name == "Message");
        Assert.Contains(properties, p => p.Name == "Attributes");
        Assert.Contains(properties, p => p.Name == "Datetime");
    }

    [Fact]
    public void Contact_ShouldImplementDictionary()
    {
        // Arrange & Act
        var contactType = typeof(Contact);

        // Assert
        Assert.True(typeof(Dictionary<string, object>).IsAssignableFrom(contactType));
    }

    [Fact]
    public void ContactInfo_ShouldInheritFromDictionary()
    {
        // Arrange & Act
        var contactInfoType = typeof(ContactInfo);

        // Assert
        Assert.True(typeof(Dictionary<string, object>).IsAssignableFrom(contactInfoType));
    }

    // ===================== Interface Compliance Tests =====================

    [Fact]
    public void SignifyClientService_ShouldImplementAllInterfaceMethods()
    {
        // Arrange
        var serviceType = typeof(SignifyClientService);
        var interfaceType = typeof(ISignifyClientService);

        // Act
        var service = new SignifyClientService(_mockLogger.Object);
        
        // Assert - Check that the service implements the interface
        Assert.True(interfaceType.IsAssignableFrom(serviceType), 
            "SignifyClientService should implement ISignifyClientService");
        
        // Also verify we can cast to the interface
        var interfaceInstance = service as ISignifyClientService;
        Assert.NotNull(interfaceInstance);
    }

    // ===================== Error Handling Tests =====================

    [Fact]
    public void ServiceMethods_ShouldReturnFluentResults()
    {
        // This test verifies that all service methods return FluentResults
        // for consistent error handling across the service layer

        var serviceType = typeof(SignifyClientService);
        var publicMethods = serviceType.GetMethods()
            .Where(m => m.IsPublic && m.DeclaringType == serviceType)
            .Where(m => m.ReturnType.IsGenericType && m.ReturnType.GetGenericTypeDefinition() == typeof(Task<>));

        foreach (var method in publicMethods)
        {
            var innerType = method.ReturnType.GetGenericArguments()[0];
            
            // All async methods should return Result<T> or Result
            Assert.True(
                innerType == typeof(Result) || 
                (innerType.IsGenericType && innerType.GetGenericTypeDefinition() == typeof(Result<>)),
                $"Method {method.Name} should return Task<Result> or Task<Result<T>>");
        }
    }

    // ===================== JSON Serialization Tests =====================

    [Fact]
    public void IpexArgs_ShouldSerializeToJson()
    {
        // Arrange
        var args = new IpexApplyArgs(
            SenderName: "test-sender",
            Recipient: "test-recipient", 
            SchemaSaid: "test-schema-said",
            Message: "test message",
            Attributes: new Dictionary<string, object> { { "key", "value" } },
            Datetime: DateTime.UtcNow.ToString("O")
        );

        // Act
        var json = JsonSerializer.Serialize(args);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("test-sender", json);
        Assert.Contains("test-recipient", json);
        Assert.Contains("test-schema-said", json);
    }

    [Fact]
    public void ContactInfo_ShouldSerializeToJson()
    {
        // Arrange
        var contactInfo = new ContactInfo
        {
            Alias = "test-alias",
            Oobi = "test-oobi"
        };
        contactInfo["customField"] = "customValue";

        // Act
        var json = JsonSerializer.Serialize(contactInfo);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("test-alias", json);
        Assert.Contains("test-oobi", json);
        Assert.Contains("customValue", json);
    }

    // ===================== Service Architecture Tests =====================

    [Fact]
    public void SignifyClientService_ShouldFollowServicePatterns()
    {
        // This test verifies the service follows expected patterns:
        // 1. Dependency injection with ILogger
        // 2. Async/await throughout
        // 3. Consistent error handling with FluentResults
        // 4. JSON serialization for interop

        var serviceType = typeof(SignifyClientService);
        
        // Should have constructor that accepts ILogger
        var constructor = serviceType.GetConstructors().FirstOrDefault();
        Assert.NotNull(constructor);
        
        var parameters = constructor.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(ILogger<SignifyClientService>), parameters[0].ParameterType);
        
        // Should implement ISignifyClientService
        Assert.True(typeof(ISignifyClientService).IsAssignableFrom(serviceType));
    }

    [Fact]
    public void NewServiceMethods_ShouldSupportFullSignifyTsFeatures()
    {
        // This test verifies that new service methods support comprehensive signify-ts features
        var serviceType = typeof(SignifyClientService);
        
        // IPEX protocol support
        Assert.NotNull(serviceType.GetMethod("IpexApply"));
        Assert.NotNull(serviceType.GetMethod("IpexOffer"));
        Assert.NotNull(serviceType.GetMethod("IpexAgree"));
        Assert.NotNull(serviceType.GetMethod("IpexGrant"));
        Assert.NotNull(serviceType.GetMethod("IpexAdmit"));
        
        // OOBI operations support
        Assert.NotNull(serviceType.GetMethod("GetOobi"));
        Assert.NotNull(serviceType.GetMethod("ResolveOobi"));
        
        // Operations management support
        Assert.NotNull(serviceType.GetMethod("GetOperation"));
        Assert.NotNull(serviceType.GetMethod("ListOperations"));
        Assert.NotNull(serviceType.GetMethod("DeleteOperation"));
        Assert.NotNull(serviceType.GetMethod("WaitForOperation"));
        
        // Registry management support
        Assert.NotNull(serviceType.GetMethod("ListRegistries"));
        Assert.NotNull(serviceType.GetMethod("CreateRegistry"));
        
        // Contact management support
        Assert.NotNull(serviceType.GetMethod("ListContacts"));
        Assert.NotNull(serviceType.GetMethod("GetContact"));
        Assert.NotNull(serviceType.GetMethod("AddContact"));
        Assert.NotNull(serviceType.GetMethod("UpdateContact"));
        Assert.NotNull(serviceType.GetMethod("DeleteContact"));
        
        // Extended credential operations support
        Assert.NotNull(serviceType.GetMethod("IssueCredential"));
        Assert.NotNull(serviceType.GetMethod("RevokeCredential"));
        Assert.NotNull(serviceType.GetMethod("GetCredentialState"));
        Assert.NotNull(serviceType.GetMethod("DeleteCredential"));
        
        // Schema operations support
        Assert.NotNull(serviceType.GetMethod("GetSchema"));
        Assert.NotNull(serviceType.GetMethod("ListSchemas"));
        
        // Notifications management support
        Assert.NotNull(serviceType.GetMethod("ListNotifications"));
        Assert.NotNull(serviceType.GetMethod("MarkNotification"));
        Assert.NotNull(serviceType.GetMethod("DeleteNotification"));
    }
}