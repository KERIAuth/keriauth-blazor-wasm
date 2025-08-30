namespace Extension.UI.Components
{
    public class OperationDisplay(string label, string successLabel) {
        public void SetIsRunning()
        {
            CompletedSuccessfully = false;
            IsPending = true;
            IsRunning = true;
            ErrorMessage = String.Empty;
        }

        public void SetCompletedWithoutErrors()
        {
            Label = SuccessLabel;
            CompletedSuccessfully = true;
            IsPending = false;
            IsRunning = false;
            ErrorMessage = String.Empty;
        }

        public void SetCompletedWithError(string error = "")
        {
            CompletedSuccessfully = false;
            IsPending = false;
            IsRunning = false;
            ErrorMessage = error;
        }

        public void Reset()
        {
            CompletedSuccessfully = false;
            IsPending = true;
            IsRunning = false;
            ErrorMessage = String.Empty;
        }

        public string Label { get; private set; } = label;
        public string SuccessLabel { get; init; } = successLabel;
        public bool CompletedSuccessfully { get; private set; }
        public bool IsPending { get; private set; } = true;
        public bool IsRunning { get; private set; }
        public string ErrorMessage { get; private set; } = String.Empty;
    }
}
