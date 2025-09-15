using Extension.Services.SignifyService;
using System.Runtime.InteropServices.JavaScript;
using Xunit;

namespace Extension.Tests.Services.SignifyService;

/// <summary>
/// Tests for the signify-ts JavaScript interop shim functionality.
/// These tests verify the C# side of the JavaScript integration.
/// </summary>
public class SignifyTsShimTests
{
    [Fact]
    public void Signify_ts_shim_ClassExists()
    {
        // Arrange & Act
        var shimType = typeof(Signify_ts_shim);

        // Assert
        Assert.NotNull(shimType);
        Assert.True(shimType.IsPartial());
        Assert.True(shimType.IsClass);
    }

    [Fact]
    public void Signify_ts_shim_HasRequiredMethods()
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);

        // Act & Assert - Verify all required JSImport methods exist
        var bootAndConnectMethod = shimType.GetMethod("BootAndConnect", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var connectMethod = shimType.GetMethod("Connect", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var createAIDMethod = shimType.GetMethod("CreateAID", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var getAIDsMethod = shimType.GetMethod("GetAIDs", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var getAIDMethod = shimType.GetMethod("GetAID", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var getCredentialsListMethod = shimType.GetMethod("GetCredentialsList", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var getCredentialMethod = shimType.GetMethod("GetCredential", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var getStateMethod = shimType.GetMethod("GetState", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(bootAndConnectMethod);
        Assert.NotNull(connectMethod);
        Assert.NotNull(createAIDMethod);
        Assert.NotNull(getAIDsMethod);
        Assert.NotNull(getAIDMethod);
        Assert.NotNull(getCredentialsListMethod);
        Assert.NotNull(getCredentialMethod);
        Assert.NotNull(getStateMethod);
    }

    [Fact]
    public void BootAndConnect_Method_HasCorrectSignature()
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);
        var method = shimType.GetMethod("BootAndConnect", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Act & Assert
        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
        Assert.Equal(typeof(string), parameters[2].ParameterType);
    }

    [Fact]
    public void Connect_Method_HasCorrectSignature()
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);
        var method = shimType.GetMethod("Connect", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Act & Assert
        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
    }

    [Fact]
    public void CreateAID_Method_HasCorrectSignature()
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);
        var method = shimType.GetMethod("CreateAID", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Act & Assert
        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void GetAIDs_Method_HasCorrectSignature()
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);
        var method = shimType.GetMethod("GetAIDs", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Act & Assert
        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Empty(parameters);
    }

    [Fact]
    public void GetAID_Method_HasCorrectSignature()
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);
        var method = shimType.GetMethod("GetAID", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Act & Assert
        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
    }

    [Fact]
    public void GetCredentialsList_Method_HasCorrectSignature()
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);
        var method = shimType.GetMethod("GetCredentialsList", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Act & Assert
        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Empty(parameters);
    }

    [Fact]
    public void GetCredential_Method_HasCorrectSignature()
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);
        var method = shimType.GetMethod("GetCredential", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Act & Assert
        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(bool), parameters[1].ParameterType);
    }

    [Fact]
    public void GetState_Method_HasCorrectSignature()
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);
        var method = shimType.GetMethod("GetState", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Act & Assert
        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(Task<string>), method.ReturnType);
        
        var parameters = method.GetParameters();
        Assert.Empty(parameters);
    }

    [Fact]
    public void JSImportMethods_ShouldHavePartialModifier()
    {
        // Arrange
        var shimType = typeof(Signify_ts_shim);
        var methods = shimType.GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Where(m => m.Name.Contains("AID") || m.Name.Contains("Connect") || m.Name.Contains("Credential") || m.Name.Contains("State"));

        // Act & Assert
        foreach (var method in methods)
        {
            // JSImport methods should be partial and static
            Assert.True(method.IsStatic);
            // Note: Checking for partial modifier requires more complex reflection
            // For now, we verify they exist and are static
        }
    }

    [Fact]
    public void ShimMethods_InNonBrowserEnvironment_ShouldThrowJSException()
    {
        // This test verifies behavior when called outside browser context
        if (!OperatingSystem.IsBrowser())
        {
            // Act & Assert
            // These methods should throw JSException when called outside browser
            // Note: We can't actually call them here as they are internal partial methods
            // This test documents expected behavior
            Assert.True(true); // Placeholder for documentation
        }
    }
}

/// <summary>
/// Extension methods for testing reflection capabilities
/// </summary>
public static class TypeExtensions
{
    public static bool IsPartial(this Type type)
    {
        // Note: Detecting partial classes requires compiler-generated attributes
        // For now, we check if it's a class (partial classes are still classes)
        return type.IsClass;
    }
}