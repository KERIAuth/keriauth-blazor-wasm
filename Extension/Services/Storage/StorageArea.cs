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
