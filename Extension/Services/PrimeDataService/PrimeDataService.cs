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

            // Step 12b: Resolve credential schema OOBIs so KERIA can validate credentials
            _logger.LogInformation("Step 12b: Resolving credential schema OOBIs...");
            var schemaOobis = new (string schemaSaid, string url)[] {
                ("EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao", "https://schema.testnet.gleif.org:7723/oobi/EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao"),
                ("ENPXp1vQzRF6JwIuS-mp2U8Uf1MoADoP_GqQ62VsDZWY", "https://schema.testnet.gleif.org:7723/oobi/ENPXp1vQzRF6JwIuS-mp2U8Uf1MoADoP_GqQ62VsDZWY"),
                ("EKA57bKBKxr_kN7iN5i7lMUxpMG-s19dRcmov1iDxz-E", "https://schema.testnet.gleif.org:7723/oobi/EKA57bKBKxr_kN7iN5i7lMUxpMG-s19dRcmov1iDxz-E"),
                ("EBNaNu-M9P5cgrnfl2Fvymy4E_jvxxyjb70PRtiANlJy", "https://schema.testnet.gleif.org:7723/oobi/EBNaNu-M9P5cgrnfl2Fvymy4E_jvxxyjb70PRtiANlJy"),
                ("EH6ekLjSr8V32WyFbGe1zXjTzFs9PkTYmupJ9H65O14g", "https://schema.testnet.gleif.org:7723/oobi/EH6ekLjSr8V32WyFbGe1zXjTzFs9PkTYmupJ9H65O14g"),
                ("EEy9PkikFcANV1l7EHukCeXqrzT1hNZjGlUk7wuMO5jw", "https://schema.testnet.gleif.org:7723/oobi/EEy9PkikFcANV1l7EHukCeXqrzT1hNZjGlUk7wuMO5jw"),
            };
            foreach (var (schemaSaid, url) in schemaOobis) {
                _logger.LogInformation("  Resolving schema OOBI {SchemaSaid}...", schemaSaid);
                var schemaResolveResult = await _signifyClient.ResolveOobi(url);
                if (schemaResolveResult.IsFailed) {
                    var err = $"Failed to resolve schema OOBI {schemaSaid}: {schemaResolveResult.Errors[0].Message}";
                    _logger.LogError("{Error}", err);
                    return Result.Ok(new PrimeDataGoResponse(false, Error: err));
                }
                _logger.LogInformation("  Schema OOBI resolved: {SchemaSaid}", schemaSaid);
            }

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

            // Step 22: Resolve OOBIs for Person (Person↔LE, Person↔QVI, Person↔Verifier)
            _logger.LogInformation("Step 22: Resolving OOBIs for Person...");
            var personOobiPairs = new (string resolver, string oobi, string alias)[] {
                (personName, leResult.Value.Oobi, leName),
                (leName, personResult.Value.Oobi, personName),
                (qviName, personResult.Value.Oobi, personName),
                (personName, qviResult.Value.Oobi, qviName),
                (personName, verifierResult.Value.Oobi, verifierName),
                (verifierName, personResult.Value.Oobi, personName),
            };

            foreach (var (resolver, oobi, alias) in personOobiPairs) {
                _logger.LogInformation("  {Resolver} resolving OOBI for {Alias}...", resolver, alias);
                var resolveResult = await _signifyClient.ResolveOobi(oobi, alias);
                if (resolveResult.IsFailed) {
                    var err = $"Failed OOBI resolve ({resolver} -> {alias}): {resolveResult.Errors[0].Message}";
                    _logger.LogError("{Error}", err);
                    return Result.Ok(new PrimeDataGoResponse(false, Error: err));
                }
                _logger.LogInformation("  OOBI resolved: {Resolver} -> {Alias}", resolver, alias);
            }

            // Step 23: LE creates credential registry
            var leRegistryName = $"{prepend}_le_oor_registry";
            _logger.LogInformation("Step 23: Creating credential registry '{Name}' for LE...", leRegistryName);
            var leRegistryResult = await _signifyClient.CreateRegistryIfNotExists(leName, leRegistryName);
            if (leRegistryResult.IsFailed) {
                var err = $"Failed to create LE registry: {leRegistryResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("LE registry: regk={Regk}, created={Created}", leRegistryResult.Value.Regk, leRegistryResult.Value.Created);

            // Step 24a: LE issues OOR Auth credential to QVI
            _logger.LogInformation("Step 24a: LE issuing OOR Auth credential to QVI...");
            var oorAuthCredData = new RecursiveDictionary();
            oorAuthCredData["AID"] = new RecursiveValue { StringValue = personResult.Value.Prefix };
            oorAuthCredData["LEI"] = new RecursiveValue { StringValue = "254900OPPU84GM83MG36" };
            oorAuthCredData["personLegalName"] = new RecursiveValue { StringValue = "John Smith" };
            oorAuthCredData["officialRole"] = new RecursiveValue { StringValue = "Head of Standards" };

            var oorAuthEdge = new RecursiveDictionary();
            oorAuthEdge["d"] = new RecursiveValue { StringValue = "" };
            var oorAuthLeEdge = new RecursiveDictionary();
            oorAuthLeEdge["n"] = new RecursiveValue { StringValue = leCredSaid };
            oorAuthLeEdge["s"] = new RecursiveValue { StringValue = "ENPXp1vQzRF6JwIuS-mp2U8Uf1MoADoP_GqQ62VsDZWY" };
            oorAuthEdge["le"] = new RecursiveValue { Dictionary = oorAuthLeEdge };

            var oorAuthRules = new RecursiveDictionary();
            oorAuthRules["d"] = new RecursiveValue { StringValue = "" };
            var oorAuthUsage = new RecursiveDictionary();
            oorAuthUsage["l"] = new RecursiveValue { StringValue = "Usage of a valid, unexpired, and non-revoked vLEI Credential, as defined in the associated Ecosystem Governance Framework, does not assert that the Legal Entity is trustworthy, honest, reputable in its business dealings, safe to do business with, or compliant with any laws or that an implied or expressly intended purpose will be fulfilled." };
            oorAuthRules["usageDisclaimer"] = new RecursiveValue { Dictionary = oorAuthUsage };
            var oorAuthIssuance = new RecursiveDictionary();
            oorAuthIssuance["l"] = new RecursiveValue { StringValue = "All information in a valid, unexpired, and non-revoked vLEI Credential, as defined in the associated Ecosystem Governance Framework, is accurate as of the date the validation process was complete. The vLEI Credential has been issued to the legal entity or person named in the vLEI Credential as the subject; and the qualified vLEI Issuer exercised reasonable care to perform the validation process set forth in the vLEI Ecosystem Governance Framework." };
            oorAuthRules["issuanceDisclaimer"] = new RecursiveValue { Dictionary = oorAuthIssuance };

            var oorAuthIssueArgs = new IssueAndGetCredentialArgs(
                IssuerAidName: leName,
                RegistryName: leRegistryName,
                Schema: "EKA57bKBKxr_kN7iN5i7lMUxpMG-s19dRcmov1iDxz-E",
                HolderPrefix: qviResult.Value.Prefix,
                CredData: oorAuthCredData,
                CredEdge: oorAuthEdge,
                CredRules: oorAuthRules
            );
            var oorAuthCredResult = await _signifyClient.IssueAndGetCredential(oorAuthIssueArgs);
            if (oorAuthCredResult.IsFailed) {
                var err = $"Failed to issue OOR Auth credential: {oorAuthCredResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            var oorAuthAcdc = oorAuthCredResult.Value["acdc"].Dictionary!;
            var oorAuthAnc = oorAuthCredResult.Value["anc"].Dictionary!;
            var oorAuthIss = oorAuthCredResult.Value["iss"].Dictionary!;
            var oorAuthCredSaid = oorAuthCredResult.Value["said"].StringValue!;
            _logger.LogInformation("OOR Auth credential issued: said={Said}", oorAuthCredSaid);

            // Step 24b: LE grants OOR Auth to QVI via IPEX
            _logger.LogInformation("Step 24b: LE granting OOR Auth credential to QVI via IPEX...");
            var oorAuthGrantArgs = new IpexGrantSubmitArgs(
                SenderName: leName,
                Recipient: qviResult.Value.Prefix,
                Acdc: oorAuthAcdc,
                Anc: oorAuthAnc,
                Iss: oorAuthIss
            );
            var oorAuthGrantResult = await _signifyClient.IpexGrantAndSubmit(oorAuthGrantArgs);
            if (oorAuthGrantResult.IsFailed) {
                var err = $"Failed to grant OOR Auth credential: {oorAuthGrantResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            var oorAuthGrantSaid = oorAuthGrantResult.Value["grantSaid"].StringValue!;
            _logger.LogInformation("OOR Auth credential granted: grantSaid={GrantSaid}", oorAuthGrantSaid);

            // Step 25: QVI admits OOR Auth credential
            _logger.LogInformation("Step 25: QVI admitting OOR Auth credential...");
            var oorAuthAdmitArgs = new IpexAdmitSubmitArgs(
                SenderName: qviName,
                Recipient: leResult.Value.Prefix,
                GrantSaid: oorAuthGrantSaid
            );
            var oorAuthAdmitResult = await _signifyClient.IpexAdmitAndSubmit(oorAuthAdmitArgs);
            if (oorAuthAdmitResult.IsFailed) {
                var err = $"Failed to admit OOR Auth credential: {oorAuthAdmitResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("OOR Auth credential admitted successfully");

            // Step 26a: QVI issues OOR credential to Person
            _logger.LogInformation("Step 26a: QVI issuing OOR credential to Person...");
            var oorCredData = new RecursiveDictionary();
            oorCredData["LEI"] = new RecursiveValue { StringValue = "254900OPPU84GM83MG36" };
            oorCredData["personLegalName"] = new RecursiveValue { StringValue = "John Smith" };
            oorCredData["officialRole"] = new RecursiveValue { StringValue = "Head of Standards" };

            var oorEdge = new RecursiveDictionary();
            oorEdge["d"] = new RecursiveValue { StringValue = "" };
            var oorAuthRef = new RecursiveDictionary();
            oorAuthRef["n"] = new RecursiveValue { StringValue = oorAuthCredSaid };
            oorAuthRef["s"] = new RecursiveValue { StringValue = "EKA57bKBKxr_kN7iN5i7lMUxpMG-s19dRcmov1iDxz-E" };
            oorAuthRef["o"] = new RecursiveValue { StringValue = "I2I" };
            oorEdge["auth"] = new RecursiveValue { Dictionary = oorAuthRef };

            var oorRules = new RecursiveDictionary();
            oorRules["d"] = new RecursiveValue { StringValue = "" };
            var oorUsage = new RecursiveDictionary();
            oorUsage["l"] = new RecursiveValue { StringValue = "Usage of a valid, unexpired, and non-revoked vLEI Credential, as defined in the associated Ecosystem Governance Framework, does not assert that the Legal Entity is trustworthy, honest, reputable in its business dealings, safe to do business with, or compliant with any laws or that an implied or expressly intended purpose will be fulfilled." };
            oorRules["usageDisclaimer"] = new RecursiveValue { Dictionary = oorUsage };
            var oorIssuance = new RecursiveDictionary();
            oorIssuance["l"] = new RecursiveValue { StringValue = "All information in a valid, unexpired, and non-revoked vLEI Credential, as defined in the associated Ecosystem Governance Framework, is accurate as of the date the validation process was complete. The vLEI Credential has been issued to the legal entity or person named in the vLEI Credential as the subject; and the qualified vLEI Issuer exercised reasonable care to perform the validation process set forth in the vLEI Ecosystem Governance Framework." };
            oorRules["issuanceDisclaimer"] = new RecursiveValue { Dictionary = oorIssuance };

            var oorIssueArgs = new IssueAndGetCredentialArgs(
                IssuerAidName: qviName,
                RegistryName: qviRegistryName,
                Schema: "EBNaNu-M9P5cgrnfl2Fvymy4E_jvxxyjb70PRtiANlJy",
                HolderPrefix: personResult.Value.Prefix,
                CredData: oorCredData,
                CredEdge: oorEdge,
                CredRules: oorRules
            );
            var oorCredResult = await _signifyClient.IssueAndGetCredential(oorIssueArgs);
            if (oorCredResult.IsFailed) {
                var err = $"Failed to issue OOR credential: {oorCredResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            var oorAcdc = oorCredResult.Value["acdc"].Dictionary!;
            var oorAnc = oorCredResult.Value["anc"].Dictionary!;
            var oorIssVal = oorCredResult.Value["iss"].Dictionary!;
            var oorCredSaid = oorCredResult.Value["said"].StringValue!;
            _logger.LogInformation("OOR credential issued: said={Said}", oorCredSaid);

            // Step 26b: QVI grants OOR credential to Person via IPEX
            _logger.LogInformation("Step 26b: QVI granting OOR credential to Person via IPEX...");
            var oorGrantArgs = new IpexGrantSubmitArgs(
                SenderName: qviName,
                Recipient: personResult.Value.Prefix,
                Acdc: oorAcdc,
                Anc: oorAnc,
                Iss: oorIssVal
            );
            var oorGrantResult = await _signifyClient.IpexGrantAndSubmit(oorGrantArgs);
            if (oorGrantResult.IsFailed) {
                var err = $"Failed to grant OOR credential: {oorGrantResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            var oorGrantSaid = oorGrantResult.Value["grantSaid"].StringValue!;
            _logger.LogInformation("OOR credential granted: grantSaid={GrantSaid}", oorGrantSaid);

            // Step 27: Person admits OOR credential
            _logger.LogInformation("Step 27: Person admitting OOR credential...");
            var oorAdmitArgs = new IpexAdmitSubmitArgs(
                SenderName: personName,
                Recipient: qviResult.Value.Prefix,
                GrantSaid: oorGrantSaid
            );
            var oorAdmitResult = await _signifyClient.IpexAdmitAndSubmit(oorAdmitArgs);
            if (oorAdmitResult.IsFailed) {
                var err = $"Failed to admit OOR credential: {oorAdmitResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("OOR credential admitted successfully");

            // Step 28: Person presents OOR credential to Verifier (direct grant)
            _logger.LogInformation("Step 28: Person presenting OOR credential to Verifier...");
            var oorPresentResult = await _signifyClient.GrantReceivedCredential(personName, oorCredSaid, verifierResult.Value.Prefix);
            if (oorPresentResult.IsFailed) {
                var err = $"Failed to present OOR credential: {oorPresentResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("OOR credential presented to Verifier successfully");

            // Step 29a: LE issues ECR Auth credential to QVI
            _logger.LogInformation("Step 29a: LE issuing ECR Auth credential to QVI...");
            var ecrAuthCredData = new RecursiveDictionary();
            ecrAuthCredData["AID"] = new RecursiveValue { StringValue = personResult.Value.Prefix };
            ecrAuthCredData["LEI"] = new RecursiveValue { StringValue = "254900OPPU84GM83MG36" };
            ecrAuthCredData["personLegalName"] = new RecursiveValue { StringValue = "John Smith" };
            ecrAuthCredData["engagementContextRole"] = new RecursiveValue { StringValue = "Project Manager" };

            var ecrAuthEdge = new RecursiveDictionary();
            ecrAuthEdge["d"] = new RecursiveValue { StringValue = "" };
            var ecrAuthLeEdge = new RecursiveDictionary();
            ecrAuthLeEdge["n"] = new RecursiveValue { StringValue = leCredSaid };
            ecrAuthLeEdge["s"] = new RecursiveValue { StringValue = "ENPXp1vQzRF6JwIuS-mp2U8Uf1MoADoP_GqQ62VsDZWY" };
            ecrAuthEdge["le"] = new RecursiveValue { Dictionary = ecrAuthLeEdge };

            var ecrAuthRules = new RecursiveDictionary();
            ecrAuthRules["d"] = new RecursiveValue { StringValue = "" };
            var ecrAuthUsage = new RecursiveDictionary();
            ecrAuthUsage["l"] = new RecursiveValue { StringValue = "Usage of a valid, unexpired, and non-revoked vLEI Credential, as defined in the associated Ecosystem Governance Framework, does not assert that the Legal Entity is trustworthy, honest, reputable in its business dealings, safe to do business with, or compliant with any laws or that an implied or expressly intended purpose will be fulfilled." };
            ecrAuthRules["usageDisclaimer"] = new RecursiveValue { Dictionary = ecrAuthUsage };
            var ecrAuthIssuance = new RecursiveDictionary();
            ecrAuthIssuance["l"] = new RecursiveValue { StringValue = "All information in a valid, unexpired, and non-revoked vLEI Credential, as defined in the associated Ecosystem Governance Framework, is accurate as of the date the validation process was complete. The vLEI Credential has been issued to the legal entity or person named in the vLEI Credential as the subject; and the qualified vLEI Issuer exercised reasonable care to perform the validation process set forth in the vLEI Ecosystem Governance Framework." };
            ecrAuthRules["issuanceDisclaimer"] = new RecursiveValue { Dictionary = ecrAuthIssuance };
            var ecrAuthPrivacy = new RecursiveDictionary();
            ecrAuthPrivacy["l"] = new RecursiveValue { StringValue = "Privacy Considerations are applicable to QVI ECR AUTH vLEI Credentials.  It is the sole responsibility of QVIs as Issuees of QVI ECR AUTH vLEI Credentials to present these Credentials in a privacy-preserving manner using the mechanisms provided in the Issuance and Presentation Exchange (IPEX) protocol specification and the Authentic Chained Data Container (ACDC) specification.  https://github.com/WebOfTrust/IETF-IPEX and https://github.com/trustoverip/tswg-acdc-specification." };
            ecrAuthRules["privacyDisclaimer"] = new RecursiveValue { Dictionary = ecrAuthPrivacy };

            var ecrAuthIssueArgs = new IssueAndGetCredentialArgs(
                IssuerAidName: leName,
                RegistryName: leRegistryName,
                Schema: "EH6ekLjSr8V32WyFbGe1zXjTzFs9PkTYmupJ9H65O14g",
                HolderPrefix: qviResult.Value.Prefix,
                CredData: ecrAuthCredData,
                CredEdge: ecrAuthEdge,
                CredRules: ecrAuthRules
            );
            var ecrAuthCredResult = await _signifyClient.IssueAndGetCredential(ecrAuthIssueArgs);
            if (ecrAuthCredResult.IsFailed) {
                var err = $"Failed to issue ECR Auth credential: {ecrAuthCredResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            var ecrAuthAcdc = ecrAuthCredResult.Value["acdc"].Dictionary!;
            var ecrAuthAnc = ecrAuthCredResult.Value["anc"].Dictionary!;
            var ecrAuthIss = ecrAuthCredResult.Value["iss"].Dictionary!;
            var ecrAuthCredSaid = ecrAuthCredResult.Value["said"].StringValue!;
            _logger.LogInformation("ECR Auth credential issued: said={Said}", ecrAuthCredSaid);

            // Step 29b: LE grants ECR Auth to QVI via IPEX
            _logger.LogInformation("Step 29b: LE granting ECR Auth credential to QVI via IPEX...");
            var ecrAuthGrantArgs = new IpexGrantSubmitArgs(
                SenderName: leName,
                Recipient: qviResult.Value.Prefix,
                Acdc: ecrAuthAcdc,
                Anc: ecrAuthAnc,
                Iss: ecrAuthIss
            );
            var ecrAuthGrantResult = await _signifyClient.IpexGrantAndSubmit(ecrAuthGrantArgs);
            if (ecrAuthGrantResult.IsFailed) {
                var err = $"Failed to grant ECR Auth credential: {ecrAuthGrantResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            var ecrAuthGrantSaid = ecrAuthGrantResult.Value["grantSaid"].StringValue!;
            _logger.LogInformation("ECR Auth credential granted: grantSaid={GrantSaid}", ecrAuthGrantSaid);

            // Step 30: QVI admits ECR Auth credential
            _logger.LogInformation("Step 30: QVI admitting ECR Auth credential...");
            var ecrAuthAdmitArgs = new IpexAdmitSubmitArgs(
                SenderName: qviName,
                Recipient: leResult.Value.Prefix,
                GrantSaid: ecrAuthGrantSaid
            );
            var ecrAuthAdmitResult = await _signifyClient.IpexAdmitAndSubmit(ecrAuthAdmitArgs);
            if (ecrAuthAdmitResult.IsFailed) {
                var err = $"Failed to admit ECR Auth credential: {ecrAuthAdmitResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("ECR Auth credential admitted successfully");

            // Step 31a: QVI issues ECR credential to Person (private, with u fields)
            _logger.LogInformation("Step 31a: QVI issuing ECR credential to Person...");
            var ecrCredData = new RecursiveDictionary();
            ecrCredData["LEI"] = new RecursiveValue { StringValue = "254900OPPU84GM83MG36" };
            ecrCredData["personLegalName"] = new RecursiveValue { StringValue = "John Smith" };
            ecrCredData["engagementContextRole"] = new RecursiveValue { StringValue = "Project Manager" };

            var ecrEdge = new RecursiveDictionary();
            ecrEdge["d"] = new RecursiveValue { StringValue = "" };
            var ecrAuthRef = new RecursiveDictionary();
            ecrAuthRef["n"] = new RecursiveValue { StringValue = ecrAuthCredSaid };
            ecrAuthRef["s"] = new RecursiveValue { StringValue = "EH6ekLjSr8V32WyFbGe1zXjTzFs9PkTYmupJ9H65O14g" };
            ecrAuthRef["o"] = new RecursiveValue { StringValue = "I2I" };
            ecrEdge["auth"] = new RecursiveValue { Dictionary = ecrAuthRef };

            var ecrRules = new RecursiveDictionary();
            ecrRules["d"] = new RecursiveValue { StringValue = "" };
            var ecrUsage = new RecursiveDictionary();
            ecrUsage["l"] = new RecursiveValue { StringValue = "Usage of a valid, unexpired, and non-revoked vLEI Credential, as defined in the associated Ecosystem Governance Framework, does not assert that the Legal Entity is trustworthy, honest, reputable in its business dealings, safe to do business with, or compliant with any laws or that an implied or expressly intended purpose will be fulfilled." };
            ecrRules["usageDisclaimer"] = new RecursiveValue { Dictionary = ecrUsage };
            var ecrIssuance = new RecursiveDictionary();
            ecrIssuance["l"] = new RecursiveValue { StringValue = "All information in a valid, unexpired, and non-revoked vLEI Credential, as defined in the associated Ecosystem Governance Framework, is accurate as of the date the validation process was complete. The vLEI Credential has been issued to the legal entity or person named in the vLEI Credential as the subject; and the qualified vLEI Issuer exercised reasonable care to perform the validation process set forth in the vLEI Ecosystem Governance Framework." };
            ecrRules["issuanceDisclaimer"] = new RecursiveValue { Dictionary = ecrIssuance };
            var ecrPrivacy = new RecursiveDictionary();
            ecrPrivacy["l"] = new RecursiveValue { StringValue = "It is the sole responsibility of Holders as Issuees of an ECR vLEI Credential to present that Credential in a privacy-preserving manner using the mechanisms provided in the Issuance and Presentation Exchange (IPEX) protocol specification and the Authentic Chained Data Container (ACDC) specification. https://github.com/WebOfTrust/IETF-IPEX and https://github.com/trustoverip/tswg-acdc-specification." };
            ecrRules["privacyDisclaimer"] = new RecursiveValue { Dictionary = ecrPrivacy };

            var ecrIssueArgs = new IssueAndGetCredentialArgs(
                IssuerAidName: qviName,
                RegistryName: qviRegistryName,
                Schema: "EEy9PkikFcANV1l7EHukCeXqrzT1hNZjGlUk7wuMO5jw",
                HolderPrefix: personResult.Value.Prefix,
                CredData: ecrCredData,
                CredEdge: ecrEdge,
                CredRules: ecrRules,
                Private: true
            );
            var ecrCredResult = await _signifyClient.IssueAndGetCredential(ecrIssueArgs);
            if (ecrCredResult.IsFailed) {
                var err = $"Failed to issue ECR credential: {ecrCredResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            var ecrAcdc = ecrCredResult.Value["acdc"].Dictionary!;
            var ecrAnc = ecrCredResult.Value["anc"].Dictionary!;
            var ecrIssVal = ecrCredResult.Value["iss"].Dictionary!;
            var ecrCredSaid = ecrCredResult.Value["said"].StringValue!;
            _logger.LogInformation("ECR credential issued: said={Said}", ecrCredSaid);

            // Step 31b: QVI grants ECR credential to Person via IPEX
            _logger.LogInformation("Step 31b: QVI granting ECR credential to Person via IPEX...");
            var ecrGrantArgs = new IpexGrantSubmitArgs(
                SenderName: qviName,
                Recipient: personResult.Value.Prefix,
                Acdc: ecrAcdc,
                Anc: ecrAnc,
                Iss: ecrIssVal
            );
            var ecrGrantResult = await _signifyClient.IpexGrantAndSubmit(ecrGrantArgs);
            if (ecrGrantResult.IsFailed) {
                var err = $"Failed to grant ECR credential: {ecrGrantResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            var ecrGrantSaid = ecrGrantResult.Value["grantSaid"].StringValue!;
            _logger.LogInformation("ECR credential granted: grantSaid={GrantSaid}", ecrGrantSaid);

            // Step 32: Person admits ECR credential
            _logger.LogInformation("Step 32: Person admitting ECR credential...");
            var ecrAdmitArgs = new IpexAdmitSubmitArgs(
                SenderName: personName,
                Recipient: qviResult.Value.Prefix,
                GrantSaid: ecrGrantSaid
            );
            var ecrAdmitResult = await _signifyClient.IpexAdmitAndSubmit(ecrAdmitArgs);
            if (ecrAdmitResult.IsFailed) {
                var err = $"Failed to admit ECR credential: {ecrAdmitResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Ok(new PrimeDataGoResponse(false, Error: err));
            }
            _logger.LogInformation("ECR credential admitted successfully");

            _logger.LogInformation("PrimeData Go completed successfully");
            return Result.Ok(new PrimeDataGoResponse(true));
        }
    }
}
