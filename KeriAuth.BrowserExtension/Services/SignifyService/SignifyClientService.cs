using FluentResults;
using KeriAuth.BrowserExtension.Helper;
using KeriAuth.BrowserExtension.Services.SignifyService.Models;
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using static KeriAuth.BrowserExtension.Services.SignifyService.Signify_ts_shim;
using Group = KeriAuth.BrowserExtension.Services.SignifyService.Models.Group;
using State = KeriAuth.BrowserExtension.Services.SignifyService.Models.State;

namespace KeriAuth.BrowserExtension.Services.SignifyService
{
    public class SignifyClientService(ILogger<SignifyClientService> logger) : ISignifyClientService
    {
        public Task<Result<HttpResponseMessage>> ApproveDelegation()
        {
            return Task.FromResult(Result.Fail<HttpResponseMessage>("Not implemented"));
        }

        public async Task<Result> HealthCheck(Uri fullUrl)
        {
            var httpClientService = new HttpClientService(new HttpClient());
            var postResult = await httpClientService.GetJsonAsync<string>(fullUrl.ToString());
            return postResult.IsSuccess ? Result.Ok() : Result.Fail(postResult.Reasons.First().Message);
        }

        public async Task<Result<bool>> Connect(string agentUrl, string passcode, string? bootUrl, bool isBootForced = true, TimeSpan? timeout = null)
        {
            Debug.Assert(bootUrl is not null);
            if (passcode.Length != 21)
            {
                return Result.Fail<bool>("Passcode must be 21 characters");
            }
            logger.LogInformation("Connect...");

            TimeSpan timeout2;
            if (timeout is null)
                timeout2 = (TimeSpan)TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs);
            else
                timeout2 = (TimeSpan)timeout;
            try
            {
                // simple example of using https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/call-javascript-from-dotnet?view=aspnetcore-8.0
                if (OperatingSystem.IsBrowser())
                {
                    if (isBootForced)
                    {
                        var res = await TimeoutHelper.WithTimeout<string>(ct => BootAndConnect(agentUrl, bootUrl, passcode), timeout2);
                        Debug.Assert(res is not null);
                        // Note that we are not parsing the result here, just logging it. The browser developer console will show the result, but can't display it as a collapse
                        // Don't log the following, since it contains the bran, passcode.
                        // logger.LogInformation("Connect: {connectResults}", res);
                        if (res is null)
                        {
                            return Result.Fail("Connect failed with null");
                        }
                        if (res.IsFailed)
                        {
                            return Result.Fail("Connect failed: " + res.Errors[0].Message);
                        }
                        return Result.Ok(true);
                    }
                    else
                    {
                        var res = await TimeoutHelper.WithTimeout<string>(ct => Signify_ts_shim.Connect(agentUrl, passcode), timeout2);
                        Debug.Assert(res is not null);
                        // Note that we are not parsing the result here, just logging it. The browser developer console will show the result, but can't display it as a collapsable object
                        // Don't log the following, since it contains the bran, passcode.
                        // logger.LogInformation("Connect: {connectResults}", res);
                        if (res is null)
                        {
                            return Result.Fail("Connect failed with null");
                        }
                        if (res.IsFailed)
                        {
                            return Result.Fail("Connect failed: " + res.Errors[0].Message);
                        }
                        return Result.Ok(true);
                    }
                }
                else return false.ToResult();
            }
            catch (JSException e)
            {
                logger.LogWarning("Connect: JSException: {e}", e);
                return Result.Fail<bool>("SignifyClientService: Connect: Exception: " + e);
            }
            catch (Exception e)
            {
                logger.LogWarning("Connect: Exception: {e}", e);
                return Result.Fail<bool>("SignifyClientService: Connect: Exception: " + e);
            }

        }

        public Task<Result<bool>> Connect()
        {
            throw new NotImplementedException();
            // return Task.FromResult(Result.Fail<bool>("Not implemented"));
        }

        public async Task<Result<string>> RunCreateAid(string aidName, TimeSpan? timeout = null)
        {
            TimeSpan timeout2;
            if (timeout is null)
                timeout2 = (TimeSpan)TimeSpan.FromMilliseconds(AppConfig.SignifyTimeoutMs);
            else
                timeout2 = (TimeSpan)timeout;
            try
            {
                var res = await TimeoutHelper.WithTimeout<string>(ct => CreateAID(aidName), timeout2);
                if (res.IsSuccess)
                {
                    logger.LogInformation("RunCreateAid: {res}", res.Value);
                    var jsonString = res.Value;
                    if (jsonString is null)
                    {
                        return Result.Fail<string>("CreateAID returned null");
                    }
                    else
                    {
                        return Result.Ok(jsonString);
                    }
                }
                else
                {
                    logger.LogWarning("RunCreateAid: {res}", res.Errors);
                    return Result.Fail<string>(res.Errors[0].Message);
                }
            }
            catch (JSException e)
            {
                logger.LogWarning("RunCreateAid: JSException: {e}", e);
                return Result.Fail<string>("SignifyClientService: CreatePersonAid: Exception: " + e);
            }
            catch (Exception e)
            {
                logger.LogWarning("RunCreateAid: Exception: {e}", e);
                return Result.Fail<string>("SignifyClientService: CreatePersonAid: Exception: " + e);
            }
        }

        public Task<Result<HttpResponseMessage>> DeletePasscode()
        {
            return Task.FromResult(Result.Fail<HttpResponseMessage>("Not implemented"));
        }

        public Task<Result<HttpResponseMessage>> Fetch(string path, string method, object data, Dictionary<string, string>? extraHeaders)
        {
            return Task.FromResult(Result.Fail<HttpResponseMessage>("Not implemented"));
        }

        public Task<Result<IList<Challenge>>> GetChallenges()
        {
            return Task.FromResult(Result.Fail<IList<Challenge>>("Not implemented"));
        }

        public Task<Result<IList<Contact>>> GetContacts()
        {
            return Task.FromResult(Result.Fail<IList<Contact>>("Not implemented"));
        }

        public Task<Result<IList<Escrow>>> GetEscrows()
        {
            return Task.FromResult(Result.Fail<IList<Escrow>>("Not implemented"));
        }

        public Task<Result<IList<Exchange>>> GetExchanges()
        {
            return Task.FromResult(Result.Fail<IList<Exchange>>("Not implemented"));
        }

        public Task<Result<IList<Group>>> GetGroups()
        {
            return Task.FromResult(Result.Fail<IList<Group>>("Not implemented"));
        }

        public async Task<Result<Identifiers>> GetIdentifiers()
        {
            try
            {
                var jsonString = await GetAIDs();
                if (jsonString is null)
                {
                    return Result.Fail<Identifiers>("GetAIDs returned null");
                }
                var identifiers = System.Text.Json.JsonSerializer.Deserialize<Identifiers>(jsonString);
                if (identifiers is null)
                {
                    return Result.Fail<Identifiers>("SignifyClientService: GetIdentifiers: Failed to deserialize Identifiers");
                }
                return Result.Ok(identifiers);
            }
            catch (JSException e)
            {
                logger.LogWarning("GetIdentifiers: JSException: {e}", e);
                return Result.Fail<Identifiers>("SignifyClientService: GetIdentifiers: Exception: " + e);
            }
            catch (Exception e)
            {
                logger.LogWarning("GetIdentifiers: Exception: {e}", e);
                return Result.Fail<Identifiers>("SignifyClientService: GetIdentifiers: Exception: " + e);
            }
        }

        public async Task<Result<Aid>> GetIdentifier(string name)
        {
            try
            {
                var jsonString = await GetAID(name);
                if (jsonString is null)
                {
                    return Result.Fail<Aid>("GetAID returned null");
                }
                var aid = System.Text.Json.JsonSerializer.Deserialize<Aid>(jsonString);
                if (aid is null)
                {
                    return Result.Fail<Aid>("Failed to deserialize Identifier");
                }
                return Result.Ok(aid);
            }
            catch (JSException e)
            {
                logger.LogWarning("GetIdentifiers: JSException: {e}", e);
                return Result.Fail<Aid>("SignifyClientService: GetIdentifier: Exception: " + e);
            }
            catch (Exception e)
            {
                logger.LogWarning("GetIdentifiers: Exception: {e}", e);
                return Result.Fail<Aid>("SignifyClientService: GetIdentifier: Exception: " + e);
            }
        }

        public Task<Result<IList<Ipex>>> GetIpex()
        {
            return Task.FromResult(Result.Fail<IList<Ipex>>("Not implemented"));
        }

        public Task<Result<IList<KeyEvent>>> GetKeyEvents()
        {
            return Task.FromResult(Result.Fail<IList<KeyEvent>>("Not implemented"));
        }

        public Task<Result<IList<KeyState>>> GetKeyStates()
        {
            return Task.FromResult(Result.Fail<IList<KeyState>>("Not implemented"));
        }

        public static Task<Result<IList<Models.Notification>>> GetNotifications()
        {
            return Task.FromResult(Result.Fail<IList<Models.Notification>>("Not implemented"));
        }

        public Task<Result<IList<Oobi>>> GetOobis()
        {
            return Task.FromResult(Result.Fail<IList<Oobi>>("Not implemented"));
        }

        public Task<Result<IList<Operation>>> GetOperations()
        {
            return Task.FromResult(Result.Fail<IList<Operation>>("Not implemented"));
        }

        public static Task<Result<IList<Models.Registry>>> GetRegistries()
        {
            return Task.FromResult(Result.Fail<IList<Models.Registry>>("Not implemented"));
        }

        public Task<Result<IList<Schema>>> GetSchemas()
        {
            return Task.FromResult(Result.Fail<IList<Schema>>("Not implemented"));
        }

        public Task<Result<State>> GetState()
        {
            return Task.FromResult(Result.Fail<State>("Not implemented"));
        }

        public Task<Result<HttpResponseMessage>> Rotate(string nbran, string[] aids)
        {
            return Task.FromResult(Result.Fail<HttpResponseMessage>("Not implemented"));
        }

        public Task<Result<HttpResponseMessage>> SaveOldPasscode(string passcode)
        {
            return Task.FromResult(Result.Fail<HttpResponseMessage>("Not implemented"));
        }

        public Task<Result<HttpResponseMessage>> SignedFetch(string url, string path, string method, object data, string aidName)
        {
            return Task.FromResult(Result.Fail<HttpResponseMessage>("Not implemented"));
        }

        public async Task<Result<Dictionary<string, object>>> GetCredential(string said)
        {
            var res = await GetCredentials();
            if (res.IsFailed)
            {
                return res.ToResult<Dictionary<string, object>>();
            }
            foreach (var credDict in res.Value)
            {
                var credDictSaid = DictionaryConverter.GetValueByPath(credDict, "sad.d")?.Value?.ToString();
                if (credDictSaid is not null && credDictSaid == said)
                {
                    return credDict.ToResult<Dictionary<string, object>>();
                }
            }
            return Result.Fail($"Could not find credential with said {said}");
        }

        readonly JsonSerializerOptions jsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new DictionaryConverter() }
        };

        public async Task<Result<List<Dictionary<string, object>>>> GetCredentials()
        {
            try
            {
                var jsonString = await GetCredentialsList();
                // logger.LogInformation("GetCredentials: {jsonString}", jsonString);
                if (jsonString is null)
                {
                    return Result.Fail("GetCredentials returned null");
                }
                var credentials = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonString, jsonSerializerOptions);
                if (credentials is null)
                {
                    return Result.Fail("SignifyClientService: GetCredentials: Failed to deserialize Credentials");
                }
                return Result.Ok<List<Dictionary<string, object>>>(credentials);
            }
            catch (JSException e)
            {
                logger.LogWarning("GetIdentifiers: JSException: {e}", e);
                return Result.Fail("SignifyClientService: GetCredentials: Exception: " + e);
            }
            catch (Exception e)
            {
                logger.LogWarning("GetIdentifiers: Exception: {e}", e);
                return Result.Fail("SignifyClientService: GetCredentials: Exception: " + e);
            }
        }

        Task<Result<IList<Models.Registry>>> ISignifyClientService.GetRegistries()
        {
            throw new NotImplementedException();
        }

        Task<Result<IList<Models.Notification>>> ISignifyClientService.GetNotifications()
        {
            throw new NotImplementedException();
        }
        async Task<Result<string>> ISignifyClientService.SignRequestHeader(string origin, string rurl, string method, Dictionary<string, string> initHeadersDict, string prefix)
        {
            await Task.Delay(0);
            throw new NotImplementedException();
        }
    }
}
