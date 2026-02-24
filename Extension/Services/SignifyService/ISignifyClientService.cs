using FluentResults;
// using Extension.Models;
using Extension.Helper;
using Extension.Services.SignifyService.Models;
using Notification = Extension.Services.SignifyService.Models.Notification;

namespace Extension.Services.SignifyService {
    public interface ISignifyClientService {
        Task<Result> HealthCheck(Uri fullUrl);

        Task<Result<string>> TestAsync();
        Task<Result> Ready();
        Task<Result<State>> Connect(string url, string passcode, string? bootUrl = null, bool isBootForced = false, TimeSpan? timeout = null);
        Task<Result<string>> RunCreateAid(string aliasStr, TimeSpan? timeout = null);
        Task<Result<RecursiveDictionary>> RenameAid(string currentName, string newName, TimeSpan? timeout = null);
        // Task<Result<string>> GetNameByPrefix(string prefix);
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
        Task<Result<List<Registry>>> ListRegistries(string name);
        Task<Result<Registry>> CreateRegistry(CreateRegistryArgs args);

        // ===================== Contact Management =====================
        Task<Result<List<Contact>>> ListContacts(string? group = null, string? filterField = null, string? filterValue = null);
        Task<Result<Contact>> GetContact(string prefix);
        Task<Result<Contact>> AddContact(string prefix, ContactInfo info);
        Task<Result<Contact>> UpdateContact(string prefix, ContactInfo info);
        Task<Result> DeleteContact(string prefix);

        // ===================== Credential Operations =====================
        Task<Result<IssueCredentialResult>> IssueCredential(string name, CredentialData args);
        Task<Result<RevokeCredentialResult>> RevokeCredential(string name, string said, string? datetime = null);
        Task<Result<CredentialState>> GetCredentialState(string ri, string said);
        Task<Result> DeleteCredential(string said);

        // ===================== Schemas Operations =====================
        Task<Result<Schema>> GetSchema(string said);
        Task<Result<List<Schema>>> ListSchemas();

        // ===================== Notifications Operations =====================
        Task<Result<List<RecursiveDictionary>>> ListNotifications(int? start = null, int? endIndex = null);
        Task<Result<string>> MarkNotification(string said);
        Task<Result> DeleteNotification(string said);

        // ===================== Escrows Operations =====================
        Task<Result<List<RecursiveDictionary>>> ListEscrowReply(string? route = null);

        // ===================== Groups Operations =====================
        Task<Result<RecursiveDictionary>> GetGroupRequest(string said);
        Task<Result<RecursiveDictionary>> SendGroupRequest(string name, RecursiveDictionary exn, List<string> sigs, string atc);
        Task<Result<RecursiveDictionary>> JoinGroup(string name, RecursiveDictionary rot, object sigs, string gid, List<string> smids, List<string> rmids);

        // ===================== Exchanges Operations =====================
        Task<Result<RecursiveDictionary>> GetExchange(string said);
        Task<Result<RecursiveDictionary>> SendExchange(string name, string topic, RecursiveDictionary sender, string route, RecursiveDictionary payload, RecursiveDictionary embeds, List<string> recipients);
        Task<Result<RecursiveDictionary>> SendExchangeFromEvents(string name, string topic, RecursiveDictionary exn, List<string> sigs, string atc, List<string> recipients);

        // ===================== Delegations Operations =====================
        Task<Result<RecursiveDictionary>> ApproveDelegation(string name, RecursiveDictionary? data = null);

        // ===================== KeyEvents Operations =====================
        Task<Result<RecursiveDictionary>> GetKeyEvents(string prefix);

        // ===================== KeyStates Operations =====================
        Task<Result<KeyState>> GetKeyState(string prefix);
        Task<Result<List<KeyState>>> ListKeyStates(List<string> prefixes);
        Task<Result<Operation>> QueryKeyState(string prefix, string? sn = null, RecursiveDictionary? anchor = null);

        // ===================== Config Operations =====================
        Task<Result<AgentConfig>> GetAgentConfig();

        // ===================== Challenges Operations =====================
        /// <summary>
        /// Generate a random challenge word list based on BIP39.
        /// </summary>
        /// <param name="strength">Challenge strength in bits (128 for 12 words, 256 for 24 words)</param>
        Task<Result<Challenge>> GenerateChallenge(int strength = 128);

        /// <summary>
        /// Respond to a challenge by signing a message with the list of words.
        /// </summary>
        /// <param name="name">Name or alias of the identifier</param>
        /// <param name="recipient">Prefix of the recipient of the response</param>
        /// <param name="words">List of challenge words to embed in signed response</param>
        Task<Result<RecursiveDictionary>> RespondToChallenge(string name, string recipient, List<string> words);

        /// <summary>
        /// Ask Agent to verify a given sender signed the provided words.
        /// </summary>
        /// <param name="source">Prefix of the identifier that was challenged</param>
        /// <param name="words">List of challenge words to check for</param>
        Task<Result<Operation>> VerifyChallenge(string source, List<string> words);

        /// <summary>
        /// Mark challenge response as signed and accepted.
        /// </summary>
        /// <param name="source">Prefix of the identifier that was challenged</param>
        /// <param name="said">qb64 AID of exn message representing the signed response</param>
        Task<Result<ChallengeRespondedResult>> ChallengeResponded(string source, string said);

        // ===================== Composite vLEI Operations =====================

        /// <summary>
        /// Create AID + add endRole('agent') + get OOBI in one operation.
        /// </summary>
        Task<Result<AidWithOobi>> CreateAidWithEndRole(string name, TimeSpan? timeout = null);

        /// <summary>
        /// Create a delegate AID under a delegator, resolving the delegator OOBI first.
        /// </summary>
        Task<Result<DelegateAidResult>> CreateDelegateAid(string name, string delegatorPrefix, string delegatorOobi, string delegatorAlias, TimeSpan? timeout = null);

        /// <summary>
        /// Idempotently create a credential registry (checks if it exists first).
        /// </summary>
        Task<Result<RegistryCheckResult>> CreateRegistryIfNotExists(string aidName, string registryName, TimeSpan? timeout = null);

        /// <summary>
        /// List credentials matching a filter, returning raw CESR to preserve ordering/signatures.
        /// </summary>
        Task<Result<string>> GetCredentialsFilteredCesr(string filterJson, TimeSpan? timeout = null);

        /// <summary>
        /// Get credentials by schema SAID and issuer prefix, returning raw CESR.
        /// </summary>
        Task<Result<string>> GetCredentialsBySchemaAndIssuerCesr(string schemaSaid, string issuerPrefix, TimeSpan? timeout = null);

        /// <summary>
        /// Issue a credential, wait for completion, and retrieve the issued credential.
        /// </summary>
        Task<Result<RecursiveDictionary>> IssueAndGetCredential(IssueAndGetCredentialArgs args, TimeSpan? timeout = null);

        /// <summary>
        /// Create IPEX grant + submit in one operation.
        /// </summary>
        Task<Result<RecursiveDictionary>> IpexGrantAndSubmit(IpexGrantSubmitArgs args, TimeSpan? timeout = null);

        /// <summary>
        /// Create IPEX admit + submit in one operation.
        /// </summary>
        Task<Result<RecursiveDictionary>> IpexAdmitAndSubmit(IpexAdmitSubmitArgs args, TimeSpan? timeout = null);

        /// <summary>
        /// Create IPEX apply + submit in one operation.
        /// </summary>
        Task<Result<RecursiveDictionary>> IpexApplyAndSubmit(IpexApplySubmitArgs args, TimeSpan? timeout = null);

        /// <summary>
        /// Create IPEX offer + submit in one operation.
        /// </summary>
        Task<Result<RecursiveDictionary>> IpexOfferAndSubmit(IpexOfferSubmitArgs args, TimeSpan? timeout = null);

        /// <summary>
        /// Create IPEX agree + submit in one operation.
        /// </summary>
        Task<Result<RecursiveDictionary>> IpexAgreeAndSubmit(IpexAgreeSubmitArgs args, TimeSpan? timeout = null);

        /// <summary>
        /// Get a received credential and grant it to another party.
        /// </summary>
        Task<Result<RecursiveDictionary>> GrantReceivedCredential(string senderAidName, string credentialSaid, string recipientPrefix, TimeSpan? timeout = null);
    }
}
