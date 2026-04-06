namespace Extension.Tests.Services.Storage;

using Extension.Services.Storage;
using Xunit;

/// <summary>
/// Unit tests for StorageGatewayValidation helper class.
/// Tests the operation validity matrix documented in STORAGE_SERVICE_MIGRATION.md.
/// </summary>
public class StorageGatewayValidationTests {
    #region Managed Storage Read-Only Tests

    [Theory]
    [InlineData(nameof(IStorageGateway.SetItem))]
    [InlineData(nameof(IStorageGateway.RemoveItem))]
    [InlineData(nameof(IStorageGateway.Clear))]
    [InlineData("RestoreBackupItems")]
    public void ValidateOperation_ManagedStorage_WriteOperations_ReturnsFailed(string operation) {
        // Act
        var result = StorageGatewayValidation.ValidateOperation(operation, StorageArea.Managed);

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("read-only", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Managed storage", result.Errors[0].Message);
    }

    [Theory]
    [InlineData(nameof(IStorageGateway.GetItem))]
    [InlineData("GetBackupItems")]
    // [InlineData(nameof(IStorageGateway.Initialize))]
    public void ValidateOperation_ManagedStorage_ReadOperations_ReturnsSuccess(string operation) {
        // Act
        var result = StorageGatewayValidation.ValidateOperation(operation, StorageArea.Managed);

        // Assert
        Assert.True(result.IsSuccess);
    }

    #endregion

    #region Quota Operations Tests

    [Theory]
    [InlineData(StorageArea.Local)]
    [InlineData(StorageArea.Managed)]
    public void ValidateOperation_QuotaOperations_OnLocalOrManaged_ReturnsFailed(StorageArea area) {
        // Act
        var getBytesResult = StorageGatewayValidation.ValidateOperation(
            "GetBytesInUse", area);
        var getQuotaResult = StorageGatewayValidation.ValidateOperation(
            "GetQuota", area);

        // Assert
        Assert.True(getBytesResult.IsFailed);
        Assert.Contains("no quota", getBytesResult.Errors[0].Message, StringComparison.OrdinalIgnoreCase);

        Assert.True(getQuotaResult.IsFailed);
        Assert.Contains("no quota", getQuotaResult.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(StorageArea.Session)]
    [InlineData(StorageArea.Sync)]
    public void ValidateOperation_QuotaOperations_OnSessionOrSync_ReturnsSuccess(StorageArea area) {
        // Act
        var getBytesResult = StorageGatewayValidation.ValidateOperation(
            "GetBytesInUse", area);
        var getQuotaResult = StorageGatewayValidation.ValidateOperation(
            "GetQuota", area);

        // Assert
        Assert.True(getBytesResult.IsSuccess);
        Assert.True(getQuotaResult.IsSuccess);
    }

    #endregion

    #region Operation Validity Matrix Tests

    /// <summary>
    /// Comprehensive test of the operation validity matrix from STORAGE_SERVICE_MIGRATION.md.
    /// Each row represents: operation name, storage area, expected validity.
    /// </summary>
    [Theory]
    // Initialize - Valid for all areas
    // [InlineData(nameof(IStorageGateway.Initialize), StorageArea.Local, true)]
    // [InlineData(nameof(IStorageGateway.Initialize), StorageArea.Session, true)]
    // [InlineData(nameof(IStorageGateway.Initialize), StorageArea.Sync, true)]
    // [InlineData(nameof(IStorageGateway.Initialize), StorageArea.Managed, true)]

    // Clear - Not valid for Managed
    [InlineData(nameof(IStorageGateway.Clear), StorageArea.Local, true)]
    [InlineData(nameof(IStorageGateway.Clear), StorageArea.Session, true)]
    [InlineData(nameof(IStorageGateway.Clear), StorageArea.Sync, true)]
    [InlineData(nameof(IStorageGateway.Clear), StorageArea.Managed, false)]

    // RemoveItem - Not valid for Managed
    [InlineData(nameof(IStorageGateway.RemoveItem), StorageArea.Local, true)]
    [InlineData(nameof(IStorageGateway.RemoveItem), StorageArea.Session, true)]
    [InlineData(nameof(IStorageGateway.RemoveItem), StorageArea.Sync, true)]
    [InlineData(nameof(IStorageGateway.RemoveItem), StorageArea.Managed, false)]

    // GetItem - Valid for all areas
    [InlineData(nameof(IStorageGateway.GetItem), StorageArea.Local, true)]
    [InlineData(nameof(IStorageGateway.GetItem), StorageArea.Session, true)]
    [InlineData(nameof(IStorageGateway.GetItem), StorageArea.Sync, true)]
    [InlineData(nameof(IStorageGateway.GetItem), StorageArea.Managed, true)]

    // SetItem - Not valid for Managed
    [InlineData(nameof(IStorageGateway.SetItem), StorageArea.Local, true)]
    [InlineData(nameof(IStorageGateway.SetItem), StorageArea.Session, true)]
    [InlineData(nameof(IStorageGateway.SetItem), StorageArea.Sync, true)]
    [InlineData(nameof(IStorageGateway.SetItem), StorageArea.Managed, false)]

    // GetBackupItems - Valid for all areas
    [InlineData("GetBackupItems", StorageArea.Local, true)]
    [InlineData("GetBackupItems", StorageArea.Session, true)]
    [InlineData("GetBackupItems", StorageArea.Sync, true)]
    [InlineData("GetBackupItems", StorageArea.Managed, true)]

    // RestoreBackupItems - Not valid for Managed
    [InlineData("RestoreBackupItems", StorageArea.Local, true)]
    [InlineData("RestoreBackupItems", StorageArea.Session, true)]
    [InlineData("RestoreBackupItems", StorageArea.Sync, true)]
    [InlineData("RestoreBackupItems", StorageArea.Managed, false)]

    // GetBytesInUse - Only valid for Session and Sync (WebExtensions.Net StorageAreaWithUsage)
    [InlineData("GetBytesInUse", StorageArea.Local, false)]
    [InlineData("GetBytesInUse", StorageArea.Session, true)]
    [InlineData("GetBytesInUse", StorageArea.Sync, true)]
    [InlineData("GetBytesInUse", StorageArea.Managed, false)]

    // GetQuota - Only valid for Session and Sync
    [InlineData("GetQuota", StorageArea.Local, false)]
    [InlineData("GetQuota", StorageArea.Session, true)]
    [InlineData("GetQuota", StorageArea.Sync, true)]
    [InlineData("GetQuota", StorageArea.Managed, false)]
    public void ValidateOperation_OperationValidityMatrix_MatchesSpecification(
        string operation,
        StorageArea area,
        bool expectedValid
    ) {
        // Act
        var result = StorageGatewayValidation.ValidateOperation(operation, area);

        // Assert
        Assert.Equal(expectedValid, result.IsSuccess);

        if (!expectedValid) {
            Assert.NotEmpty(result.Errors);
            Assert.NotNull(result.Errors[0].Message);
        }
    }

    #endregion

    #region ValidateAndFail Tests

    [Fact]
    public void ValidateAndFail_WithInvalidOperation_ReturnsTypedFailure() {
        // Act
        var result = StorageGatewayValidation.ValidateAndFail<string>(
            nameof(IStorageGateway.SetItem),
            StorageArea.Managed
        );

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("read-only", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateAndFail_WithValidOperation_ThrowsException() {
        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => {
            StorageGatewayValidation.ValidateAndFail<string>(
                nameof(IStorageGateway.GetItem),
                StorageArea.Local
            );
        });

        Assert.Contains("expecting failure", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Error Message Quality Tests

    [Fact]
    public void ValidateOperation_ManagedWriteError_ContainsHelpfulMessage() {
        // Act
        var result = StorageGatewayValidation.ValidateOperation(
            nameof(IStorageGateway.SetItem),
            StorageArea.Managed
        );

        // Assert
        Assert.True(result.IsFailed);
        var errorMessage = result.Errors[0].Message;

        // Error should explain why it's read-only
        Assert.Contains("read-only", errorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("extensions", errorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Managed storage", errorMessage);

        // Should mention it's for IT administrators
        Assert.Contains("administrators", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateOperation_QuotaError_ContainsHelpfulMessage() {
        // Act
        var result = StorageGatewayValidation.ValidateOperation(
            "GetBytesInUse",
            StorageArea.Local
        );

        // Assert
        Assert.True(result.IsFailed);
        var errorMessage = result.Errors[0].Message;

        // Error should explain the API limitation
        Assert.Contains("no quota", errorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Local", errorMessage);
        Assert.Contains("WebExtensions.Net", errorMessage);

        // Should mention which areas ARE supported
        Assert.Contains("Session", errorMessage);
        Assert.Contains("Sync", errorMessage);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ValidateOperation_UnknownOperation_ReturnsSuccess() {
        // Act - Unknown operation names should not cause validation failures
        var result = StorageGatewayValidation.ValidateOperation(
            "NonExistentOperation",
            StorageArea.Local
        );

        // Assert - Unknown operations pass validation (fail at runtime instead)
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateOperation_NullOperation_DoesNotThrow() {
        // Act & Assert - Should handle null gracefully
        var exception = Record.Exception(() => {
            StorageGatewayValidation.ValidateOperation(null!, StorageArea.Local);
        });

        // Null operation should not throw during validation
        // (may throw ArgumentNullException, but that's expected)
        Assert.True(exception == null || exception is ArgumentNullException);
    }

    [Fact]
    public void ValidateOperation_CaseSensitiveOperationName_Matters() {
        // Act
        var lowerCaseResult = StorageGatewayValidation.ValidateOperation(
            "setitem", // lowercase - won't match
            StorageArea.Managed
        );
        var correctCaseResult = StorageGatewayValidation.ValidateOperation(
            nameof(IStorageGateway.SetItem), // correct case
            StorageArea.Managed
        );

        // Assert
        // Lowercase version won't match the validation set, so passes (then fails at runtime)
        Assert.True(lowerCaseResult.IsSuccess);

        // Correct case should be caught by validation
        Assert.True(correctCaseResult.IsFailed);
    }

    #endregion

    #region Documentation Compliance Tests

    [Fact]
    public void ValidateOperation_SubscribeOperation_AlwaysValid() {
        // Subscribe should work for ALL storage areas including Managed (important!)
        // This is documented in STORAGE_SERVICE_MIGRATION.md

        // Act & Assert
        foreach (StorageArea area in Enum.GetValues<StorageArea>()) {
            // Subscribe is not in the validation lists, so should always pass
            var result = StorageGatewayValidation.ValidateOperation("Subscribe", area);
            Assert.True(result.IsSuccess,
                $"Subscribe should be valid for {area} storage per STORAGE_SERVICE_MIGRATION.md");
        }
    }

    [Fact]
    public void ValidateOperation_ManagedStorageImportantNote_CanSubscribe() {
        // STORAGE_SERVICE_MIGRATION.md explicitly states:
        // "Subscribe | ✅ | ✅ | ✅ | ✅ Important!"
        // Managed storage needs Subscribe for IT policy changes

        // Act
        var result = StorageGatewayValidation.ValidateOperation(
            "Subscribe", // Not in any restriction list
            StorageArea.Managed
        );

        // Assert
        Assert.True(result.IsSuccess,
            "Managed storage MUST support Subscribe for enterprise policy monitoring");
    }

    #endregion
}
