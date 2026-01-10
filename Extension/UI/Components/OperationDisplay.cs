namespace Extension.UI.Components {
    /// <summary>
    /// Represents the display state and labels for an operation in a multi-step flow.
    /// </summary>
    public class OperationDisplay {
        public OperationDisplay(string pendingLabel, string runningLabel, string successLabel) {
            PendingLabel = pendingLabel;
            RunningLabel = runningLabel;
            SuccessLabel = successLabel;
            State = OperationState.InvisibleInactive;
        }

        /// <summary>
        /// Convenience constructor that uses the same label for pending and running states.
        /// </summary>
        public OperationDisplay(string pendingAndRunningLabel, string successLabel)
            : this(pendingAndRunningLabel, pendingAndRunningLabel, successLabel) {
        }

        /// <summary>
        /// Current state of the operation.
        /// </summary>
        public OperationState State { get; private set; }

        /// <summary>
        /// Label shown in Pending state.
        /// </summary>
        public string PendingLabel { get; }

        /// <summary>
        /// Label shown in Running state.
        /// </summary>
        public string RunningLabel { get; }

        /// <summary>
        /// Label shown in CompletedSuccess state.
        /// </summary>
        public string SuccessLabel { get; }

        /// <summary>
        /// Error message shown in CompletedFailed state.
        /// If empty, displays "{PendingLabel} - Failed".
        /// </summary>
        public string ErrorMessage { get; private set; } = string.Empty;

        /// <summary>
        /// Gets the appropriate label based on current state.
        /// </summary>
        public string DisplayLabel => State switch {
            OperationState.Pending => PendingLabel,
            OperationState.Running => RunningLabel,
            OperationState.CompletedSuccess => SuccessLabel,
            OperationState.CompletedFailed => string.IsNullOrEmpty(ErrorMessage)
                ? $"{PendingLabel} - Failed"
                : ErrorMessage,
            _ => PendingLabel
        };

        /// <summary>
        /// Sets the operation to the specified state.
        /// </summary>
        public void SetState(OperationState state) {
            State = state;
            if (state != OperationState.CompletedFailed) {
                ErrorMessage = string.Empty;
            }
        }

        /// <summary>
        /// Sets the operation to Running state.
        /// </summary>
        public void SetRunning() {
            State = OperationState.Running;
            ErrorMessage = string.Empty;
        }

        /// <summary>
        /// Sets the operation to CompletedSuccess state.
        /// </summary>
        public void SetCompletedSuccess() {
            State = OperationState.CompletedSuccess;
            ErrorMessage = string.Empty;
        }

        /// <summary>
        /// Sets the operation to CompletedFailed state with an optional error message.
        /// </summary>
        public void SetCompletedFailed(string? errorMessage = null) {
            State = OperationState.CompletedFailed;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        /// <summary>
        /// Resets the operation to InvisibleInactive state.
        /// </summary>
        public void Reset() {
            State = OperationState.InvisibleInactive;
            ErrorMessage = string.Empty;
        }
    }
}
