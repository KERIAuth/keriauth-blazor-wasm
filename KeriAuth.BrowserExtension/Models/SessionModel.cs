namespace KeriAuth.BrowserExtension.Models
{
    public class SessionModel
    {
        public string? WalletLogin { get; set; }
        public bool IsBooleanTest { get; set; } = false;
        public int IntTest { get; set; } = 0;
        public List<int> ListIntTest { get; set; } = new();
    }
}