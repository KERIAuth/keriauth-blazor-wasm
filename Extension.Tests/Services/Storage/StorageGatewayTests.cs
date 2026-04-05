namespace Extension.Tests.Services.Storage;

using System.Text.Json;
using Extension.Helper;
using Extension.Models;
using Extension.Models.Storage;
using Extension.Services.Storage;
using Extension.Tests.Models;
using Microsoft.Extensions.Logging;
using Moq;
using WebExtensions.Net.Mock;
using Xunit;

/// <summary>
/// Unit tests for StorageGateway Phase 2 — single-item operations.
/// Bulk read/write, write transactions, and batch observers (Phases 3–5) are tested
/// separately once those methods are implemented.
///
/// As with StorageServiceTests, full integration testing against WebExtensions.Net
/// requires a browser environment. These tests focus on contract behavior:
/// validation rules, return types, and subscribe/unsubscribe bookkeeping.
/// </summary>
public class StorageGatewayTests {
    private readonly Mock<ILogger<StorageGateway>> _mockLogger;
    private readonly MockJsRuntimeAdapter _mockJsRuntimeAdapter;
    private readonly StorageGateway _sut;

    public StorageGatewayTests() {
        _mockLogger = new Mock<ILogger<StorageGateway>>();
        _mockJsRuntimeAdapter = new MockJsRuntimeAdapter();
        _sut = new StorageGateway(_mockJsRuntimeAdapter, _mockLogger.Object);
    }

    #region Validation — Managed storage is read-only

    [Fact]
    public async Task SetItem_OnManagedStorage_ReturnsValidationError() {
        var model = new TestPasscodeModel { Passcode = "test", SessionExpirationUtc = DateTime.UtcNow.AddMinutes(5) };

        var result = await _sut.SetItem(model, StorageArea.Managed);

        Assert.True(result.IsFailed);
        Assert.Contains("read-only", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Managed", result.Errors[0].Message);
    }

    [Fact]
    public async Task RemoveItem_OnManagedStorage_ReturnsValidationError() {
        var result = await _sut.RemoveItem<TestPasscodeModel>(StorageArea.Managed);

        Assert.True(result.IsFailed);
        Assert.Contains("read-only", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Subscribe/Unsubscribe bookkeeping

    [Fact]
    public void Subscribe_ReturnsDisposable() {
        var observer = new Mock<IObserver<TestPasscodeModel>>();

        var subscription = _sut.Subscribe(observer.Object, StorageArea.Session);

        Assert.NotNull(subscription);
        Assert.IsAssignableFrom<IDisposable>(subscription);
        subscription.Dispose();
    }

    [Fact]
    public void Subscribe_SameObserverTwice_BothDisposeWithoutError() {
        var observer = new Mock<IObserver<TestPasscodeModel>>();

        var sub1 = _sut.Subscribe(observer.Object, StorageArea.Session);
        var sub2 = _sut.Subscribe(observer.Object, StorageArea.Session);

        // Both handles are valid IDisposables. The second subscribe must not
        // add a duplicate observer entry (deduplication by ReferenceEquals).
        Assert.NotNull(sub1);
        Assert.NotNull(sub2);
        var exception = Record.Exception(() => { sub1.Dispose(); sub2.Dispose(); });
        Assert.Null(exception);
    }

    [Fact]
    public void Subscribe_DisposeIsIdempotent() {
        var observer = new Mock<IObserver<TestPasscodeModel>>();
        var subscription = _sut.Subscribe(observer.Object, StorageArea.Session);

        // Multiple dispose calls must not throw.
        var exception = Record.Exception(() => { subscription.Dispose(); subscription.Dispose(); });
        Assert.Null(exception);
    }

    #endregion

    #region GetItems — Phase 3 bulk read

    [Fact]
    public async Task GetItems_EmptyTypes_ReturnsEmptyResult() {
        var result = await _sut.GetItems(StorageArea.Local);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value.VersionMismatchKeys);
        // No round-trip was made, so any Get<T>() returns null.
        Assert.Null(result.Value.Get<TestPasscodeModel>());
    }

    [Fact]
    public async Task GetItems_NonStorageModelType_ReturnsValidationError() {
        // string is not an IStorageModel — this is misuse that should be caught at runtime.
        var result = await _sut.GetItems(StorageArea.Local, typeof(string));

        Assert.True(result.IsFailed);
        Assert.Contains("IStorageModel", result.Errors[0].Message);
    }

    #endregion

    #region StorageReadResult — direct unit tests via internal constructor

    [Fact]
    public void StorageReadResult_Get_PresentRecord_DeserializesAndCaches() {
        var model = new TestPasscodeModel { Passcode = "hunter2", SessionExpirationUtc = new DateTime(2026, 1, 1) };
        var raw = new Dictionary<string, JsonElement?> {
            [nameof(TestPasscodeModel)] = JsonSerializer.SerializeToElement(model, JsonOptions.Storage)
        };

        var sut = new StorageReadResult(raw, new HashSet<string>(), NullLogger);

        var first = sut.Get<TestPasscodeModel>();
        var second = sut.Get<TestPasscodeModel>();

        Assert.NotNull(first);
        Assert.Equal("hunter2", first!.Passcode);
        Assert.Same(first, second); // cached
    }

    [Fact]
    public void StorageReadResult_Get_AbsentKey_ReturnsNull() {
        var raw = new Dictionary<string, JsonElement?>();
        var sut = new StorageReadResult(raw, new HashSet<string>(), NullLogger);

        Assert.Null(sut.Get<TestPasscodeModel>());
    }

    [Fact]
    public void StorageReadResult_Get_KeyPresentButNullElement_ReturnsNull() {
        var raw = new Dictionary<string, JsonElement?> { [nameof(TestPasscodeModel)] = null };
        var sut = new StorageReadResult(raw, new HashSet<string>(), NullLogger);

        Assert.Null(sut.Get<TestPasscodeModel>());
    }

    [Fact]
    public void StorageReadResult_VersionMismatchKey_GetReturnsNullAndIsVersionMismatchTrue() {
        // Simulate: Preferences was present in storage but with a bad SchemaVersion,
        // detected during GetItems and recorded in the mismatch set.
        var stalePrefs = JsonDocument.Parse("""{"SchemaVersion":1,"IsStored":false}""").RootElement;
        var raw = new Dictionary<string, JsonElement?> {
            [nameof(Preferences)] = stalePrefs
        };
        var mismatches = new HashSet<string> { nameof(Preferences) };

        var sut = new StorageReadResult(raw, mismatches, NullLogger);

        Assert.Null(sut.Get<Preferences>());
        Assert.True(sut.IsVersionMismatch<Preferences>());
        Assert.Contains(nameof(Preferences), sut.VersionMismatchKeys);
    }

    [Fact]
    public void StorageReadResult_MultipleRecords_EachIndependentlyRetrieved() {
        var passcode = new TestPasscodeModel { Passcode = "a", SessionExpirationUtc = DateTime.UtcNow };
        var raw = new Dictionary<string, JsonElement?> {
            [nameof(TestPasscodeModel)] = JsonSerializer.SerializeToElement(passcode, JsonOptions.Storage),
            [nameof(Preferences)] = null  // absent
        };

        var sut = new StorageReadResult(raw, new HashSet<string>(), NullLogger);

        Assert.NotNull(sut.Get<TestPasscodeModel>());
        Assert.Null(sut.Get<Preferences>());
        Assert.Empty(sut.VersionMismatchKeys);
    }

    [Fact]
    public void StorageReadResult_MalformedJson_GetReturnsNull() {
        // Record is present but not deserializable into the target type.
        var nonsense = JsonDocument.Parse("\"not an object\"").RootElement;
        var raw = new Dictionary<string, JsonElement?> {
            [nameof(TestPasscodeModel)] = nonsense
        };

        var sut = new StorageReadResult(raw, new HashSet<string>(), NullLogger);

        Assert.Null(sut.Get<TestPasscodeModel>());
    }

    /// <summary>
    /// CESR/SAID ordering regression guard: a credential is stored in CachedCredentials as an
    /// opaque raw JSON string keyed by SAID. When that CachedCredentials record is serialized
    /// via JsonOptions.Storage and then deserialized back through StorageReadResult, the raw
    /// JSON string must come out byte-for-byte identical. Any change to JsonOptions.Storage
    /// that breaks this round-trip would silently corrupt ACDC signatures.
    ///
    /// This test pins the current safe behavior. It does NOT try to prove CESR ordering of
    /// any deserialized structure — that is a separate concern owned by CredentialHelper
    /// and RecursiveDictionary, and is forbidden to go through System.Text.Json at all
    /// (CLAUDE.md invariant #13). This test only guards the storage envelope.
    ///
    /// Uses a real vLEI ECR AUTH sample credential from
    /// https://github.com/WebOfTrust/vLEI/blob/main/samples/acdc/ecr-authorization-vlei-credential.json
    /// so the guard exercises genuine field ordering, nested 'e'/'r' structures, and the
    /// long human-readable disclaimer text that signify-ts would emit.
    /// </summary>
    [Fact]
    public void StorageReadResult_CachedCredentials_RoundTrip_PreservesRawJsonByteForByte() {
        // Load the vLEI sample credential straight from disk. We use the raw file bytes
        // (not a re-serialization) so the test is insensitive to formatting differences —
        // the whole point is that whatever bytes went in come back identical.
        var samplePath = Path.Combine(
            AppContext.BaseDirectory, "TestData", "vlei", "ecr-authorization-vlei-credential.json");
        var rawCredentialJson = File.ReadAllText(samplePath);

        // Sanity check the fixture — if this fails the resource isn't being copied to output.
        Assert.False(string.IsNullOrEmpty(rawCredentialJson));

        // The vLEI sample is a bare ACDC body; its top-level 'd' field is the SAID.
        // We could parse it here, but a fixed literal keeps the test hermetic and obvious.
        const string said = "EuF1gpodKbbqS0fqmUiOYf-MusuNvi0OmY8Js6SKSdfE";
        Assert.Contains($"\"d\": \"{said}\"", rawCredentialJson); // fixture alignment check

        // Build the CachedCredentials value that would be stored, and serialize it
        // through JsonOptions.Storage exactly as StorageGateway.SetItem would.
        var cached = new Extension.Models.CachedCredentials {
            Credentials = new Dictionary<string, string> { [said] = rawCredentialJson }
        };
        var storageElement = JsonSerializer.SerializeToElement(cached, JsonOptions.Storage);

        // Simulate the StorageReadResult retrieval path.
        var raw = new Dictionary<string, JsonElement?> {
            [nameof(Extension.Models.CachedCredentials)] = storageElement
        };
        var sut = new StorageReadResult(raw, new HashSet<string>(), NullLogger);

        var roundTripped = sut.Get<Extension.Models.CachedCredentials>();

        Assert.NotNull(roundTripped);
        Assert.True(roundTripped!.Credentials.ContainsKey(said));
        // Byte-for-byte equality of the raw credential JSON string.
        // If this assertion ever fails, CachedCredentials is no longer CESR-round-trip-safe
        // and the StorageGateway JsonOptions must be revisited before shipping.
        Assert.Equal(rawCredentialJson, roundTripped.Credentials[said]);
    }

    private static ILogger? NullLogger => null;

    #endregion

    #region SetItems — Phase 4 bulk write

    [Fact]
    public async Task SetItems_OnManagedStorage_ReturnsValidationError() {
        var model = new TestPasscodeModel { Passcode = "x", SessionExpirationUtc = DateTime.UtcNow };

        var result = await _sut.SetItems(StorageArea.Managed, model);

        Assert.True(result.IsFailed);
        Assert.Contains("read-only", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetItems_EmptyValues_IsNoOp() {
        // No round-trip, no validation error.
        var result = await _sut.SetItems(StorageArea.Session);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SetItems_NullValue_ReturnsValidationError() {
        var result = await _sut.SetItems(StorageArea.Session, (IStorageModel)null!);

        Assert.True(result.IsFailed);
        Assert.Contains("null", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region WriteTransaction — Phase 4 builder form

    [Fact]
    public async Task WriteTransaction_NullBuilder_ReturnsValidationError() {
        var result = await _sut.WriteTransaction(StorageArea.Local, null!);

        Assert.True(result.IsFailed);
        Assert.Contains("null", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteTransaction_OnManagedStorage_ReturnsValidationError() {
        // Managed-storage rejection happens after the builder runs (SetItems validates),
        // but the contract is what matters: transactional writes against Managed fail.
        var result = await _sut.WriteTransaction(StorageArea.Managed, t => {
            t.SetItem(new TestPasscodeModel { Passcode = "x", SessionExpirationUtc = DateTime.UtcNow });
        });

        Assert.True(result.IsFailed);
        Assert.Contains("read-only", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteTransaction_EmptyBuilder_IsNoOp() {
        var result = await _sut.WriteTransaction(StorageArea.Session, _ => { });

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task WriteTransaction_BuilderThrows_WrapsException() {
        var result = await _sut.WriteTransaction(StorageArea.Session, _ =>
            throw new InvalidOperationException("builder boom"));

        Assert.True(result.IsFailed);
        Assert.Contains("build", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteTransaction_SetItemNullValue_Throws() {
        // IStorageTransaction.SetItem<T> rejects null at call time via ArgumentNullException,
        // and WriteTransaction wraps that into a validation Result.
        var result = await _sut.WriteTransaction(StorageArea.Session, t => {
            t.SetItem<TestPasscodeModel>(null!);
        });

        Assert.True(result.IsFailed);
    }

    #endregion


    #region Dispose

    [Fact]
    public void Dispose_DoesNotThrow() {
        var exception = Record.Exception(() => _sut.Dispose());
        Assert.Null(exception);
    }

    #endregion
}
