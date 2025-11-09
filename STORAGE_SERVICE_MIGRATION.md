# Storage Service Migration Plan

## Overview

Migrate IStorageService to a unified interface supporting all chrome.storage areas (local, session, sync, managed) with type-safe operations and WebExtensions.Net native event handling.

**Status:** Planning Complete - Ready for Implementation
**Estimated Duration:** 4 weeks
**Last Updated:** 2025-11-08

---

## Goals

1. **Unified Interface**: Single IStorageService interface with StorageArea parameter
2. **Type Safety**: All storage operations use strongly-typed record models (no string keys)
3. **No JavaScript Helpers**: Use WebExtensions.Net native `Storage.OnChanged` event handling
4. **Support All Areas**: Local, Session, Sync, and Managed storage areas
5. **Observable Pattern**: Subscribe to changes across all storage areas
6. **Validation**: Enforce invalid operation combinations (e.g., writing to Managed storage)

---

## Design Principles

### Type-Safe Models Only

**Every storage item must have a corresponding record model:**

```csharp
// ✅ CORRECT - Type-safe model
public record PasscodeModel {
    public required string Passcode { get; init; }
    public DateTime SetAtUtc { get; init; } = DateTime.UtcNow;
}
await storage.SetItem(new PasscodeModel { Passcode = "test123" }, StorageArea.Session);

// ❌ WRONG - String keys not allowed
await storage.SetItem("passcode", "test123", StorageArea.Session);
```

**Storage key is always `typeof(T).Name`:**
- `PasscodeModel` → storage key: `"PasscodeModel"`
- `Preferences` → storage key: `"Preferences"`
- `AppState` → storage key: `"AppState"`

### WebExtensions.Net Native Events

**No JavaScript helpers required:**

```csharp
// Use WebExtensions.Net's Storage.OnChanged directly
Action<object, string> callback = (changes, areaName) => OnStorageChanged(changes, areaName);
_webExtensionsApi.Storage.OnChanged.AddListener(callback);

// Delete these files:
// - Extension/wwwroot/scripts/es6/storageHelper.ts
// - Extension/wwwroot/scripts/es6/storageHelper.js
```

---

## Phase 1: Foundation (Week 1)

### 1.1 Create Storage Area Enum

**File:** `Extension/Services/Storage/StorageArea.cs`

```csharp
namespace Extension.Services.Storage;

/// <summary>
/// Chrome storage areas supported by the extension.
/// See: https://developer.chrome.com/docs/extensions/reference/api/storage
/// </summary>
public enum StorageArea {
    /// <summary>
    /// Local storage area - persisted locally, 10MB quota.
    /// Survives browser restarts and extension updates.
    /// </summary>
    Local,

    /// <summary>
    /// Session storage area - cleared when browser closes.
    /// Survives service worker restarts but not browser restart.
    /// No quota limits.
    /// </summary>
    Session,

    /// <summary>
    /// Sync storage area - synced across devices via Chrome Sync.
    /// Strict quotas: 100KB total, 8KB per item, max 512 items.
    /// Requires user to be signed in to Chrome.
    /// </summary>
    Sync,

    /// <summary>
    /// Managed storage area - READ ONLY for extensions.
    /// Set via enterprise policies. Extensions can read and listen for changes.
    /// Useful for IT-managed deployments.
    /// </summary>
    Managed
}
```

### 1.2 Create Unified IStorageService Interface

**File:** `Extension/Services/Storage/IStorageService.cs`

```csharp
namespace Extension.Services.Storage;

using FluentResults;

/// <summary>
/// Unified storage service interface supporting all chrome.storage areas.
/// All operations are type-safe using record models (no string keys).
/// </summary>
public interface IStorageService {
    /// <summary>
    /// Initialize storage change listeners for specified area.
    /// Must be called before Subscribe() will work for that area.
    /// </summary>
    Task<Result> Initialize(StorageArea area = StorageArea.Local);

    /// <summary>
    /// Clear all items in specified storage area.
    /// NOTE: Not valid for StorageArea.Managed (read-only).
    /// </summary>
    Task<Result> Clear(StorageArea area = StorageArea.Local);

    /// <summary>
    /// Remove item by type name from specified storage area.
    /// Storage key is derived from typeof(T).Name.
    /// NOTE: Not valid for StorageArea.Managed (read-only).
    /// </summary>
    Task<Result> RemoveItem<T>(StorageArea area = StorageArea.Local);

    /// <summary>
    /// Get item by type name from specified storage area.
    /// Storage key is derived from typeof(T).Name.
    /// </summary>
    Task<Result<T?>> GetItem<T>(StorageArea area = StorageArea.Local);

    /// <summary>
    /// Set item using type name as key in specified storage area.
    /// Storage key is derived from typeof(T).Name.
    /// NOTE: Not valid for StorageArea.Managed (read-only).
    /// </summary>
    Task<Result> SetItem<T>(T value, StorageArea area = StorageArea.Local);

    /// <summary>
    /// Get backup of all items in specified storage area as JSON string.
    /// </summary>
    Task<Result<string>> GetBackupItems(
        StorageArea area = StorageArea.Local,
        List<string>? excludeKeys = null
    );

    /// <summary>
    /// Restore items from backup JSON string to specified storage area.
    /// NOTE: Not valid for StorageArea.Managed (read-only).
    /// </summary>
    Task<Result> RestoreBackupItems(
        string backupJson,
        StorageArea area = StorageArea.Local
    );

    /// <summary>
    /// Get bytes used in specified storage area.
    /// Only valid for StorageArea.Local and StorageArea.Sync (have quotas).
    /// </summary>
    Task<Result<long>> GetBytesInUse(StorageArea area = StorageArea.Local);

    /// <summary>
    /// Get bytes used by specific types in specified storage area.
    /// Only valid for StorageArea.Local and StorageArea.Sync.
    /// </summary>
    Task<Result<long>> GetBytesInUse<T>(StorageArea area = StorageArea.Local);

    /// <summary>
    /// Get quota information for specified storage area.
    /// Only valid for StorageArea.Local and StorageArea.Sync.
    /// </summary>
    Task<Result<StorageQuota>> GetQuota(StorageArea area = StorageArea.Local);

    /// <summary>
    /// Subscribe to storage changes for a specific type in specified storage area.
    /// Call Initialize(area) first to enable change notifications.
    /// Works for ALL storage areas including Managed (IT policies can change).
    /// </summary>
    IDisposable Subscribe<T>(
        IObserver<T> observer,
        StorageArea area = StorageArea.Local
    );
}

/// <summary>
/// Storage quota information for areas with quota limits.
/// </summary>
public record StorageQuota {
    /// <summary>Total bytes available in this storage area</summary>
    public long QuotaBytes { get; init; }

    /// <summary>Bytes currently in use</summary>
    public long UsedBytes { get; init; }

    /// <summary>Bytes remaining</summary>
    public long RemainingBytes => QuotaBytes - UsedBytes;

    /// <summary>Percentage used (0-100)</summary>
    public double PercentUsed => QuotaBytes > 0 ? (UsedBytes * 100.0 / QuotaBytes) : 0;

    /// <summary>Max bytes per item (only for Sync storage)</summary>
    public long? MaxBytesPerItem { get; init; }

    /// <summary>Max number of items (only for Sync storage)</summary>
    public int? MaxItems { get; init; }
}
```

### 1.3 Create Validation Helper

**File:** `Extension/Services/Storage/StorageServiceValidation.cs`

```csharp
namespace Extension.Services.Storage;

using FluentResults;
using Extension.Models;

internal static class StorageServiceValidation {
    /// <summary>
    /// Operations not allowed on Managed storage (read-only for extensions)
    /// </summary>
    private static readonly HashSet<string> ManagedReadOnlyOps = new() {
        nameof(IStorageService.Clear),
        nameof(IStorageService.RemoveItem),
        nameof(IStorageService.SetItem),
        nameof(IStorageService.RestoreBackupItems)
    };

    /// <summary>
    /// Operations not allowed on Session/Managed storage (no quota tracking)
    /// </summary>
    private static readonly HashSet<string> QuotaRequiredOps = new() {
        nameof(IStorageService.GetBytesInUse),
        nameof(IStorageService.GetQuota)
    };

    public static Result ValidateOperation(string operation, StorageArea area) {
        // Managed storage is read-only
        if (area == StorageArea.Managed && ManagedReadOnlyOps.Contains(operation)) {
            return Result.Fail(new StorageError(
                $"{operation} not allowed on Managed storage area (read-only for extensions)"
            ));
        }

        // Session/Managed have no quota tracking
        if ((area == StorageArea.Session || area == StorageArea.Managed)
            && QuotaRequiredOps.Contains(operation)) {
            return Result.Fail(new StorageError(
                $"{operation} not available for {area} storage (no quota limits)"
            ));
        }

        return Result.Ok();
    }

    public static Result<T> ValidateAndFail<T>(string operation, StorageArea area) {
        var validation = ValidateOperation(operation, area);
        return validation.IsFailed
            ? Result.Fail<T>(validation.Errors)
            : throw new InvalidOperationException("Use ValidateOperation for non-generic Result");
    }
}
```

**Operation Validity Matrix:**

| Operation | Local | Session | Sync | Managed |
|-----------|-------|---------|------|---------|
| Initialize | ✅ | ✅ | ✅ | ✅ |
| Clear | ✅ | ✅ | ✅ | ❌ Read-only |
| RemoveItem | ✅ | ✅ | ✅ | ❌ Read-only |
| GetItem | ✅ | ✅ | ✅ | ✅ |
| SetItem | ✅ | ✅ | ✅ | ❌ Read-only |
| GetBackupItems | ✅ | ✅ | ✅ | ✅ |
| RestoreBackupItems | ✅ | ✅ | ✅ | ❌ Read-only |
| GetBytesInUse | ✅ | ❌ No quota | ✅ | ❌ No quota |
| GetQuota | ✅ | ❌ No quota | ✅ | ❌ No quota |
| Subscribe | ✅ | ✅ | ✅ | ✅ Important! |

### 1.4 Create Storage Models

**File:** `Extension/Models/Storage/PasscodeModel.cs`

```csharp
namespace Extension.Models.Storage;

/// <summary>
/// Passcode stored in session storage (cleared on browser close).
/// Replaces legacy string key "passcode".
/// Storage key: "PasscodeModel"
/// Storage area: Session
/// </summary>
public record PasscodeModel {
    public required string Passcode { get; init; }
    public DateTime SetAtUtc { get; init; } = DateTime.UtcNow;
}
```

**File:** `Extension/Models/Storage/InactivityTimeoutCacheModel.cs`

```csharp
namespace Extension.Models.Storage;

/// <summary>
/// Cached inactivity timeout in session storage for quick access.
/// Duplicates value from Preferences in local storage.
/// Replaces legacy string key "inactivityTimeoutMinutes".
/// Storage key: "InactivityTimeoutCacheModel"
/// Storage area: Session
/// </summary>
public record InactivityTimeoutCacheModel {
    public required float InactivityTimeoutMinutes { get; init; }
    public DateTime CachedAtUtc { get; init; } = DateTime.UtcNow;
}
```

**File:** `Extension/Models/Storage/EnterprisePolicyConfig.cs`

```csharp
namespace Extension.Models.Storage;

/// <summary>
/// Enterprise policy configuration set by IT via Managed storage.
/// IT admins configure these via Chrome Enterprise Policy JSON.
/// See: https://support.google.com/chrome/a/answer/9296680
/// Storage key: "EnterprisePolicyConfig"
/// Storage area: Managed (read-only)
/// </summary>
public record EnterprisePolicyConfig {
    /// <summary>Disable auto sign-in features</summary>
    public bool DisableAutoSignIn { get; init; }

    /// <summary>Required KERIA URL (prevents user from using other servers)</summary>
    public string? RequiredKeriaUrl { get; init; }

    /// <summary>Minimum passcode length (0 = no minimum)</summary>
    public int MinimumPasscodeLength { get; init; }

    /// <summary>Require WebAuthn for unlock (disable passcode-only)</summary>
    public bool RequireWebAuthn { get; init; }

    /// <summary>Maximum inactivity timeout in minutes (0 = no max)</summary>
    public float MaxInactivityTimeoutMinutes { get; init; }

    /// <summary>Allowed origins for auto-sign (whitelist)</summary>
    public List<string>? AllowedOrigins { get; init; }
}
```

**Existing Models (No Changes Needed):**
- `Preferences` - Already stored in local storage
- `AppState` - Already stored in local storage
- `WebsiteConfigList` - Already stored in local storage
- `KeriaConnectConfig` - Already stored in local storage

### 1.5 Implement StorageService

**File:** `Extension/Services/Storage/StorageService.cs`

Key implementation points:

1. **Use WebExtensions.Net native events:**
   ```csharp
   Action<object, string> callback = (changes, areaName) => OnStorageChanged(changes, areaName);
   _webExtensionsApi.Storage.OnChanged.AddListener(callback);
   ```

2. **No DotNetObjectReference needed** - Use normal C# callbacks

3. **Store callback references** to prevent garbage collection:
   ```csharp
   private readonly Dictionary<StorageArea, Action<object, string>> _callbacks = new();
   ```

4. **Remove listeners on Dispose:**
   ```csharp
   public void Dispose() {
       foreach (var (area, callback) in _callbacks) {
           _webExtensionsApi.Storage.OnChanged.RemoveListener(callback);
       }
   }
   ```

5. **Validate operations** before executing:
   ```csharp
   var validation = StorageServiceValidation.ValidateOperation(nameof(SetItem), area);
   if (validation.IsFailed) return validation;
   ```

See full implementation in previous conversation response.

### 1.6 Update DI Registration

**File:** `Extension/Program.cs`

```csharp
// Add new storage service
builder.Services.AddSingleton<IStorageService, StorageService>();

// Backward compatibility during migration (optional - remove after Phase 4)
// Keep old interface pointing to new implementation
```

### 1.7 Delete JavaScript Helpers

**Files to DELETE:**
- `Extension/wwwroot/scripts/es6/storageHelper.ts`
- `Extension/wwwroot/scripts/es6/storageHelper.js` (if compiled)

**Update:** `Extension/wwwroot/app.ts`

Remove this import line:
```typescript
// DELETE THIS LINE:
import('./scripts/es6/storageHelper.js'),
```

---

## Phase 2: Migration (Week 2)

### 2.1 Migrate Passcode Storage

**Files to Update:**

1. **UnlockPage.razor** (~line 246)
   ```csharp
   // BEFORE
   await webExtensionsApi.Storage.Session.Set(new { passcode = password });

   // AFTER
   @inject IStorageService Storage
   var result = await Storage.SetItem(
       new PasscodeModel { Passcode = password },
       StorageArea.Session
   );
   if (result.IsFailed) {
       // Handle error
   }
   ```

2. **BackgroundWorker.cs** (~line 1691)
   ```csharp
   // BEFORE
   var passcodeResult = await _webExtensionsApi.Storage.Session.Get("passcode");
   var passcodeJsonElement = passcodeResult.GetProperty("passcode"u8);
   var passcode = passcodeJsonElement.GetString();

   // AFTER
   private readonly IStorageService _storage;

   var result = await _storage.GetItem<PasscodeModel>(StorageArea.Session);
   if (result.IsFailed || result.Value == null) {
       return Result.Fail("Passcode not found");
   }
   var passcode = result.Value.Passcode;
   ```

3. **StateService.cs** (~line 127)
   ```csharp
   // BEFORE
   await webExtensionsApi.Storage.Session.Remove("passcode");

   // AFTER
   await _storage.RemoveItem<PasscodeModel>(StorageArea.Session);
   ```

4. **WebauthnService.cs** (~line 148)
   ```csharp
   // BEFORE
   var passcodeElement = await webExtensionsApi!.Storage.Session.Get("passcode");

   // AFTER
   var result = await _storage.GetItem<PasscodeModel>(StorageArea.Session);
   var passcode = result.Value?.Passcode;
   ```

**Other Files with Passcode Access:**
- DeletePage.razor (~line 59)
- App.razor (~line 88)

**TypeScript Files (Keep Direct Access):**
- `signifyClient.ts` (~line 89) - Can continue using `chrome.storage.session.get('PasscodeModel')` directly
  - Note: Key changes from `"passcode"` to `"PasscodeModel"`

### 2.2 Migrate InactivityTimeout Cache

**File:** `PreferencesService.cs` (~line 64)

```csharp
// BEFORE
var data = new Dictionary<string, object?> {
    { "inactivityTimeoutMinutes", preferences.InactivityTimeoutMinutes }
};
await webExtensionsApi!.Storage.Session.Set(data);

// AFTER
private readonly IStorageService _storage;

await _storage.SetItem(
    new InactivityTimeoutCacheModel {
        InactivityTimeoutMinutes = preferences.InactivityTimeoutMinutes
    },
    StorageArea.Session
);

// To retrieve:
var result = await _storage.GetItem<InactivityTimeoutCacheModel>(StorageArea.Session);
var timeout = result.Value?.InactivityTimeoutMinutes ?? 5.0f;
```

### 2.3 Update Local Storage Usage

**Files Already Using IStorageService:**
- StateService.cs
- PreferencesService.cs
- WebsiteConfigService.cs

**Changes Needed:**
Minimal - just add explicit `StorageArea.Local` parameter (optional, as it's the default):

```csharp
// BEFORE
await storageService.GetItem<Preferences>();

// AFTER (optional - Local is default)
await storageService.GetItem<Preferences>(StorageArea.Local);
```

### 2.4 Update signifyClient.ts

**File:** `Extension/wwwroot/scripts/esbuild/signifyClient.ts` (~line 89)

```typescript
// BEFORE
const passcode = (await chrome.storage.session.get('passcode'))?.passcode as unknown as string;

// AFTER - Update key name to match model type
const result = await chrome.storage.session.get('PasscodeModel');
const passcode = result?.PasscodeModel?.Passcode as unknown as string;
```

### 2.5 Grep for Remaining Direct Storage Calls

**Run these commands to find remaining direct usage:**

```bash
# Find C# direct storage calls
grep -r "webExtensionsApi.Storage\." Extension/ --include="*.cs" --include="*.razor" | grep -v "StorageService.cs"

# Find TypeScript direct storage calls
grep -r "chrome.storage" Extension/wwwroot/ --include="*.ts" --include="*.js"
```

**Review each result** and migrate to IStorageService or update to use new model names.

---

## Phase 3: Testing (Week 3)

### 3.1 Create Unit Tests

**File:** `Extension.Tests/Services/Storage/StorageServiceTests.cs`

```csharp
namespace Extension.Tests.Services.Storage;

using Extension.Services.Storage;
using Extension.Models.Storage;
using FluentResults;
using Moq;
using Xunit;
using WebExtensions.Net;

public class StorageServiceTests {
    private readonly Mock<WebExtensionsApi> _mockWebExtensions;
    private readonly Mock<IJSRuntime> _mockJsRuntime;
    private readonly Mock<ILogger<StorageService>> _mockLogger;
    private readonly StorageService _sut;

    public StorageServiceTests() {
        _mockWebExtensions = new Mock<WebExtensionsApi>();
        _mockJsRuntime = new Mock<IJSRuntime>();
        _mockLogger = new Mock<ILogger<StorageService>>();
        _sut = new StorageService(
            _mockWebExtensions.Object,
            _mockJsRuntime.Object,
            _mockLogger.Object
        );
    }

    [Theory]
    [InlineData(StorageArea.Local)]
    [InlineData(StorageArea.Session)]
    [InlineData(StorageArea.Sync)]
    [InlineData(StorageArea.Managed)]
    public async Task Initialize_AllAreas_Succeeds(StorageArea area) {
        // Arrange
        var mockOnChanged = new Mock<OnChangedEvent>();
        _mockWebExtensions.Setup(x => x.Storage.OnChanged).Returns(mockOnChanged.Object);

        // Act
        var result = await _sut.Initialize(area);

        // Assert
        Assert.True(result.IsSuccess);
        mockOnChanged.Verify(x => x.AddListener(It.IsAny<Action<object, string>>()), Times.Once);
    }

    [Fact]
    public async Task SetItem_OnManagedStorage_ReturnsError() {
        // Arrange
        var model = new PasscodeModel { Passcode = "test123" };

        // Act
        var result = await _sut.SetItem(model, StorageArea.Managed);

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("read-only", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetBytesInUse_OnSessionStorage_ReturnsError() {
        // Act
        var result = await _sut.GetBytesInUse(StorageArea.Session);

        // Assert
        Assert.True(result.IsFailed);
        Assert.Contains("no quota", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetItem_WithPasscodeModel_CallsSessionStorageSet() {
        // Arrange
        var model = new PasscodeModel { Passcode = "test123" };
        var mockStorage = new Mock<WebExtensions.Net.Storage.StorageArea>();
        _mockWebExtensions.Setup(x => x.Storage.Session).Returns(mockStorage.Object);

        // Act
        var result = await _sut.SetItem(model, StorageArea.Session);

        // Assert
        Assert.True(result.IsSuccess);
        mockStorage.Verify(
            x => x.Set(It.Is<Dictionary<string, object?>>(
                d => d.ContainsKey("PasscodeModel") && d["PasscodeModel"] == model
            )),
            Times.Once
        );
    }

    [Fact]
    public async Task GetItem_WithPasscodeModel_ReturnsStoredValue() {
        // Arrange
        var expectedPasscode = "test123";
        var model = new PasscodeModel { Passcode = expectedPasscode };
        var jsonElement = JsonSerializer.SerializeToElement(model);

        var mockStorage = new Mock<WebExtensions.Net.Storage.StorageArea>();
        mockStorage.Setup(x => x.Get(It.IsAny<StorageAreaGetKeys>()))
            .ReturnsAsync(CreateJsonElementWithProperty("PasscodeModel", jsonElement));
        _mockWebExtensions.Setup(x => x.Storage.Session).Returns(mockStorage.Object);

        // Act
        var result = await _sut.GetItem<PasscodeModel>(StorageArea.Session);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(expectedPasscode, result.Value.Passcode);
    }

    [Fact]
    public async Task Subscribe_ToManagedStorage_ReceivesNotifications() {
        // Arrange
        var mockOnChanged = new Mock<OnChangedEvent>();
        _mockWebExtensions.Setup(x => x.Storage.OnChanged).Returns(mockOnChanged.Object);
        await _sut.Initialize(StorageArea.Managed);

        var observer = new Mock<IObserver<EnterprisePolicyConfig>>();
        var subscription = _sut.Subscribe(observer.Object, StorageArea.Managed);

        // Simulate IT policy change
        var changes = new Dictionary<string, object> {
            ["EnterprisePolicyConfig"] = new {
                newValue = new EnterprisePolicyConfig { MinimumPasscodeLength = 12 }
            }
        };

        // Get the callback that was registered
        Action<object, string>? registeredCallback = null;
        mockOnChanged.Setup(x => x.AddListener(It.IsAny<Action<object, string>>()))
            .Callback<Action<object, string>>(cb => registeredCallback = cb);

        await _sut.Initialize(StorageArea.Managed);

        // Act - Trigger the callback
        registeredCallback?.Invoke(changes, "managed");

        // Assert
        observer.Verify(x => x.OnNext(It.Is<EnterprisePolicyConfig>(
            p => p.MinimumPasscodeLength == 12
        )), Times.Once);
    }

    private static JsonElement CreateJsonElementWithProperty(string propertyName, JsonElement value) {
        var dict = new Dictionary<string, JsonElement> { [propertyName] = value };
        var json = JsonSerializer.Serialize(dict);
        return JsonDocument.Parse(json).RootElement;
    }
}
```

**File:** `Extension.Tests/Services/Storage/StorageServiceValidationTests.cs`

```csharp
public class StorageServiceValidationTests {
    [Theory]
    [InlineData(nameof(IStorageService.SetItem), StorageArea.Managed, false)]
    [InlineData(nameof(IStorageService.SetItem), StorageArea.Local, true)]
    [InlineData(nameof(IStorageService.GetBytesInUse), StorageArea.Session, false)]
    [InlineData(nameof(IStorageService.GetBytesInUse), StorageArea.Local, true)]
    [InlineData(nameof(IStorageService.GetItem), StorageArea.Managed, true)]
    public void ValidateOperation_ChecksValidCombinations(
        string operation,
        StorageArea area,
        bool expectedValid
    ) {
        // Act
        var result = StorageServiceValidation.ValidateOperation(operation, area);

        // Assert
        Assert.Equal(expectedValid, result.IsSuccess);
    }
}
```

### 3.2 Manual Testing Checklist

**Session Storage:**
- [ ] Unlock page stores passcode
- [ ] BackgroundWorker retrieves passcode for KERIA operations
- [ ] Passcode cleared on lock/timeout
- [ ] Session storage cleared on browser close (not just tab close)
- [ ] Session storage persists across service worker restarts
- [ ] InactivityTimeout cache works correctly

**Local Storage:**
- [ ] Preferences save and load correctly
- [ ] AppState persists across browser restarts
- [ ] WebsiteConfigList CRUD operations work
- [ ] Observable pattern triggers UI updates on Preferences changes
- [ ] Backup/restore excludes AppState correctly

**Sync Storage (if implemented):**
- [ ] Preferences sync across devices (requires Chrome sign-in)
- [ ] Quota limits enforced (100KB total, 8KB per item)
- [ ] Sync conflicts handled gracefully

**Managed Storage (if implemented):**
- [ ] Enterprise policy loads on startup
- [ ] Policy changes trigger observers
- [ ] Cannot write to managed storage (validation error)
- [ ] Test with sample enterprise policy JSON

**Change Notifications:**
- [ ] Subscribe() receives updates for all storage areas
- [ ] Multiple observers for same type all notified
- [ ] Unsubscribe() stops notifications
- [ ] Initialize() required before Subscribe() works

**Quota Monitoring:**
- [ ] GetBytesInUse() returns accurate sizes
- [ ] GetQuota() shows correct limits per area
- [ ] Quota APIs fail for Session/Managed areas

### 3.3 TypeScript Integration Test

**Verify signifyClient.ts still works:**

```typescript
// Test in browser console after loading extension
const result = await chrome.storage.session.get('PasscodeModel');
console.log('Passcode:', result?.PasscodeModel?.Passcode);

// Should return passcode if unlocked, undefined if locked
```

---

## Phase 4: Cleanup & Documentation (Week 4)

### 4.1 Remove Old IStorageService (If Refactoring)

**Option 1: Keep Interface Name (Recommended)**
- New implementation replaces old one
- No breaking changes for consumers
- Remove `GetAppHostingKind()` method (always returned hardcoded value)

**Option 2: Deprecate Old Interface**
```csharp
[Obsolete("Replaced by unified IStorageService with StorageArea parameter. Will be removed in v2.0.")]
public interface ILegacyStorageService : IObservable<Preferences> {
    // Old methods...
}
```

### 4.2 Verify No Direct WebExtensions.Net Storage Calls

```bash
# Should only find StorageService.cs
grep -r "webExtensionsApi.Storage\." Extension/ --include="*.cs" --include="*.razor"

# TypeScript can still use chrome.storage directly (acceptable)
grep -r "chrome.storage" Extension/wwwroot/ --include="*.ts"
```

**All C# code should use IStorageService**, except:
- StorageService.cs implementation itself
- Test mocks

### 4.3 Update CLAUDE.md Documentation

Add new section after "## C#-TypeScript Interop Guidance":

````markdown
## Storage Service Architecture

The extension uses a unified `IStorageService` interface supporting all chrome.storage areas: Local, Session, Sync, and Managed.

### Storage Areas

| Area | Persistence | Quota | Use Cases |
|------|-------------|-------|-----------|
| Local | Until cleared | 10MB | Preferences, app state, credentials |
| Session | Browser close | Unlimited | Passcode cache, temporary state |
| Sync | Cross-device | 100KB total, 8KB/item | User preferences (if sync enabled) |
| Managed | Read-only | N/A | Enterprise policies (IT configured) |

### Type-Safe Storage Pattern

**All storage operations use strongly-typed record models:**

```csharp
// ✅ CORRECT - Type-safe model
public record PasscodeModel {
    public required string Passcode { get; init; }
}

await _storage.SetItem(new PasscodeModel { Passcode = "test123" }, StorageArea.Session);
var result = await _storage.GetItem<PasscodeModel>(StorageArea.Session);

// ❌ WRONG - String keys not supported
await _storage.SetItem("passcode", "test123", StorageArea.Session);
```

**Storage key is always `typeof(T).Name`:**
- `PasscodeModel` → key: `"PasscodeModel"`
- `Preferences` → key: `"Preferences"`

### Storage Models Location

**All storage models are records in `Extension/Models/Storage/`:**

- **Session Storage:**
  - `PasscodeModel` - Temporary passcode cache
  - `InactivityTimeoutCacheModel` - Cached timeout value

- **Local Storage:**
  - `Preferences` - User preferences (observable)
  - `AppState` - Application state
  - `WebsiteConfigList` - Website configurations
  - `KeriaConnectConfig` - KERIA connection settings

- **Managed Storage:**
  - `EnterprisePolicyConfig` - IT policies (read-only)

### Observable Pattern

Subscribe to storage changes for any type in any area:

```csharp
public class MyService : IObserver<Preferences> {
    private IDisposable? _subscription;

    public async Task Initialize(IStorageService storage) {
        // Initialize listener first
        await storage.Initialize(StorageArea.Local);

        // Subscribe to changes
        _subscription = storage.Subscribe<Preferences>(this, StorageArea.Local);
    }

    public void OnNext(Preferences value) {
        // Handle preference changes
    }
}
```

**Important:** Call `Initialize(area)` before `Subscribe()` for that area.

### Managed Storage - Enterprise Policies

IT administrators can configure extension behavior via Managed storage:

**Example Enterprise Policy (set via Group Policy):**
```json
{
  "3rdparty": {
    "extensions": {
      "YOUR_EXTENSION_ID": {
        "EnterprisePolicyConfig": {
          "RequiredKeriaUrl": "https://keria.company.com",
          "MinimumPasscodeLength": 12,
          "RequireWebAuthn": true,
          "MaxInactivityTimeoutMinutes": 5.0
        }
      }
    }
  }
}
```

**Extension reads and reacts to policy changes:**
```csharp
var result = await _storage.GetItem<EnterprisePolicyConfig>(StorageArea.Managed);
if (result.IsSuccess && result.Value != null) {
    ApplyEnterprisePolicy(result.Value);
}

// Monitor for IT policy updates
_storage.Subscribe<EnterprisePolicyConfig>(this, StorageArea.Managed);
```

### Quota Management

**Check storage usage:**
```csharp
var quota = await _storage.GetQuota(StorageArea.Local);
if (quota.IsSuccess) {
    var pct = quota.Value.PercentUsed;
    if (pct > 80) {
        _logger.LogWarning("Local storage {pct}% full", pct);
    }
}
```

**Quota Limits:**
- **Local:** 10MB (10,485,760 bytes)
- **Sync:** 100KB total, 8KB per item, max 512 items
- **Session/Managed:** No quota tracking

### Validation

Invalid operations return `Result.Fail()`:

```csharp
// ❌ Cannot write to Managed storage
var result = await _storage.SetItem(config, StorageArea.Managed);
// result.IsFailed == true, error: "read-only for extensions"

// ❌ Cannot check quota on Session storage
var quota = await _storage.GetQuota(StorageArea.Session);
// quota.IsFailed == true, error: "no quota limits"
```

### WebExtensions.Net Native Events

The implementation uses WebExtensions.Net's `Storage.OnChanged` event directly - **no JavaScript helpers required**.

**Removed files:**
- ~~`wwwroot/scripts/es6/storageHelper.ts`~~ (deleted)
- ~~`wwwroot/scripts/es6/storageHelper.js`~~ (deleted)

Change notifications are handled entirely in C# via callbacks.
````

### 4.4 Update TypeScript Documentation

Add note to TypeScript section in CLAUDE.md:

````markdown
### TypeScript Storage Access

TypeScript code can continue using `chrome.storage` API directly:

```typescript
// Accessing storage from TypeScript
const result = await chrome.storage.session.get('PasscodeModel');
const passcode = result?.PasscodeModel?.Passcode;

// Note: Storage keys match C# model type names
// PasscodeModel in C# → 'PasscodeModel' key in storage
```

**Key Naming Convention:**
C# models use `typeof(T).Name` as storage key, so TypeScript must use matching names:
- C#: `PasscodeModel` → JS: `'PasscodeModel'`
- C#: `Preferences` → JS: `'Preferences'`
````

### 4.5 Create Storage Quota Dashboard (Optional)

**File:** `Extension/UI/Pages/StorageQuotaPage.razor`

```razor
@page "/storage-quota"
@inject IStorageService Storage

<MudContainer>
    <MudText Typo="Typo.h4">Storage Quota</MudText>

    @if (_localQuota != null) {
        <MudCard Class="my-4">
            <MudCardContent>
                <MudText Typo="Typo.h6">Local Storage</MudText>
                <MudProgressLinear
                    Value="_localQuota.PercentUsed"
                    Color="@GetColor(_localQuota.PercentUsed)"
                    Class="my-2">
                </MudProgressLinear>
                <MudText>
                    @FormatBytes(_localQuota.UsedBytes) / @FormatBytes(_localQuota.QuotaBytes)
                    (@_localQuota.PercentUsed.ToString("F1")%)
                </MudText>
            </MudCardContent>
        </MudCard>
    }

    @if (_syncQuota != null) {
        <MudCard Class="my-4">
            <MudCardContent>
                <MudText Typo="Typo.h6">Sync Storage</MudText>
                <MudProgressLinear
                    Value="_syncQuota.PercentUsed"
                    Color="@GetColor(_syncQuota.PercentUsed)"
                    Class="my-2">
                </MudProgressLinear>
                <MudText>
                    @FormatBytes(_syncQuota.UsedBytes) / @FormatBytes(_syncQuota.QuotaBytes)
                    (@_syncQuota.PercentUsed.ToString("F1")%)
                </MudText>
                <MudText Typo="Typo.caption">
                    Max @_syncQuota.MaxItems items, @FormatBytes(_syncQuota.MaxBytesPerItem ?? 0) per item
                </MudText>
            </MudCardContent>
        </MudCard>
    }

    @if (_error != null) {
        <MudAlert Severity="Severity.Error">@_error</MudAlert>
    }
</MudContainer>

@code {
    private StorageQuota? _localQuota;
    private StorageQuota? _syncQuota;
    private string? _error;

    protected override async Task OnInitializedAsync() {
        await LoadQuotas();
    }

    private async Task LoadQuotas() {
        var localResult = await Storage.GetQuota(StorageArea.Local);
        if (localResult.IsSuccess) {
            _localQuota = localResult.Value;
        } else {
            _error = $"Local quota error: {localResult.Errors[0].Message}";
        }

        var syncResult = await Storage.GetQuota(StorageArea.Sync);
        if (syncResult.IsSuccess) {
            _syncQuota = syncResult.Value;
        }
    }

    private static Color GetColor(double percentUsed) => percentUsed switch {
        < 50 => Color.Success,
        < 80 => Color.Warning,
        _ => Color.Error
    };

    private static string FormatBytes(long bytes) {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1) {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
```

### 4.6 Enterprise Policy Service (Optional)

**File:** `Extension/Services/EnterprisePolicyService.cs`

```csharp
namespace Extension.Services;

/// <summary>
/// Monitors and applies enterprise policies from Managed storage.
/// </summary>
public class EnterprisePolicyService : IObserver<EnterprisePolicyConfig>, IAsyncDisposable {
    private readonly IStorageService _storage;
    private readonly ILogger<EnterprisePolicyService> _logger;
    private IDisposable? _subscription;
    private EnterprisePolicyConfig? _currentPolicy;

    public EnterprisePolicyConfig? CurrentPolicy => _currentPolicy;

    public EnterprisePolicyService(
        IStorageService storage,
        ILogger<EnterprisePolicyService> logger
    ) {
        _storage = storage;
        _logger = logger;
    }

    public async Task<Result> Initialize() {
        var initResult = await _storage.Initialize(StorageArea.Managed);
        if (initResult.IsFailed) {
            _logger.LogWarning("Failed to initialize managed storage: {Errors}",
                string.Join(", ", initResult.Errors));
            return initResult;
        }

        _subscription = _storage.Subscribe<EnterprisePolicyConfig>(this, StorageArea.Managed);

        var policyResult = await _storage.GetItem<EnterprisePolicyConfig>(StorageArea.Managed);
        if (policyResult.IsSuccess && policyResult.Value != null) {
            _currentPolicy = policyResult.Value;
            _logger.LogInformation("Loaded enterprise policy: {@Policy}", _currentPolicy);
            ApplyPolicy(_currentPolicy);
        } else {
            _logger.LogInformation("No enterprise policy configured");
        }

        return Result.Ok();
    }

    public void OnNext(EnterprisePolicyConfig value) {
        _logger.LogInformation("Enterprise policy updated: {@Policy}", value);
        _currentPolicy = value;
        ApplyPolicy(value);
    }

    public void OnError(Exception error) {
        _logger.LogError(error, "Error receiving enterprise policy updates");
    }

    public void OnCompleted() {
        _logger.LogInformation("Enterprise policy monitoring completed");
    }

    private void ApplyPolicy(EnterprisePolicyConfig policy) {
        if (policy.DisableAutoSignIn) {
            _logger.LogWarning("Auto sign-in disabled by enterprise policy");
        }

        if (!string.IsNullOrEmpty(policy.RequiredKeriaUrl)) {
            _logger.LogInformation("KERIA URL enforced by policy: {Url}",
                policy.RequiredKeriaUrl);
        }

        if (policy.MinimumPasscodeLength > 0) {
            _logger.LogInformation("Minimum passcode length enforced: {Length}",
                policy.MinimumPasscodeLength);
        }
    }

    public async ValueTask DisposeAsync() {
        _subscription?.Dispose();
        await Task.CompletedTask;
    }
}
```

**Register in Program.cs:**
```csharp
// BackgroundWorker context only
if (mode == BrowserExtensionMode.Background) {
    builder.Services.AddSingleton<EnterprisePolicyService>();
}
```

---

## Migration Checklist Summary

### Phase 1: Foundation ✅
- [ ] Create `StorageArea` enum
- [ ] Create `IStorageService` interface
- [ ] Create `StorageServiceValidation` helper
- [ ] Create storage models (PasscodeModel, InactivityTimeoutCacheModel, EnterprisePolicyConfig)
- [ ] Implement `StorageService` with WebExtensions.Net events
- [ ] Update DI registration
- [ ] Delete JavaScript helpers (storageHelper.ts/js)
- [ ] Update app.ts to remove storageHelper import

### Phase 2: Migration ✅
- [ ] Migrate passcode storage in 11+ files
- [ ] Migrate inactivity timeout cache
- [ ] Update local storage usage (minimal changes)
- [ ] Update signifyClient.ts to use new key names
- [ ] Grep and verify no remaining direct storage calls

### Phase 3: Testing ✅
- [ ] Create unit tests for StorageService
- [ ] Create validation tests
- [ ] Manual testing: Session storage
- [ ] Manual testing: Local storage
- [ ] Manual testing: Observable pattern
- [ ] Manual testing: Quota monitoring
- [ ] TypeScript integration test

### Phase 4: Cleanup ✅
- [ ] Remove old IStorageService or deprecate
- [ ] Verify no direct WebExtensions.Net storage calls
- [ ] Update CLAUDE.md documentation
- [ ] Create storage quota dashboard (optional)
- [ ] Implement EnterprisePolicyService (optional)

---

## File Structure Reference

```
Extension/
├── Models/
│   └── Storage/
│       ├── PasscodeModel.cs                    [NEW]
│       ├── InactivityTimeoutCacheModel.cs      [NEW]
│       └── EnterprisePolicyConfig.cs           [NEW]
├── Services/
│   ├── Storage/
│   │   ├── StorageArea.cs                      [NEW]
│   │   ├── IStorageService.cs                  [REPLACE]
│   │   ├── StorageService.cs                   [REPLACE]
│   │   └── StorageServiceValidation.cs         [NEW]
│   ├── EnterprisePolicyService.cs              [NEW - Optional]
│   ├── StateService.cs                         [MODIFY]
│   ├── PreferencesService.cs                   [MODIFY]
│   └── WebsiteConfigService.cs                 [MODIFY]
├── UI/
│   ├── Pages/
│   │   ├── UnlockPage.razor                    [MODIFY]
│   │   ├── DeletePage.razor                    [MODIFY]
│   │   └── StorageQuotaPage.razor              [NEW - Optional]
│   └── App.razor                               [MODIFY]
├── BackgroundWorker.cs                          [MODIFY]
├── Program.cs                                   [MODIFY]
└── wwwroot/
    ├── app.ts                                   [MODIFY]
    └── scripts/
        ├── es6/
        │   ├── storageHelper.ts                 [DELETE]
        │   └── storageHelper.js                 [DELETE]
        └── esbuild/
            └── signifyClient.ts                 [MODIFY]

Extension.Tests/
└── Services/
    └── Storage/
        ├── StorageServiceTests.cs               [NEW]
        └── StorageServiceValidationTests.cs     [NEW]
```

---

## Key Design Decisions

### 1. Why Unified Interface?
- Single service injection instead of 4 separate services
- Runtime selection of storage area
- Consistent API across all areas
- Easier testing with single mock

### 2. Why Type-Safe Models Only?
- Prevents typos in storage keys (e.g., "passode" vs "passcode")
- Compile-time safety
- Better IntelliSense
- Self-documenting code
- Easy to find all storage models in one location

### 3. Why WebExtensions.Net Events (No JavaScript)?
- Native C# event handling
- No DotNetObjectReference lifecycle issues
- No JavaScript interop overhead
- Simpler architecture
- Fewer files to maintain

### 4. Why Support Managed Storage?
- Enterprise deployments need IT policy control
- Read-only for extension, set via Group Policy
- Observable pattern allows dynamic policy updates
- Differentiator for enterprise customers

---

## Risk Mitigation

### Breaking Changes
**Risk:** Passcode key changes from `"passcode"` to `"PasscodeModel"`
**Mitigation:**
- Migration runs on first launch
- Detect old key, copy to new model, delete old key
- Log migration for debugging

**Code:**
```csharp
// In StorageService or BackgroundWorker initialization
var oldPasscode = await _jsRuntime.InvokeAsync<JsonElement>(
    "chrome.storage.session.get", "passcode"
);
if (oldPasscode.TryGetProperty("passcode"u8, out var passcodeValue)) {
    var passcodeString = passcodeValue.GetString();
    if (!string.IsNullOrEmpty(passcodeString)) {
        await _storage.SetItem(
            new PasscodeModel { Passcode = passcodeString },
            StorageArea.Session
        );
        await _jsRuntime.InvokeVoidAsync("chrome.storage.session.remove", "passcode");
        _logger.LogInformation("Migrated passcode from old key to PasscodeModel");
    }
}
```

### Testing Gaps
**Risk:** Cannot fully test WebExtensions.Net in unit tests
**Mitigation:**
- Use mocks for unit tests
- Integration tests in actual browser extension
- Manual testing checklist
- Monitor production errors

### Performance
**Risk:** Observable pattern overhead for frequent changes
**Mitigation:**
- Observers only trigger for subscribed types
- Filter changes by storage area
- Dispose subscriptions when not needed

---

## Success Metrics

- [ ] Zero direct `webExtensionsApi.Storage.*` calls in C# (except StorageService.cs)
- [ ] All session storage uses typed models
- [ ] Unit test coverage >80% for StorageService
- [ ] Manual testing checklist 100% complete
- [ ] No regression in existing functionality
- [ ] Documentation updated in CLAUDE.md
- [ ] Enterprise policy support working (if implemented)

---

## Future Enhancements

### 1. Storage Encryption
Encrypt sensitive data at rest:
```csharp
public interface IEncryptedStorageService : IStorageService {
    Task<Result> SetEncryptedItem<T>(T value, StorageArea area);
    Task<Result<T?>> GetEncryptedItem<T>(StorageArea area);
}
```

### 2. Storage Migration Helper
Automated migration from old keys to new models:
```csharp
public class StorageMigrationService {
    public async Task<Result> MigrateToV2();
}
```

### 3. Quota Warnings
Automatic notifications when approaching limits:
```csharp
public event EventHandler<QuotaWarningEventArgs>? QuotaWarningThresholdReached;
```

### 4. Storage Analytics
Track storage usage patterns:
```csharp
public record StorageAnalytics {
    public int ReadCount { get; init; }
    public int WriteCount { get; init; }
    public Dictionary<string, int> PopularKeys { get; init; }
}
```

---

## References

- [Chrome Storage API](https://developer.chrome.com/docs/extensions/reference/api/storage)
- [WebExtensions.Net Documentation](https://github.com/mingyaulee/WebExtensions.Net)
- [Chrome Enterprise Policies](https://support.google.com/chrome/a/answer/9296680)
- [FluentResults GitHub](https://github.com/altmann/FluentResults)

---

**End of Migration Plan**
