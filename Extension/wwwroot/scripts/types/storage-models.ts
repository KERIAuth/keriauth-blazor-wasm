/**
 * TypeScript interfaces that mirror C# storage models.
 * These ensure type safety when accessing chrome.storage directly from TypeScript.
 *
 * Note: These interfaces should be kept in sync with their C# counterparts:
 * - Extension/Models/KeriaConnectConfig.cs
 * - Extension/Models/Storage/PasscodeModel.cs
 * - Extension/Models/OnboardState.cs
 * - Extension/Models/Preferences.cs
  * - Extension/Models/Websites.cs
 * - Extension/Models/Storage/EnterprisePolicyConfig.cs
 * - Extension/Models/Storage/InactivityTimeoutCacheModel.cs
 */

/**
 * Storage key constants that match C# type names.
 * These keys correspond to typeof(T).Name in C# StorageService.
 *
 * IMPORTANT: Keep these synchronized with C# type names!
 * The C# StorageService uses typeof(T).Name as the storage key.
 */
export const StorageKeys = {
    KeriaConnectConfig: 'KeriaConnectConfig' as const,
    PasscodeModel: 'PasscodeModel' as const,
    OnboardState: 'OnboardState' as const,
    Preferences: 'Preferences' as const,
    WebsiteConfigList: 'WebsiteConfigList' as const,
    EnterprisePolicyConfig: 'EnterprisePolicyConfig' as const,
    InactivityTimeoutCacheModel: 'InactivityTimeoutCacheModel' as const,
} as const;

/**
 * Type for storage keys (enables type checking and autocomplete)
 */
export type StorageKey = typeof StorageKeys[keyof typeof StorageKeys];

/**
 * KERIA connection configuration stored in local storage.
 * Storage key: "KeriaConnectConfig"
 * Storage area: Local
 */
export interface KeriaConnectConfig {
    ProviderName?: string | null;
    Alias?: string | null;
    AdminUrl?: string | null;
    BootUrl?: string | null;
    PasscodeHash: number;
    ClientAidPrefix?: string | null;
    AgentAidPrefix?: string | null;
}

/**
 * Passcode stored in session storage (cleared when browser closes).
 * Storage key: "PasscodeModel"
 * Storage area: Session
 */
export interface PasscodeModel {
    Passcode: string;
}

/**
 * Onboarding state stored in local storage.
 * Storage key: "OnboardState"
 * Storage area: Local
 */
export interface OnboardState {
    HasAcknowledgedInstall: boolean;
    AcknowledgedInstalledVersion?: string | null;
    TosAgreedUtc?: string | null; // ISO 8601 datetime string
    TosAgreedHash: number;
    PrivacyAgreedUtc?: string | null; // ISO 8601 datetime string
    PrivacyAgreedHash: number;
}

/**
 * User preferences stored in local storage.
 * Storage key: "Preferences"
 * Storage area: Local
 */
export interface Preferences {
    IsDarkTheme: boolean;
    InactivityTimeoutMinutes: number;
    AutoLockEnabled: boolean;
}

/**
 * Website configuration list stored in local storage.
 * Storage key: "WebsiteConfigList"
 * Storage area: Local
 */
export interface WebsiteConfigList {
    Websites: WebsiteConfig[];
}

export interface WebsiteConfig {
    Domain: string;
    // Add other WebsiteConfig properties as needed
}

/**
 * Enterprise policy configuration from managed storage (read-only).
 * Storage key: "EnterprisePolicyConfig"
 * Storage area: Managed
 */
export interface EnterprisePolicyConfig {
    KeriaAdminUrl?: string | null;
    KeriaBootUrl?: string | null;
    UpdatedUtc?: string | null; // ISO 8601 datetime string
}

/**
 * Session expiration time cached in session storage.
 * Storage key: "InactivityTimeoutCacheModel"
 * Storage area: Session
 */
export interface InactivityTimeoutCacheModel {
    SessionExpirationUtc: string; // ISO 8601 datetime string
}

/**
 * Chrome storage result wrapper for type-safe storage access.
 */
export interface StorageResult<T> {
    [key: string]: T | undefined;
}
