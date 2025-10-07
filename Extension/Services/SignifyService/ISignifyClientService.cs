using FluentResults;
// using Extension.Models;
using Extension.Helper;
using Extension.Services.SignifyService.Models;
using Notification = Extension.Services.SignifyService.Models.Notification;

namespace Extension.Services.SignifyService {
    public interface ISignifyClientService {
        Task<Result> HealthCheck(Uri fullUrl);
        Task<Result<State>> Connect(string url, string passcode, string? bootUrl = null, bool isBootForced = false, TimeSpan? timeout = null);
        Task<Result<string>> RunCreateAid(string aliasStr, TimeSpan? timeout = null);

        Task<Result<string>> GetNameByPrefix(string prefix);
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
        Task<Result<List<RecursiveDictionary>>> GetCredentials();

        Task<Result<RecursiveDictionary>> GetCredential(string said);
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
        Task<Result<string>> SignRequestHeader(string origin, string rurl, string method, Dictionary<string, string> initHeaders, string prefix);

        // ===================== IPEX Protocol Methods =====================
        Task<Result<IpexExchangeResult>> IpexApply(IpexApplyArgs args);
        Task<Result<IpexExchangeResult>> IpexOffer(IpexOfferArgs args);
        Task<Result<IpexExchangeResult>> IpexAgree(IpexAgreeArgs args);
        Task<Result<IpexExchangeResult>> IpexGrant(IpexGrantArgs args);
        Task<Result<IpexExchangeResult>> IpexAdmit(IpexAdmitArgs args);

        // ===================== OOBI Operations =====================
        Task<Result<RecursiveDictionary>> GetOobi(string name, string? role = null);
        Task<Result<RecursiveDictionary>> ResolveOobi(string oobi, string? aliasName = null);

        // ===================== Operations Management =====================
        Task<Result<Operation>> GetOperation(string name);
        Task<Result<List<Operation>>> ListOperations(string? type = null);
        Task<Result> DeleteOperation(string name);
        Task<Result<Operation>> WaitForOperation(Operation operation, Dictionary<string, object>? options = null);

        // ===================== Registry Management =====================
        Task<Result<List<RecursiveDictionary>>> ListRegistries(string name);
        Task<Result<RecursiveDictionary>> CreateRegistry(string name, string registryName, int? toad = null, bool noBackers = false, List<string>? baks = null, string? nonce = null);

        // ===================== Contact Management =====================
        Task<Result<List<Contact>>> ListContacts(string? group = null, string? filterField = null, string? filterValue = null);
        Task<Result<Contact>> GetContact(string prefix);
        Task<Result<Contact>> AddContact(string prefix, ContactInfo info);
        Task<Result<Contact>> UpdateContact(string prefix, ContactInfo info);
        Task<Result> DeleteContact(string prefix);

        // ===================== Additional Credential Operations =====================
        Task<Result<RecursiveDictionary>> IssueCredential(string name, CredentialData args);
        Task<Result<RecursiveDictionary>> RevokeCredential(string name, string said, string? datetime = null);
        Task<Result<RecursiveDictionary>> GetCredentialState(string ri, string said);
        Task<Result> DeleteCredential(string said);

        // ===================== Schemas Operations =====================
        Task<Result<RecursiveDictionary>> GetSchema(string said);
        Task<Result<List<RecursiveDictionary>>> ListSchemas();

        // ===================== Notifications Operations =====================
        Task<Result<List<RecursiveDictionary>>> ListNotifications(int? start = null, int? endIndex = null);
        Task<Result<string>> MarkNotification(string said);
        Task<Result> DeleteNotification(string said);
    }
}
