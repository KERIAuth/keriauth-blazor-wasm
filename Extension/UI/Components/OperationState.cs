namespace Extension.UI.Components {
    /// <summary>
    /// Represents the display state of an operation in a multi-step flow.
    /// </summary>
    public enum OperationState {
        /// <summary>
        /// Operation is not displayed (display:none). Takes no space in layout.
        /// </summary>
        InvisibleInactive,

        /// <summary>
        /// Operation reserves space but is not visible (visibility:hidden).
        /// Used to maintain consistent layout while waiting for state changes.
        /// </summary>
        DisplayedHidden,

        /// <summary>
        /// Operation is visible and waiting to start.
        /// </summary>
        Pending,

        /// <summary>
        /// Operation is currently executing.
        /// </summary>
        Running,

        /// <summary>
        /// Operation completed successfully.
        /// </summary>
        CompletedSuccess,

        /// <summary>
        /// Operation failed with an error.
        /// </summary>
        CompletedFailed
    }
}
