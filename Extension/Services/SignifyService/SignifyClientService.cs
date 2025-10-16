using Extension.Helper;
using Extension.Services.JsBindings;
using Extension.Services.SignifyService.Models;
using FluentResults;
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Group = Extension.Services.SignifyService.Models.Group;
using State = Extension.Services.SignifyService.Models.State;

namespace Extension.Services.SignifyService {
    public class SignifyClientService(ILogger<SignifyClientService> logger, ISignifyClientBinding signifyClientBinding) : ISignifyClientService {
        private readonly ISignifyClientBinding _binding = signifyClientBinding;
        public Task<Result<HttpResponseMessage>> ApproveDelegation() {
            return Task.FromResult(Result.Fail<HttpResponseMessage>("Not implemented"));
        }

        public async Task<Result> HealthCheck(Uri fullUrl) {
            var httpClientService = new HttpClientService(new HttpClient());
            var postResult = await httpClientService.GetJsonAsync<string>(fullUrl.ToString());
            return postResult.IsSuccess ? Result.Ok() : Result.Fail(postResult.Reasons.First().Message);
        }

        public async Task<Result<State>> Connect(string url, string passcode, string? bootUrl, bool isBootForced = false, TimeSpan? timeout = null) {
            if (passcode.Length != 21) {
                return Result.Fail<State>("Passcode must be 21 characters");
            }
            logger.LogInformation("Connect...");

            TimeSpan timeout2;
            if (timeout is null) {
                timeout2 = (TimeSpan)TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs);
                logger.LogInformation("Connect: Using default timeout of {timeout} ms", AppConfig.SignifyTimeoutMs);
            }
            else {
                timeout2 = (TimeSpan)timeout;
                logger.LogInformation("Connect: Using provided timeout of {timeout} ms", timeout2.TotalMilliseconds);
            }
            try {
                // simple example of using https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/call-javascript-from-dotnet?view=aspnetcore-8.0
                if (OperatingSystem.IsBrowser()) {
                    if (isBootForced) {
                        logger.LogInformation("Connect: BootAndConnect to {url} and {bootUrl}...", url, bootUrl);
                        if (bootUrl is null) {
                            return Result.Fail("Connect failed. bootUrl must be set when setting up a new KERIA connection.");
                        }
                        var res = await TimeoutHelper.WithTimeout<string>(ct => _binding.BootAndConnectAsync(url, bootUrl, passcode, ct), timeout2);
                        Debug.Assert(res is not null);
                        // Note that we are not parsing the result here, just logging it. The browser developer console will show the result, but can't display it as a collapse
                        // Don't log the following, since it contains the bran, passcode.
                        // logger.LogInformation("Connect: {connectResults}", res);
                        if (res is null) {
                            return Result.Fail("Connect failed with null");
                        }
                        if (res.IsFailed) {
                            return Result.Fail("Connect failed #1: " + res.Errors[0].Message);
                        }
                        // TODO P2 remove log, since it exposes sensitive info!
                        logger.LogInformation("Connect: BootAndConnect succeeded res: {res}", res.Value);
                    }
                    else {
                        logger.LogInformation("Connect: Connecting to {url}...", url);
                        var res = await TimeoutHelper.WithTimeout<string>(ct => _binding.ConnectAsync(url, passcode, ct), timeout2);
                        if (res is null) {
                            return Result.Fail("Connect failed with null");
                        }
                        if (res.IsFailed) {
                            return Result.Fail("Connect failed #2: " + res.Errors[0].Message);
                        }
                        // Note that we are not parsing the result here, just logging it. The browser developer console will show the result, but can't display it as a collapsable object
                        // TODO P2 Don't log the following, since it contains the bran, passcode.
                        logger.LogInformation("Connect: {connectResults}", res.Value);
                    }
                    var stateRes = await GetState();
                    // TODO P2 remove this log...
                    logger.LogInformation("Connect: GetState after BootAndConnect: {agent prefix} {controller prefix}", stateRes.Value.Agent!.I, stateRes.Value.Controller!.State!.I);
                    return Result.Ok(stateRes.Value);
                }
                else {
                    return Result.Fail("not running in Browser");
                }
            }
            catch (JSException e) {
                logger.LogWarning("Connect: JSException: {e}", e);
                return Result.Fail<State>("SignifyClientService: Connect: Exception: " + e);
            }
            catch (Exception e) {
                logger.LogWarning("Connect: Exception: {e}", e);
                return Result.Fail<State>("SignifyClientService: Connect: Exception: " + e);
            }
        }

        public Task<Result<bool>> Connect() {
            throw new NotImplementedException();
            // return Task.FromResult(Result.Fail<bool>("Not implemented"));
        }

        public async Task<Result<string>> RunCreateAid(string aliasStr, TimeSpan? timeout = null) {
            TimeSpan timeout2;
            if (timeout is null) {
                timeout2 = (TimeSpan)TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs);
            }
            else {
                timeout2 = (TimeSpan)timeout;
            }
            try {
                var res = await TimeoutHelper.WithTimeout<string>(ct => _binding.CreateAIDAsync(aliasStr, ct), timeout2);
                if (res.IsSuccess) {
                    logger.LogInformation("RunCreateAid: {res}", res.Value);
                    var jsonString = res.Value;
                    if (jsonString is null) {
                        return Result.Fail<string>("CreateAID returned null");
                    }
                    else {
                        return Result.Ok(jsonString);
                    }
                }
                else {
                    logger.LogWarning("RunCreateAid: {res}", res.Errors);
                    return Result.Fail<string>(res.Errors[0].Message);
                }
            }
            catch (JSException e) {
                logger.LogWarning("RunCreateAid: JSException: {e}", e);
                return Result.Fail<string>("SignifyClientService: CreatePersonAid: Exception: " + e);
            }
            catch (Exception e) {
                logger.LogWarning("RunCreateAid: Exception: {e}", e);
                return Result.Fail<string>("SignifyClientService: CreatePersonAid: Exception: " + e);
            }
        }

        public Task<Result<HttpResponseMessage>> DeletePasscode() {
            return Task.FromResult(Result.Fail<HttpResponseMessage>("Not implemented"));
        }

        public Task<Result<HttpResponseMessage>> Fetch(string path, string method, object data, Dictionary<string, string>? extraHeaders) {
            return Task.FromResult(Result.Fail<HttpResponseMessage>("Not implemented"));
        }

        public Task<Result<IList<Challenge>>> GetChallenges() {
            return Task.FromResult(Result.Fail<IList<Challenge>>("Not implemented"));
        }

        public Task<Result<IList<Contact>>> GetContacts() {
            return Task.FromResult(Result.Fail<IList<Contact>>("Not implemented"));
        }

        public Task<Result<IList<Escrow>>> GetEscrows() {
            return Task.FromResult(Result.Fail<IList<Escrow>>("Not implemented"));
        }

        public Task<Result<IList<Exchange>>> GetExchanges() {
            return Task.FromResult(Result.Fail<IList<Exchange>>("Not implemented"));
        }

        public Task<Result<IList<Group>>> GetGroups() {
            return Task.FromResult(Result.Fail<IList<Group>>("Not implemented"));
        }

        public async Task<Result<Identifiers>> GetIdentifiers() {
            try {
                var jsonString = await _binding.GetAIDsAsync();
                if (jsonString is null) {
                    return Result.Fail<Identifiers>("GetAIDs returned null");
                }
                var identifiers = System.Text.Json.JsonSerializer.Deserialize<Identifiers>(jsonString);
                if (identifiers is null) {
                    return Result.Fail<Identifiers>("SignifyClientService: GetIdentifiers: Failed to deserialize Identifiers");
                }
                return Result.Ok(identifiers);
            }
            catch (JSException e) {
                logger.LogWarning("GetIdentifiers: JSException: {e}", e);
                return Result.Fail<Identifiers>("SignifyClientService: GetIdentifiers: Exception: " + e);
            }
            catch (Exception e) {
                logger.LogWarning("GetIdentifiers: Exception: {e}", e);
                return Result.Fail<Identifiers>("SignifyClientService: GetIdentifiers: Exception: " + e);
            }
        }

        public async Task<Result<Aid>> GetIdentifier(string name) {
            try {
                var jsonString = await _binding.GetAIDAsync(name);
                if (jsonString is null) {
                    return Result.Fail<Aid>("GetAID returned null");
                }
                var aid = System.Text.Json.JsonSerializer.Deserialize<Aid>(jsonString);
                if (aid is null) {
                    return Result.Fail<Aid>("Failed to deserialize Identifier");
                }
                return Result.Ok(aid);
            }
            catch (JSException e) {
                logger.LogWarning("GetIdentifiers: JSException: {e}", e);
                return Result.Fail<Aid>("SignifyClientService: GetIdentifier: Exception: " + e);
            }
            catch (Exception e) {
                logger.LogWarning("GetIdentifiers: Exception: {e}", e);
                return Result.Fail<Aid>("SignifyClientService: GetIdentifier: Exception: " + e);
            }
        }

        public Task<Result<IList<Ipex>>> GetIpex() {
            return Task.FromResult(Result.Fail<IList<Ipex>>("Not implemented"));
        }

        public Task<Result<IList<KeyEvent>>> GetKeyEvents() {
            return Task.FromResult(Result.Fail<IList<KeyEvent>>("Not implemented"));
        }

        public Task<Result<IList<KeyState>>> GetKeyStates() {
            return Task.FromResult(Result.Fail<IList<KeyState>>("Not implemented"));
        }

        public static Task<Result<IList<Models.Notification>>> GetNotifications() {
            return Task.FromResult(Result.Fail<IList<Models.Notification>>("Not implemented"));
        }

        public Task<Result<IList<Oobi>>> GetOobis() {
            return Task.FromResult(Result.Fail<IList<Oobi>>("Not implemented"));
        }

        public Task<Result<IList<Operation>>> GetOperations() {
            return Task.FromResult(Result.Fail<IList<Operation>>("Not implemented"));
        }

        public static Task<Result<IList<Models.Registry>>> GetRegistries() {
            return Task.FromResult(Result.Fail<IList<Models.Registry>>("Not implemented"));
        }

        public Task<Result<IList<Schema>>> GetSchemas() {
            return Task.FromResult(Result.Fail<IList<Schema>>("Not implemented"));
        }

        public async Task<Result<State>> GetState() {
            try {
                var jsonString = await _binding.GetStateAsync();
                if (jsonString is null) {
                    return Result.Fail<State>("GetState returned null");
                }
                var state = System.Text.Json.JsonSerializer.Deserialize<State>(jsonString);
                if (state is null) {
                    return Result.Fail<State>("SignifyClientService: GetState: Failed to deserialize");
                }
                return Result.Ok(state);
            }
            catch (JSException e) {
                logger.LogWarning("GetState: JSException: {e}", e);
                return Result.Fail<State>("SignifyClientService: GetState: Exception: " + e);
            }
            catch (Exception e) {
                logger.LogWarning("GetState: Exception: {e}", e);
                return Result.Fail<State>("SignifyClientService: GetState: Exception: " + e);
            }
        }

        public Task<Result<HttpResponseMessage>> Rotate(string nbran, string[] aids) {
            return Task.FromResult(Result.Fail<HttpResponseMessage>("Not implemented"));
        }

        public Task<Result<HttpResponseMessage>> SaveOldPasscode(string passcode) {
            return Task.FromResult(Result.Fail<HttpResponseMessage>("Not implemented"));
        }

        public Task<Result<HttpResponseMessage>> SignedFetch(string url, string path, string method, object data, string aidName) {
            return Task.FromResult(Result.Fail<HttpResponseMessage>("Not implemented"));
        }

        public async Task<Result<RecursiveDictionary>> GetCredential(string said) {
            var res = await GetCredentials();
            if (res.IsFailed) {
                return res.ToResult<RecursiveDictionary>();
            }
            foreach (var credDict in res.Value) {
                var credDictSaid = credDict.GetValueByPath("sad.d")?.Value?.ToString();
                if (credDictSaid is not null && credDictSaid == said) {
                    return Result.Ok(credDict);
                }
            }
            return Result.Fail($"Could not find credential with said {said}");
        }

        private readonly JsonSerializerOptions jsonSerializerOptions = new() {
            PropertyNameCaseInsensitive = true,
            Converters = { new DictionaryConverter() }
        };

        private readonly JsonSerializerOptions recursiveJsonSerializerOptions = new() {
            PropertyNameCaseInsensitive = true,
            Converters = { new RecursiveDictionaryConverter() }
        };

        public async Task<Result<List<RecursiveDictionary>>> GetCredentials() {
            try {
                var jsonString = await _binding.GetCredentialsListAsync();
                // logger.LogInformation("GetCredentials: {jsonString}", jsonString);
                if (jsonString is null) {
                    return Result.Fail("GetCredentials returned null");
                }
                var credentialsDict = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonString, jsonSerializerOptions);
                if (credentialsDict is null) {
                    return Result.Fail("SignifyClientService: GetCredentials: Failed to deserialize Credentials");
                }
                var credentials = credentialsDict.Select(RecursiveDictionary.FromObjectDictionary).ToList();
                return Result.Ok(credentials);
            }
            catch (JSException e) {
                logger.LogWarning("GetIdentifiers: JSException: {e}", e);
                return Result.Fail("SignifyClientService: GetCredentials: Exception: " + e);
            }
            catch (Exception e) {
                logger.LogWarning("GetIdentifiers: Exception: {e}", e);
                return Result.Fail("SignifyClientService: GetCredentials: Exception: " + e);
            }
        }

        Task<Result<IList<Models.Registry>>> ISignifyClientService.GetRegistries() {
            throw new NotImplementedException();
        }

        Task<Result<IList<Models.Notification>>> ISignifyClientService.GetNotifications() {
            throw new NotImplementedException();
        }
        async Task<Result<string>> ISignifyClientService.SignRequestHeader(string origin, string rurl, string method, Dictionary<string, string> initHeadersDict, string prefix) {
            await Task.Delay(0);
            throw new NotImplementedException();
        }

        // ===================== IPEX Protocol Methods =====================

        public async Task<Result<IpexExchangeResult>> IpexApply(IpexApplyArgs args) {
            try {
                var argsJson = System.Text.Json.JsonSerializer.Serialize(args, jsonSerializerOptions);
                var jsonString = await _binding.IpexApplyAsync(argsJson);
                if (jsonString is null) {
                    return Result.Fail<IpexExchangeResult>("IpexApply returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<IpexExchangeResult>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<IpexExchangeResult>("Failed to deserialize IpexExchangeResult");
                }
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("IpexApply: Exception: {e}", e);
                return Result.Fail<IpexExchangeResult>("IpexApply: Exception: " + e);
            }
        }

        public async Task<Result<IpexExchangeResult>> IpexOffer(IpexOfferArgs args) {
            try {
                var argsJson = System.Text.Json.JsonSerializer.Serialize(args, jsonSerializerOptions);
                var jsonString = await _binding.IpexOfferAsync(argsJson);
                if (jsonString is null) {
                    return Result.Fail<IpexExchangeResult>("IpexOffer returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<IpexExchangeResult>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<IpexExchangeResult>("Failed to deserialize IpexExchangeResult");
                }
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("IpexOffer: Exception: {e}", e);
                return Result.Fail<IpexExchangeResult>("IpexOffer: Exception: " + e);
            }
        }

        public async Task<Result<IpexExchangeResult>> IpexAgree(IpexAgreeArgs args) {
            try {
                var argsJson = System.Text.Json.JsonSerializer.Serialize(args, jsonSerializerOptions);
                var jsonString = await _binding.IpexAgreeAsync(argsJson);
                if (jsonString is null) {
                    return Result.Fail<IpexExchangeResult>("IpexAgree returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<IpexExchangeResult>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<IpexExchangeResult>("Failed to deserialize IpexExchangeResult");
                }
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("IpexAgree: Exception: {e}", e);
                return Result.Fail<IpexExchangeResult>("IpexAgree: Exception: " + e);
            }
        }

        public async Task<Result<IpexExchangeResult>> IpexGrant(IpexGrantArgs args) {
            try {
                var argsJson = System.Text.Json.JsonSerializer.Serialize(args, jsonSerializerOptions);
                var jsonString = await _binding.IpexGrantAsync(argsJson);
                if (jsonString is null) {
                    return Result.Fail<IpexExchangeResult>("IpexGrant returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<IpexExchangeResult>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<IpexExchangeResult>("Failed to deserialize IpexExchangeResult");
                }
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("IpexGrant: Exception: {e}", e);
                return Result.Fail<IpexExchangeResult>("IpexGrant: Exception: " + e);
            }
        }

        public async Task<Result<IpexExchangeResult>> IpexAdmit(IpexAdmitArgs args) {
            try {
                var argsJson = System.Text.Json.JsonSerializer.Serialize(args, jsonSerializerOptions);
                var jsonString = await _binding.IpexAdmitAsync(argsJson);
                if (jsonString is null) {
                    return Result.Fail<IpexExchangeResult>("IpexAdmit returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<IpexExchangeResult>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<IpexExchangeResult>("Failed to deserialize IpexExchangeResult");
                }
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("IpexAdmit: Exception: {e}", e);
                return Result.Fail<IpexExchangeResult>("IpexAdmit: Exception: " + e);
            }
        }

        // ===================== OOBI Operations =====================

        public async Task<Result<RecursiveDictionary>> GetOobi(string name, string? role = null) {
            try {
                var jsonString = await _binding.OobiGetAsync(name, role);
                if (jsonString is null) {
                    return Result.Fail<RecursiveDictionary>("GetOobi returned null");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize OOBI result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("GetOobi: Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("GetOobi: Exception: " + e);
            }
        }

        public async Task<Result<RecursiveDictionary>> ResolveOobi(string oobi, string? aliasName = null) {
            try {
                var jsonString = await _binding.OobiResolveAsync(oobi, aliasName);
                if (jsonString is null) {
                    return Result.Fail<RecursiveDictionary>("ResolveOobi returned null");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize OOBI resolve result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("ResolveOobi: Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("ResolveOobi: Exception: " + e);
            }
        }

        // ===================== Operations Management =====================

        public async Task<Result<Operation>> GetOperation(string name) {
            try {
                var jsonString = await _binding.OperationsGetAsync(name);
                if (jsonString is null) {
                    return Result.Fail<Operation>("GetOperation returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<Operation>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<Operation>("Failed to deserialize Operation");
                }
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("GetOperation: Exception: {e}", e);
                return Result.Fail<Operation>("GetOperation: Exception: " + e);
            }
        }

        public async Task<Result<List<Operation>>> ListOperations(string? type = null) {
            try {
                var jsonString = await _binding.OperationsListAsync(type);
                if (jsonString is null) {
                    return Result.Fail<List<Operation>>("ListOperations returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<List<Operation>>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<List<Operation>>("Failed to deserialize Operations list");
                }
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("ListOperations: Exception: {e}", e);
                return Result.Fail<List<Operation>>("ListOperations: Exception: " + e);
            }
        }

        public async Task<Result> DeleteOperation(string name) {
            try {
                var jsonString = await _binding.OperationsDeleteAsync(name);
                if (jsonString is null) {
                    return Result.Fail("DeleteOperation returned null");
                }
                return Result.Ok();
            }
            catch (Exception e) {
                logger.LogWarning("DeleteOperation: Exception: {e}", e);
                return Result.Fail("DeleteOperation: Exception: " + e);
            }
        }

        public async Task<Result<Operation>> WaitForOperation(Operation operation, Dictionary<string, object>? options = null) {
            try {
                var operationJson = System.Text.Json.JsonSerializer.Serialize(operation, jsonSerializerOptions);
                var optionsJson = options != null ? System.Text.Json.JsonSerializer.Serialize(options, jsonSerializerOptions) : null;
                var jsonString = await _binding.OperationsWaitAsync(operationJson, optionsJson);
                if (jsonString is null) {
                    return Result.Fail<Operation>("WaitForOperation returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<Operation>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<Operation>("Failed to deserialize Operation result");
                }
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("WaitForOperation: Exception: {e}", e);
                return Result.Fail<Operation>("WaitForOperation: Exception: " + e);
            }
        }

        // ===================== Contact Management =====================

        public async Task<Result<List<Contact>>> ListContacts(string? group = null, string? filterField = null, string? filterValue = null) {
            try {
                var jsonString = await _binding.ContactsListAsync(group, filterField, filterValue);
                if (jsonString is null) {
                    return Result.Fail<List<Contact>>("ListContacts returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<List<Contact>>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<List<Contact>>("Failed to deserialize Contacts list");
                }
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("ListContacts: Exception: {e}", e);
                return Result.Fail<List<Contact>>("ListContacts: Exception: " + e);
            }
        }

        public async Task<Result<Contact>> GetContact(string prefix) {
            try {
                var jsonString = await _binding.ContactsGetAsync(prefix);
                if (jsonString is null) {
                    return Result.Fail<Contact>("GetContact returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<Contact>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<Contact>("Failed to deserialize Contact");
                }
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("GetContact: Exception: {e}", e);
                return Result.Fail<Contact>("GetContact: Exception: " + e);
            }
        }

        public async Task<Result<Contact>> AddContact(string prefix, ContactInfo info) {
            try {
                var infoJson = System.Text.Json.JsonSerializer.Serialize(info, jsonSerializerOptions);
                var jsonString = await _binding.ContactsAddAsync(prefix, infoJson);
                if (jsonString is null) {
                    return Result.Fail<Contact>("AddContact returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<Contact>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<Contact>("Failed to deserialize Contact");
                }
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("AddContact: Exception: {e}", e);
                return Result.Fail<Contact>("AddContact: Exception: " + e);
            }
        }

        public async Task<Result<Contact>> UpdateContact(string prefix, ContactInfo info) {
            try {
                var infoJson = System.Text.Json.JsonSerializer.Serialize(info, jsonSerializerOptions);
                var jsonString = await _binding.ContactsUpdateAsync(prefix, infoJson);
                if (jsonString is null) {
                    return Result.Fail<Contact>("UpdateContact returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<Contact>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<Contact>("Failed to deserialize Contact");
                }
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("UpdateContact: Exception: {e}", e);
                return Result.Fail<Contact>("UpdateContact: Exception: " + e);
            }
        }

        public async Task<Result> DeleteContact(string prefix) {
            try {
                var jsonString = await _binding.ContactsDeleteAsync(prefix);
                if (jsonString is null) {
                    return Result.Fail("DeleteContact returned null");
                }
                return Result.Ok();
            }
            catch (Exception e) {
                logger.LogWarning("DeleteContact: Exception: {e}", e);
                return Result.Fail("DeleteContact: Exception: " + e);
            }
        }

        // ===================== Registry and Additional Operations =====================

        public async Task<Result<List<RecursiveDictionary>>> ListRegistries(string name) {
            try {
                var jsonString = await _binding.RegistriesListAsync(name);
                if (jsonString is null) {
                    return Result.Fail<List<RecursiveDictionary>>("ListRegistries returned null");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonString, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<List<RecursiveDictionary>>("Failed to deserialize Registries list");
                }
                var result = resultDict.Select(RecursiveDictionary.FromObjectDictionary).ToList();
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("ListRegistries: Exception: {e}", e);
                return Result.Fail<List<RecursiveDictionary>>("ListRegistries: Exception: " + e);
            }
        }

        public async Task<Result<RecursiveDictionary>> CreateRegistry(string name, string registryName, int? toad = null, bool noBackers = false, List<string>? baks = null, string? nonce = null) {
            try {
                var args = new {
                    name,
                    registryName,
                    toad,
                    noBackers,
                    baks,
                    nonce
                };
                var argsJson = System.Text.Json.JsonSerializer.Serialize(args, jsonSerializerOptions);
                var jsonString = await _binding.RegistriesCreateAsync(argsJson);
                if (jsonString is null) {
                    return Result.Fail<RecursiveDictionary>("CreateRegistry returned null");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Registry creation result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("CreateRegistry: Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("CreateRegistry: Exception: " + e);
            }
        }

        public async Task<Result<RecursiveDictionary>> IssueCredential(string name, CredentialData args) {
            try {
                var argsJson = System.Text.Json.JsonSerializer.Serialize(args, jsonSerializerOptions);
                var jsonString = await _binding.CredentialsIssueAsync(name, argsJson);
                if (jsonString is null) {
                    return Result.Fail<RecursiveDictionary>("IssueCredential returned null");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Credential issuance result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("IssueCredential: Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("IssueCredential: Exception: " + e);
            }
        }

        public async Task<Result<RecursiveDictionary>> RevokeCredential(string name, string said, string? datetime = null) {
            try {
                var jsonString = await _binding.CredentialsRevokeAsync(name, said, datetime);
                if (jsonString is null) {
                    return Result.Fail<RecursiveDictionary>("RevokeCredential returned null");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Credential revocation result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("RevokeCredential: Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("RevokeCredential: Exception: " + e);
            }
        }

        public async Task<Result<RecursiveDictionary>> GetCredentialState(string ri, string said) {
            try {
                var jsonString = await _binding.CredentialsStateAsync(ri, said);
                if (jsonString is null) {
                    return Result.Fail<RecursiveDictionary>("GetCredentialState returned null");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Credential state");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("GetCredentialState: Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("GetCredentialState: Exception: " + e);
            }
        }

        public async Task<Result> DeleteCredential(string said) {
            try {
                var jsonString = await _binding.CredentialsDeleteAsync(said);
                if (jsonString is null) {
                    return Result.Fail("DeleteCredential returned null");
                }
                return Result.Ok();
            }
            catch (Exception e) {
                logger.LogWarning("DeleteCredential: Exception: {e}", e);
                return Result.Fail("DeleteCredential: Exception: " + e);
            }
        }

        public async Task<Result<RecursiveDictionary>> GetSchema(string said) {
            try {
                var jsonString = await _binding.SchemasGetAsync(said);
                if (jsonString is null) {
                    return Result.Fail<RecursiveDictionary>("GetSchema returned null");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Schema");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("GetSchema: Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("GetSchema: Exception: " + e);
            }
        }

        public async Task<Result<List<RecursiveDictionary>>> ListSchemas() {
            try {
                var jsonString = await _binding.SchemasListAsync();
                if (jsonString is null) {
                    return Result.Fail<List<RecursiveDictionary>>("ListSchemas returned null");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonString, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<List<RecursiveDictionary>>("Failed to deserialize Schemas list");
                }
                var result = resultDict.Select(RecursiveDictionary.FromObjectDictionary).ToList();
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("ListSchemas: Exception: {e}", e);
                return Result.Fail<List<RecursiveDictionary>>("ListSchemas: Exception: " + e);
            }
        }

        public async Task<Result<List<RecursiveDictionary>>> ListNotifications(int? start = null, int? endIndex = null) {
            try {
                var jsonString = await _binding.NotificationsListAsync(start, endIndex);
                if (jsonString is null) {
                    return Result.Fail<List<RecursiveDictionary>>("ListNotifications returned null");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonString, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<List<RecursiveDictionary>>("Failed to deserialize Notifications list");
                }
                var result = resultDict.Select(RecursiveDictionary.FromObjectDictionary).ToList();
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("ListNotifications: Exception: {e}", e);
                return Result.Fail<List<RecursiveDictionary>>("ListNotifications: Exception: " + e);
            }
        }

        public async Task<Result<string>> MarkNotification(string said) {
            try {
                var jsonString = await _binding.NotificationsMarkAsync(said);
                if (jsonString is null) {
                    return Result.Fail<string>("MarkNotification returned null");
                }
                return Result.Ok(jsonString);
            }
            catch (Exception e) {
                logger.LogWarning("MarkNotification: Exception: {e}", e);
                return Result.Fail<string>("MarkNotification: Exception: " + e);
            }
        }

        public async Task<Result> DeleteNotification(string said) {
            try {
                var jsonString = await _binding.NotificationsDeleteAsync(said);
                if (jsonString is null) {
                    return Result.Fail("DeleteNotification returned null");
                }
                return Result.Ok();
            }
            catch (Exception e) {
                logger.LogWarning("DeleteNotification: Exception: {e}", e);
                return Result.Fail("DeleteNotification: Exception: " + e);
            }
        }

        // ===================== Escrows Operations =====================

        public async Task<Result<List<RecursiveDictionary>>> ListEscrowReply(string? route = null) {
            try {
                var jsonString = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.EscrowsListReplyAsync(route, ct),
                    TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs)
                );
                if (jsonString is null || jsonString.IsFailed) {
                    return Result.Fail<List<RecursiveDictionary>>("ListEscrowReply returned null or failed");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<List<RecursiveDictionary>>("Failed to deserialize Escrow reply list");
                }
                var result = resultDict.Select(RecursiveDictionary.FromObjectDictionary).ToList();
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("ListEscrowReply: Exception: {e}", e);
                return Result.Fail<List<RecursiveDictionary>>("ListEscrowReply: Exception: " + e);
            }
        }

        // ===================== Groups Operations =====================

        public async Task<Result<RecursiveDictionary>> GetGroupRequest(string said) {
            try {
                var jsonString = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.GroupsGetRequestAsync(said, ct),
                    TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs)
                );
                if (jsonString is null || jsonString.IsFailed) {
                    return Result.Fail<RecursiveDictionary>("GetGroupRequest returned null or failed");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Group request");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("GetGroupRequest: Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("GetGroupRequest: Exception: " + e);
            }
        }

        public async Task<Result<RecursiveDictionary>> SendGroupRequest(string name, RecursiveDictionary exn, List<string> sigs, string atc) {
            try {
                var exnJson = System.Text.Json.JsonSerializer.Serialize(exn.ToDictionary(), jsonSerializerOptions);
                var sigsJson = System.Text.Json.JsonSerializer.Serialize(sigs, jsonSerializerOptions);
                var jsonString = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.GroupsSendRequestAsync(name, exnJson, sigsJson, atc, ct),
                    TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs)
                );
                if (jsonString is null || jsonString.IsFailed) {
                    return Result.Fail<RecursiveDictionary>("SendGroupRequest returned null or failed");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Group send request result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("SendGroupRequest: Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("SendGroupRequest: Exception: " + e);
            }
        }

        public async Task<Result<RecursiveDictionary>> JoinGroup(string name, RecursiveDictionary rot, object sigs, string gid, List<string> smids, List<string> rmids) {
            try {
                var rotJson = System.Text.Json.JsonSerializer.Serialize(rot.ToDictionary(), jsonSerializerOptions);
                var sigsJson = System.Text.Json.JsonSerializer.Serialize(sigs, jsonSerializerOptions);
                var smidsJson = System.Text.Json.JsonSerializer.Serialize(smids, jsonSerializerOptions);
                var rmidsJson = System.Text.Json.JsonSerializer.Serialize(rmids, jsonSerializerOptions);
                var jsonString = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.GroupsJoinAsync(name, rotJson, sigsJson, gid, smidsJson, rmidsJson, ct),
                    TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs)
                );
                if (jsonString is null || jsonString.IsFailed) {
                    return Result.Fail<RecursiveDictionary>("JoinGroup returned null or failed");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Group join result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("JoinGroup: Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("JoinGroup: Exception: " + e);
            }
        }

        // ===================== Exchanges Operations =====================

        public async Task<Result<RecursiveDictionary>> GetExchange(string said) {
            try {
                var jsonString = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.ExchangesGetAsync(said, ct),
                    TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs)
                );
                if (jsonString is null || jsonString.IsFailed) {
                    return Result.Fail<RecursiveDictionary>("GetExchange returned null or failed");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Exchange");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("GetExchange: Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("GetExchange: Exception: " + e);
            }
        }

        public async Task<Result<RecursiveDictionary>> SendExchange(string name, string topic, RecursiveDictionary sender, string route, RecursiveDictionary payload, RecursiveDictionary embeds, List<string> recipients) {
            try {
                var senderJson = System.Text.Json.JsonSerializer.Serialize(sender.ToDictionary(), jsonSerializerOptions);
                var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload.ToDictionary(), jsonSerializerOptions);
                var embedsJson = System.Text.Json.JsonSerializer.Serialize(embeds.ToDictionary(), jsonSerializerOptions);
                var recipientsJson = System.Text.Json.JsonSerializer.Serialize(recipients, jsonSerializerOptions);
                var jsonString = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.ExchangesSendAsync(name, topic, senderJson, route, payloadJson, embedsJson, recipientsJson, ct),
                    TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs)
                );
                if (jsonString is null || jsonString.IsFailed) {
                    return Result.Fail<RecursiveDictionary>("SendExchange returned null or failed");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Exchange send result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("SendExchange: Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("SendExchange: Exception: " + e);
            }
        }

        public async Task<Result<RecursiveDictionary>> SendExchangeFromEvents(string name, string topic, RecursiveDictionary exn, List<string> sigs, string atc, List<string> recipients) {
            try {
                var exnJson = System.Text.Json.JsonSerializer.Serialize(exn.ToDictionary(), jsonSerializerOptions);
                var sigsJson = System.Text.Json.JsonSerializer.Serialize(sigs, jsonSerializerOptions);
                var recipientsJson = System.Text.Json.JsonSerializer.Serialize(recipients, jsonSerializerOptions);
                var jsonString = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.ExchangesSendFromEventsAsync(name, topic, exnJson, sigsJson, atc, recipientsJson, ct),
                    TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs)
                );
                if (jsonString is null || jsonString.IsFailed) {
                    return Result.Fail<RecursiveDictionary>("SendExchangeFromEvents returned null or failed");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Exchange send from events result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("SendExchangeFromEvents: Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("SendExchangeFromEvents: Exception: " + e);
            }
        }

        // ===================== Delegations Operations =====================

        public async Task<Result<RecursiveDictionary>> ApproveDelegation(string name, RecursiveDictionary? data = null) {
            try {
                var dataJson = data != null ? System.Text.Json.JsonSerializer.Serialize(data.ToDictionary(), jsonSerializerOptions) : null;
                var jsonString = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.DelegationsApproveAsync(name, dataJson, ct),
                    TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs)
                );
                if (jsonString is null || jsonString.IsFailed) {
                    return Result.Fail<RecursiveDictionary>("ApproveDelegation returned null or failed");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Delegation approval result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("ApproveDelegation: Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("ApproveDelegation: Exception: " + e);
            }
        }

        // ===================== KeyEvents Operations =====================

        public async Task<Result<RecursiveDictionary>> GetKeyEvents(string prefix) {
            try {
                var jsonString = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.KeyEventsGetAsync(prefix, ct),
                    TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs)
                );
                if (jsonString is null || jsonString.IsFailed) {
                    return Result.Fail<RecursiveDictionary>("GetKeyEvents returned null or failed");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize KeyEvents");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("GetKeyEvents: Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("GetKeyEvents: Exception: " + e);
            }
        }

        // ===================== KeyStates Operations =====================

        public async Task<Result<RecursiveDictionary>> GetKeyState(string prefix) {
            try {
                var jsonString = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.KeyStatesGetAsync(prefix, ct),
                    TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs)
                );
                if (jsonString is null || jsonString.IsFailed) {
                    return Result.Fail<RecursiveDictionary>("GetKeyState returned null or failed");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize KeyState");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("GetKeyState: Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("GetKeyState: Exception: " + e);
            }
        }

        public async Task<Result<List<RecursiveDictionary>>> ListKeyStates(List<string> prefixes) {
            try {
                var prefixesJson = System.Text.Json.JsonSerializer.Serialize(prefixes, jsonSerializerOptions);
                var jsonString = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.KeyStatesListAsync(prefixesJson, ct),
                    TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs)
                );
                if (jsonString is null || jsonString.IsFailed) {
                    return Result.Fail<List<RecursiveDictionary>>("ListKeyStates returned null or failed");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<List<RecursiveDictionary>>("Failed to deserialize KeyStates list");
                }
                var result = resultDict.Select(RecursiveDictionary.FromObjectDictionary).ToList();
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("ListKeyStates: Exception: {e}", e);
                return Result.Fail<List<RecursiveDictionary>>("ListKeyStates: Exception: " + e);
            }
        }

        public async Task<Result<RecursiveDictionary>> QueryKeyState(string prefix, string? sn = null, RecursiveDictionary? anchor = null) {
            try {
                var anchorJson = anchor != null ? System.Text.Json.JsonSerializer.Serialize(anchor.ToDictionary(), jsonSerializerOptions) : null;
                var jsonString = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.KeyStatesQueryAsync(prefix, sn, anchorJson, ct),
                    TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs)
                );
                if (jsonString is null || jsonString.IsFailed) {
                    return Result.Fail<RecursiveDictionary>("QueryKeyState returned null or failed");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize KeyState query result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("QueryKeyState: Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("QueryKeyState: Exception: " + e);
            }
        }

        // ===================== Config Operations =====================

        public async Task<Result<RecursiveDictionary>> GetAgentConfig() {
            try {
                var jsonString = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.ConfigGetAsync(ct),
                    TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs)
                );
                if (jsonString is null || jsonString.IsFailed) {
                    return Result.Fail<RecursiveDictionary>("GetAgentConfig returned null or failed");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Agent config");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (Exception e) {
                logger.LogWarning("GetAgentConfig: Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("GetAgentConfig: Exception: " + e);
            }
        }
    }
}
