using FluentResults;
// using KeriAuth.BrowserExtension.Models;
using KeriAuth.BrowserExtension.Services.SignifyService.Models;
using Notification = KeriAuth.BrowserExtension.Services.SignifyService.Models.Notification;

namespace KeriAuth.BrowserExtension.Services.SignifyService
{
    public interface ISignifyClientService
    {
        Task<Result> HealthCheck(Uri fullUrl);
        Task<Result<bool>> Connect(string url, string passcode, string? boot_url = null, bool isBootForced = false, TimeSpan? timeout = null);
        // Task<Result<ClientState>> BootAndConnect(Uri url, String BootPort, string passcode);
        // Task<Result> BootPort(string url);
        Task<Result<string>> RunCreateAid(string name, TimeSpan? timeout = null);

        // Task<Result<State>> Boot(string url);
        Task<Result<State>> GetState();
        Task<Result<bool>> Connect();
        Task<Result<HttpResponseMessage>> Fetch(string path, string method, object data, Dictionary<string, string>? extraHeaders = null);
        Task<Result<HttpResponseMessage>> SignedFetch(string url, string path, string method, object data, string aidName);
        Task<Result<HttpResponseMessage>> ApproveDelegation();
        Task<Result<HttpResponseMessage>> SaveOldPasscode(string passcode);
        Task<Result<HttpResponseMessage>> DeletePasscode();
        Task<Result<HttpResponseMessage>> Rotate(string nbran, string[] aids);
        Task<Result<Identifiers>> GetIdentifiers();
        Task<Result<IList<Oobi>>> GetOobis();
        Task<Result<IList<Operation>>> GetOperations();
        Task<Result<IList<KeyEvent>>> GetKeyEvents();
        Task<Result<IList<KeyState>>> GetKeyStates();
        Task<Result<List<Dictionary<string, object>>>> GetCredentials();
        Task<Result<IList<Ipex>>> GetIpex();
        Task<Result<IList<Registry>>> GetRegistries();
        Task<Result<IList<Schema>>> GetSchemas();
        Task<Result<IList<Challenge>>> GetChallenges();
        Task<Result<IList<Contact>>> GetContacts();
        Task<Result<IList<Notification>>> GetNotifications();
        Task<Result<IList<Escrow>>> GetEscrows();
        Task<Result<IList<Models.Group>>> GetGroups();
        Task<Result<IList<Exchange>>> GetExchanges();
        Task<Result<Aid>> GetIdentifier(string name);
        Task<Result<string>> SignRequestHeader(string origin, string rurl, string method, Dictionary<string, string> inputHeaders, string prefix);
    }
}
