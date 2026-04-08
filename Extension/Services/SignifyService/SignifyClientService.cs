using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Extension.Helper;
using Extension.Models;
using Extension.Services.JsBindings;
using Extension.Services.SignifyService.Models;
using FluentResults;
using Group = Extension.Services.SignifyService.Models.Group;
using State = Extension.Services.SignifyService.Models.State;

namespace Extension.Services.SignifyService {
    public class SignifyClientService(ILogger<SignifyClientService> logger, ISignifyClientBinding signifyClientBinding) : ISignifyClientService {
        private readonly ISignifyClientBinding _binding = signifyClientBinding;

        // DTO for deserializing the Result<T> envelope from signifyClient.ts
        private record JsResult {
            [System.Text.Json.Serialization.JsonPropertyName("ok")]
            public bool Ok { get; init; }

            [System.Text.Json.Serialization.JsonPropertyName("value")]
            public JsonElement? Value { get; init; }

            [System.Text.Json.Serialization.JsonPropertyName("code")]
            public string? Code { get; init; }

            [System.Text.Json.Serialization.JsonPropertyName("message")]
            public string? Message { get; init; }
        }

        /// <summary>
        /// Unwraps a JSON Result envelope from signifyClient.ts.
        /// On success, returns the "value" portion as a raw JSON string for further deserialization.
        /// On failure, maps the error code to a typed FluentResults IError.
        /// </summary>
        private Result<string> UnwrapJsResult(string? json, [System.Runtime.CompilerServices.CallerMemberName] string operationName = "") {
            if (string.IsNullOrEmpty(json)) {
                return Result.Fail<string>(new JavaScriptInteropError(operationName, "Null or empty response from JS"));
            }

            JsResult? envelope;
            try {
                envelope = JsonSerializer.Deserialize<JsResult>(json);
            }
            catch (JsonException ex) {
                logger.LogError(ex, "{Op}: Failed to deserialize Result envelope", operationName);
                return Result.Fail<string>(new JavaScriptInteropError(operationName, $"Invalid Result envelope: {ex.Message}", ex));
            }

            if (envelope is null) {
                return Result.Fail<string>(new JavaScriptInteropError(operationName, "Null Result envelope"));
            }

            if (envelope.Ok) {
                SignalKeriaReachability(true);
                var rawValue = envelope.Value?.GetRawText();
                if (string.IsNullOrEmpty(rawValue)) {
                    return Result.Fail<string>(new JavaScriptInteropError(operationName, "Success result with null/empty value"));
                }
                return Result.Ok(rawValue);
            }

            // Map error codes to typed FluentResults errors
            // TODO P2: For non-idempotent operations that fail with network_error,
            // implement recovery logic (check-then-retry) rather than blind retry.
            // Idempotent operations already retry automatically in signifyClient.ts.
            IError error = envelope.Code switch {
                "not_connected" => new NotConnectedError(envelope.Message ?? "Unknown"),
                "network_error" => new ConnectionError("KERIA", envelope.Message ?? "Network error"),
                "operation_timeout" => new OperationTimeoutError(operationName, 30),
                "validation_error" => new ValidationError(operationName, envelope.Message ?? "Validation failed"),
                _ => new JavaScriptInteropError(operationName, envelope.Message ?? "Unknown error"),
            };

            if (envelope.Code is "network_error" or "not_connected") {
                SignalKeriaReachability(false);
            }

            logger.LogWarning("{Op}: JS error [{Code}]: {Message}", operationName, envelope.Code, envelope.Message);
            return Result.Fail<string>(error);
        }

        public bool IsConnected { get; private set; }

        public event Action<bool>? KeriaReachabilityChanged;
        private bool _lastKeriaReachable = true;

        private void SignalKeriaReachability(bool reachable) {
            if (_lastKeriaReachable != reachable) {
                _lastKeriaReachable = reachable;
                KeriaReachabilityChanged?.Invoke(reachable);
            }
        }

        private int _longOperationCount;
        public bool IsLongOperationActive => _longOperationCount > 0;

        public IDisposable BeginLongOperation() {
            Interlocked.Increment(ref _longOperationCount);
            logger.LogDebug("BeginLongOperation: count={Count}", _longOperationCount);
            return new LongOperationScope(this, logger);
        }

        private sealed class LongOperationScope(SignifyClientService owner, ILogger log) : IDisposable {
            private int _disposed;
            public void Dispose() {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) {
                    Interlocked.Decrement(ref owner._longOperationCount);
                    log.LogDebug("LongOperationScope.Dispose: count={Count}", owner._longOperationCount);
                }
            }
        }

        public async Task Disconnect() {
            IsConnected = false;
            // Reset KERIA reachability to optimistic default — the previous endpoint's
            // state is meaningless after disconnect (config change or session lock).
            SignalKeriaReachability(true);
            try {
                await _binding.DisconnectAsync();
            }
            catch (Exception ex) {
                logger.LogWarning(ex, nameof(Disconnect) + ": Error disconnecting signify-ts client");
            }
        }

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

        public async Task<Result<State>> Connect(string url, string passcode, string? bootUrl, bool isBootForced = false, string? bootAuthUsername = null, string? bootAuthPassword = null, TimeSpan? timeout = null) {
            if (passcode.Length != 21) {
                return Result.Fail<State>("Passcode must be 21 characters");
            }
            logger.LogInformation(nameof(Connect) + "...");

            // Mark as disconnected immediately — the JS side resets _client = null at the
            // start of connect(), so any concurrent polling that checks IsClientReady must
            // see the client as unavailable until reconnection completes.
            IsConnected = false;

            TimeSpan timeout2;
            if (timeout is null) {
                timeout2 = AppConfig.SignifyTimeout;
                logger.LogInformation(nameof(Connect) + ": Using default timeout of {timeout} ms", AppConfig.SignifyTimeoutMs);
            }
            else {
                timeout2 = (TimeSpan)timeout;
                logger.LogInformation(nameof(Connect) + ": Using provided timeout of {timeout} ms", timeout2.TotalMilliseconds);
            }
            try {
                if (OperatingSystem.IsBrowser()) {
                    Result<string> timeoutResult;
                    if (isBootForced) {
                        logger.LogInformation(nameof(Connect) + ": BootAndConnect to {url} and {bootUrl}...", url, bootUrl);
                        if (bootUrl is null) {
                            return Result.Fail("Connect failed. bootUrl must be set when setting up a new KERIA connection.");
                        }
                        timeoutResult = await TimeoutHelper.WithTimeout<string>(ct => _binding.BootAndConnectAsync(url, bootUrl, passcode, bootAuthUsername, bootAuthPassword, ct), timeout2);
                    }
                    else {
                        logger.LogInformation(nameof(Connect) + ": Connecting to {url}...", url);
                        timeoutResult = await TimeoutHelper.WithTimeout<string>(ct => _binding.ConnectAsync(url, passcode, ct), timeout2);
                    }

                    if (timeoutResult.IsFailed) {
                        return Result.Fail<State>(timeoutResult.Errors);
                    }
                    var unwrapped = UnwrapJsResult(timeoutResult.Value);
                    if (unwrapped.IsFailed) {
                        return Result.Fail<State>(unwrapped.Errors);
                    }

                    // The connect/bootAndConnect result embeds state, but we call GetState
                    // separately for consistent deserialization through the standard path.
                    var stateRes = await GetState();
                    if (stateRes.IsFailed) {
                        return Result.Fail<State>(stateRes.Errors);
                    }
                    IsConnected = true;
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
        }

        public Task<Result<bool>> Connect() {
            throw new NotImplementedException();
            // return Task.FromResult(Result.Fail<bool>("Not implemented"));
        }

        public async Task<Result<RecursiveDictionary>> RenameAid(string currentName, string newName, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? AppConfig.SignifyTimeout;
            try {
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(ct => _binding.RenameAIDAsync(currentName, newName, ct), timeout2);
                if (timeoutResult.IsFailed) {
                    return Result.Fail<RecursiveDictionary>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<RecursiveDictionary>(unwrapped.Errors);

                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(unwrapped.Value);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize rename result");
                }
                var recursiveDict = RecursiveDictionary.FromObjectDictionary(resultDict);
                logger.LogInformation(nameof(RenameAid) + ": Renamed '{currentName}' to '{newName}'", currentName, newName);
                return Result.Ok(recursiveDict);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(RenameAid));
                return Result.Fail<RecursiveDictionary>(new JavaScriptInteropError(nameof(RenameAid), e.Message, e));
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
            var readyRes = await Ready();
            if (readyRes.IsFailed) {
                logger.LogError(nameof(GetIdentifiers) + ": Not ready: {reasons}", readyRes.Reasons);
                return Result.Fail<Identifiers>($"SignifyClientService: GetIdentifiers: Not ready.");
            }
            try {
                var jsonString = await _binding.GetAIDsAsync();
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<Identifiers>(unwrapped.Errors);

                var identifiers = JsonSerializer.Deserialize<Identifiers>(unwrapped.Value);
                if (identifiers is null) {
                    return Result.Fail<Identifiers>("Failed to deserialize Identifiers");
                }
                return Result.Ok(identifiers);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GetIdentifiers));
                return Result.Fail<Identifiers>(new JavaScriptInteropError(nameof(GetIdentifiers), e.Message, e));
            }
        }

        public async Task<Result<Aid>> GetIdentifier(string name) {
            try {
                var jsonString = await _binding.GetAIDAsync(name);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<Aid>(unwrapped.Errors);

                var aid = JsonSerializer.Deserialize<Aid>(unwrapped.Value);
                if (aid is null) {
                    return Result.Fail<Aid>("Failed to deserialize Identifier");
                }
                return Result.Ok(aid);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GetIdentifier));
                return Result.Fail<Aid>(new JavaScriptInteropError(nameof(GetIdentifier), e.Message, e));
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
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<State>(unwrapped.Errors);

                var state = JsonSerializer.Deserialize<State>(unwrapped.Value);
                if (state is null) {
                    return Result.Fail<State>("Failed to deserialize State");
                }
                return Result.Ok(state);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GetState));
                return Result.Fail<State>(new JavaScriptInteropError(nameof(GetState), e.Message, e));
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
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<List<RecursiveDictionary>>(unwrapped.Errors);

                var credentialsDict = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(unwrapped.Value, jsonSerializerOptions);
                if (credentialsDict is null) {
                    return Result.Fail("Failed to deserialize Credentials");
                }
                var credentials = credentialsDict.Select(RecursiveDictionary.FromObjectDictionary).ToList();
                return Result.Ok(credentials);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GetCredentials));
                return Result.Fail<List<RecursiveDictionary>>(new JavaScriptInteropError(nameof(GetCredentials), e.Message, e));
            }
        }

        public async Task<Result<string>> GetCredentialsRaw() {
            try {
                var jsonString = await _binding.GetCredentialsListAsync();
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<string>(unwrapped.Errors);

                return Result.Ok(unwrapped.Value);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GetCredentialsRaw));
                return Result.Fail<string>(new JavaScriptInteropError(nameof(GetCredentialsRaw), e.Message, e));
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
                var argsJson = JsonSerializer.Serialize(args, jsonSerializerOptions);
                var jsonString = await _binding.IpexApplyAsync(argsJson);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<IpexExchangeResult>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<IpexExchangeResult>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<IpexExchangeResult>("Failed to deserialize IpexExchangeResult");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(IpexApply));
                return Result.Fail<IpexExchangeResult>(new JavaScriptInteropError(nameof(IpexApply), e.Message, e));
            }
        }

        public async Task<Result<IpexExchangeResult>> IpexOffer(IpexOfferArgs args) {
            try {
                var argsJson = JsonSerializer.Serialize(args, jsonSerializerOptions);
                var jsonString = await _binding.IpexOfferAsync(argsJson);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<IpexExchangeResult>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<IpexExchangeResult>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<IpexExchangeResult>("Failed to deserialize IpexExchangeResult");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(IpexOffer));
                return Result.Fail<IpexExchangeResult>(new JavaScriptInteropError(nameof(IpexOffer), e.Message, e));
            }
        }

        public async Task<Result<IpexExchangeResult>> IpexAgree(IpexAgreeArgs args) {
            try {
                var argsJson = JsonSerializer.Serialize(args, jsonSerializerOptions);
                var jsonString = await _binding.IpexAgreeAsync(argsJson);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<IpexExchangeResult>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<IpexExchangeResult>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<IpexExchangeResult>("Failed to deserialize IpexExchangeResult");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(IpexAgree));
                return Result.Fail<IpexExchangeResult>(new JavaScriptInteropError(nameof(IpexAgree), e.Message, e));
            }
        }

        public async Task<Result<IpexExchangeResult>> IpexGrant(IpexGrantArgs args) {
            try {
                var argsJson = JsonSerializer.Serialize(args, jsonSerializerOptions);
                var jsonString = await _binding.IpexGrantAsync(argsJson);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<IpexExchangeResult>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<IpexExchangeResult>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<IpexExchangeResult>("Failed to deserialize IpexExchangeResult");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(IpexGrant));
                return Result.Fail<IpexExchangeResult>(new JavaScriptInteropError(nameof(IpexGrant), e.Message, e));
            }
        }

        public async Task<Result<IpexExchangeResult>> IpexAdmit(IpexAdmitArgs args) {
            try {
                var argsJson = JsonSerializer.Serialize(args, jsonSerializerOptions);
                var jsonString = await _binding.IpexAdmitAsync(argsJson);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<IpexExchangeResult>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<IpexExchangeResult>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<IpexExchangeResult>("Failed to deserialize IpexExchangeResult");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(IpexAdmit));
                return Result.Fail<IpexExchangeResult>(new JavaScriptInteropError(nameof(IpexAdmit), e.Message, e));
            }
        }

        // ===================== OOBI Operations =====================

        public async Task<Result<RecursiveDictionary>> GetOobi(string name, string? role = null) {
            try {
                var jsonString = await _binding.OobiGetAsync(name, role);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<RecursiveDictionary>(unwrapped.Errors);

                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(unwrapped.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize OOBI result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GetOobi));
                return Result.Fail<RecursiveDictionary>(new JavaScriptInteropError(nameof(GetOobi), e.Message, e));
            }
        }

        public async Task<Result<RecursiveDictionary>> ResolveOobi(string oobi, string? aliasName = null) {
            try {
                var jsonString = await _binding.OobiResolveAsync(oobi, aliasName);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<RecursiveDictionary>(unwrapped.Errors);

                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(unwrapped.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize OOBI resolve result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(ResolveOobi));
                return Result.Fail<RecursiveDictionary>(new JavaScriptInteropError(nameof(ResolveOobi), e.Message, e));
            }
        }

        // ===================== Operations Management =====================

        public async Task<Result<Operation>> GetOperation(string name) {
            try {
                var jsonString = await _binding.OperationsGetAsync(name);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<Operation>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<Operation>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<Operation>("Failed to deserialize Operation");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GetOperation));
                return Result.Fail<Operation>(new JavaScriptInteropError(nameof(GetOperation), e.Message, e));
            }
        }

        public async Task<Result<List<Operation>>> ListOperations(string? type = null) {
            try {
                var jsonString = await _binding.OperationsListAsync(type);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<List<Operation>>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<List<Operation>>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<List<Operation>>("Failed to deserialize Operations list");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(ListOperations));
                return Result.Fail<List<Operation>>(new JavaScriptInteropError(nameof(ListOperations), e.Message, e));
            }
        }

        public async Task<Result> DeleteOperation(string name) {
            try {
                var jsonString = await _binding.OperationsDeleteAsync(name);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail(unwrapped.Errors);

                return Result.Ok();
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(DeleteOperation));
                return Result.Fail(new JavaScriptInteropError(nameof(DeleteOperation), e.Message, e));
            }
        }

        public async Task<Result<Operation>> WaitForOperation(Operation operation, Dictionary<string, object>? options = null) {
            try {
                var operationJson = JsonSerializer.Serialize(operation, jsonSerializerOptions);
                var optionsJson = options != null ? JsonSerializer.Serialize(options, jsonSerializerOptions) : null;
                var jsonString = await _binding.OperationsWaitAsync(operationJson, optionsJson);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<Operation>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<Operation>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<Operation>("Failed to deserialize Operation result");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(WaitForOperation));
                return Result.Fail<Operation>(new JavaScriptInteropError(nameof(WaitForOperation), e.Message, e));
            }
        }

        // ===================== Contact Management =====================

        public async Task<Result<List<Contact>>> ListContacts(string? group = null, string? filterField = null, string? filterValue = null) {
            try {
                var jsonString = await _binding.ContactsListAsync(group, filterField, filterValue);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<List<Contact>>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<List<Contact>>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<List<Contact>>("Failed to deserialize Contacts list");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(ListContacts));
                return Result.Fail<List<Contact>>(new JavaScriptInteropError(nameof(ListContacts), e.Message, e));
            }
        }

        public async Task<Result<Contact>> GetContact(string prefix) {
            try {
                var jsonString = await _binding.ContactsGetAsync(prefix);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<Contact>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<Contact>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<Contact>("Failed to deserialize Contact");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GetContact));
                return Result.Fail<Contact>(new JavaScriptInteropError(nameof(GetContact), e.Message, e));
            }
        }

        public async Task<Result<Contact>> AddContact(string prefix, ContactInfo info) {
            try {
                var infoJson = JsonSerializer.Serialize(info, jsonSerializerOptions);
                var jsonString = await _binding.ContactsAddAsync(prefix, infoJson);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<Contact>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<Contact>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<Contact>("Failed to deserialize Contact");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(AddContact));
                return Result.Fail<Contact>(new JavaScriptInteropError(nameof(AddContact), e.Message, e));
            }
        }

        public async Task<Result<Contact>> UpdateContact(string prefix, ContactInfo info) {
            try {
                var infoJson = JsonSerializer.Serialize(info, jsonSerializerOptions);
                var jsonString = await _binding.ContactsUpdateAsync(prefix, infoJson);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<Contact>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<Contact>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<Contact>("Failed to deserialize Contact");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(UpdateContact));
                return Result.Fail<Contact>(new JavaScriptInteropError(nameof(UpdateContact), e.Message, e));
            }
        }

        public async Task<Result> DeleteContact(string prefix) {
            try {
                var jsonString = await _binding.ContactsDeleteAsync(prefix);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail(unwrapped.Errors);

                return Result.Ok();
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(DeleteContact));
                return Result.Fail(new JavaScriptInteropError(nameof(DeleteContact), e.Message, e));
            }
        }

        // ===================== Registry and Additional Operations =====================

        public async Task<Result<List<Registry>>> ListRegistries(string name) {
            try {
                var jsonString = await _binding.RegistriesListAsync(name);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<List<Registry>>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<List<Registry>>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<List<Registry>>("Failed to deserialize Registries list");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(ListRegistries));
                return Result.Fail<List<Registry>>(new JavaScriptInteropError(nameof(ListRegistries), e.Message, e));
            }
        }

        public async Task<Result<Registry>> CreateRegistry(CreateRegistryArgs args) {
            try {
                var argsJson = JsonSerializer.Serialize(args, jsonSerializerOptions);
                var jsonString = await _binding.RegistriesCreateAsync(argsJson);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<Registry>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<Registry>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<Registry>("Failed to deserialize Registry creation result");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(CreateRegistry));
                return Result.Fail<Registry>(new JavaScriptInteropError(nameof(CreateRegistry), e.Message, e));
            }
        }

        public async Task<Result<IssueCredentialResult>> IssueCredential(string name, CredentialData args) {
            try {
                var argsJson = JsonSerializer.Serialize(args, jsonSerializerOptions);
                logger.LogInformation("IssueCredential: Calling JS with name={name} and args={argsJson}", name, argsJson);
                var jsonString = await _binding.CredentialsIssueAsync(name, argsJson);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<IssueCredentialResult>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<IssueCredentialResult>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<IssueCredentialResult>("Failed to deserialize Credential issuance result");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(IssueCredential));
                return Result.Fail<IssueCredentialResult>(new JavaScriptInteropError(nameof(IssueCredential), e.Message, e));
            }
        }

        public async Task<Result<RevokeCredentialResult>> RevokeCredential(string name, string said, string? datetime = null) {
            try {
                var jsonString = await _binding.CredentialsRevokeAsync(name, said, datetime);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<RevokeCredentialResult>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<RevokeCredentialResult>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<RevokeCredentialResult>("Failed to deserialize Credential revocation result");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(RevokeCredential));
                return Result.Fail<RevokeCredentialResult>(new JavaScriptInteropError(nameof(RevokeCredential), e.Message, e));
            }
        }

        public async Task<Result<CredentialState>> GetCredentialState(string ri, string said) {
            try {
                var jsonString = await _binding.CredentialsStateAsync(ri, said);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<CredentialState>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<CredentialState>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<CredentialState>("Failed to deserialize Credential state");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GetCredentialState));
                return Result.Fail<CredentialState>(new JavaScriptInteropError(nameof(GetCredentialState), e.Message, e));
            }
        }

        public async Task<Result> DeleteCredential(string said) {
            try {
                var jsonString = await _binding.CredentialsDeleteAsync(said);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail(unwrapped.Errors);

                return Result.Ok();
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(DeleteCredential));
                return Result.Fail(new JavaScriptInteropError(nameof(DeleteCredential), e.Message, e));
            }
        }

        public async Task<Result<Schema>> GetSchema(string said) {
            try {
                var jsonString = await _binding.SchemasGetAsync(said);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<Schema>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<Schema>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<Schema>("Failed to deserialize Schema");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GetSchema));
                return Result.Fail<Schema>(new JavaScriptInteropError(nameof(GetSchema), e.Message, e));
            }
        }

        public async Task<Result<string>> GetSchemaRaw(string said) {
            try {
                var jsonString = await _binding.SchemasGetAsync(said);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<string>(unwrapped.Errors);
                return Result.Ok(unwrapped.Value);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GetSchemaRaw));
                return Result.Fail<string>(new JavaScriptInteropError(nameof(GetSchemaRaw), e.Message, e));
            }
        }

        public async Task<Result<List<Schema>>> ListSchemas() {
            try {
                var jsonString = await _binding.SchemasListAsync();
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<List<Schema>>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<List<Schema>>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<List<Schema>>("Failed to deserialize Schemas list");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(ListSchemas));
                return Result.Fail<List<Schema>>(new JavaScriptInteropError(nameof(ListSchemas), e.Message, e));
            }
        }

        public async Task<Result<List<RecursiveDictionary>>> ListNotifications(int? start = null, int? endIndex = null) {
            try {
                var jsonString = await _binding.NotificationsListAsync(start, endIndex);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<List<RecursiveDictionary>>(unwrapped.Errors);

                var resultDict = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(unwrapped.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<List<RecursiveDictionary>>("Failed to deserialize Notifications list");
                }
                var result = resultDict.Select(RecursiveDictionary.FromObjectDictionary).ToList();
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(ListNotifications));
                return Result.Fail<List<RecursiveDictionary>>(new JavaScriptInteropError(nameof(ListNotifications), e.Message, e));
            }
        }

        public async Task<Result<string>> MarkNotification(string said) {
            try {
                var jsonString = await _binding.NotificationsMarkAsync(said);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<string>(unwrapped.Errors);

                return Result.Ok(unwrapped.Value);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(MarkNotification));
                return Result.Fail<string>(new JavaScriptInteropError(nameof(MarkNotification), e.Message, e));
            }
        }

        public async Task<Result> DeleteNotification(string said) {
            try {
                var jsonString = await _binding.NotificationsDeleteAsync(said);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail(unwrapped.Errors);

                return Result.Ok();
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(DeleteNotification));
                return Result.Fail(new JavaScriptInteropError(nameof(DeleteNotification), e.Message, e));
            }
        }

        // ===================== Escrows Operations =====================

        public async Task<Result<List<RecursiveDictionary>>> ListEscrowReply(string? route = null) {
            try {
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.EscrowsListReplyAsync(route, ct),
                    AppConfig.SignifyTimeout
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<List<RecursiveDictionary>>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<List<RecursiveDictionary>>(unwrapped.Errors);

                var resultDict = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(unwrapped.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<List<RecursiveDictionary>>("Failed to deserialize Escrow reply list");
                }
                var result = resultDict.Select(RecursiveDictionary.FromObjectDictionary).ToList();
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(ListEscrowReply));
                return Result.Fail<List<RecursiveDictionary>>(new JavaScriptInteropError(nameof(ListEscrowReply), e.Message, e));
            }
        }

        // ===================== Groups Operations =====================

        public async Task<Result<RecursiveDictionary>> GetGroupRequest(string said) {
            try {
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.GroupsGetRequestAsync(said, ct),
                    AppConfig.SignifyTimeout
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<RecursiveDictionary>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<RecursiveDictionary>(unwrapped.Errors);

                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(unwrapped.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Group request");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GetGroupRequest));
                return Result.Fail<RecursiveDictionary>(new JavaScriptInteropError(nameof(GetGroupRequest), e.Message, e));
            }
        }

        public async Task<Result<RecursiveDictionary>> SendGroupRequest(string name, RecursiveDictionary exn, List<string> sigs, string atc) {
            try {
                var exnJson = JsonSerializer.Serialize(exn.ToDictionary(), jsonSerializerOptions);
                var sigsJson = JsonSerializer.Serialize(sigs, jsonSerializerOptions);
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.GroupsSendRequestAsync(name, exnJson, sigsJson, atc, ct),
                    AppConfig.SignifyTimeout
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<RecursiveDictionary>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<RecursiveDictionary>(unwrapped.Errors);

                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(unwrapped.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Group send request result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(SendGroupRequest));
                return Result.Fail<RecursiveDictionary>(new JavaScriptInteropError(nameof(SendGroupRequest), e.Message, e));
            }
        }

        public async Task<Result<RecursiveDictionary>> JoinGroup(string name, RecursiveDictionary rot, object sigs, string gid, List<string> smids, List<string> rmids) {
            try {
                var rotJson = JsonSerializer.Serialize(rot.ToDictionary(), jsonSerializerOptions);
                var sigsJson = JsonSerializer.Serialize(sigs, jsonSerializerOptions);
                var smidsJson = JsonSerializer.Serialize(smids, jsonSerializerOptions);
                var rmidsJson = JsonSerializer.Serialize(rmids, jsonSerializerOptions);
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.GroupsJoinAsync(name, rotJson, sigsJson, gid, smidsJson, rmidsJson, ct),
                    AppConfig.SignifyTimeout
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<RecursiveDictionary>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<RecursiveDictionary>(unwrapped.Errors);

                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(unwrapped.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Group join result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(JoinGroup));
                return Result.Fail<RecursiveDictionary>(new JavaScriptInteropError(nameof(JoinGroup), e.Message, e));
            }
        }

        // ===================== Exchanges Operations =====================

        public async Task<Result<RecursiveDictionary>> GetExchange(string said) {
            try {
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.ExchangesGetAsync(said, ct),
                    AppConfig.SignifyTimeout
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<RecursiveDictionary>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<RecursiveDictionary>(unwrapped.Errors);

                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(unwrapped.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Exchange");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GetExchange));
                return Result.Fail<RecursiveDictionary>(new JavaScriptInteropError(nameof(GetExchange), e.Message, e));
            }
        }

        public async Task<Result<string>> GetExchangeRaw(string said) {
            try {
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.ExchangesGetAsync(said, ct),
                    AppConfig.SignifyTimeout
                );
                if (timeoutResult.IsFailed) return Result.Fail<string>(timeoutResult.Errors);
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<string>(unwrapped.Errors);
                return Result.Ok(unwrapped.Value);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GetExchangeRaw));
                return Result.Fail<string>(new JavaScriptInteropError(nameof(GetExchangeRaw), e.Message, e));
            }
        }

        public async Task<Result<RecursiveDictionary>> SendExchange(string name, string topic, RecursiveDictionary sender, string route, RecursiveDictionary payload, RecursiveDictionary embeds, List<string> recipients) {
            try {
                var senderJson = JsonSerializer.Serialize(sender.ToDictionary(), jsonSerializerOptions);
                var payloadJson = JsonSerializer.Serialize(payload.ToDictionary(), jsonSerializerOptions);
                var embedsJson = JsonSerializer.Serialize(embeds.ToDictionary(), jsonSerializerOptions);
                var recipientsJson = JsonSerializer.Serialize(recipients, jsonSerializerOptions);
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.ExchangesSendAsync(name, topic, senderJson, route, payloadJson, embedsJson, recipientsJson, ct),
                    AppConfig.SignifyTimeout
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<RecursiveDictionary>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<RecursiveDictionary>(unwrapped.Errors);

                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(unwrapped.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Exchange send result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(SendExchange));
                return Result.Fail<RecursiveDictionary>(new JavaScriptInteropError(nameof(SendExchange), e.Message, e));
            }
        }

        public async Task<Result<RecursiveDictionary>> SendExchangeFromEvents(string name, string topic, RecursiveDictionary exn, List<string> sigs, string atc, List<string> recipients) {
            try {
                var exnJson = JsonSerializer.Serialize(exn.ToDictionary(), jsonSerializerOptions);
                var sigsJson = JsonSerializer.Serialize(sigs, jsonSerializerOptions);
                var recipientsJson = JsonSerializer.Serialize(recipients, jsonSerializerOptions);
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.ExchangesSendFromEventsAsync(name, topic, exnJson, sigsJson, atc, recipientsJson, ct),
                    AppConfig.SignifyTimeout
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<RecursiveDictionary>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<RecursiveDictionary>(unwrapped.Errors);

                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(unwrapped.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Exchange send from events result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(SendExchangeFromEvents));
                return Result.Fail<RecursiveDictionary>(new JavaScriptInteropError(nameof(SendExchangeFromEvents), e.Message, e));
            }
        }

        // ===================== Delegations Operations =====================

        public async Task<Result<RecursiveDictionary>> ApproveDelegation(string name, RecursiveDictionary? data = null) {
            try {
                var dataJson = data != null ? JsonSerializer.Serialize(data.ToDictionary(), jsonSerializerOptions) : null;
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.DelegationsApproveAsync(name, dataJson, ct),
                    AppConfig.SignifyTimeout
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<RecursiveDictionary>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<RecursiveDictionary>(unwrapped.Errors);

                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(unwrapped.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize Delegation approval result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(ApproveDelegation));
                return Result.Fail<RecursiveDictionary>(new JavaScriptInteropError(nameof(ApproveDelegation), e.Message, e));
            }
        }

        // ===================== KeyEvents Operations =====================

        public async Task<Result<RecursiveDictionary>> GetKeyEvents(string prefix) {
            try {
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.KeyEventsGetAsync(prefix, ct),
                    AppConfig.SignifyTimeout
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<RecursiveDictionary>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<RecursiveDictionary>(unwrapped.Errors);

                logger.LogInformation(nameof(GetKeyEvents) + ": raw response (first 500 chars): {response}",
                    unwrapped.Value.Substring(0, Math.Min(500, unwrapped.Value.Length)));

                // GetKeyEvents returns an array of event objects, each with 'ked' (key event data) and 'atc' (attachment) fields
                var resultArray = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(unwrapped.Value, jsonSerializerOptions);
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
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GetKeyEvents));
                return Result.Fail<RecursiveDictionary>(new JavaScriptInteropError(nameof(GetKeyEvents), e.Message, e));
            }
        }

        // ===================== KeyStates Operations =====================

        public async Task<Result<KeyState>> GetKeyState(string prefix) {
            try {
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.KeyStatesGetAsync(prefix, ct),
                    AppConfig.SignifyTimeout
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<KeyState>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<KeyState>(unwrapped.Errors);

                logger.LogDebug(nameof(GetKeyState) + ": raw response (first 500 chars): {response}",
                    unwrapped.Value.Substring(0, Math.Min(500, unwrapped.Value.Length)));

                // GetKeyState returns an array with a single key state object
                var resultArray = JsonSerializer.Deserialize<List<KeyState>>(unwrapped.Value, jsonSerializerOptions);
                if (resultArray is null || resultArray.Count == 0) {
                    return Result.Fail<KeyState>("Failed to deserialize KeyState or array is empty");
                }

                // Extract the first (and typically only) key state from the array
                return Result.Ok(resultArray[0]);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GetKeyState));
                return Result.Fail<KeyState>(new JavaScriptInteropError(nameof(GetKeyState), e.Message, e));
            }
        }

        public async Task<Result<List<KeyState>>> ListKeyStates(List<string> prefixes) {
            try {
                var prefixesJson = JsonSerializer.Serialize(prefixes, jsonSerializerOptions);
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.KeyStatesListAsync(prefixesJson, ct),
                    AppConfig.SignifyTimeout
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<List<KeyState>>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<List<KeyState>>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<List<KeyState>>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<List<KeyState>>("Failed to deserialize KeyStates list");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(ListKeyStates));
                return Result.Fail<List<KeyState>>(new JavaScriptInteropError(nameof(ListKeyStates), e.Message, e));
            }
        }

        public async Task<Result<Operation>> QueryKeyState(string prefix, string? sn = null, RecursiveDictionary? anchor = null) {
            try {
                var anchorJson = anchor != null ? JsonSerializer.Serialize(anchor.ToDictionary(), jsonSerializerOptions) : null;
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.KeyStatesQueryAsync(prefix, sn, anchorJson, ct),
                    AppConfig.SignifyTimeout
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<Operation>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<Operation>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<Operation>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<Operation>("Failed to deserialize KeyState query result");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(QueryKeyState));
                return Result.Fail<Operation>(new JavaScriptInteropError(nameof(QueryKeyState), e.Message, e));
            }
        }

        // ===================== Config Operations =====================

        public async Task<Result<AgentConfig>> GetAgentConfig() {
            try {
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.ConfigGetAsync(ct),
                    AppConfig.SignifyTimeout
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<AgentConfig>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<AgentConfig>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<AgentConfig>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<AgentConfig>("Failed to deserialize Agent config");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GetAgentConfig));
                return Result.Fail<AgentConfig>(new JavaScriptInteropError(nameof(GetAgentConfig), e.Message, e));
            }
        }

        // ===================== Challenges Operations =====================

        public async Task<Result<Challenge>> GenerateChallenge(int strength = 128) {
            try {
                var jsonString = await _binding.ChallengesGenerateAsync(strength);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<Challenge>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<Challenge>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<Challenge>("Failed to deserialize Challenge");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GenerateChallenge));
                return Result.Fail<Challenge>(new JavaScriptInteropError(nameof(GenerateChallenge), e.Message, e));
            }
        }

        public async Task<Result<RecursiveDictionary>> RespondToChallenge(string name, string recipient, List<string> words) {
            try {
                var wordsJson = JsonSerializer.Serialize(words, jsonSerializerOptions);
                var jsonString = await _binding.ChallengesRespondAsync(name, recipient, wordsJson);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<RecursiveDictionary>(unwrapped.Errors);

                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(unwrapped.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize challenge response");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(RespondToChallenge));
                return Result.Fail<RecursiveDictionary>(new JavaScriptInteropError(nameof(RespondToChallenge), e.Message, e));
            }
        }

        public async Task<Result<Operation>> VerifyChallenge(string source, List<string> words) {
            try {
                var wordsJson = JsonSerializer.Serialize(words, jsonSerializerOptions);
                var jsonString = await _binding.ChallengesVerifyAsync(source, wordsJson);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<Operation>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<Operation>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<Operation>("Failed to deserialize verify challenge result");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(VerifyChallenge));
                return Result.Fail<Operation>(new JavaScriptInteropError(nameof(VerifyChallenge), e.Message, e));
            }
        }

        public async Task<Result<ChallengeRespondedResult>> ChallengeResponded(string source, string said) {
            try {
                var jsonString = await _binding.ChallengesRespondedAsync(source, said);
                var unwrapped = UnwrapJsResult(jsonString);
                if (unwrapped.IsFailed) return Result.Fail<ChallengeRespondedResult>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<ChallengeRespondedResult>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<ChallengeRespondedResult>("Failed to deserialize challenge responded result");
                }
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(ChallengeResponded));
                return Result.Fail<ChallengeRespondedResult>(new JavaScriptInteropError(nameof(ChallengeResponded), e.Message, e));
            }
        }

        // ===================== Composite vLEI Operations =====================

        public async Task<Result<AidWithOobi>> CreateAidWithEndRole(string name, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? AppConfig.SignifyLongOperationTimeout;
            try {
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.CreateAidWithEndRoleAsync(name, ct),
                    timeout2
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<AidWithOobi>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<AidWithOobi>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<AidWithOobi>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<AidWithOobi>("Failed to deserialize AidWithOobi");
                }
                logger.LogInformation(nameof(CreateAidWithEndRole) + ": Created AID '{name}' with prefix {prefix}", name, result.Prefix);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(CreateAidWithEndRole));
                return Result.Fail<AidWithOobi>(new JavaScriptInteropError(nameof(CreateAidWithEndRole), e.Message, e));
            }
        }

        public async Task<Result<DelegateAidResult>> CreateDelegateAid(string name, string delegatorPrefix, string delegatorOobi, string delegatorAlias, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? AppConfig.SignifyLongOperationTimeout;
            try {
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.CreateDelegateAidAsync(name, delegatorPrefix, delegatorOobi, delegatorAlias, ct),
                    timeout2
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<DelegateAidResult>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<DelegateAidResult>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<DelegateAidResult>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<DelegateAidResult>("Failed to deserialize DelegateAidResult");
                }
                logger.LogInformation(nameof(CreateDelegateAid) + ": Created delegate AID '{name}' with prefix {prefix}", name, result.Prefix);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(CreateDelegateAid));
                return Result.Fail<DelegateAidResult>(new JavaScriptInteropError(nameof(CreateDelegateAid), e.Message, e));
            }
        }

        public async Task<Result<RegistryCheckResult>> CreateRegistryIfNotExists(string aidName, string registryName, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? AppConfig.SignifyLongOperationTimeout;
            try {
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.CreateRegistryIfNotExistsAsync(aidName, registryName, ct),
                    timeout2
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<RegistryCheckResult>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<RegistryCheckResult>(unwrapped.Errors);

                var result = JsonSerializer.Deserialize<RegistryCheckResult>(unwrapped.Value, jsonSerializerOptions);
                if (result is null) {
                    return Result.Fail<RegistryCheckResult>("Failed to deserialize RegistryCheckResult");
                }
                logger.LogInformation(nameof(CreateRegistryIfNotExists) + ": Registry '{registryName}' for AID '{aidName}': created={created}", registryName, aidName, result.Created);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(CreateRegistryIfNotExists));
                return Result.Fail<RegistryCheckResult>(new JavaScriptInteropError(nameof(CreateRegistryIfNotExists), e.Message, e));
            }
        }

        public async Task<Result<string>> GetCredentialsFilteredCesr(string filterJson, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? AppConfig.SignifyTimeout;
            try {
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.CredentialsListFilteredCesrAsync(filterJson, ct),
                    timeout2
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<string>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<string>(unwrapped.Errors);

                return Result.Ok(unwrapped.Value);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GetCredentialsFilteredCesr));
                return Result.Fail<string>(new JavaScriptInteropError(nameof(GetCredentialsFilteredCesr), e.Message, e));
            }
        }

        public async Task<Result<string>> GetCredentialsBySchemaAndIssuerCesr(string schemaSaid, string issuerPrefix, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? AppConfig.SignifyTimeout;
            try {
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.CredentialsBySchemaAndIssuerCesrAsync(schemaSaid, issuerPrefix, ct),
                    timeout2
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<string>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<string>(unwrapped.Errors);

                return Result.Ok(unwrapped.Value);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GetCredentialsBySchemaAndIssuerCesr));
                return Result.Fail<string>(new JavaScriptInteropError(nameof(GetCredentialsBySchemaAndIssuerCesr), e.Message, e));
            }
        }

        public async Task<Result<RecursiveDictionary>> IssueAndGetCredential(IssueAndGetCredentialArgs args, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? AppConfig.SignifyLongOperationTimeout;
            try {
                var argsJson = JsonSerializer.Serialize(args, recursiveJsonSerializerOptions);
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.IssueAndGetCredentialAsync(argsJson, ct),
                    timeout2
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<RecursiveDictionary>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<RecursiveDictionary>(unwrapped.Errors);

                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(unwrapped.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize IssueAndGetCredential result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(IssueAndGetCredential));
                return Result.Fail<RecursiveDictionary>(new JavaScriptInteropError(nameof(IssueAndGetCredential), e.Message, e));
            }
        }

        public async Task<Result<RecursiveDictionary>> IpexGrantAndSubmit(IpexGrantSubmitArgs args, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? AppConfig.SignifyLongOperationTimeout;
            try {
                var argsJson = JsonSerializer.Serialize(args, recursiveJsonSerializerOptions);
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.IpexGrantAndSubmitAsync(argsJson, ct),
                    timeout2
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<RecursiveDictionary>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<RecursiveDictionary>(unwrapped.Errors);

                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(unwrapped.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize IpexGrantAndSubmit result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(IpexGrantAndSubmit));
                return Result.Fail<RecursiveDictionary>(new JavaScriptInteropError(nameof(IpexGrantAndSubmit), e.Message, e));
            }
        }

        public async Task<Result<RecursiveDictionary>> IpexAdmitAndSubmit(IpexAdmitSubmitArgs args, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? AppConfig.SignifyLongOperationTimeout;
            logger.LogInformation("{Op}: Calling JS interop (timeout={Timeout}s)", nameof(IpexAdmitAndSubmit), timeout2.TotalSeconds);
            try {
                var argsJson = JsonSerializer.Serialize(args, jsonSerializerOptions);
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.IpexAdmitAndSubmitAsync(argsJson, ct),
                    timeout2
                );
                logger.LogInformation("{Op}: JS interop returned, IsFailed={IsFailed}", nameof(IpexAdmitAndSubmit), timeoutResult.IsFailed);
                if (timeoutResult.IsFailed) {
                    var failMsg = timeoutResult.Errors.Count > 0 ? timeoutResult.Errors[0].Message : "unknown";
                    logger.LogWarning("{Op}: TimeoutHelper returned failure: {Error}", nameof(IpexAdmitAndSubmit), failMsg);
                    return Result.Fail<RecursiveDictionary>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) {
                    logger.LogWarning("{Op}: UnwrapJsResult failed: {Error}", nameof(IpexAdmitAndSubmit),
                        unwrapped.Errors.Count > 0 ? unwrapped.Errors[0].Message : "unknown");
                    return Result.Fail<RecursiveDictionary>(unwrapped.Errors);
                }

                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(unwrapped.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    logger.LogWarning("{Op}: Deserialization returned null", nameof(IpexAdmitAndSubmit));
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize IpexAdmitAndSubmit result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                logger.LogInformation("{Op}: Completed successfully", nameof(IpexAdmitAndSubmit));
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(IpexAdmitAndSubmit));
                return Result.Fail<RecursiveDictionary>(new JavaScriptInteropError(nameof(IpexAdmitAndSubmit), e.Message, e));
            }
        }

        public async Task<Result<RecursiveDictionary>> IpexApplyAndSubmit(IpexApplySubmitArgs args, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? AppConfig.SignifyLongOperationTimeout;
            try {
                var argsJson = JsonSerializer.Serialize(args, recursiveJsonSerializerOptions);
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.IpexApplyAndSubmitAsync(argsJson, ct),
                    timeout2
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<RecursiveDictionary>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<RecursiveDictionary>(unwrapped.Errors);

                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(unwrapped.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize IpexApplyAndSubmit result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(IpexApplyAndSubmit));
                return Result.Fail<RecursiveDictionary>(new JavaScriptInteropError(nameof(IpexApplyAndSubmit), e.Message, e));
            }
        }

        public async Task<Result<RecursiveDictionary>> IpexOfferAndSubmit(IpexOfferSubmitArgs args, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? AppConfig.SignifyLongOperationTimeout;
            try {
                var argsJson = JsonSerializer.Serialize(args, jsonSerializerOptions);
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.IpexOfferAndSubmitAsync(argsJson, ct),
                    timeout2
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<RecursiveDictionary>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<RecursiveDictionary>(unwrapped.Errors);

                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(unwrapped.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize IpexOfferAndSubmit result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(IpexOfferAndSubmit));
                return Result.Fail<RecursiveDictionary>(new JavaScriptInteropError(nameof(IpexOfferAndSubmit), e.Message, e));
            }
        }

        public async Task<Result<RecursiveDictionary>> IpexAgreeAndSubmit(IpexAgreeSubmitArgs args, TimeSpan? timeout = null) {
            logger.LogInformation("{Op}: sender={Sender}, recipient={Recipient}, offerSaid={OfferSaid}",
                nameof(IpexAgreeAndSubmit), args.SenderNameOrPrefix, args.RecipientPrefix, args.OfferSaid);
            var timeout2 = timeout ?? AppConfig.SignifyLongOperationTimeout;
            try {
                var argsJson = JsonSerializer.Serialize(args, jsonSerializerOptions);
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.IpexAgreeAndSubmitAsync(argsJson, ct),
                    timeout2
                );
                if (timeoutResult.IsFailed) {
                    logger.LogWarning("{Op}: timeout or failure: {Errors}", nameof(IpexAgreeAndSubmit),
                        string.Join("; ", timeoutResult.Errors.Select(e => e.Message)));
                    return Result.Fail<RecursiveDictionary>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<RecursiveDictionary>(unwrapped.Errors);

                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(unwrapped.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize IpexAgreeAndSubmit result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                logger.LogInformation("{Op}: succeeded", nameof(IpexAgreeAndSubmit));
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(IpexAgreeAndSubmit));
                return Result.Fail<RecursiveDictionary>(new JavaScriptInteropError(nameof(IpexAgreeAndSubmit), e.Message, e));
            }
        }

        public async Task<Result<RecursiveDictionary>> GrantReceivedCredential(string senderAidNameOrPrefix, string credentialSaid, string recipientPrefix, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? AppConfig.SignifyLongOperationTimeout;
            try {
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.GrantReceivedCredentialAsync(senderAidNameOrPrefix, credentialSaid, recipientPrefix, ct),
                    timeout2
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<RecursiveDictionary>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<RecursiveDictionary>(unwrapped.Errors);

                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(unwrapped.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize GrantReceivedCredential result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GrantReceivedCredential));
                return Result.Fail<RecursiveDictionary>(new JavaScriptInteropError(nameof(GrantReceivedCredential), e.Message, e));
            }
        }

        public async Task<Result<RecursiveDictionary>> GrantWithElidedAcdc(string senderAidNameOrPrefix, string elidedAcdcJson, string credentialSaid, string recipientPrefix, TimeSpan? timeout = null) {
            var timeout2 = timeout ?? AppConfig.SignifyLongOperationTimeout;
            try {
                var timeoutResult = await TimeoutHelper.WithTimeout<string>(
                    ct => _binding.GrantWithElidedAcdcAsync(senderAidNameOrPrefix, elidedAcdcJson, credentialSaid, recipientPrefix, ct),
                    timeout2
                );
                if (timeoutResult.IsFailed) {
                    return Result.Fail<RecursiveDictionary>(timeoutResult.Errors);
                }
                var unwrapped = UnwrapJsResult(timeoutResult.Value);
                if (unwrapped.IsFailed) return Result.Fail<RecursiveDictionary>(unwrapped.Errors);

                var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(unwrapped.Value, jsonSerializerOptions);
                if (resultDict is null) {
                    return Result.Fail<RecursiveDictionary>("Failed to deserialize GrantWithElidedAcdc result");
                }
                var result = RecursiveDictionary.FromObjectDictionary(resultDict);
                return Result.Ok(result);
            }
            catch (JSException e) {
                logger.LogError(e, "{Op}: Unexpected JSException", nameof(GrantWithElidedAcdc));
                return Result.Fail<RecursiveDictionary>(new JavaScriptInteropError(nameof(GrantWithElidedAcdc), e.Message, e));
            }
        }
    }
}
