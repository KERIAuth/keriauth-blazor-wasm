using Microsoft.JSInterop;
using System.Runtime.Versioning;

namespace Extension.Services.JsBindings;

/// <summary>
/// Binding for signify-ts client operations (bundled with esbuild)
/// Provides strongly-typed C# API for the signifyClient JavaScript module
/// </summary>
public interface ISignifyClientBinding {
    // ===================== Connection & Initialization =====================
    ValueTask<string> BootAndConnectAsync(string agentUrl, string bootUrl, string passcode, CancellationToken cancellationToken = default);
    ValueTask<string> ConnectAsync(string agentUrl, string passcode, CancellationToken cancellationToken = default);
    ValueTask<string> GetStateAsync(CancellationToken cancellationToken = default);
    ValueTask DisconnectAsync(CancellationToken cancellationToken = default);

    // ===================== Identifier (AID) Operations =====================
    ValueTask<string> CreateAIDAsync(string name, CancellationToken cancellationToken = default);
    ValueTask<string> GetAIDsAsync(CancellationToken cancellationToken = default);
    ValueTask<string> GetAIDAsync(string name, CancellationToken cancellationToken = default);
    ValueTask<string> GetIdentifierByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    // ===================== Credential (ACDC) Operations =====================
    ValueTask<string> GetCredentialsListAsync(CancellationToken cancellationToken = default);
    ValueTask<string> GetCredentialAsync(string id, bool includeCESR, CancellationToken cancellationToken = default);
    ValueTask<string> CredentialsIssueAsync(string name, string argsJson, CancellationToken cancellationToken = default);
    ValueTask<string> CredentialsRevokeAsync(string name, string said, string? datetime, CancellationToken cancellationToken = default);
    ValueTask<string> CredentialsStateAsync(string ri, string said, CancellationToken cancellationToken = default);
    ValueTask<string> CredentialsDeleteAsync(string said, CancellationToken cancellationToken = default);

    // ===================== Signed Headers =====================
    ValueTask<string> GetSignedHeadersAsync(string origin, string url, string method, string headersDict, string aidName, CancellationToken cancellationToken = default);

    // ===================== IPEX Protocol Methods =====================
    ValueTask<string> IpexApplyAsync(string argsJson, CancellationToken cancellationToken = default);
    ValueTask<string> IpexOfferAsync(string argsJson, CancellationToken cancellationToken = default);
    ValueTask<string> IpexAgreeAsync(string argsJson, CancellationToken cancellationToken = default);
    ValueTask<string> IpexGrantAsync(string argsJson, CancellationToken cancellationToken = default);
    ValueTask<string> IpexAdmitAsync(string argsJson, CancellationToken cancellationToken = default);
    ValueTask<string> IpexSubmitApplyAsync(string name, string exnJson, string sigsJson, string recipientsJson, CancellationToken cancellationToken = default);
    ValueTask<string> IpexSubmitOfferAsync(string name, string exnJson, string sigsJson, string atc, string recipientsJson, CancellationToken cancellationToken = default);
    ValueTask<string> IpexSubmitAgreeAsync(string name, string exnJson, string sigsJson, string recipientsJson, CancellationToken cancellationToken = default);
    ValueTask<string> IpexSubmitGrantAsync(string name, string exnJson, string sigsJson, string atc, string recipientsJson, CancellationToken cancellationToken = default);
    ValueTask<string> IpexSubmitAdmitAsync(string name, string exnJson, string sigsJson, string atc, string recipientsJson, CancellationToken cancellationToken = default);

    // ===================== OOBI Operations =====================
    ValueTask<string> OobiGetAsync(string name, string? role, CancellationToken cancellationToken = default);
    ValueTask<string> OobiResolveAsync(string oobi, string? aliasName, CancellationToken cancellationToken = default);

    // ===================== Operations Management =====================
    ValueTask<string> OperationsGetAsync(string name, CancellationToken cancellationToken = default);
    ValueTask<string> OperationsListAsync(string? type, CancellationToken cancellationToken = default);
    ValueTask<string> OperationsDeleteAsync(string name, CancellationToken cancellationToken = default);
    ValueTask<string> OperationsWaitAsync(string operationJson, string? optionsJson, CancellationToken cancellationToken = default);

    // ===================== Registry Management =====================
    ValueTask<string> RegistriesListAsync(string name, CancellationToken cancellationToken = default);
    ValueTask<string> RegistriesCreateAsync(string argsJson, CancellationToken cancellationToken = default);
    ValueTask<string> RegistriesRenameAsync(string name, string registryName, string newName, CancellationToken cancellationToken = default);

    // ===================== Contact Management =====================
    ValueTask<string> ContactsListAsync(string? group, string? filterField, string? filterValue, CancellationToken cancellationToken = default);
    ValueTask<string> ContactsGetAsync(string prefix, CancellationToken cancellationToken = default);
    ValueTask<string> ContactsAddAsync(string prefix, string infoJson, CancellationToken cancellationToken = default);
    ValueTask<string> ContactsUpdateAsync(string prefix, string infoJson, CancellationToken cancellationToken = default);
    ValueTask<string> ContactsDeleteAsync(string prefix, CancellationToken cancellationToken = default);

    // ===================== Schema Operations =====================
    ValueTask<string> SchemasGetAsync(string said, CancellationToken cancellationToken = default);
    ValueTask<string> SchemasListAsync(CancellationToken cancellationToken = default);

    // ===================== Notifications Operations =====================
    ValueTask<string> NotificationsListAsync(int? start, int? endIndex, CancellationToken cancellationToken = default);
    ValueTask<string> NotificationsMarkAsync(string said, CancellationToken cancellationToken = default);
    ValueTask<string> NotificationsDeleteAsync(string said, CancellationToken cancellationToken = default);
}

[SupportedOSPlatform("browser")]
public class SignifyClientBinding : ISignifyClientBinding {
    private readonly IJsModuleLoader _moduleLoader;

    public SignifyClientBinding(IJsModuleLoader moduleLoader) {
        _moduleLoader = moduleLoader;
    }

    private IJSObjectReference Module => _moduleLoader.GetModule("signifyClient");

    // ===================== Connection & Initialization =====================

    public ValueTask<string> BootAndConnectAsync(string agentUrl, string bootUrl, string passcode, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("bootAndConnect", cancellationToken, agentUrl, bootUrl, passcode);

    public ValueTask<string> ConnectAsync(string agentUrl, string passcode, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("connect", cancellationToken, agentUrl, passcode);

    public ValueTask<string> GetStateAsync(CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("getState", cancellationToken);

    public ValueTask DisconnectAsync(CancellationToken cancellationToken = default) =>
        Module.InvokeVoidAsync("disconnect", cancellationToken);

    // ===================== Identifier (AID) Operations =====================

    public ValueTask<string> CreateAIDAsync(string name, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("createAID", cancellationToken, name);

    public ValueTask<string> GetAIDsAsync(CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("getAIDs", cancellationToken);

    public ValueTask<string> GetAIDAsync(string name, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("getAID", cancellationToken, name);

    public ValueTask<string> GetIdentifierByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("getIdentifierByPrefix", cancellationToken, prefix);

    // ===================== Credential (ACDC) Operations =====================

    public ValueTask<string> GetCredentialsListAsync(CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("getCredentialsList", cancellationToken);

    public ValueTask<string> GetCredentialAsync(string id, bool includeCESR, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("getCredential", cancellationToken, id, includeCESR);

    public ValueTask<string> CredentialsIssueAsync(string name, string argsJson, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("credentialsIssue", cancellationToken, name, argsJson);

    public ValueTask<string> CredentialsRevokeAsync(string name, string said, string? datetime, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("credentialsRevoke", cancellationToken, name, said, datetime);

    public ValueTask<string> CredentialsStateAsync(string ri, string said, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("credentialsState", cancellationToken, ri, said);

    public ValueTask<string> CredentialsDeleteAsync(string said, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("credentialsDelete", cancellationToken, said);

    // ===================== Signed Headers =====================

    public async ValueTask<string> GetSignedHeadersAsync(string origin, string url, string method, string headersDict, string aidName, CancellationToken cancellationToken = default) {
        var headersDictObj = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(headersDict);
        var result = await Module.InvokeAsync<Dictionary<string, string>>("getSignedHeaders", cancellationToken, origin, url, method, headersDictObj, aidName);
        return System.Text.Json.JsonSerializer.Serialize(result);
    }

    // ===================== IPEX Protocol Methods =====================

    public ValueTask<string> IpexApplyAsync(string argsJson, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("ipexApply", cancellationToken, argsJson);

    public ValueTask<string> IpexOfferAsync(string argsJson, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("ipexOffer", cancellationToken, argsJson);

    public ValueTask<string> IpexAgreeAsync(string argsJson, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("ipexAgree", cancellationToken, argsJson);

    public ValueTask<string> IpexGrantAsync(string argsJson, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("ipexGrant", cancellationToken, argsJson);

    public ValueTask<string> IpexAdmitAsync(string argsJson, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("ipexAdmit", cancellationToken, argsJson);

    public ValueTask<string> IpexSubmitApplyAsync(string name, string exnJson, string sigsJson, string recipientsJson, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("ipexSubmitApply", cancellationToken, name, exnJson, sigsJson, recipientsJson);

    public ValueTask<string> IpexSubmitOfferAsync(string name, string exnJson, string sigsJson, string atc, string recipientsJson, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("ipexSubmitOffer", cancellationToken, name, exnJson, sigsJson, atc, recipientsJson);

    public ValueTask<string> IpexSubmitAgreeAsync(string name, string exnJson, string sigsJson, string recipientsJson, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("ipexSubmitAgree", cancellationToken, name, exnJson, sigsJson, recipientsJson);

    public ValueTask<string> IpexSubmitGrantAsync(string name, string exnJson, string sigsJson, string atc, string recipientsJson, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("ipexSubmitGrant", cancellationToken, name, exnJson, sigsJson, atc, recipientsJson);

    public ValueTask<string> IpexSubmitAdmitAsync(string name, string exnJson, string sigsJson, string atc, string recipientsJson, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("ipexSubmitAdmit", cancellationToken, name, exnJson, sigsJson, atc, recipientsJson);

    // ===================== OOBI Operations =====================

    public ValueTask<string> OobiGetAsync(string name, string? role, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("oobiGet", cancellationToken, name, role);

    public ValueTask<string> OobiResolveAsync(string oobi, string? aliasName, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("oobiResolve", cancellationToken, oobi, aliasName);

    // ===================== Operations Management =====================

    public ValueTask<string> OperationsGetAsync(string name, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("operationsGet", cancellationToken, name);

    public ValueTask<string> OperationsListAsync(string? type, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("operationsList", cancellationToken, type);

    public ValueTask<string> OperationsDeleteAsync(string name, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("operationsDelete", cancellationToken, name);

    public ValueTask<string> OperationsWaitAsync(string operationJson, string? optionsJson, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("operationsWait", cancellationToken, operationJson, optionsJson);

    // ===================== Registry Management =====================

    public ValueTask<string> RegistriesListAsync(string name, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("registriesList", cancellationToken, name);

    public ValueTask<string> RegistriesCreateAsync(string argsJson, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("registriesCreate", cancellationToken, argsJson);

    public ValueTask<string> RegistriesRenameAsync(string name, string registryName, string newName, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("registriesRename", cancellationToken, name, registryName, newName);

    // ===================== Contact Management =====================

    public ValueTask<string> ContactsListAsync(string? group, string? filterField, string? filterValue, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("contactsList", cancellationToken, group, filterField, filterValue);

    public ValueTask<string> ContactsGetAsync(string prefix, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("contactsGet", cancellationToken, prefix);

    public ValueTask<string> ContactsAddAsync(string prefix, string infoJson, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("contactsAdd", cancellationToken, prefix, infoJson);

    public ValueTask<string> ContactsUpdateAsync(string prefix, string infoJson, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("contactsUpdate", cancellationToken, prefix, infoJson);

    public ValueTask<string> ContactsDeleteAsync(string prefix, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("contactsDelete", cancellationToken, prefix);

    // ===================== Schema Operations =====================

    public ValueTask<string> SchemasGetAsync(string said, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("schemasGet", cancellationToken, said);

    public ValueTask<string> SchemasListAsync(CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("schemasList", cancellationToken);

    // ===================== Notifications Operations =====================

    public ValueTask<string> NotificationsListAsync(int? start, int? endIndex, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("notificationsList", cancellationToken, start, endIndex);

    public ValueTask<string> NotificationsMarkAsync(string said, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("notificationsMark", cancellationToken, said);

    public ValueTask<string> NotificationsDeleteAsync(string said, CancellationToken cancellationToken = default) =>
        Module.InvokeAsync<string>("notificationsDelete", cancellationToken, said);
}
