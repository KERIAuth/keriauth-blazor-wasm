using Extension.Models.Messages.AppBw;
using Extension.Services.SignifyService;
using Extension.Services.SignifyService.Models;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace Extension.Services.PrimeDataService {
    public class PrimeDataService : IPrimeDataService {
        private readonly ISignifyClientService _signifyClient;
        private readonly ILogger<PrimeDataService> _logger;

        public PrimeDataService(ISignifyClientService signifyClient, ILogger<PrimeDataService> logger) {
            _signifyClient = signifyClient;
            _logger = logger;
        }

        public async Task<Result<PrimeDataGoResponse>> GoAsync(PrimeDataGoPayload payload) {
            var prepend = payload.Prepend;
            _logger.LogInformation("PrimeData Go starting with prepend '{Prepend}'", prepend);

            // Step 1: Create GEDA AID
            var gedaName = $"{prepend}_geda";
            _logger.LogInformation("Step 1: Creating GEDA AID '{Name}'...", gedaName);
            var gedaResult = await _signifyClient.CreateAidWithEndRole(gedaName);
            if (gedaResult.IsFailed) {
                var err = $"Failed to create GEDA AID: {gedaResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("GEDA AID created: prefix={Prefix}, oobi={Oobi}", gedaResult.Value.Prefix, gedaResult.Value.Oobi);

            // Step 2: Create QVI AID
            var qviName = $"{prepend}_qvi";
            _logger.LogInformation("Step 2: Creating QVI AID '{Name}'...", qviName);
            var qviResult = await _signifyClient.CreateAidWithEndRole(qviName);
            if (qviResult.IsFailed) {
                var err = $"Failed to create QVI AID: {qviResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("QVI AID created: prefix={Prefix}, oobi={Oobi}", qviResult.Value.Prefix, qviResult.Value.Oobi);

            // Step 3: Create LE AID
            var leName = $"{prepend}_le";
            _logger.LogInformation("Step 3: Creating LE AID '{Name}'...", leName);
            var leResult = await _signifyClient.CreateAidWithEndRole(leName);
            if (leResult.IsFailed) {
                var err = $"Failed to create LE AID: {leResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("LE AID created: prefix={Prefix}, oobi={Oobi}", leResult.Value.Prefix, leResult.Value.Oobi);

            // Step 4: Create PERSON AID
            var personName = $"{prepend}_person";
            _logger.LogInformation("Step 4: Creating PERSON AID '{Name}'...", personName);
            var personResult = await _signifyClient.CreateAidWithEndRole(personName);
            if (personResult.IsFailed) {
                var err = $"Failed to create PERSON AID: {personResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("PERSON AID created: prefix={Prefix}, oobi={Oobi}", personResult.Value.Prefix, personResult.Value.Oobi);

            // Step 5: Resolve OOBIs between roles
            _logger.LogInformation("Step 5: Resolving OOBIs between roles...");
            var oobiPairs = new (string resolver, string oobi, string alias)[] {
                (gedaName, qviResult.Value.Oobi, qviName),
                (qviName, gedaResult.Value.Oobi, gedaName),
                (leName, qviResult.Value.Oobi, qviName),
                (qviName, leResult.Value.Oobi, leName),
            };

            foreach (var (resolver, oobi, alias) in oobiPairs) {
                _logger.LogInformation("  {Resolver} resolving OOBI for {Alias}...", resolver, alias);
                var resolveResult = await _signifyClient.ResolveOobi(oobi, alias);
                if (resolveResult.IsFailed) {
                    var err = $"Failed OOBI resolve ({resolver} -> {alias}): {resolveResult.Errors[0].Message}";
                    _logger.LogError("{Error}", err);
                    return Result.Ok(new PrimeDataGoResponse(false, Error: err));
                }
                _logger.LogInformation("  OOBI resolved: {Resolver} -> {Alias}", resolver, alias);
            }

            _logger.LogInformation("PrimeData Go completed successfully");
            return Result.Ok(new PrimeDataGoResponse(true));
        }
    }
}
