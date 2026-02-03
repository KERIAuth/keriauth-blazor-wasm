# Plan: Multi-KERIA Configuration Support

## Overview
Refactor the extension to support multiple KERIA Cloud Service configurations, allowing users to switch between different KERIA connections. This involves storage model changes, UI updates to UnlockPage and PreferencesPage, a new management page, and status indicator updates.

**Assumptions:**
- Clean install (no migration from existing single-config storage)
- No backward compatibility with old `KeriaConnectConfig` key

---

## Implementation Phases

Each phase is designed to be compilable and testable before proceeding to the next.

---

## Phase 1: New Models and Utility Classes

**Goal:** Create all new model files and utility classes. Builds and compiles but doesn't integrate with existing code yet.

### 1.1 Create `KeriaPreference` Model
**File:** `Extension/Models/KeriaPreference.cs` (new file)

```csharp
namespace Extension.Models;

using System.Text.Json.Serialization;

public record KeriaPreference {
    /// <summary>
    /// The KeriaConnectionDigest of the currently selected KERIA configuration.
    /// </summary>
    [JsonPropertyName("SelectedKeriaConnectionDigest")]
    public string? SelectedKeriaConnectionDigest { get; init; }

    /// <summary>
    /// The selected AID prefix within the selected KERIA configuration.
    /// </summary>
    [JsonPropertyName("SelectedPrefix")]
    public string SelectedPrefix { get; init; } = string.Empty;
}
```

### 1.2 Create `KeriaConnectConfigs` Storage Model
**File:** `Extension/Models/KeriaConnectConfigs.cs` (new file)

```csharp
namespace Extension.Models;

using System.Text.Json.Serialization;
using Extension.Models.Storage;

public record KeriaConnectConfigs : IStorageModel {
    /// <summary>
    /// Dictionary of KeriaConnectConfig items keyed by their computed KeriaConnectionDigest.
    /// The digest is computed as SHA256(ClientAidPrefix + AgentAidPrefix + PasscodeHash).
    /// </summary>
    [JsonPropertyName("Configs")]
    public Dictionary<string, KeriaConnectConfig> Configs { get; init; } = new();

    [JsonPropertyName("IsStored")]
    public bool IsStored { get; init; }
}
```

### 1.3 Create `KeriaConnectionDigestHelper` Utility
**File:** `Extension/Utilities/KeriaConnectionDigestHelper.cs` (new file)

Extract `ComputeKeriaConnectionDigest` from WebauthnService into a static helper:

```csharp
namespace Extension.Utilities;

using System.Security.Cryptography;
using System.Text;
using Extension.Models;
using FluentResults;

public static class KeriaConnectionDigestHelper {
    /// <summary>
    /// Computes the KeriaConnectionDigest as a hex-encoded SHA256 hash of
    /// ClientAidPrefix + AgentAidPrefix + PasscodeHash.
    /// </summary>
    public static Result<string> Compute(KeriaConnectConfig config) {
        if (string.IsNullOrWhiteSpace(config.ClientAidPrefix)) {
            return Result.Fail<string>("ClientAidPrefix is required to compute KeriaConnectionDigest");
        }
        if (string.IsNullOrWhiteSpace(config.AgentAidPrefix)) {
            return Result.Fail<string>("AgentAidPrefix is required to compute KeriaConnectionDigest");
        }
        if (config.PasscodeHash == 0) {
            return Result.Fail<string>("PasscodeHash is required to compute KeriaConnectionDigest");
        }

        var input = config.ClientAidPrefix + config.AgentAidPrefix +
                    config.PasscodeHash.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var hexString = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return Result.Ok(hexString);
    }
}
```

### 1.4 Unit Tests for Phase 1
**File:** `Extension.Tests/Utilities/KeriaConnectionDigestHelperTests.cs` (new file)

```csharp
public class KeriaConnectionDigestHelperTests {
    [Fact]
    public void Compute_ValidConfig_ReturnsHexDigest() { ... }

    [Fact]
    public void Compute_MissingClientAidPrefix_ReturnsFail() { ... }

    [Fact]
    public void Compute_MissingAgentAidPrefix_ReturnsFail() { ... }

    [Fact]
    public void Compute_ZeroPasscodeHash_ReturnsFail() { ... }

    [Fact]
    public void Compute_SameInputs_ReturnsSameDigest() { ... }
}
```

**Verification:** Build with `/build-windows`, run `dotnet test`

---

## Phase 2: Update Preferences Model

**Goal:** Add new preference properties. Existing functionality remains unchanged.

### 2.1 Update `Preferences` Model
**File:** `Extension/Models/Preferences.cs`

Add new properties:

```csharp
[JsonPropertyName("IsMultiKeriaConfigEnabled")]
public bool IsMultiKeriaConfigEnabled { get; init; } = true;

[JsonPropertyName("IsMultiKeriaOnUnlock")]
public bool IsMultiKeriaOnUnlock { get; init; } = true;

[JsonPropertyName("KeriaPreference")]
public KeriaPreference KeriaPreference { get; init; } = new();
```

Update `SelectedPrefix` to be a computed backward-compatible property:

```csharp
// Backward compatibility - reads from KeriaPreference
[JsonIgnore]
public string SelectedPrefix => KeriaPreference.SelectedPrefix;
```

**Note:** Remove the existing `SelectedPrefix` property with `[JsonPropertyName]` and replace with the `[JsonIgnore]` computed version.

### 2.2 Update `AppConfig.DefaultPreferences`
**File:** `Extension/AppConfig.cs`

Ensure default preferences include new properties.

### 2.3 Unit Tests for Phase 2
Update existing Preferences tests if any, add tests for:
- Default values of new properties
- `SelectedPrefix` computed property reads from `KeriaPreference.SelectedPrefix`

**Verification:** Build with `/build-windows`, run `dotnet test`

---

## Phase 3: Update WebauthnService to Use Shared Helper

**Goal:** Refactor WebauthnService to use the shared `KeriaConnectionDigestHelper`.

### 3.1 Update `WebauthnService.cs`
**File:** `Extension/Services/WebauthnService.cs`

Replace private `ComputeKeriaConnectionDigest` method with call to `KeriaConnectionDigestHelper.Compute()`.

### 3.2 Update Unit Tests
Verify existing WebauthnService tests still pass.

**Verification:** Build with `/build-windows`, run `dotnet test`

---

## Phase 4: Update AppCache for New Storage Models

**Goal:** Add observers for `KeriaConnectConfigs`, update existing observers.

### 4.1 Add `KeriaConnectConfigs` Observer
**File:** `Extension/Services/AppCache.cs`

Add:
```csharp
private StorageObserver<KeriaConnectConfigs>? keriaConnectConfigsObserver;
public KeriaConnectConfigs MyKeriaConnectConfigs { get; private set; } = new KeriaConnectConfigs();
```

Add observer initialization in `Initialize()`:
```csharp
keriaConnectConfigsObserver = new StorageObserver<KeriaConnectConfigs>(
    storageService,
    StorageArea.Local,
    onNext: (value) => {
        MyKeriaConnectConfigs = value;
        _logger.LogInformation("AppCache updated MyKeriaConnectConfigs");
        Changed?.Invoke();
    },
    onError: ex => _logger.LogError(ex, "Error observing KeriaConnectConfigs storage"),
    null,
    _logger
);
```

### 4.2 Add Helper Methods
```csharp
/// <summary>
/// Gets a KeriaConnectConfig by its digest from the configs collection.
/// </summary>
public KeriaConnectConfig? GetConfigByDigest(string? digest) {
    if (string.IsNullOrEmpty(digest)) return null;
    return MyKeriaConnectConfigs.Configs.TryGetValue(digest, out var config) ? config : null;
}

/// <summary>
/// Gets the currently selected KeriaConnectConfig based on preferences.
/// </summary>
public KeriaConnectConfig? GetSelectedKeriaConnectConfig() {
    return GetConfigByDigest(MyPreferences.KeriaPreference.SelectedKeriaConnectionDigest);
}

/// <summary>
/// Gets all available KeriaConnectConfigs as a list.
/// </summary>
public List<KeriaConnectConfig> GetAvailableKeriaConfigs() {
    return MyKeriaConnectConfigs.Configs.Values.ToList();
}
```

### 4.3 Update Dispose and FetchInitialStorageValuesAsync

Update `Dispose()` to dispose new observer.
Update `FetchInitialStorageValuesAsync()` to fetch `KeriaConnectConfigs`.

### 4.4 Update Derived Properties

Update `MyKeriaConnectConfig` to be a computed property that returns the selected config:
```csharp
public KeriaConnectConfig MyKeriaConnectConfig => GetSelectedKeriaConnectConfig() ?? DefaultKeriaConnectConfig;
```

**Verification:** Build with `/build-windows`, run `dotnet test`

---

## Phase 5: Create ManageKeriAgentServicesPage

**Goal:** Add new page and route. Page is sparse but functional.

### 5.1 Add Route in Routes.cs
**File:** `Extension/Routes.cs`

```csharp
[typeof(ManageKeriAgentServicesPage)] = new("Manage KERIA Connections", "/ManageKeriAgentServices.html", RequiresAuth: false),
```

### 5.2 Create ManageKeriAgentServicesPage.razor
**File:** `Extension/UI/Pages/ManageKeriAgentServicesPage.razor`

```razor
@page "/ManageKeriAgentServices.html"
@layout Layouts.MainLayout
@using Extension.Models
@using Extension.Services
@using Extension.Services.Storage
@using static Extension.Helper.PreviousPage

@inject NavigationManager navManager
@inject IJSRuntime js
@inject AppCache appCache
@inject ILogger<ManageKeriAgentServicesPage> logger

@implements IDisposable

@code {
    protected override async Task OnInitializedAsync() {
        await this.SubscribeToAppCache(appCache);
        logger.LogInformation("OnInitializedAsync");
    }

    public void Dispose() {
        this.UnsubscribeFromAppCache();
        GC.SuppressFinalize(this);
    }
}

<div id="@this.GetType().Name" class="bt-body-page">
    <div class="d-flex gap-3 bt-main">
        <div class="bt-main-inside-scroll">
            <MudText Class="bt-page-title">Manage KERIA Connections</MudText>
            <MudStack>
                <MudText Typo="Typo.body1" Class="pt-4">
                    Configure and manage your KERIA Cloud Service connections.
                </MudText>

                @if (appCache.MyKeriaConnectConfigs.Configs.Count == 0)
                {
                    <MudText Typo="Typo.body2" Color="Color.Secondary" Class="pt-2">
                        No KERIA connections configured.
                    </MudText>
                }
                else
                {
                    <MudList T="string" Class="pt-2">
                        @foreach (var kvp in appCache.MyKeriaConnectConfigs.Configs)
                        {
                            <MudListItem>
                                <MudText Typo="Typo.body2">
                                    <b>@(kvp.Value.Alias ?? "(unnamed)")</b> - @kvp.Value.AdminUrl
                                </MudText>
                            </MudListItem>
                        }
                    </MudList>
                }

                <MudText Typo="Typo.caption" Color="Color.Secondary" Class="pt-4">
                    Add/Remove functionality coming soon.
                </MudText>
            </MudStack>
        </div>
    </div>
    <MudStack Row="true" class="bt-button-tray">
        <MudIconButton Icon="@Icons.Material.Filled.ArrowBackIosNew" Variant="Variant.Text"
                       OnClick='@(async () => await GoBack(js))' Class="justify-start" />
        <MudSpacer />
    </MudStack>
</div>
```

**Verification:** Build with `/build-windows`, manually navigate to `/ManageKeriAgentServices.html`

---

## Phase 6: Update UnlockPage

**Goal:** Add KERIA Cloud Service selector to UnlockPage.

### 6.1 Add KERIA Selector UI
**File:** `Extension/UI/Pages/UnlockPage.razor`

Add using statement:
```razor
@using Extension.Models
```

Insert before line 571 (before the `<MudStack Style="justify-content: end;...>`):

```razor
@if (appCache.MyPreferences.IsMultiKeriaConfigEnabled && appCache.MyPreferences.IsMultiKeriaOnUnlock)
{
    <MudStack Style="justify-content: center; align-items: center;" Class="pt-3">
        <MudSelect T="KeriaConnectConfig?"
                   Label="KERIA Cloud Service"
                   Value="@_selectedKeriaConfig"
                   ValueChanged="OnKeriaConfigChanged"
                   ToStringFunc="@(c => c?.Alias ?? "Add/Remove KERIA Cloud Service")"
                   Variant="Variant.Outlined"
                   Style="max-width: 18rem;">
            @foreach (var config in _availableKeriaConfigs)
            {
                <MudSelectItem Value="@config">@config.Alias</MudSelectItem>
            }
            <MudSelectItem Value="@((KeriaConnectConfig?)null)">Add/Remove KERIA Cloud Service</MudSelectItem>
        </MudSelect>
    </MudStack>
}
```

### 6.2 Add Code Block Variables and Methods

```csharp
// KERIA config selection
private KeriaConnectConfig? _selectedKeriaConfig;
private List<KeriaConnectConfig> _availableKeriaConfigs = new();
private Uri? _initialUri;
```

In `OnInitializedAsync()`:
```csharp
_initialUri = new Uri(navManager.Uri);

// Initialize KERIA config list
_availableKeriaConfigs = appCache.GetAvailableKeriaConfigs();
_selectedKeriaConfig = appCache.GetSelectedKeriaConnectConfig();
```

Add handler:
```csharp
private async Task OnKeriaConfigChanged(KeriaConnectConfig? config)
{
    if (config == null)
    {
        navManager.NavigateTo(Routes.PathFor<ManageKeriAgentServicesPage>());
        return;
    }

    if (_selectedKeriaConfig != config)
    {
        _selectedKeriaConfig = config;

        var digestResult = KeriaConnectionDigestHelper.Compute(config);
        if (digestResult.IsFailed)
        {
            logger.LogError("Failed to compute digest: {Errors}", digestResult.Errors);
            return;
        }

        var newKeriaPreference = appCache.MyPreferences.KeriaPreference with {
            SelectedKeriaConnectionDigest = digestResult.Value
        };
        await storageService.SetItem<Preferences>(appCache.MyPreferences with {
            KeriaPreference = newKeriaPreference
        });

        snackbar.Add("Configuration changed. App will restart in 3 seconds.", Severity.Warning);
        await Task.Delay(3000);
        navManager.NavigateTo(_initialUri?.AbsolutePath ?? "/index.html", true);
    }
}
```

Add using for utility:
```csharp
@using Extension.Utilities
```

**Verification:** Build with `/build-windows`, manually test UnlockPage

---

## Phase 7: Update PreferencesPage

**Goal:** Add Optional Features section with multi-KERIA settings.

### 7.1 Add Optional Features Section
**File:** `Extension/UI/Pages/PreferencesPage.razor`

Add using statements:
```razor
@using Extension.Models
@using Extension.Utilities
```

Add after "Inactivity Timeout" section (before closing `</div>` of `bt-main-inside-scroll`):

```razor
<MudStack Row="false">
    <MudText Class="bt-pref-group-label0">
        Optional Features
    </MudText>
    <MudStack Row="true" Class="ml-5" AlignItems="AlignItems.Center">
        <MudSwitch Value="IsMultiKeriaConfigEnabled"
                   ValueChanged="async (bool isOn) => await SetAndPersistIsMultiKeriaConfigEnabled(isOn)"
                   T="bool" Color="Color.Primary" />
        <MudText>Enable multiple KERIA Cloud Service configurations</MudText>
    </MudStack>
    @if (IsMultiKeriaConfigEnabled)
    {
        <MudStack Row="true" Class="ml-5 mt-2" AlignItems="AlignItems.Center">
            <MudSwitch Value="IsMultiKeriaOnUnlock"
                       ValueChanged="async (bool isOn) => await SetAndPersistIsMultiKeriaOnUnlock(isOn)"
                       T="bool" Color="Color.Primary" />
            <MudText>Show KERIA selector on Unlock page</MudText>
        </MudStack>
        <MudStack Row="false" Class="ml-5 mt-3" Style="width:18rem;">
            <MudSelect T="KeriaConnectConfig?"
                       Label="KERIA Cloud Service"
                       Value="@_selectedKeriaConfig"
                       ValueChanged="OnKeriaConfigChanged"
                       ToStringFunc="@(c => c?.Alias ?? "Add/Remove KERIA Cloud Service")"
                       Variant="Variant.Outlined">
                @foreach (var config in _availableKeriaConfigs)
                {
                    <MudSelectItem Value="@config">@config.Alias</MudSelectItem>
                }
                <MudSelectItem Value="@((KeriaConnectConfig?)null)">Add/Remove KERIA Cloud Service</MudSelectItem>
            </MudSelect>
        </MudStack>
    }
</MudStack>
```

### 7.2 Add Supporting Code

```csharp
// Reactive properties
bool IsMultiKeriaConfigEnabled => Prefs.IsMultiKeriaConfigEnabled;
bool IsMultiKeriaOnUnlock => Prefs.IsMultiKeriaOnUnlock;

// KERIA config selection
private KeriaConnectConfig? _selectedKeriaConfig;
private List<KeriaConnectConfig> _availableKeriaConfigs = new();
private Uri? _initialUri;
```

In `OnInitializedAsync()`:
```csharp
_initialUri = new Uri(navManager.Uri);
_availableKeriaConfigs = appCache.GetAvailableKeriaConfigs();
_selectedKeriaConfig = appCache.GetSelectedKeriaConnectConfig();
```

Add handlers:
```csharp
async Task SetAndPersistIsMultiKeriaConfigEnabled(bool isEnabled)
{
    await Persist(Prefs with { IsMultiKeriaConfigEnabled = isEnabled });
}

async Task SetAndPersistIsMultiKeriaOnUnlock(bool isEnabled)
{
    await Persist(Prefs with { IsMultiKeriaOnUnlock = isEnabled });
}

private async Task OnKeriaConfigChanged(KeriaConnectConfig? config)
{
    if (config == null)
    {
        navManager.NavigateTo(Routes.PathFor<ManageKeriAgentServicesPage>());
        return;
    }

    if (_selectedKeriaConfig != config)
    {
        _selectedKeriaConfig = config;

        var digestResult = KeriaConnectionDigestHelper.Compute(config);
        if (digestResult.IsFailed)
        {
            logger.LogError("Failed to compute digest: {Errors}", digestResult.Errors);
            return;
        }

        var newKeriaPreference = Prefs.KeriaPreference with {
            SelectedKeriaConnectionDigest = digestResult.Value
        };
        await Persist(Prefs with { KeriaPreference = newKeriaPreference });

        // Note: On PreferencesPage, no restart required - just update selection
    }
}
```

**Verification:** Build with `/build-windows`, manually test PreferencesPage

---

## Phase 8: Update SessionStatusIndicator

**Goal:** Add KERIA alias to tooltip texts when multi-KERIA feature is enabled.

### 8.1 Add Parameters
**File:** `Extension/UI/Components/SessionStatusIndicator.razor`

Add new parameters:
```csharp
[Parameter]
public string? KeriaAlias { get; set; }

[Parameter]
public bool IsMultiKeriaConfigEnabled { get; set; }
```

### 8.2 Update Tooltip Texts

Update line 42:
```razor
<MudTooltip Text="@(IsMultiKeriaConfigEnabled ? $"Connected to KERIA Cloud Service ({KeriaAlias ?? "unknown"})" : "Connected to KERIA Cloud Service")" ...>
```

Update line 50:
```razor
<MudTooltip Text="@(IsMultiKeriaConfigEnabled ? $"Not Connected to KERIA Cloud Service ({KeriaAlias ?? "unknown"})" : "Not Connected to KERIA Cloud Service")" ...>
```

Update line 59:
```razor
<MudTooltip Text="@(IsMultiKeriaConfigEnabled ? $"KERI Auth is locked ({KeriaAlias ?? "unknown"})" : "KERI Auth is locked")" ...>
```

### 8.3 Update MainLayout to Pass Parameters
**File:** `Extension/UI/Layouts/MainLayout.razor`

Update SessionStatusIndicator usage to include new parameters:
```razor
<SessionStatusIndicator
    ...existing parameters...
    KeriaAlias="@appCache.MyKeriaConnectConfig?.Alias"
    IsMultiKeriaConfigEnabled="@appCache.MyPreferences.IsMultiKeriaConfigEnabled" />
```

**Verification:** Build with `/build-windows`, manually verify tooltips

---

## Phase 9: Add Menu Item

**Goal:** Add "Manage KERIA Connections" menu item.

### 9.1 Update MainLayout Menu
**File:** `Extension/UI/Layouts/MainLayout.razor`

Insert after line 284 (after "KERIA Config" MudNavLink):
```razor
<MudNavLink Href="@(Routes.PathFor<ManageKeriAgentServicesPage>())" Icon="@Icons.Material.Outlined.CloudCircle" IconColor="Color.Surface">Manage KERIA Connections</MudNavLink>
```

**Verification:** Build with `/build-windows`, manually verify menu navigation

---

## Files Summary

### New Files
1. `Extension/Models/KeriaConnectConfigs.cs`
2. `Extension/Models/KeriaPreference.cs`
3. `Extension/Utilities/KeriaConnectionDigestHelper.cs`
4. `Extension/UI/Pages/ManageKeriAgentServicesPage.razor`
5. `Extension.Tests/Utilities/KeriaConnectionDigestHelperTests.cs`

### Modified Files
1. `Extension/Models/Preferences.cs` - Add `IsMultiKeriaConfigEnabled`, `IsMultiKeriaOnUnlock`, `KeriaPreference`
2. `Extension/Routes.cs` - Add ManageKeriAgentServicesPage route
3. `Extension/Services/AppCache.cs` - Add KeriaConnectConfigs observer and helpers
4. `Extension/Services/WebauthnService.cs` - Use shared digest helper
5. `Extension/UI/Pages/UnlockPage.razor` - Add KERIA selector
6. `Extension/UI/Pages/PreferencesPage.razor` - Add Optional Features section
7. `Extension/UI/Components/SessionStatusIndicator.razor` - Add KeriaAlias and conditional tooltips
8. `Extension/UI/Layouts/MainLayout.razor` - Add menu item, pass alias to indicator
9. `Extension/AppConfig.cs` - Update DefaultPreferences if needed

---

## Testing Checklist

### Per-Phase Testing
- [ ] Phase 1: Unit tests for KeriaConnectionDigestHelper pass
- [ ] Phase 2: Build succeeds, existing tests pass
- [ ] Phase 3: Build succeeds, WebauthnService tests pass
- [ ] Phase 4: Build succeeds, AppCache compiles
- [ ] Phase 5: ManageKeriAgentServicesPage loads at correct URL
- [ ] Phase 6: UnlockPage shows KERIA selector (when enabled)
- [ ] Phase 7: PreferencesPage Optional Features section works
- [ ] Phase 8: Tooltips show alias when feature enabled
- [ ] Phase 9: Menu item appears and navigates correctly

### Integration Testing
- [ ] Select different KERIA config triggers 3-second restart
- [ ] `IsMultiKeriaConfigEnabled: false` hides all multi-KERIA UI
- [ ] `IsMultiKeriaOnUnlock: false` hides selector on UnlockPage only
- [ ] "Add/Remove" option navigates to ManageKeriAgentServicesPage
