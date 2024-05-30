using FluentResults;
using KeriAuth.BrowserExtension.Services.SignifyService.Models;
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
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

        public async Task<Result<bool>> Connect(string agentUrl, string passcode, string? bootUrl, bool isBootForced = true)
        {
            Debug.Assert(bootUrl is not null);
            if (passcode.Length != 21)
            {
                return Result.Fail<bool>("Passcode must be 21 characters long");
            }
            await Task.Delay(0);
            logger.LogInformation("Connect...");

            try
            {
                // simple example of using https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/call-javascript-from-dotnet?view=aspnetcore-8.0
                if (OperatingSystem.IsBrowser())
                {
                    if (isBootForced)
                    {
                        var res = await BootAndConnect(agentUrl, bootUrl, passcode);
                        Debug.Assert(res is not null);
                        // Note that we are not parsing the result here, just logging it. The browser developer console will show the result, but can't display it as a collapse
                        logger.LogInformation("Connect: {connectResults}", res);
                        // TODO fix
                        return true.ToResult();
                    }
                    else
                    {
                        throw new NotImplementedException();
                        //var res = await Signify_ts_shim.Connect(agentUrl, bootUrl, passcode);
                        //Debug.Assert(res is not null);
                        //// Note that we are not parsing the result here, just logging it. The browser developer console will show the result, but can't display it as a collapse
                        // logger.LogInformation("SignifyClientService: Connect: {@Details}", res);
                        //// TODO fix
                        //return true.ToResult<bool>();
                    }
                }
                else return false.ToResult();
            }
            catch (JSException e)
            {
                logger.LogWarning("SignifyClientService: Connect: JSException: {e}", e);
                return Result.Fail<bool>("SignifyClientService: Connect: Exception: " + e);
            }
            catch (Exception e)
            {
                logger.LogWarning("SignifyClientService: Connect: Exception: {e}", e);
                return Result.Fail<bool>("SignifyClientService: Connect: Exception: " + e);
            }
        }

        //public async Task<Result<ClientState>> BootAndConnect(Uri url, String BootPort, string passcode)
        //{
        //    Debug.Assert(url is not null);
        //    try
        //    {
        //        string agentUrl = url.ToString()!;
        //        var ClientRes = await Signify_ts_shim.BootAndConnect(agentUrl, $"{url}:{BootPort}/boot", "passcode");
        //        if (ClientRes is not null)
        //        {
        //            var details = new
        //            {
        //                BootAndConnectResults = ClientRes,
        //                Tmp = "tmp"
        //            };
        //            Console.WriteLine("SignifyClientService BootAndConnect ClientRes 11: {@details} ", details);
        //            logger.LogInformation("SignifyClientService BootAndConnect ClientRes: {@Details}", details);
        //            // TODO EE!: parse what we need from ClientRes json
        //            return Result.Ok<ClientState>(new ClientState());
        //        }
        //        else
        //        {
        //            return Result.Fail<ClientState>("SignifyClientService: BootAndConnect: ClientRes is null");
        //        }
        //    }
        //    catch (JSException e)
        //    {
        //        logger.LogInformation("SignifyClientService: BootAndConnect: JSException: {e}", e);
        //        return Result.Fail<ClientState>("SignifyClientService: BootAndConnect: Exception: " + e);
        //    }
        //    catch (Exception e)
        //    {
        //        logger.LogInformation("SignifyClientService: BootAndConnect: Exception: {e}", e);
        //        return Result.Fail<ClientState>("SignifyClientService: BootAndConnect: Exception: " + e);
        //    }
        //}

        public Task<Result<bool>> Connect()
        {
            return Task.FromResult(Result.Fail<bool>("Not implemented"));
        }

        public async Task<Result<string>> CreatePersonAid(string aidName)
        {
            try
            {
                var res = await CreateAID(aidName);
                Debug.Assert(res is not null);
                // TODO verify res and parse what we need
                logger.LogInformation("CreatePersonAid: res: {res}", res);
                // return Result.Ok<Models.Identifier>(new Models.Identifier());
                return Result.Ok(res);
            }
            catch (JSException e)
            {
                logger.LogWarning("CreatePersonAid: JSException: {e}", e);
                return Result.Fail<string>("SignifyClientService: CreatePersonAid: Exception: " + e);
            }
            catch (Exception e)
            {
                logger.LogWarning("CreatePersonAid: Exception: {e}", e);
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

        public static Task<Result<IList<Credential>>> GetCredentials()
        {
            return Task.FromResult(Result.Fail<IList<Credential>>("Not implemented"));
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

        public async Task<Result<string>> GetIdentifiers()
        {
            try
            {
                var res = await GetAIDs();
                Debug.Assert(res is not null);
                // TODO EE! verify res and parse what we need and store them
                // logger.LogInformation("GetIdentifiers: {ids}", res);
                return Result.Ok(res);
            }
            catch (JSException e)
            {
                logger.LogWarning("GetIdentifiers: JSException: {e}", e);
                return Result.Fail<string>("SignifyClientService: CreatePersonAid: Exception: " + e);
            }
            catch (Exception e)
            {
                logger.LogWarning("GetIdentifiers: Exception: {e}", e);
                return Result.Fail<string>("SignifyClientService: GetIdentifiers: Exception: " + e);
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

        Task<Result<IList<Credential>>> ISignifyClientService.GetCredentials()
        {
            throw new NotImplementedException();
        }

        Task<Result<IList<Models.Registry>>> ISignifyClientService.GetRegistries()
        {
            throw new NotImplementedException();
        }

        Task<Result<IList<Models.Notification>>> ISignifyClientService.GetNotifications()
        {
            throw new NotImplementedException();
        }
    }
}
