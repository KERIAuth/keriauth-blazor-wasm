using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Extension.Services.SignifyService {
    // Important: keep the imported method and property names aligned with signify_ts_shim.ts
    [SupportedOSPlatform("browser")]
#pragma warning disable CA1707 // Identifiers should not contain underscores
    public partial class Signify_ts_shim
#pragma warning restore CA1707 // Identifiers should not contain underscores
    {
        [JSImport("bootAndConnect", "signify_ts_shim")]
        internal static partial Task<string> BootAndConnect(string agentUrl, string bootUrl, string passcode);

        [JSImport("connect", "signify_ts_shim")]
        internal static partial Task<string> Connect(string agentUrl, string passcode);

        [JSImport("createAID", "signify_ts_shim")]
        internal static partial Task<string> CreateAID(string name);

        [JSImport("getAIDs", "signify_ts_shim")]
        internal static partial Task<string> GetAIDs();

        [JSImport("getAID", "signify_ts_shim")]
        internal static partial Task<string> GetAID(string name);

        [JSImport("getCredentialsList", "signify_ts_shim")]
        internal static partial Task<string> GetCredentialsList();

        [JSImport("getCredential", "signify_ts_shim")]
        internal static partial Task<string> GetCredential(string id, bool includeCESR);

        [JSImport("getState", "signify_ts_shim")]
        internal static partial Task<string> GetState();

        [JSImport("getSignedHeaders", "signify_ts_shim")]
        internal static partial Task<string> GetSignedHeaders(string origin, string url, string method, string headersDict, string aidName);

        [JSImport("getNameByPrefix", "signify_ts_shim")]
        internal static partial Task<string> GetNameByPrefix(string prefix);

        [JSImport("getIdentifierByPrefix", "signify_ts_shim")]
        internal static partial Task<string> GetIdentifierByPrefix(string prefix);

        // ===================== IPEX Protocol Methods =====================
        
        [JSImport("ipexApply", "signify_ts_shim")]
        internal static partial Task<string> IpexApply(string argsJson);

        [JSImport("ipexOffer", "signify_ts_shim")]
        internal static partial Task<string> IpexOffer(string argsJson);

        [JSImport("ipexAgree", "signify_ts_shim")]
        internal static partial Task<string> IpexAgree(string argsJson);

        [JSImport("ipexGrant", "signify_ts_shim")]
        internal static partial Task<string> IpexGrant(string argsJson);

        [JSImport("ipexAdmit", "signify_ts_shim")]
        internal static partial Task<string> IpexAdmit(string argsJson);

        [JSImport("ipexSubmitApply", "signify_ts_shim")]
        internal static partial Task<string> IpexSubmitApply(string name, string exnJson, string sigsJson, string recipientsJson);

        [JSImport("ipexSubmitOffer", "signify_ts_shim")]
        internal static partial Task<string> IpexSubmitOffer(string name, string exnJson, string sigsJson, string atc, string recipientsJson);

        [JSImport("ipexSubmitAgree", "signify_ts_shim")]
        internal static partial Task<string> IpexSubmitAgree(string name, string exnJson, string sigsJson, string recipientsJson);

        [JSImport("ipexSubmitGrant", "signify_ts_shim")]
        internal static partial Task<string> IpexSubmitGrant(string name, string exnJson, string sigsJson, string atc, string recipientsJson);

        [JSImport("ipexSubmitAdmit", "signify_ts_shim")]
        internal static partial Task<string> IpexSubmitAdmit(string name, string exnJson, string sigsJson, string atc, string recipientsJson);

        // ===================== OOBI Operations =====================
        
        [JSImport("oobiGet", "signify_ts_shim")]
        internal static partial Task<string> OobiGet(string name, string? role);

        [JSImport("oobiResolve", "signify_ts_shim")]
        internal static partial Task<string> OobiResolve(string oobi, string? alias);

        // ===================== Operations Management =====================
        
        [JSImport("operationsGet", "signify_ts_shim")]
        internal static partial Task<string> OperationsGet(string name);

        [JSImport("operationsList", "signify_ts_shim")]
        internal static partial Task<string> OperationsList(string? type);

        [JSImport("operationsDelete", "signify_ts_shim")]
        internal static partial Task<string> OperationsDelete(string name);

        [JSImport("operationsWait", "signify_ts_shim")]
        internal static partial Task<string> OperationsWait(string operationJson, string? optionsJson);

        // ===================== Registry Management =====================
        
        [JSImport("registriesList", "signify_ts_shim")]
        internal static partial Task<string> RegistriesList(string name);

        [JSImport("registriesCreate", "signify_ts_shim")]
        internal static partial Task<string> RegistriesCreate(string argsJson);

        [JSImport("registriesRename", "signify_ts_shim")]
        internal static partial Task<string> RegistriesRename(string name, string registryName, string newName);

        // ===================== Contact Management =====================
        
        [JSImport("contactsList", "signify_ts_shim")]
        internal static partial Task<string> ContactsList(string? group, string? filterField, string? filterValue);

        [JSImport("contactsGet", "signify_ts_shim")]
        internal static partial Task<string> ContactsGet(string prefix);

        [JSImport("contactsAdd", "signify_ts_shim")]
        internal static partial Task<string> ContactsAdd(string prefix, string infoJson);

        [JSImport("contactsUpdate", "signify_ts_shim")]
        internal static partial Task<string> ContactsUpdate(string prefix, string infoJson);

        [JSImport("contactsDelete", "signify_ts_shim")]
        internal static partial Task<string> ContactsDelete(string prefix);

        // ===================== Additional Credential Operations =====================
        
        [JSImport("credentialsIssue", "signify_ts_shim")]
        internal static partial Task<string> CredentialsIssue(string name, string argsJson);

        [JSImport("credentialsRevoke", "signify_ts_shim")]
        internal static partial Task<string> CredentialsRevoke(string name, string said, string? datetime);

        [JSImport("credentialsState", "signify_ts_shim")]
        internal static partial Task<string> CredentialsState(string ri, string said);

        [JSImport("credentialsDelete", "signify_ts_shim")]
        internal static partial Task<string> CredentialsDelete(string said);

        // ===================== Schemas Operations =====================
        
        [JSImport("schemasGet", "signify_ts_shim")]
        internal static partial Task<string> SchemasGet(string said);

        [JSImport("schemasList", "signify_ts_shim")]
        internal static partial Task<string> SchemasList();

        // ===================== Notifications Operations =====================
        
        [JSImport("notificationsList", "signify_ts_shim")]
        internal static partial Task<string> NotificationsList(int? start, int? end);

        [JSImport("notificationsMark", "signify_ts_shim")]
        internal static partial Task<string> NotificationsMark(string said);

        [JSImport("notificationsDelete", "signify_ts_shim")]
        internal static partial Task<string> NotificationsDelete(string said);

        // note that GetSignedHeaders has bugs when running in WASM.  See https://github.com/WebOfTrust/signify-ts/issues/284
    }
}
