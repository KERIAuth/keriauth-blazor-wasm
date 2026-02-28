using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Extension.Helper;
using Extension.Services.JsBindings;
using Extension.Services.SignifyService.Models;
using FluentResults;
using Group = Extension.Services.SignifyService.Models.Group;
using State = Extension.Services.SignifyService.Models.State;

namespace Extension.Services.SignifyService {
    public class SignifyClientService(ILogger<SignifyClientService> logger, ISignifyClientBinding signifyClientBinding) : ISignifyClientService {
        private readonly ISignifyClientBinding _binding = signifyClientBinding;
        public Task<Result<HttpResponseMessage>> ApproveDelegation() {
            return Task.FromResult(Result.Fail<HttpResponseMessage>("Not implemented"));
        }

        public async Task<Result<string>> TestAsync() {
            logger.LogInformation(nameof(TestAsync) + ": called");
            try {
                var res = await _binding.TestAsync();
                logger.LogInformation(nameof(TestAsync) + ": completed with result: {res}", res);
                return Result.Ok(res);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(TestAsync) + ": JSException: {e}", e);
                return Result.Fail("SignifyClientService: TestAsync: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(TestAsync) + ": Exception: {e}", e);
                return Result.Fail("SignifyClientService: TestAsync: Exception: " + e.Message);
            }
        }

        public async Task<Result> Ready() {
            logger.LogInformation(nameof(Ready) + ": called");
            logger.LogInformation(nameof(Ready) + ": _binding type: {bindingType}", _binding.GetType().ToString());
            try {
                await _binding.Ready();
                logger.LogInformation(nameof(Ready) + ": completed");
                return Result.Ok();
            }
            catch (JSException e) {
                logger.LogWarning(nameof(Ready) + ": JSException: {e}", e);
                return Result.Fail("SignifyClientService: Ready: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(Ready) + ": Exception: {e}", e);
                return Result.Fail("SignifyClientService: Ready: Exception: " + e.Message);
            }
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
            logger.LogInformation(nameof(Connect) + "...");

            TimeSpan timeout2;
            if (timeout is null) {
                timeout2 = (TimeSpan)TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs);
                logger.LogInformation(nameof(Connect) + ": Using default timeout of {timeout} ms", AppConfig.SignifyTimeoutMs);
            }
            else {
                timeout2 = (TimeSpan)timeout;
                logger.LogInformation(nameof(Connect) + ": Using provided timeout of {timeout} ms", timeout2.TotalMilliseconds);
            }
            try {
                // simple example of using https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/call-javascript-from-dotnet?view=aspnetcore-8.0
                if (OperatingSystem.IsBrowser()) {
                    if (isBootForced) {
                        logger.LogInformation(nameof(Connect) + ": BootAndConnect to {url} and {bootUrl}...", url, bootUrl);
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
                        logger.LogInformation(nameof(Connect) + ": BootAndConnect succeeded res: {res}", res.Value);
                    }
                    else {
                        logger.LogInformation(nameof(Connect) + ": Connecting to {url}...", url);
                        var res = await TimeoutHelper.WithTimeout<string>(ct => _binding.ConnectAsync(url, passcode, ct), timeout2);
                        if (res is null) {
                            return Result.Fail("Connect failed with null");
                        }
                        if (res.IsFailed) {
                            return Result.Fail("Connect failed #2: " + res.Errors[0].Message);
                        }
                        // Note that we are not parsing the result here, just logging it. The browser developer console will show the result, but can't display it as a collapsable object
                        // TODO P2 Don't log the following, since it contains the bran, passcode.
                        logger.LogInformation(nameof(Connect) + ": {connectResults}", res.Value);
                    }
                    var stateRes = await GetState();
                    // TODO P2 remove this log...
                    logger.LogInformation(nameof(Connect) + ": GetState after BootAndConnect: {agent prefix} {controller prefix}", stateRes.Value.Agent!.I, stateRes.Value.Controller!.State!.I);
                    return Result.Ok(stateRes.Value);
                }
                else {
                    return Result.Fail("not running in Browser");
                }
            }
            catch (JSException e) {
                logger.LogWarning(nameof(Connect) + ": JSException: {e}", e);
                return Result.Fail<State>("SignifyClientService: Connect: Exception: " + e);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(Connect) + ": Exception: {e}", e);
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
                    logger.LogInformation(nameof(RunCreateAid) + ": {res}", res.Value);
                    var jsonString = res.Value;
                    if (jsonString is null) {
                        return Result.Fail<string>("CreateAID returned null");
                    }
                    else {
                        return Result.Ok(jsonString);
                    }
                }
                else {
                    logger.LogWarning(nameof(RunCreateAid) + ": {res}", res.Errors);
                    return Result.Fail<string>(res.Errors[0].Message);
                }
            }
            catch (JSException e) {
                logger.LogWarning(nameof(RunCreateAid) + ": JSException: {e}", e);
                return Result.Fail<string>("SignifyClientService: CreatePersonAid: Exception: " + e);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(RunCreateAid) + ": Exception: {e}", e);
                return Result.Fail<string>("SignifyClientService: CreatePersonAid: Exception: " + e);
            }
        }

        public async Task<Result<RecursiveDictionary>> RenameAid(string currentName, string newName, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs);
            try {
                var res = await TimeoutHelper.WithTimeout<string>(ct => _binding.RenameAIDAsync(currentName, newName, ct), timeout2);
                if (res.IsFailed) {
                    logger.LogWarning(nameof(RenameAid) + ": Failed - {errors}", res.Errors);
                    return Result.Fail<RecursiveDictionary>(res.Errors[0].Message);
                }
                var jsonString = res.Value;
                if (jsonString is null) {
                    return Result.Fail<RecursiveDictionary>("RenameAID returned null");
                }
                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize rename result");
                }
                var recursiveDict = RecursiveDictionary.FromObjectDictionary(resultDict);
                logger.LogInformation(nameof(RenameAid) + ": Renamed '{currentName}' to '{newName}'", currentName, newName);
                return Result.Ok(recursiveDict);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(RenameAid) + ": JSException: {e}", e);
                return Result.Fail<RecursiveDictionary>("SignifyClientService: RenameAid: Exception: " + e);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(RenameAid) + ": Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("SignifyClientService: RenameAid: Exception: " + e);
            }
        }

        public Task<Result<HttpResponseMessage>> DeletePasscode() {
            return Task.FromResult(Result.Fail<HttpResponseMessage>("Not implemented"));
        }

        public Task<Result<HttpResponseMessage>> Fetch(string path, string method, object data, Dictionary<string, string>? extraHeaders) {
            return Task.FromResult(Result.Fail<HttpResponseMessage>("Not implemented"));
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
            // TODO P2 test this path
            var readyRes = await Ready();
            if (readyRes.IsFailed) {
                logger.LogError(nameof(GetIdentifiers) + ": Not ready: {reasons}", readyRes.Reasons);
                return Result.Fail<Identifiers>($"SignifyClientService: GetIdentifiers: Not ready.");
            }
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
                logger.LogWarning(nameof(GetIdentifiers) + ": JSException: {e}", e);
                return Result.Fail<Identifiers>("SignifyClientService: GetIdentifiers: Exception: " + e);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(GetIdentifiers) + ": Exception: {e}", e);
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
                logger.LogWarning(nameof(GetIdentifier) + ": JSException: {e}", e);
                return Result.Fail<Aid>("SignifyClientService: GetIdentifier: Exception: " + e);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(GetIdentifier) + ": Exception: {e}", e);
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

        // TODO P1: Return Result<StateResult> where StateResult includes a NotConnected state,
        // allowing callers to distinguish connection errors from other failures without brittle string matching.
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
                logger.LogWarning(nameof(GetState) + ": JSException: {e}", e);
                return Result.Fail<State>("SignifyClientService: GetState: Exception: " + e);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(GetState) + ": Exception: {e}", e);
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
                logger.LogWarning(nameof(GetCredentials) + ": JSException: {e}", e);
                return Result.Fail("SignifyClientService: GetCredentials: Exception: " + e);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(GetCredentials) + ": Exception: {e}", e);
                return Result.Fail("SignifyClientService: GetCredentials: Exception: " + e);
            }
        }

        public async Task<Result<string>> GetCredentialsRaw() {
            try {
                var jsonString = await _binding.GetCredentialsListAsync();
                if (jsonString is null) {
                    return Result.Fail("GetCredentialsRaw returned null");
                }
                return Result.Ok(jsonString);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(GetCredentialsRaw) + ": JSException: {e}", e);
                return Result.Fail("SignifyClientService: GetCredentialsRaw: Exception: " + e);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(GetCredentialsRaw) + ": Exception: {e}", e);
                return Result.Fail("SignifyClientService: GetCredentialsRaw: Exception: " + e);
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
            catch (JSException e) {
                logger.LogWarning(nameof(IpexApply) + ": JSException: {e}", e);
                return Result.Fail<IpexExchangeResult>("IpexApply: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(IpexApply) + ": Exception: {e}", e);
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
            catch (JSException e) {
                logger.LogWarning(nameof(IpexOffer) + ": JSException: {e}", e);
                return Result.Fail<IpexExchangeResult>("IpexOffer: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(IpexOffer) + ": Exception: {e}", e);
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
            catch (JSException e) {
                logger.LogWarning(nameof(IpexAgree) + ": JSException: {e}", e);
                return Result.Fail<IpexExchangeResult>("IpexAgree: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(IpexAgree) + ": Exception: {e}", e);
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
            catch (JSException e) {
                logger.LogWarning(nameof(IpexGrant) + ": JSException: {e}", e);
                return Result.Fail<IpexExchangeResult>("IpexGrant: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(IpexGrant) + ": Exception: {e}", e);
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
            catch (JSException e) {
                logger.LogWarning(nameof(IpexAdmit) + ": JSException: {e}", e);
                return Result.Fail<IpexExchangeResult>("IpexAdmit: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(IpexAdmit) + ": Exception: {e}", e);
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
            catch (JSException e) {
                logger.LogWarning(nameof(GetOobi) + ": JSException: {e}", e);
                return Result.Fail<RecursiveDictionary>("GetOobi: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(GetOobi) + ": Exception: {e}", e);
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
            catch (JSException e) {
                logger.LogWarning(nameof(ResolveOobi) + ": JSException: {e}", e);
                return Result.Fail<RecursiveDictionary>("ResolveOobi: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(ResolveOobi) + ": Exception: {e}", e);
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
            catch (JSException e) {
                logger.LogWarning(nameof(GetOperation) + ": JSException: {e}", e);
                return Result.Fail<Operation>("GetOperation: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(GetOperation) + ": Exception: {e}", e);
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
            catch (JSException e) {
                logger.LogWarning(nameof(ListOperations) + ": JSException: {e}", e);
                return Result.Fail<List<Operation>>("ListOperations: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(ListOperations) + ": Exception: {e}", e);
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
            catch (JSException e) {
                logger.LogWarning(nameof(DeleteOperation) + ": JSException: {e}", e);
                return Result.Fail("DeleteOperation: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(DeleteOperation) + ": Exception: {e}", e);
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
            catch (JSException e) {
                logger.LogWarning(nameof(WaitForOperation) + ": JSException: {e}", e);
                return Result.Fail<Operation>("WaitForOperation: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(WaitForOperation) + ": Exception: {e}", e);
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
            catch (JSException e) {
                logger.LogWarning(nameof(ListContacts) + ": JSException: {e}", e);
                return Result.Fail<List<Contact>>("ListContacts: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(ListContacts) + ": Exception: {e}", e);
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
            catch (JSException e) {
                logger.LogWarning(nameof(GetContact) + ": JSException: {e}", e);
                return Result.Fail<Contact>("GetContact: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(GetContact) + ": Exception: {e}", e);
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
            catch (JSException e) {
                logger.LogWarning(nameof(AddContact) + ": JSException: {e}", e);
                return Result.Fail<Contact>("AddContact: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(AddContact) + ": Exception: {e}", e);
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
            catch (JSException e) {
                logger.LogWarning(nameof(UpdateContact) + ": JSException: {e}", e);
                return Result.Fail<Contact>("UpdateContact: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(UpdateContact) + ": Exception: {e}", e);
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
            catch (JSException e) {
                logger.LogWarning(nameof(DeleteContact) + ": JSException: {e}", e);
                return Result.Fail("DeleteContact: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(DeleteContact) + ": Exception: {e}", e);
                return Result.Fail("DeleteContact: Exception: " + e);
            }
        }

        // ===================== Registry and Additional Operations =====================

        public async Task<Result<List<Registry>>> ListRegistries(string name) {
            try {
                var jsonString = await _binding.RegistriesListAsync(name);
                if (jsonString is null) {
                    return Result.Fail<List<Registry>>("ListRegistries returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<List<Registry>>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<List<Registry>>("Failed to deserialize Registries list");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(ListRegistries) + ": JSException: {e}", e);
                return Result.Fail<List<Registry>>("ListRegistries: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(ListRegistries) + ": Exception: {e}", e);
                return Result.Fail<List<Registry>>("ListRegistries: Exception: " + e);
            }
        }

        public async Task<Result<Registry>> CreateRegistry(CreateRegistryArgs args) {
            try {
                var argsJson = System.Text.Json.JsonSerializer.Serialize(args, jsonSerializerOptions);
                var jsonString = await _binding.RegistriesCreateAsync(argsJson);
                if (jsonString is null) {
                    return Result.Fail<Registry>("CreateRegistry returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<Registry>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<Registry>("Failed to deserialize Registry creation result");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(CreateRegistry) + ": JSException: {e}", e);
                return Result.Fail<Registry>("CreateRegistry: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(CreateRegistry) + ": Exception: {e}", e);
                return Result.Fail<Registry>("CreateRegistry: Exception: " + e);
            }
        }

        public async Task<Result<IssueCredentialResult>> IssueCredential(string name, CredentialData args) {
            try {
                var argsJson = System.Text.Json.JsonSerializer.Serialize(args, jsonSerializerOptions);
                logger.LogInformation("IssueCredential: Calling JS with name={name} and args={argsJson}", name, argsJson);
                var jsonString = await _binding.CredentialsIssueAsync(name, argsJson);
                if (jsonString is null) {
                    return Result.Fail<IssueCredentialResult>("IssueCredential returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<IssueCredentialResult>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<IssueCredentialResult>("Failed to deserialize Credential issuance result");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(IssueCredential) + ": JSException: {e}", e);
                return Result.Fail<IssueCredentialResult>("IssueCredential: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(IssueCredential) + ": Exception: {e}", e);
                return Result.Fail<IssueCredentialResult>("IssueCredential: Exception: " + e);
            }
        }

        public async Task<Result<RevokeCredentialResult>> RevokeCredential(string name, string said, string? datetime = null) {
            try {
                var jsonString = await _binding.CredentialsRevokeAsync(name, said, datetime);
                if (jsonString is null) {
                    return Result.Fail<RevokeCredentialResult>("RevokeCredential returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<RevokeCredentialResult>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<RevokeCredentialResult>("Failed to deserialize Credential revocation result");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(RevokeCredential) + ": JSException: {e}", e);
                return Result.Fail<RevokeCredentialResult>("RevokeCredential: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(RevokeCredential) + ": Exception: {e}", e);
                return Result.Fail<RevokeCredentialResult>("RevokeCredential: Exception: " + e);
            }
        }

        public async Task<Result<CredentialState>> GetCredentialState(string ri, string said) {
            try {
                var jsonString = await _binding.CredentialsStateAsync(ri, said);
                if (jsonString is null) {
                    return Result.Fail<CredentialState>("GetCredentialState returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<CredentialState>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<CredentialState>("Failed to deserialize Credential state");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(GetCredentialState) + ": JSException: {e}", e);
                return Result.Fail<CredentialState>("GetCredentialState: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(GetCredentialState) + ": Exception: {e}", e);
                return Result.Fail<CredentialState>("GetCredentialState: Exception: " + e);
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
            catch (JSException e) {
                logger.LogWarning(nameof(DeleteCredential) + ": JSException: {e}", e);
                return Result.Fail("DeleteCredential: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(DeleteCredential) + ": Exception: {e}", e);
                return Result.Fail("DeleteCredential: Exception: " + e);
            }
        }

        public async Task<Result<Schema>> GetSchema(string said) {
            try {
                var jsonString = await _binding.SchemasGetAsync(said);
                if (jsonString is null) {
                    return Result.Fail<Schema>("GetSchema returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<Schema>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<Schema>("Failed to deserialize Schema");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(GetSchema) + ": JSException: {e}", e);
                return Result.Fail<Schema>("GetSchema: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(GetSchema) + ": Exception: {e}", e);
                return Result.Fail<Schema>("GetSchema: Exception: " + e);
            }
        }

        public async Task<Result<List<Schema>>> ListSchemas() {
            try {
                var jsonString = await _binding.SchemasListAsync();
                if (jsonString is null) {
                    return Result.Fail<List<Schema>>("ListSchemas returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<List<Schema>>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<List<Schema>>("Failed to deserialize Schemas list");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(ListSchemas) + ": JSException: {e}", e);
                return Result.Fail<List<Schema>>("ListSchemas: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(ListSchemas) + ": Exception: {e}", e);
                return Result.Fail<List<Schema>>("ListSchemas: Exception: " + e);
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
            catch (JSException e) {
                logger.LogWarning(nameof(ListNotifications) + ": JSException: {e}", e);
                return Result.Fail<List<RecursiveDictionary>>("ListNotifications: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(ListNotifications) + ": Exception: {e}", e);
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
            catch (JSException e) {
                logger.LogWarning(nameof(MarkNotification) + ": JSException: {e}", e);
                return Result.Fail<string>("MarkNotification: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(MarkNotification) + ": Exception: {e}", e);
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
            catch (JSException e) {
                logger.LogWarning(nameof(DeleteNotification) + ": JSException: {e}", e);
                return Result.Fail("DeleteNotification: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(DeleteNotification) + ": Exception: {e}", e);
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
                if (string.IsNullOrEmpty(jsonString.Value)) {
                    return Result.Fail<List<RecursiveDictionary>>("ListEscrowReply returned empty value");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<List<RecursiveDictionary>>("Failed to deserialize Escrow reply list");
                }
                var result = resultDict.Select(RecursiveDictionary.FromObjectDictionary).ToList();
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(ListEscrowReply) + ": JSException: {e}", e);
                return Result.Fail<List<RecursiveDictionary>>("ListEscrowReply: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(ListEscrowReply) + ": Exception: {e}", e);
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
                if (string.IsNullOrEmpty(jsonString.Value)) {
                    return Result.Fail<RecursiveDictionary>("GetGroupRequest returned empty value");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Group request");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(GetGroupRequest) + ": JSException: {e}", e);
                return Result.Fail<RecursiveDictionary>("GetGroupRequest: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(GetGroupRequest) + ": Exception: {e}", e);
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
                if (string.IsNullOrEmpty(jsonString.Value)) {
                    return Result.Fail<RecursiveDictionary>("SendGroupRequest returned empty value");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Group send request result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(SendGroupRequest) + ": JSException: {e}", e);
                return Result.Fail<RecursiveDictionary>("SendGroupRequest: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(SendGroupRequest) + ": Exception: {e}", e);
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
                if (string.IsNullOrEmpty(jsonString.Value)) {
                    return Result.Fail<RecursiveDictionary>("JoinGroup returned empty value");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Group join result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(JoinGroup) + ": JSException: {e}", e);
                return Result.Fail<RecursiveDictionary>("JoinGroup: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(JoinGroup) + ": Exception: {e}", e);
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
                if (string.IsNullOrEmpty(jsonString.Value)) {
                    return Result.Fail<RecursiveDictionary>("GetExchange returned empty value");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Exchange");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(GetExchange) + ": JSException: {e}", e);
                return Result.Fail<RecursiveDictionary>("GetExchange: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(GetExchange) + ": Exception: {e}", e);
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
                if (string.IsNullOrEmpty(jsonString.Value)) {
                    return Result.Fail<RecursiveDictionary>("SendExchange returned empty value");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Exchange send result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(SendExchange) + ": JSException: {e}", e);
                return Result.Fail<RecursiveDictionary>("SendExchange: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(SendExchange) + ": Exception: {e}", e);
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
                if (string.IsNullOrEmpty(jsonString.Value)) {
                    return Result.Fail<RecursiveDictionary>("SendExchangeFromEvents returned empty value");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Exchange send from events result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(SendExchangeFromEvents) + ": JSException: {e}", e);
                return Result.Fail<RecursiveDictionary>("SendExchangeFromEvents: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(SendExchangeFromEvents) + ": Exception: {e}", e);
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
                if (string.IsNullOrEmpty(jsonString.Value)) {
                    return Result.Fail<RecursiveDictionary>("ApproveDelegation returned empty value");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Delegation approval result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(ApproveDelegation) + ": JSException: {e}", e);
                return Result.Fail<RecursiveDictionary>("ApproveDelegation: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(ApproveDelegation) + ": Exception: {e}", e);
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

                // Log raw response for debugging
                if (string.IsNullOrEmpty(jsonString.Value)) {
                    return Result.Fail<RecursiveDictionary>("GetKeyEvents returned empty value");
                }

                logger.LogInformation(nameof(GetKeyEvents) + ": raw response (first 500 chars): {response}",
                    jsonString.Value.Substring(0, Math.Min(500, jsonString.Value.Length)));

                // GetKeyEvents returns an array of event objects, each with 'ked' (key event data) and 'atc' (attachment) fields
                var resultArray = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonString.Value, jsonSerializerOptions);
                if (resultArray is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize KeyEvents array");
                }

                // Convert array to dictionary with index keys for compatibility with existing parsing logic
                var result = new RecursiveDictionary();
                for (int i = 0; i < resultArray.Count; i++) {
                    result[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new RecursiveValue { Dictionary = RecursiveDictionary.FromObjectDictionary(resultArray[i]) };
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(GetKeyEvents) + ": JSException: {e}", e);
                return Result.Fail<RecursiveDictionary>("GetKeyEvents: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(GetKeyEvents) + ": Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("GetKeyEvents: Exception: " + e);
            }
        }

        // ===================== KeyStates Operations =====================

        public async Task<Result<KeyState>> GetKeyState(string prefix) {
            try {
                var jsonString = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.KeyStatesGetAsync(prefix, ct),
                    TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs)
                );
                if (jsonString is null || jsonString.IsFailed) {
                    return Result.Fail<KeyState>("GetKeyState returned null or failed");
                }

                if (string.IsNullOrEmpty(jsonString.Value)) {
                    return Result.Fail<KeyState>("GetKeyState returned empty value");
                }

                logger.LogDebug(nameof(GetKeyState) + ": raw response (first 500 chars): {response}",
                    jsonString.Value.Substring(0, Math.Min(500, jsonString.Value.Length)));

                // GetKeyState returns an array with a single key state object
                var resultArray = System.Text.Json.JsonSerializer.Deserialize<List<KeyState>>(jsonString.Value, jsonSerializerOptions);
                if (resultArray is null || resultArray.Count == 0) {
                    return Result.Fail<KeyState>("Failed to deserialize KeyState or array is empty");
                }

                // Extract the first (and typically only) key state from the array
                return Result.Ok(resultArray[0]);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(GetKeyState) + ": JSException: {e}", e);
                return Result.Fail<KeyState>("GetKeyState: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(GetKeyState) + ": Exception: {e}", e);
                return Result.Fail<KeyState>("GetKeyState: Exception: " + e);
            }
        }

        public async Task<Result<List<KeyState>>> ListKeyStates(List<string> prefixes) {
            try {
                var prefixesJson = System.Text.Json.JsonSerializer.Serialize(prefixes, jsonSerializerOptions);
                var jsonString = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.KeyStatesListAsync(prefixesJson, ct),
                    TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs)
                );
                if (jsonString is null || jsonString.IsFailed) {
                    return Result.Fail<List<KeyState>>("ListKeyStates returned null or failed");
                }
                if (string.IsNullOrEmpty(jsonString.Value)) {
                    return Result.Fail<List<KeyState>>("ListKeyStates returned empty value");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<List<KeyState>>(jsonString.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<List<KeyState>>("Failed to deserialize KeyStates list");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(ListKeyStates) + ": JSException: {e}", e);
                return Result.Fail<List<KeyState>>("ListKeyStates: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(ListKeyStates) + ": Exception: {e}", e);
                return Result.Fail<List<KeyState>>("ListKeyStates: Exception: " + e);
            }
        }

        public async Task<Result<Operation>> QueryKeyState(string prefix, string? sn = null, RecursiveDictionary? anchor = null) {
            try {
                var anchorJson = anchor != null ? System.Text.Json.JsonSerializer.Serialize(anchor.ToDictionary(), jsonSerializerOptions) : null;
                var jsonString = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.KeyStatesQueryAsync(prefix, sn, anchorJson, ct),
                    TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs)
                );
                if (jsonString is null || jsonString.IsFailed) {
                    return Result.Fail<Operation>("QueryKeyState returned null or failed");
                }
                if (string.IsNullOrEmpty(jsonString.Value)) {
                    return Result.Fail<Operation>("QueryKeyState returned empty value");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<Operation>(jsonString.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<Operation>("Failed to deserialize KeyState query result");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(QueryKeyState) + ": JSException: {e}", e);
                return Result.Fail<Operation>("QueryKeyState: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(QueryKeyState) + ": Exception: {e}", e);
                return Result.Fail<Operation>("QueryKeyState: Exception: " + e);
            }
        }

        // ===================== Config Operations =====================

        public async Task<Result<AgentConfig>> GetAgentConfig() {
            try {
                var jsonString = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.ConfigGetAsync(ct),
                    TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs)
                );
                if (jsonString is null || jsonString.IsFailed) {
                    return Result.Fail<AgentConfig>("GetAgentConfig returned null or failed");
                }
                if (string.IsNullOrEmpty(jsonString.Value)) {
                    return Result.Fail<AgentConfig>("GetAgentConfig returned empty value");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<AgentConfig>(jsonString.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<AgentConfig>("Failed to deserialize Agent config");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(GetAgentConfig) + ": JSException: {e}", e);
                return Result.Fail<AgentConfig>("GetAgentConfig: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(GetAgentConfig) + ": Exception: {e}", e);
                return Result.Fail<AgentConfig>("GetAgentConfig: Exception: " + e);
            }
        }

        // ===================== Challenges Operations =====================

        public async Task<Result<Challenge>> GenerateChallenge(int strength = 128) {
            try {
                var jsonString = await _binding.ChallengesGenerateAsync(strength);
                if (jsonString is null) {
                    return Result.Fail<Challenge>("GenerateChallenge returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<Challenge>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<Challenge>("Failed to deserialize Challenge");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(GenerateChallenge) + ": JSException: {e}", e);
                return Result.Fail<Challenge>("GenerateChallenge: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(GenerateChallenge) + ": Exception: {e}", e);
                return Result.Fail<Challenge>("GenerateChallenge: Exception: " + e);
            }
        }

        public async Task<Result<RecursiveDictionary>> RespondToChallenge(string name, string recipient, List<string> words) {
            try {
                var wordsJson = System.Text.Json.JsonSerializer.Serialize(words, jsonSerializerOptions);
                var jsonString = await _binding.ChallengesRespondAsync(name, recipient, wordsJson);
                if (jsonString is null) {
                    return Result.Fail<RecursiveDictionary>("RespondToChallenge returned null");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize challenge response");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(RespondToChallenge) + ": JSException: {e}", e);
                return Result.Fail<RecursiveDictionary>("RespondToChallenge: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(RespondToChallenge) + ": Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("RespondToChallenge: Exception: " + e);
            }
        }

        public async Task<Result<Operation>> VerifyChallenge(string source, List<string> words) {
            try {
                var wordsJson = System.Text.Json.JsonSerializer.Serialize(words, jsonSerializerOptions);
                var jsonString = await _binding.ChallengesVerifyAsync(source, wordsJson);
                if (jsonString is null) {
                    return Result.Fail<Operation>("VerifyChallenge returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<Operation>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<Operation>("Failed to deserialize verify challenge result");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(VerifyChallenge) + ": JSException: {e}", e);
                return Result.Fail<Operation>("VerifyChallenge: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(VerifyChallenge) + ": Exception: {e}", e);
                return Result.Fail<Operation>("VerifyChallenge: Exception: " + e);
            }
        }

        public async Task<Result<ChallengeRespondedResult>> ChallengeResponded(string source, string said) {
            try {
                var jsonString = await _binding.ChallengesRespondedAsync(source, said);
                if (jsonString is null) {
                    return Result.Fail<ChallengeRespondedResult>("ChallengeResponded returned null");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<ChallengeRespondedResult>(jsonString, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<ChallengeRespondedResult>("Failed to deserialize challenge responded result");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(ChallengeResponded) + ": JSException: {e}", e);
                return Result.Fail<ChallengeRespondedResult>("ChallengeResponded: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(ChallengeResponded) + ": Exception: {e}", e);
                return Result.Fail<ChallengeRespondedResult>("ChallengeResponded: Exception: " + e);
            }
        }

        // ===================== Composite vLEI Operations =====================

        public async Task<Result<AidWithOobi>> CreateAidWithEndRole(string name, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs);
            try {
                var res = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.CreateAidWithEndRoleAsync(name, ct),
                    timeout2
                );
                if (res is null || res.IsFailed) {
                    return Result.Fail<AidWithOobi>("CreateAidWithEndRole returned null or failed");
                }
                if (string.IsNullOrEmpty(res.Value)) {
                    return Result.Fail<AidWithOobi>("CreateAidWithEndRole returned empty value");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<AidWithOobi>(res.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<AidWithOobi>("Failed to deserialize AidWithOobi");
                }
                logger.LogInformation(nameof(CreateAidWithEndRole) + ": Created AID '{name}' with prefix {prefix}", name, result.Prefix);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(CreateAidWithEndRole) + ": JSException: {e}", e);
                return Result.Fail<AidWithOobi>("CreateAidWithEndRole: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(CreateAidWithEndRole) + ": Exception: {e}", e);
                return Result.Fail<AidWithOobi>("CreateAidWithEndRole: Exception: " + e);
            }
        }

        public async Task<Result<DelegateAidResult>> CreateDelegateAid(string name, string delegatorPrefix, string delegatorOobi, string delegatorAlias, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs);
            try {
                var res = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.CreateDelegateAidAsync(name, delegatorPrefix, delegatorOobi, delegatorAlias, ct),
                    timeout2
                );
                if (res is null || res.IsFailed) {
                    return Result.Fail<DelegateAidResult>("CreateDelegateAid returned null or failed");
                }
                if (string.IsNullOrEmpty(res.Value)) {
                    return Result.Fail<DelegateAidResult>("CreateDelegateAid returned empty value");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<DelegateAidResult>(res.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<DelegateAidResult>("Failed to deserialize DelegateAidResult");
                }
                logger.LogInformation(nameof(CreateDelegateAid) + ": Created delegate AID '{name}' with prefix {prefix}", name, result.Prefix);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(CreateDelegateAid) + ": JSException: {e}", e);
                return Result.Fail<DelegateAidResult>("CreateDelegateAid: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(CreateDelegateAid) + ": Exception: {e}", e);
                return Result.Fail<DelegateAidResult>("CreateDelegateAid: Exception: " + e);
            }
        }

        public async Task<Result<RegistryCheckResult>> CreateRegistryIfNotExists(string aidName, string registryName, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs);
            try {
                var res = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.CreateRegistryIfNotExistsAsync(aidName, registryName, ct),
                    timeout2
                );
                if (res is null || res.IsFailed) {
                    return Result.Fail<RegistryCheckResult>("CreateRegistryIfNotExists returned null or failed");
                }
                if (string.IsNullOrEmpty(res.Value)) {
                    return Result.Fail<RegistryCheckResult>("CreateRegistryIfNotExists returned empty value");
                }
                var result = System.Text.Json.JsonSerializer.Deserialize<RegistryCheckResult>(res.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<RegistryCheckResult>("Failed to deserialize RegistryCheckResult");
                }
                logger.LogInformation(nameof(CreateRegistryIfNotExists) + ": Registry '{registryName}' for AID '{aidName}': created={created}", registryName, aidName, result.Created);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(CreateRegistryIfNotExists) + ": JSException: {e}", e);
                return Result.Fail<RegistryCheckResult>("CreateRegistryIfNotExists: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(CreateRegistryIfNotExists) + ": Exception: {e}", e);
                return Result.Fail<RegistryCheckResult>("CreateRegistryIfNotExists: Exception: " + e);
            }
        }

        public async Task<Result<string>> GetCredentialsFilteredCesr(string filterJson, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs);
            try {
                var res = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.CredentialsListFilteredCesrAsync(filterJson, ct),
                    timeout2
                );
                if (res is null || res.IsFailed) {
                    return Result.Fail<string>("GetCredentialsFilteredCesr returned null or failed");
                }
                if (res.Value is null) {
                    return Result.Fail<string>("GetCredentialsFilteredCesr returned null value");
                }
                return Result.Ok(res.Value);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(GetCredentialsFilteredCesr) + ": JSException: {e}", e);
                return Result.Fail<string>("GetCredentialsFilteredCesr: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(GetCredentialsFilteredCesr) + ": Exception: {e}", e);
                return Result.Fail<string>("GetCredentialsFilteredCesr: Exception: " + e);
            }
        }

        public async Task<Result<string>> GetCredentialsBySchemaAndIssuerCesr(string schemaSaid, string issuerPrefix, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs);
            try {
                var res = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.CredentialsBySchemaAndIssuerCesrAsync(schemaSaid, issuerPrefix, ct),
                    timeout2
                );
                if (res is null || res.IsFailed) {
                    return Result.Fail<string>("GetCredentialsBySchemaAndIssuerCesr returned null or failed");
                }
                if (res.Value is null) {
                    return Result.Fail<string>("GetCredentialsBySchemaAndIssuerCesr returned null value");
                }
                return Result.Ok(res.Value);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(GetCredentialsBySchemaAndIssuerCesr) + ": JSException: {e}", e);
                return Result.Fail<string>("GetCredentialsBySchemaAndIssuerCesr: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(GetCredentialsBySchemaAndIssuerCesr) + ": Exception: {e}", e);
                return Result.Fail<string>("GetCredentialsBySchemaAndIssuerCesr: Exception: " + e);
            }
        }

        public async Task<Result<RecursiveDictionary>> IssueAndGetCredential(IssueAndGetCredentialArgs args, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs);
            try {
                var argsJson = System.Text.Json.JsonSerializer.Serialize(args, recursiveJsonSerializerOptions);
                var res = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.IssueAndGetCredentialAsync(argsJson, ct),
                    timeout2
                );
                if (res is null || res.IsFailed) {
                    return Result.Fail<RecursiveDictionary>("IssueAndGetCredential returned null or failed");
                }
                if (string.IsNullOrEmpty(res.Value)) {
                    return Result.Fail<RecursiveDictionary>("IssueAndGetCredential returned empty value");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(res.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize IssueAndGetCredential result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(IssueAndGetCredential) + ": JSException: {e}", e);
                return Result.Fail<RecursiveDictionary>("IssueAndGetCredential: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(IssueAndGetCredential) + ": Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("IssueAndGetCredential: Exception: " + e);
            }
        }

        public async Task<Result<RecursiveDictionary>> IpexGrantAndSubmit(IpexGrantSubmitArgs args, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs);
            try {
                var argsJson = System.Text.Json.JsonSerializer.Serialize(args, recursiveJsonSerializerOptions);
                var res = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.IpexGrantAndSubmitAsync(argsJson, ct),
                    timeout2
                );
                if (res is null || res.IsFailed) {
                    return Result.Fail<RecursiveDictionary>("IpexGrantAndSubmit returned null or failed");
                }
                if (string.IsNullOrEmpty(res.Value)) {
                    return Result.Fail<RecursiveDictionary>("IpexGrantAndSubmit returned empty value");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(res.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize IpexGrantAndSubmit result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(IpexGrantAndSubmit) + ": JSException: {e}", e);
                return Result.Fail<RecursiveDictionary>("IpexGrantAndSubmit: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(IpexGrantAndSubmit) + ": Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("IpexGrantAndSubmit: Exception: " + e);
            }
        }

        public async Task<Result<RecursiveDictionary>> IpexAdmitAndSubmit(IpexAdmitSubmitArgs args, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs);
            try {
                var argsJson = System.Text.Json.JsonSerializer.Serialize(args, jsonSerializerOptions);
                var res = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.IpexAdmitAndSubmitAsync(argsJson, ct),
                    timeout2
                );
                if (res is null || res.IsFailed) {
                    return Result.Fail<RecursiveDictionary>("IpexAdmitAndSubmit returned null or failed");
                }
                if (string.IsNullOrEmpty(res.Value)) {
                    return Result.Fail<RecursiveDictionary>("IpexAdmitAndSubmit returned empty value");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(res.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize IpexAdmitAndSubmit result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(IpexAdmitAndSubmit) + ": JSException: {e}", e);
                return Result.Fail<RecursiveDictionary>("IpexAdmitAndSubmit: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(IpexAdmitAndSubmit) + ": Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("IpexAdmitAndSubmit: Exception: " + e);
            }
        }

        public async Task<Result<RecursiveDictionary>> IpexApplyAndSubmit(IpexApplySubmitArgs args, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs);
            try {
                var argsJson = System.Text.Json.JsonSerializer.Serialize(args, recursiveJsonSerializerOptions);
                var res = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.IpexApplyAndSubmitAsync(argsJson, ct),
                    timeout2
                );
                if (res is null || res.IsFailed) {
                    return Result.Fail<RecursiveDictionary>("IpexApplyAndSubmit returned null or failed");
                }
                if (string.IsNullOrEmpty(res.Value)) {
                    return Result.Fail<RecursiveDictionary>("IpexApplyAndSubmit returned empty value");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(res.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize IpexApplyAndSubmit result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(IpexApplyAndSubmit) + ": JSException: {e}", e);
                return Result.Fail<RecursiveDictionary>("IpexApplyAndSubmit: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(IpexApplyAndSubmit) + ": Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("IpexApplyAndSubmit: Exception: " + e);
            }
        }

        public async Task<Result<RecursiveDictionary>> IpexOfferAndSubmit(IpexOfferSubmitArgs args, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs);
            try {
                var argsJson = System.Text.Json.JsonSerializer.Serialize(args, jsonSerializerOptions);
                var res = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.IpexOfferAndSubmitAsync(argsJson, ct),
                    timeout2
                );
                if (res is null || res.IsFailed) {
                    return Result.Fail<RecursiveDictionary>("IpexOfferAndSubmit returned null or failed");
                }
                if (string.IsNullOrEmpty(res.Value)) {
                    return Result.Fail<RecursiveDictionary>("IpexOfferAndSubmit returned empty value");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(res.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize IpexOfferAndSubmit result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(IpexOfferAndSubmit) + ": JSException: {e}", e);
                return Result.Fail<RecursiveDictionary>("IpexOfferAndSubmit: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(IpexOfferAndSubmit) + ": Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("IpexOfferAndSubmit: Exception: " + e);
            }
        }

        public async Task<Result<RecursiveDictionary>> IpexAgreeAndSubmit(IpexAgreeSubmitArgs args, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs);
            try {
                var argsJson = System.Text.Json.JsonSerializer.Serialize(args, jsonSerializerOptions);
                var res = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.IpexAgreeAndSubmitAsync(argsJson, ct),
                    timeout2
                );
                if (res is null || res.IsFailed) {
                    return Result.Fail<RecursiveDictionary>("IpexAgreeAndSubmit returned null or failed");
                }
                if (string.IsNullOrEmpty(res.Value)) {
                    return Result.Fail<RecursiveDictionary>("IpexAgreeAndSubmit returned empty value");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(res.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize IpexAgreeAndSubmit result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(IpexAgreeAndSubmit) + ": JSException: {e}", e);
                return Result.Fail<RecursiveDictionary>("IpexAgreeAndSubmit: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(IpexAgreeAndSubmit) + ": Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("IpexAgreeAndSubmit: Exception: " + e);
            }
        }

        public async Task<Result<RecursiveDictionary>> GrantReceivedCredential(string senderAidNameOrPrefix, string credentialSaid, string recipientPrefix, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs);
            try {
                var res = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.GrantReceivedCredentialAsync(senderAidNameOrPrefix, credentialSaid, recipientPrefix, ct),
                    timeout2
                );
                if (res is null || res.IsFailed) {
                    return Result.Fail<RecursiveDictionary>("GrantReceivedCredential returned null or failed");
                }
                if (string.IsNullOrEmpty(res.Value)) {
                    return Result.Fail<RecursiveDictionary>("GrantReceivedCredential returned empty value");
                }
                var resultDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(res.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize GrantReceivedCredential result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogWarning(nameof(GrantReceivedCredential) + ": JSException: {e}", e);
                return Result.Fail<RecursiveDictionary>("GrantReceivedCredential: JSException: " + e.Message);
            }
            catch (Exception e) {
                logger.LogWarning(nameof(GrantReceivedCredential) + ": Exception: {e}", e);
                return Result.Fail<RecursiveDictionary>("GrantReceivedCredential: Exception: " + e);
            }
        }
    }
}
