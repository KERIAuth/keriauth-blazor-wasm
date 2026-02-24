using System.Text.Json;
using Extension.Helper;
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

            // Step 12: GEDA creates credential registry
            var gedaRegistryName = $"{prepend}_geda_registry";
            _logger.LogInformation("Step 12: Creating credential registry '{Name}' for GEDA...", gedaRegistryName);
            var registryResult = await _signifyClient.CreateRegistryIfNotExists(gedaName, gedaRegistryName);
            if (registryResult.IsFailed) {
                var err = $"Failed to create GEDA registry: {registryResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("GEDA registry: regk={Regk}, created={Created}", registryResult.Value.Regk, registryResult.Value.Created);

            // Step 13a: GEDA issues QVI credential
            _logger.LogInformation("Step 13a: GEDA issuing QVI credential to QVI...");
            var credData = new RecursiveDictionary();
            credData["LEI"] = new RecursiveValue { StringValue = "5493001KJTIIGC8Y1R17" };
            var issueArgs = new IssueAndGetCredentialArgs(
                IssuerAidName: gedaName,
                RegistryName: gedaRegistryName,
                Schema: "EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao",
                HolderPrefix: qviResult.Value.Prefix,
                CredData: credData
            );
            var credResult = await _signifyClient.IssueAndGetCredential(issueArgs);
            if (credResult.IsFailed) {
                var err = $"Failed to issue QVI credential: {credResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            var acdc = credResult.Value["acdc"].Dictionary!;
            var anc = credResult.Value["anc"].Dictionary!;
            var iss = credResult.Value["iss"].Dictionary!;
            var credSaid = credResult.Value["said"].StringValue!;
            _logger.LogInformation("QVI credential issued: said={Said}", credSaid);

            // Step 13b: GEDA grants QVI credential to QVI via IPEX
            _logger.LogInformation("Step 13b: GEDA granting QVI credential to QVI via IPEX...");
            var grantArgs = new IpexGrantSubmitArgs(
                SenderName: gedaName,
                Recipient: qviResult.Value.Prefix,
                Acdc: acdc,
                Anc: anc,
                Iss: iss
            );
            var grantResult = await _signifyClient.IpexGrantAndSubmit(grantArgs);
            if (grantResult.IsFailed) {
                var err = $"Failed to grant QVI credential: {grantResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            var grantSaid = grantResult.Value["grantSaid"].StringValue!;
            _logger.LogInformation("QVI credential granted: grantSaid={GrantSaid}", grantSaid);

            // Step 14: QVI admits QVI credential
            _logger.LogInformation("Step 14: QVI admitting QVI credential...");
            var admitArgs = new IpexAdmitSubmitArgs(
                SenderName: qviName,
                Recipient: gedaResult.Value.Prefix,
                GrantSaid: grantSaid
            );
            var admitResult = await _signifyClient.IpexAdmitAndSubmit(admitArgs);
            if (admitResult.IsFailed) {
                var err = $"Failed to admit QVI credential: {admitResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("QVI credential admitted successfully");

            // Step 15a: Create Verifier AID
            var verifierName = $"{prepend}_verifier";
            _logger.LogInformation("Step 15a: Creating Verifier AID '{Name}'...", verifierName);
            var verifierResult = await _signifyClient.CreateAidWithEndRole(verifierName);
            if (verifierResult.IsFailed) {
                var err = $"Failed to create Verifier AID: {verifierResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("Verifier AID created: prefix={Prefix}, oobi={Oobi}", verifierResult.Value.Prefix, verifierResult.Value.Oobi);

            // Step 15b: Resolve OOBIs between QVI and Verifier
            _logger.LogInformation("Step 15b: Resolving OOBIs between QVI and Verifier...");
            var verifierOobiPairs = new (string resolver, string oobi, string alias)[] {
                (qviName, verifierResult.Value.Oobi, verifierName),
                (verifierName, qviResult.Value.Oobi, qviName),
            };

            foreach (var (resolver, oobi, alias) in verifierOobiPairs) {
                _logger.LogInformation("  {Resolver} resolving OOBI for {Alias}...", resolver, alias);
                var resolveResult = await _signifyClient.ResolveOobi(oobi, alias);
                if (resolveResult.IsFailed) {
                    var err = $"Failed OOBI resolve ({resolver} -> {alias}): {resolveResult.Errors[0].Message}";
                    _logger.LogError("{Error}", err);
                    return Result.Ok(new PrimeDataGoResponse(false, Error: err));
                }
                _logger.LogInformation("  OOBI resolved: {Resolver} -> {Alias}", resolver, alias);
            }

            // Step 16a: Verifier applies (requests QVI credential)
            _logger.LogInformation("Step 16a: Verifier requesting QVI credential via IPEX apply...");
            var applyArgs = new IpexApplySubmitArgs(
                SenderName: verifierName,
                Recipient: qviResult.Value.Prefix,
                SchemaSaid: "EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao"
            );
            var applyResult = await _signifyClient.IpexApplyAndSubmit(applyArgs);
            if (applyResult.IsFailed) {
                var err = $"Failed verifier IPEX apply: {applyResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            var applySaid = applyResult.Value["applySaid"].StringValue!;
            _logger.LogInformation("Verifier IPEX apply submitted: applySaid={ApplySaid}", applySaid);

            // Step 16b: QVI offers credential to verifier
            _logger.LogInformation("Step 16b: QVI offering credential to Verifier via IPEX offer...");
            var offerArgs = new IpexOfferSubmitArgs(
                SenderName: qviName,
                Recipient: verifierResult.Value.Prefix,
                CredentialSaid: credSaid,
                ApplySaid: applySaid
            );
            var offerResult = await _signifyClient.IpexOfferAndSubmit(offerArgs);
            if (offerResult.IsFailed) {
                var err = $"Failed QVI IPEX offer: {offerResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            var offerSaid = offerResult.Value["offerSaid"].StringValue!;
            _logger.LogInformation("QVI IPEX offer submitted: offerSaid={OfferSaid}", offerSaid);

            // Step 16c: Verifier agrees to credential offer
            _logger.LogInformation("Step 16c: Verifier agreeing to credential offer via IPEX agree...");
            var agreeArgs = new IpexAgreeSubmitArgs(
                SenderName: verifierName,
                Recipient: qviResult.Value.Prefix,
                OfferSaid: offerSaid
            );
            var agreeResult = await _signifyClient.IpexAgreeAndSubmit(agreeArgs);
            if (agreeResult.IsFailed) {
                var err = $"Failed verifier IPEX agree: {agreeResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("Verifier IPEX agree submitted successfully");

            // Step 17: QVI creates credential registry for LE credentials
            var qviRegistryName = $"{prepend}_qvi_le_registry";
            _logger.LogInformation("Step 17: Creating credential registry '{Name}' for QVI...", qviRegistryName);
            var qviRegistryResult = await _signifyClient.CreateRegistryIfNotExists(qviName, qviRegistryName);
            if (qviRegistryResult.IsFailed) {
                var err = $"Failed to create QVI registry: {qviRegistryResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("QVI registry: regk={Regk}, created={Created}", qviRegistryResult.Value.Regk, qviRegistryResult.Value.Created);

            // Step 18a: QVI issues LE credential (with edges chaining to QVI credential + rules)
            _logger.LogInformation("Step 18a: QVI issuing LE credential to LE...");
            var leCredData = new RecursiveDictionary();
            leCredData["LEI"] = new RecursiveValue { StringValue = "254900OPPU84GM83MG36" };

            // Edge: chain LE credential to QVI credential
            var credEdge = new RecursiveDictionary();
            credEdge["d"] = new RecursiveValue { StringValue = "" };
            var qviEdge = new RecursiveDictionary();
            qviEdge["n"] = new RecursiveValue { StringValue = credSaid };
            qviEdge["s"] = new RecursiveValue { StringValue = "EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao" };
            credEdge["qvi"] = new RecursiveValue { Dictionary = qviEdge };

            // Rules: standard vLEI disclaimers
            var credRules = new RecursiveDictionary();
            credRules["d"] = new RecursiveValue { StringValue = "" };
            var usageDisclaimer = new RecursiveDictionary();
            usageDisclaimer["l"] = new RecursiveValue { StringValue = "Usage of a valid, unexpired, and non-revoked vLEI Credential, as defined in the associated Ecosystem Governance Framework, does not assert that the Legal Entity is trustworthy, honest, reputable in its business dealings, safe to do business with, or compliant with any laws or that an implied or expressly intended purpose will be fulfilled." };
            credRules["usageDisclaimer"] = new RecursiveValue { Dictionary = usageDisclaimer };
            var issuanceDisclaimer = new RecursiveDictionary();
            issuanceDisclaimer["l"] = new RecursiveValue { StringValue = "All information in a valid, unexpired, and non-revoked vLEI Credential, as defined in the associated Ecosystem Governance Framework, is accurate as of the date the validation process was complete. The vLEI Credential has been issued to the legal entity or person named in the vLEI Credential as the subject; and the qualified vLEI Issuer exercised reasonable care to perform the validation process set forth in the vLEI Ecosystem Governance Framework." };
            credRules["issuanceDisclaimer"] = new RecursiveValue { Dictionary = issuanceDisclaimer };

            var leIssueArgs = new IssueAndGetCredentialArgs(
                IssuerAidName: qviName,
                RegistryName: qviRegistryName,
                Schema: "ENPXp1vQzRF6JwIuS-mp2U8Uf1MoADoP_GqQ62VsDZWY",
                HolderPrefix: leResult.Value.Prefix,
                CredData: leCredData,
                CredEdge: credEdge,
                CredRules: credRules
            );
            var leCredResult = await _signifyClient.IssueAndGetCredential(leIssueArgs);
            if (leCredResult.IsFailed) {
                var err = $"Failed to issue LE credential: {leCredResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            var leCredAcdc = leCredResult.Value["acdc"].Dictionary!;
            var leCredAnc = leCredResult.Value["anc"].Dictionary!;
            var leCredIss = leCredResult.Value["iss"].Dictionary!;
            var leCredSaid = leCredResult.Value["said"].StringValue!;
            _logger.LogInformation("LE credential issued: said={Said}", leCredSaid);

            // Step 18b: QVI grants LE credential to LE via IPEX
            _logger.LogInformation("Step 18b: QVI granting LE credential to LE via IPEX...");
            var leGrantArgs = new IpexGrantSubmitArgs(
                SenderName: qviName,
                Recipient: leResult.Value.Prefix,
                Acdc: leCredAcdc,
                Anc: leCredAnc,
                Iss: leCredIss
            );
            var leGrantResult = await _signifyClient.IpexGrantAndSubmit(leGrantArgs);
            if (leGrantResult.IsFailed) {
                var err = $"Failed to grant LE credential: {leGrantResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            var leGrantSaid = leGrantResult.Value["grantSaid"].StringValue!;
            _logger.LogInformation("LE credential granted: grantSaid={GrantSaid}", leGrantSaid);

            // Step 19: LE admits LE credential
            _logger.LogInformation("Step 19: LE admitting LE credential...");
            var leAdmitArgs = new IpexAdmitSubmitArgs(
                SenderName: leName,
                Recipient: qviResult.Value.Prefix,
                GrantSaid: leGrantSaid
            );
            var leAdmitResult = await _signifyClient.IpexAdmitAndSubmit(leAdmitArgs);
            if (leAdmitResult.IsFailed) {
                var err = $"Failed to admit LE credential: {leAdmitResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("LE credential admitted successfully");

            // Step 20: Resolve OOBIs between LE and Verifier
            _logger.LogInformation("Step 20: Resolving OOBIs between LE and Verifier...");
            var leVerifierOobiPairs = new (string resolver, string oobi, string alias)[] {
                (leName, verifierResult.Value.Oobi, verifierName),
                (verifierName, leResult.Value.Oobi, leName),
            };

            foreach (var (resolver, oobi, alias) in leVerifierOobiPairs) {
                _logger.LogInformation("  {Resolver} resolving OOBI for {Alias}...", resolver, alias);
                var resolveResult = await _signifyClient.ResolveOobi(oobi, alias);
                if (resolveResult.IsFailed) {
                    var err = $"Failed OOBI resolve ({resolver} -> {alias}): {resolveResult.Errors[0].Message}";
                    _logger.LogError("{Error}", err);
                    return Result.Ok(new PrimeDataGoResponse(false, Error: err));
                }
                _logger.LogInformation("  OOBI resolved: {Resolver} -> {Alias}", resolver, alias);
            }

            // Step 21: LE presents LE credential to Verifier (direct grant)
            _logger.LogInformation("Step 21: LE presenting LE credential to Verifier...");
            var lePresentResult = await _signifyClient.GrantReceivedCredential(leName, leCredSaid, verifierResult.Value.Prefix);
            if (lePresentResult.IsFailed) {
                var err = $"Failed to present LE credential: {lePresentResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("LE credential presented to Verifier successfully");

            _logger.LogInformation("PrimeData Go completed successfully");
            return Result.Ok(new PrimeDataGoResponse(true));
        }
    }
}
