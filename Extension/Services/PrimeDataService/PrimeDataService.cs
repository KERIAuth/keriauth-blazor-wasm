using System.Text.Json;
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

            // Step 6: GEDA challenges QVI
            _logger.LogInformation("Step 6: GEDA generating challenge for QVI...");
            var gedaChallengeResult = await _signifyClient.GenerateChallenge(128);
            if (gedaChallengeResult.IsFailed) {
                var err = $"Failed to generate GEDA challenge: {gedaChallengeResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("GEDA challenge generated: {WordCount} words", gedaChallengeResult.Value.Words.Count);

            // Step 7: QVI responds to GEDA's challenge
            _logger.LogInformation("Step 7: QVI responding to GEDA's challenge...");
            var qviRespondResult = await _signifyClient.RespondToChallenge(qviName, gedaResult.Value.Prefix, gedaChallengeResult.Value.Words);
            if (qviRespondResult.IsFailed) {
                var err = $"Failed QVI respond to GEDA challenge: {qviRespondResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("QVI responded to GEDA's challenge");

            // Step 8: GEDA verifies QVI's response
            _logger.LogInformation("Step 8: GEDA verifying QVI's challenge response...");
            var verifyOp = await _signifyClient.VerifyChallenge(qviResult.Value.Prefix, gedaChallengeResult.Value.Words);
            if (verifyOp.IsFailed) {
                var err = $"Failed GEDA verify QVI response: {verifyOp.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            var completedOp = await _signifyClient.WaitForOperation(verifyOp.Value);
            if (completedOp.IsFailed) {
                var err = $"Failed waiting for GEDA verify operation: {completedOp.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            var responseEl = (JsonElement)completedOp.Value.Response!;
            var said = responseEl.GetProperty("exn").GetProperty("d").GetString()!;
            var respondedResult = await _signifyClient.ChallengeResponded(qviResult.Value.Prefix, said);
            if (respondedResult.IsFailed) {
                var err = $"Failed GEDA mark QVI challenge responded: {respondedResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("GEDA verified QVI's challenge response (SAID={Said})", said);

            // Step 9: QVI challenges GEDA
            _logger.LogInformation("Step 9: QVI generating challenge for GEDA...");
            var qviChallengeResult = await _signifyClient.GenerateChallenge(128);
            if (qviChallengeResult.IsFailed) {
                var err = $"Failed to generate QVI challenge: {qviChallengeResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("QVI challenge generated: {WordCount} words", qviChallengeResult.Value.Words.Count);

            // Step 10: GEDA responds to QVI's challenge
            _logger.LogInformation("Step 10: GEDA responding to QVI's challenge...");
            var gedaRespondResult = await _signifyClient.RespondToChallenge(gedaName, qviResult.Value.Prefix, qviChallengeResult.Value.Words);
            if (gedaRespondResult.IsFailed) {
                var err = $"Failed GEDA respond to QVI challenge: {gedaRespondResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("GEDA responded to QVI's challenge");

            // Step 11: QVI verifies GEDA's response
            _logger.LogInformation("Step 11: QVI verifying GEDA's challenge response...");
            var verifyOp2 = await _signifyClient.VerifyChallenge(gedaResult.Value.Prefix, qviChallengeResult.Value.Words);
            if (verifyOp2.IsFailed) {
                var err = $"Failed QVI verify GEDA response: {verifyOp2.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            var completedOp2 = await _signifyClient.WaitForOperation(verifyOp2.Value);
            if (completedOp2.IsFailed) {
                var err = $"Failed waiting for QVI verify operation: {completedOp2.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            var responseEl2 = (JsonElement)completedOp2.Value.Response!;
            var said2 = responseEl2.GetProperty("exn").GetProperty("d").GetString()!;
            var respondedResult2 = await _signifyClient.ChallengeResponded(gedaResult.Value.Prefix, said2);
            if (respondedResult2.IsFailed) {
                var err = $"Failed QVI mark GEDA challenge responded: {respondedResult2.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("QVI verified GEDA's challenge response (SAID={Said})", said2);

            _logger.LogInformation("PrimeData Go completed successfully");
            return Result.Ok(new PrimeDataGoResponse(true));
        }
    }
}
