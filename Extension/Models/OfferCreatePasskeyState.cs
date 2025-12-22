namespace Extension.Models;

/// <summary>
/// State for the passkey creation offer flow during initial setup.
/// This is a transient state that controls navigation after KERIA connection.
/// </summary>
public enum OfferCreatePasskeyState
{
    /// <summary>
    /// Initial state - passkey offer has not been shown yet.
    /// ConfigurePage sets this to Yes after successful connection.
    /// </summary>
    NotOffered,

    /// <summary>
    /// User accepted the passkey offer - Index.razor should route to AddPasskeyPage.
    /// Set by OfferPasskeyPage when user clicks "Create Passkey".
    /// Index.razor changes this to InProcess before navigating.
    /// </summary>
    Yes,

    /// <summary>
    /// User is currently creating a passkey via the offer flow.
    /// Set by Index.razor after detecting Yes state.
    /// AddPasskeyPage uses this to determine cancel behavior.
    /// </summary>
    InProcess,

    /// <summary>
    /// User declined the passkey offer or completed/cancelled creation.
    /// Flow is complete - Index.razor should route to Dashboard.
    /// </summary>
    No
}
