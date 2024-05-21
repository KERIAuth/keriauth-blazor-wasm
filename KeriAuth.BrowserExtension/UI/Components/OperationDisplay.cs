namespace KeriAuth.BrowserExtension.UI.Components
{
    public class OperationDisplay
    {
        public OperationDisplay(string label, string successLabel)
        {
            this.Label = label;
            this.SuccessLabel = successLabel;
            this.CompletedSuccessfully = false;
            this.IsPending = true;
            this.IsRunning = false;
            this.ErrorMessage = String.Empty;
        }

        public void SetIsRunning()
        {
            this.CompletedSuccessfully = false;
            this.IsPending = true;
            this.IsRunning = true;
            this.ErrorMessage = String.Empty;
        }

        public void SetCompletedWithoutErrors()
        {
            this.Label = SuccessLabel;
            this.CompletedSuccessfully = true;
            this.IsPending = false;
            this.IsRunning = false;
            this.ErrorMessage = String.Empty;
        }

        public void SetCompletedWithError(string error = "")
        {
            this.CompletedSuccessfully = false;
            this.IsPending = false;
            this.IsRunning = false;
            this.ErrorMessage = error;
        }

        public string Label { get; private set; } = String.Empty;
        public string SuccessLabel { get; init; } = String.Empty;
        public bool CompletedSuccessfully { get; private set; }
        public bool IsPending { get; private set; }
        public bool IsRunning { get; private set; }
        public string ErrorMessage { get; private set; } = String.Empty;
    }
}