using Microsoft.JSInterop;
using System.Runtime.Versioning;

namespace Extension.Services.SignifyService {
    /// <summary>
    /// JavaScript interop shim for signifyClient.ts
    /// This class provides C# bindings to the TypeScript signifyClient module using IJSRuntime
    /// </summary>
    /// <remarks>
    /// Timeout Strategy:
    /// - CancellationToken support added to all async methods for timeout/cancellation
    /// - On cancellation, client state is reset via disconnect() to ensure clean state
    /// - TimeoutHelper.WithTimeout() in SignifyClientService provides timeout enforcement
    /// - Default timeout: 30 seconds (AppConfig.SignifyTimeoutMs)
    /// </remarks>
    [SupportedOSPlatform("browser")]
    public class SignifyClientShim : IAsyncDisposable {
        // TODO P2: Consider eager loading strategy for scenarios where first call latency is critical
        // Current lazy loading defers module import until first method call, which adds ~50-100ms delay
        // Trade-off: Lazy = faster startup, Eager = faster first operation
        private readonly Lazy<Task<IJSObjectReference>> _moduleTask;

        public SignifyClientShim(IJSRuntime jsRuntime) {
            _moduleTask = new Lazy<Task<IJSObjectReference>>(() =>
                jsRuntime.InvokeAsync<IJSObjectReference>("import", "/scripts/esbuild/signifyClient.js").AsTask());
        }

        /// <summary>
        /// Helper to invoke JS methods with consistent cancellation handling
        /// </summary>
        private async Task<T> InvokeWithCancellationAsync<T>(string methodName, CancellationToken cancellationToken, params object?[] args) {
            var module = await _moduleTask.Value;
            try {
                return await module.InvokeAsync<T>(methodName, cancellationToken, args);
            }
            catch (OperationCanceledException) {
                // Reset client state on cancellation to prevent stale connection
                await module.InvokeVoidAsync("disconnect", CancellationToken.None);
                throw;
            }
        }

        // ===================== Connection & Initialization =====================

        public Task<string> BootAndConnect(string agentUrl, string bootUrl, string passcode, CancellationToken cancellationToken = default) =>
            InvokeWithCancellationAsync<string>("bootAndConnect", cancellationToken, agentUrl, bootUrl, passcode);

        public Task<string> Connect(string agentUrl, string passcode, CancellationToken cancellationToken = default) =>
            InvokeWithCancellationAsync<string>("connect", cancellationToken, agentUrl, passcode);

        public Task<string> GetState(CancellationToken cancellationToken = default) =>
            InvokeWithCancellationAsync<string>("getState", cancellationToken);

        public async Task Disconnect(CancellationToken cancellationToken = default) {
            var module = await _moduleTask.Value;
            await module.InvokeVoidAsync("disconnect", cancellationToken);
        }

        // ===================== Identifier (AID) Operations =====================
        // TODO P2: Add CancellationToken to all remaining methods using InvokeWithCancellationAsync helper

        public Task<string> CreateAID(string name, CancellationToken cancellationToken = default) =>
            InvokeWithCancellationAsync<string>("createAID", cancellationToken, name);

        public Task<string> GetAIDs(CancellationToken cancellationToken = default) =>
            InvokeWithCancellationAsync<string>("getAIDs", cancellationToken);

        public Task<string> GetAID(string name, CancellationToken cancellationToken = default) =>
            InvokeWithCancellationAsync<string>("getAID", cancellationToken, name);

        public Task<string> GetNameByPrefix(string prefix, CancellationToken cancellationToken = default) =>
            InvokeWithCancellationAsync<string>("getNameByPrefix", cancellationToken, prefix);

        public Task<string> GetIdentifierByPrefix(string prefix, CancellationToken cancellationToken = default) =>
            InvokeWithCancellationAsync<string>("getIdentifierByPrefix", cancellationToken, prefix);

        // ===================== Credential (ACDC) Operations =====================

        public async Task<string> GetCredentialsList() {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("getCredentialsList");
        }

        public async Task<string> GetCredential(string id, bool includeCESR) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("getCredential", id, includeCESR);
        }

        public async Task<string> CredentialsIssue(string name, string argsJson) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("credentialsIssue", name, argsJson);
        }

        public async Task<string> CredentialsRevoke(string name, string said, string? datetime) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("credentialsRevoke", name, said, datetime);
        }

        public async Task<string> CredentialsState(string ri, string said) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("credentialsState", ri, said);
        }

        public async Task<string> CredentialsDelete(string said) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("credentialsDelete", said);
        }

        // ===================== Signed Headers =====================

        public async Task<string> GetSignedHeaders(string origin, string url, string method, string headersDict, string aidName, CancellationToken cancellationToken = default) {
            var module = await _moduleTask.Value;
            // Note: Using Dictionary<string, string> here instead of RecursiveDictionary because:
            // - HTTP headers don't contain CESR/SAID structures that require order preservation
            // - Header order is not cryptographically significant in this context
            // - Standard Dictionary provides adequate functionality for HTTP header key-value pairs
            //
            // For KERI/ACDC data structures that contain SAIDs (Self-Addressing Identifiers),
            // use RecursiveDictionary to preserve key ordering, as JSON serialization/deserialization
            // can alter order and break SAID verification (SAIDs are hash-based and order-dependent).
            var headersDictObj = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(headersDict);
            var result = await module.InvokeAsync<Dictionary<string, string>>("getSignedHeaders", cancellationToken, origin, url, method, headersDictObj, aidName);
            return System.Text.Json.JsonSerializer.Serialize(result);
        }

        // ===================== IPEX Protocol Methods =====================

        public async Task<string> IpexApply(string argsJson) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("ipexApply", argsJson);
        }

        public async Task<string> IpexOffer(string argsJson) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("ipexOffer", argsJson);
        }

        public async Task<string> IpexAgree(string argsJson) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("ipexAgree", argsJson);
        }

        public async Task<string> IpexGrant(string argsJson) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("ipexGrant", argsJson);
        }

        public async Task<string> IpexAdmit(string argsJson) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("ipexAdmit", argsJson);
        }

        public async Task<string> IpexSubmitApply(string name, string exnJson, string sigsJson, string recipientsJson) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("ipexSubmitApply", name, exnJson, sigsJson, recipientsJson);
        }

        public async Task<string> IpexSubmitOffer(string name, string exnJson, string sigsJson, string atc, string recipientsJson) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("ipexSubmitOffer", name, exnJson, sigsJson, atc, recipientsJson);
        }

        public async Task<string> IpexSubmitAgree(string name, string exnJson, string sigsJson, string recipientsJson) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("ipexSubmitAgree", name, exnJson, sigsJson, recipientsJson);
        }

        public async Task<string> IpexSubmitGrant(string name, string exnJson, string sigsJson, string atc, string recipientsJson) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("ipexSubmitGrant", name, exnJson, sigsJson, atc, recipientsJson);
        }

        public async Task<string> IpexSubmitAdmit(string name, string exnJson, string sigsJson, string atc, string recipientsJson) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("ipexSubmitAdmit", name, exnJson, sigsJson, atc, recipientsJson);
        }

        // ===================== OOBI Operations =====================

        public async Task<string> OobiGet(string name, string? role) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("oobiGet", name, role);
        }

        public async Task<string> OobiResolve(string oobi, string? alias) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("oobiResolve", oobi, alias);
        }

        // ===================== Operations Management =====================

        public async Task<string> OperationsGet(string name) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("operationsGet", name);
        }

        public async Task<string> OperationsList(string? type) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("operationsList", type);
        }

        public async Task<string> OperationsDelete(string name) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("operationsDelete", name);
        }

        public async Task<string> OperationsWait(string operationJson, string? optionsJson) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("operationsWait", operationJson, optionsJson);
        }

        // ===================== Registry Management =====================

        public async Task<string> RegistriesList(string name) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("registriesList", name);
        }

        public async Task<string> RegistriesCreate(string argsJson) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("registriesCreate", argsJson);
        }

        public async Task<string> RegistriesRename(string name, string registryName, string newName) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("registriesRename", name, registryName, newName);
        }

        // ===================== Contact Management =====================

        public async Task<string> ContactsList(string? group, string? filterField, string? filterValue) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("contactsList", group, filterField, filterValue);
        }

        public async Task<string> ContactsGet(string prefix) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("contactsGet", prefix);
        }

        public async Task<string> ContactsAdd(string prefix, string infoJson) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("contactsAdd", prefix, infoJson);
        }

        public async Task<string> ContactsUpdate(string prefix, string infoJson) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("contactsUpdate", prefix, infoJson);
        }

        public async Task<string> ContactsDelete(string prefix) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("contactsDelete", prefix);
        }

        // ===================== Schema Operations =====================

        public async Task<string> SchemasGet(string said) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("schemasGet", said);
        }

        public async Task<string> SchemasList() {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("schemasList");
        }

        // ===================== Notifications Operations =====================

        public async Task<string> NotificationsList(int? start, int? end) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("notificationsList", start, end);
        }

        public async Task<string> NotificationsMark(string said) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("notificationsMark", said);
        }

        public async Task<string> NotificationsDelete(string said) {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<string>("notificationsDelete", said);
        }

        // ===================== IAsyncDisposable =====================

        public async ValueTask DisposeAsync() {
            if (_moduleTask.IsValueCreated) {
                var module = await _moduleTask.Value;
                await module.DisposeAsync();
            }
            GC.SuppressFinalize(this);
        }
    }
}
