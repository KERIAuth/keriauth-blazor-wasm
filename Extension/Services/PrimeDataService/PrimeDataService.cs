using System.Text.Json;
using Extension.Helper;
using Extension.Models;
using Extension.Models.Messages.AppBw;
using Extension.Models.Storage;
using Extension.Services.SignifyService;
using Extension.Services.SignifyService.Models;
using Extension.Services.Storage;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace Extension.Services.PrimeDataService {
    public class PrimeDataService : IPrimeDataService {
        private readonly ISignifyClientService _signifyClient;
        private readonly IStorageService _storageService;
        private readonly ILogger<PrimeDataService> _logger;

        public PrimeDataService(ISignifyClientService signifyClient, IStorageService storageService, ILogger<PrimeDataService> logger) {
            _signifyClient = signifyClient;
            _storageService = storageService;
            _logger = logger;
        }

        public async Task<Result<PrimeDataGoResponse>> GoAsync(PrimeDataGoPayload? payload = null) {
            var prepend = payload?.Prepend ?? string.Empty;
            _logger.LogInformation("PrimeData Go starting with prepend '{Prepend}'", prepend);

            var generatedExchangeSaids = new HashSet<string>();

            // Steps 1-4: Create AIDs
            var nameToPrefix = new Dictionary<string, string>();

            var gedaName = $"{prepend}geda";
            var gedaResult = await CreateAidStep(gedaName, "Step 1");
            if (gedaResult.IsFailed) return FailResponse(gedaResult.Errors[0].Message);
            nameToPrefix[gedaName] = gedaResult.Value.Prefix;

            var qviName = $"{prepend}qvi";
            var qviResult = await CreateAidStep(qviName, "Step 2");
            if (qviResult.IsFailed) return FailResponse(qviResult.Errors[0].Message);
            nameToPrefix[qviName] = qviResult.Value.Prefix;

            var leName = $"{prepend}le";
            var leResult = await CreateAidStep(leName, "Step 3");
            if (leResult.IsFailed) return FailResponse(leResult.Errors[0].Message);
            nameToPrefix[leName] = leResult.Value.Prefix;

            var personName = $"{prepend}person";
            var personResult = await CreateAidStep(personName, "Step 4");
            if (personResult.IsFailed) return FailResponse(personResult.Errors[0].Message);
            nameToPrefix[personName] = personResult.Value.Prefix;

            // Step 5: Resolve OOBIs between roles
            var step5Pairs = new[] {
                (gedaName, qviResult.Value.Oobi, qviName),
                (qviName, gedaResult.Value.Oobi, gedaName),
                (leName, qviResult.Value.Oobi, qviName),
                (qviName, leResult.Value.Oobi, leName),
            };
            var step5 = await ResolveOobiPairsStep(step5Pairs, "Step 5");
            if (step5.IsFailed) return FailResponse(step5.Errors[0].Message);
            var step5Store = await StoreConnectionsStep(step5Pairs, nameToPrefix, "Step 5");
            if (step5Store.IsFailed) return FailResponse(step5Store.Errors[0].Message);

            // Steps 6-8: GEDA challenges QVI
            var step6 = await ChallengeExchangeStep(
                gedaName, gedaResult.Value.Prefix,
                qviName, qviResult.Value.Prefix,
                "Step 6", "Step 7", "Step 8");
            if (step6.IsFailed) return FailResponse(step6.Errors[0].Message);

            // Steps 9-11: QVI challenges GEDA
            var step9 = await ChallengeExchangeStep(
                qviName, qviResult.Value.Prefix,
                gedaName, gedaResult.Value.Prefix,
                "Step 9", "Step 10", "Step 11");
            if (step9.IsFailed) return FailResponse(step9.Errors[0].Message);

            // Step 12: GEDA creates credential registry
            var gedaRegistryName = $"{prepend}geda_registry";
            var registryResult = await CreateRegistryStep(gedaName, gedaRegistryName, "Step 12");
            if (registryResult.IsFailed) return FailResponse(registryResult.Errors[0].Message);

            // Step 12b: Resolve credential schema OOBIs
            _logger.LogInformation("Step 12b: Resolving credential schema OOBIs...");
            var schemaOobis = new[] {
                QviSchemaSaid, LeSchemaSaid, OorAuthSchemaSaid, OorSchemaSaid, EcrAuthSchemaSaid, EcrSchemaSaid
            };
            foreach (var said in schemaOobis) {
                _logger.LogInformation("  Resolving schema OOBI {SchemaSaid}...", said);
                var schemaResolveResult = await _signifyClient.ResolveOobi(SchemaOobiBaseUrl + said);
                if (schemaResolveResult.IsFailed) {
                    var err = $"Failed to resolve schema OOBI {said}: {schemaResolveResult.Errors[0].Message}";
                    _logger.LogError("{Error}", err);
                    return FailResponse(err);
                }
                _logger.LogInformation("  Schema OOBI resolved: {SchemaSaid}", said);
            }

            // Step 13a: GEDA issues QVI credential
            var qviCredData = new RecursiveDictionary();
            qviCredData["LEI"] = new RecursiveValue { StringValue = "5493001KJTIIGC8Y1R17" };
            var qviCredIssued = await IssueCredentialStep(new IssueAndGetCredentialArgs(
                IssuerAidNameOrPrefix: gedaName,
                RegistryName: gedaRegistryName,
                Schema: QviSchemaSaid,
                HolderPrefix: qviResult.Value.Prefix,
                CredData: qviCredData
            ), "Step 13a", "QVI credential");
            if (qviCredIssued.IsFailed) return FailResponse(qviCredIssued.Errors[0].Message);

            // Step 13b: GEDA grants QVI credential to QVI via IPEX
            var qviGrantSaid = await GrantCredentialStep(new IpexGrantSubmitArgs(
                SenderNameOrPrefix: gedaName,
                RecipientPrefix: qviResult.Value.Prefix,
                Acdc: qviCredIssued.Value.Acdc,
                Anc: qviCredIssued.Value.Anc,
                Iss: qviCredIssued.Value.Iss
            ), "Step 13b", "QVI credential");
            if (qviGrantSaid.IsFailed) return FailResponse(qviGrantSaid.Errors[0].Message);
            generatedExchangeSaids.Add(qviGrantSaid.Value);

            // Step 14: QVI admits QVI credential
            var step14 = await AdmitCredentialStep(new IpexAdmitSubmitArgs(
                SenderNameOrPrefix: qviName,
                RecipientPrefix: gedaResult.Value.Prefix,
                GrantSaid: qviGrantSaid.Value
            ), "Step 14", "QVI credential");
            if (step14.IsFailed) return FailResponse(step14.Errors[0].Message);
            generatedExchangeSaids.Add(step14.Value);

            // Step 15a: Create Verifier AID
            var verifierName = $"{prepend}verifier";
            var verifierResult = await CreateAidStep(verifierName, "Step 15a");
            if (verifierResult.IsFailed) return FailResponse(verifierResult.Errors[0].Message);
            nameToPrefix[verifierName] = verifierResult.Value.Prefix;

            // Step 15b: Resolve OOBIs between QVI and Verifier
            var step15bPairs = new[] {
                (qviName, verifierResult.Value.Oobi, verifierName),
                (verifierName, qviResult.Value.Oobi, qviName),
            };
            var step15b = await ResolveOobiPairsStep(step15bPairs, "Step 15b");
            if (step15b.IsFailed) return FailResponse(step15b.Errors[0].Message);
            var step15bStore = await StoreConnectionsStep(step15bPairs, nameToPrefix, "Step 15b");
            if (step15bStore.IsFailed) return FailResponse(step15bStore.Errors[0].Message);

            // Step 16a: Verifier requests QVI credential via IPEX apply
            _logger.LogInformation("Step 16a: Verifier requesting QVI credential via IPEX apply...");
            var applyResult = await _signifyClient.IpexApplyAndSubmit(new IpexApplySubmitArgs(
                SenderNameOrPrefix: verifierName,
                RecipientPrefix: qviResult.Value.Prefix,
                SchemaSaid: QviSchemaSaid
            ));
            if (applyResult.IsFailed) {
                var err = $"Failed verifier IPEX apply: {applyResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return FailResponse(err);
            }
            var applySaid = applyResult.Value["applySaid"].StringValue!;
            _logger.LogInformation("Verifier IPEX apply submitted: applySaid={ApplySaid}", applySaid);
            generatedExchangeSaids.Add(applySaid);

            // Step 16b: QVI offers credential to Verifier
            _logger.LogInformation("Step 16b: QVI offering credential to Verifier via IPEX offer...");
            var offerResult = await _signifyClient.IpexOfferAndSubmit(new IpexOfferSubmitArgs(
                SenderNameOrPrefix: qviName,
                RecipientPrefix: verifierResult.Value.Prefix,
                CredentialSaid: qviCredIssued.Value.Said,
                ApplySaid: applySaid
            ));
            if (offerResult.IsFailed) {
                var err = $"Failed QVI IPEX offer: {offerResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return FailResponse(err);
            }
            var offerSaid = offerResult.Value["offerSaid"].StringValue!;
            _logger.LogInformation("QVI IPEX offer submitted: offerSaid={OfferSaid}", offerSaid);
            generatedExchangeSaids.Add(offerSaid);

            // Step 16c: Verifier agrees to credential offer
            _logger.LogInformation("Step 16c: Verifier agreeing to credential offer via IPEX agree...");
            var agreeResult = await _signifyClient.IpexAgreeAndSubmit(new IpexAgreeSubmitArgs(
                SenderNameOrPrefix: verifierName,
                RecipientPrefix: qviResult.Value.Prefix,
                OfferSaid: offerSaid
            ));
            if (agreeResult.IsFailed) {
                var err = $"Failed verifier IPEX agree: {agreeResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return FailResponse(err);
            }
            var agreeSaid = agreeResult.Value["agreeSaid"].StringValue!;
            _logger.LogInformation("Verifier IPEX agree submitted: agreeSaid={AgreeSaid}", agreeSaid);
            generatedExchangeSaids.Add(agreeSaid);

            // Step 17: QVI creates credential registry for LE credentials
            var qviRegistryName = $"{prepend}qvi_le_registry";
            var qviRegistryResult = await CreateRegistryStep(qviName, qviRegistryName, "Step 17");
            if (qviRegistryResult.IsFailed) return FailResponse(qviRegistryResult.Errors[0].Message);

            // Step 18a: QVI issues LE credential
            var leCredData = new RecursiveDictionary();
            leCredData["LEI"] = new RecursiveValue { StringValue = "254900OPPU84GM83MG36" };

            var leCredEdge = new RecursiveDictionary();
            leCredEdge["d"] = new RecursiveValue { StringValue = "" };
            var qviEdge = new RecursiveDictionary();
            qviEdge["n"] = new RecursiveValue { StringValue = qviCredIssued.Value.Said };
            qviEdge["s"] = new RecursiveValue { StringValue = QviSchemaSaid };
            leCredEdge["qvi"] = new RecursiveValue { Dictionary = qviEdge };

            var leCredIssued = await IssueCredentialStep(new IssueAndGetCredentialArgs(
                IssuerAidNameOrPrefix: qviName,
                RegistryName: qviRegistryName,
                Schema: LeSchemaSaid,
                HolderPrefix: leResult.Value.Prefix,
                CredData: leCredData,
                CredEdge: leCredEdge,
                CredRules: VleiCredentialHelper.BuildVleiRules()
            ), "Step 18a", "LE credential");
            if (leCredIssued.IsFailed) return FailResponse(leCredIssued.Errors[0].Message);

            // Step 18b: QVI grants LE credential to LE via IPEX
            var leGrantSaid = await GrantCredentialStep(new IpexGrantSubmitArgs(
                SenderNameOrPrefix: qviName,
                RecipientPrefix: leResult.Value.Prefix,
                Acdc: leCredIssued.Value.Acdc,
                Anc: leCredIssued.Value.Anc,
                Iss: leCredIssued.Value.Iss
            ), "Step 18b", "LE credential");
            if (leGrantSaid.IsFailed) return FailResponse(leGrantSaid.Errors[0].Message);
            generatedExchangeSaids.Add(leGrantSaid.Value);

            // Step 19: LE admits LE credential
            var step19 = await AdmitCredentialStep(new IpexAdmitSubmitArgs(
                SenderNameOrPrefix: leName,
                RecipientPrefix: qviResult.Value.Prefix,
                GrantSaid: leGrantSaid.Value
            ), "Step 19", "LE credential");
            if (step19.IsFailed) return FailResponse(step19.Errors[0].Message);
            generatedExchangeSaids.Add(step19.Value);

            // Step 20: Resolve OOBIs between LE and Verifier
            var step20Pairs = new[] {
                (leName, verifierResult.Value.Oobi, verifierName),
                (verifierName, leResult.Value.Oobi, leName),
            };
            var step20 = await ResolveOobiPairsStep(step20Pairs, "Step 20");
            if (step20.IsFailed) return FailResponse(step20.Errors[0].Message);
            var step20Store = await StoreConnectionsStep(step20Pairs, nameToPrefix, "Step 20");
            if (step20Store.IsFailed) return FailResponse(step20Store.Errors[0].Message);

            // Step 21: LE presents LE credential to Verifier
            var step21 = await PresentCredentialStep(leName, leCredIssued.Value.Said, verifierResult.Value.Prefix, "Step 21", "LE credential");
            if (step21.IsFailed) return FailResponse(step21.Errors[0].Message);
            generatedExchangeSaids.Add(step21.Value);

            // Step 22: Resolve OOBIs for Person
            var step22Pairs = new[] {
                (personName, leResult.Value.Oobi, leName),
                (leName, personResult.Value.Oobi, personName),
                (qviName, personResult.Value.Oobi, personName),
                (personName, qviResult.Value.Oobi, qviName),
                (personName, verifierResult.Value.Oobi, verifierName),
                (verifierName, personResult.Value.Oobi, personName),
            };
            var step22 = await ResolveOobiPairsStep(step22Pairs, "Step 22");
            if (step22.IsFailed) return FailResponse(step22.Errors[0].Message);
            var step22Store = await StoreConnectionsStep(step22Pairs, nameToPrefix, "Step 22");
            if (step22Store.IsFailed) return FailResponse(step22Store.Errors[0].Message);

            // Step 23: LE creates credential registry
            var leRegistryName = $"{prepend}le_oor_registry";
            var leRegistryResult = await CreateRegistryStep(leName, leRegistryName, "Step 23");
            if (leRegistryResult.IsFailed) return FailResponse(leRegistryResult.Errors[0].Message);

            // Step 24a: LE issues OOR Auth credential to QVI
            var oorAuthCredData = new RecursiveDictionary();
            oorAuthCredData["AID"] = new RecursiveValue { StringValue = personResult.Value.Prefix };
            oorAuthCredData["LEI"] = new RecursiveValue { StringValue = "254900OPPU84GM83MG36" };
            oorAuthCredData["personLegalName"] = new RecursiveValue { StringValue = "John Smith" };
            oorAuthCredData["officialRole"] = new RecursiveValue { StringValue = "Head of Standards" };

            var oorAuthEdge = new RecursiveDictionary();
            oorAuthEdge["d"] = new RecursiveValue { StringValue = "" };
            var oorAuthLeEdge = new RecursiveDictionary();
            oorAuthLeEdge["n"] = new RecursiveValue { StringValue = leCredIssued.Value.Said };
            oorAuthLeEdge["s"] = new RecursiveValue { StringValue = LeSchemaSaid };
            oorAuthEdge["le"] = new RecursiveValue { Dictionary = oorAuthLeEdge };

            var oorAuthIssued = await IssueCredentialStep(new IssueAndGetCredentialArgs(
                IssuerAidNameOrPrefix: leName,
                RegistryName: leRegistryName,
                Schema: OorAuthSchemaSaid,
                HolderPrefix: qviResult.Value.Prefix,
                CredData: oorAuthCredData,
                CredEdge: oorAuthEdge,
                CredRules: VleiCredentialHelper.BuildVleiRules()
            ), "Step 24a", "OOR Auth credential");
            if (oorAuthIssued.IsFailed) return FailResponse(oorAuthIssued.Errors[0].Message);

            // Step 24b: LE grants OOR Auth to QVI via IPEX
            var oorAuthGrantSaid = await GrantCredentialStep(new IpexGrantSubmitArgs(
                SenderNameOrPrefix: leName,
                RecipientPrefix: qviResult.Value.Prefix,
                Acdc: oorAuthIssued.Value.Acdc,
                Anc: oorAuthIssued.Value.Anc,
                Iss: oorAuthIssued.Value.Iss
            ), "Step 24b", "OOR Auth credential");
            if (oorAuthGrantSaid.IsFailed) return FailResponse(oorAuthGrantSaid.Errors[0].Message);
            generatedExchangeSaids.Add(oorAuthGrantSaid.Value);

            // Step 25: QVI admits OOR Auth credential
            var step25 = await AdmitCredentialStep(new IpexAdmitSubmitArgs(
                SenderNameOrPrefix: qviName,
                RecipientPrefix: leResult.Value.Prefix,
                GrantSaid: oorAuthGrantSaid.Value
            ), "Step 25", "OOR Auth credential");
            if (step25.IsFailed) return FailResponse(step25.Errors[0].Message);
            generatedExchangeSaids.Add(step25.Value);

            // Step 26a: QVI issues OOR credential to Person
            var oorCredData = new RecursiveDictionary();
            oorCredData["LEI"] = new RecursiveValue { StringValue = "254900OPPU84GM83MG36" };
            oorCredData["personLegalName"] = new RecursiveValue { StringValue = "John Smith" };
            oorCredData["officialRole"] = new RecursiveValue { StringValue = "Head of Standards" };

            var oorEdge = new RecursiveDictionary();
            oorEdge["d"] = new RecursiveValue { StringValue = "" };
            var oorAuthRef = new RecursiveDictionary();
            oorAuthRef["n"] = new RecursiveValue { StringValue = oorAuthIssued.Value.Said };
            oorAuthRef["s"] = new RecursiveValue { StringValue = OorAuthSchemaSaid };
            oorAuthRef["o"] = new RecursiveValue { StringValue = "I2I" };
            oorEdge["auth"] = new RecursiveValue { Dictionary = oorAuthRef };

            var oorIssued = await IssueCredentialStep(new IssueAndGetCredentialArgs(
                IssuerAidNameOrPrefix: qviName,
                RegistryName: qviRegistryName,
                Schema: OorSchemaSaid,
                HolderPrefix: personResult.Value.Prefix,
                CredData: oorCredData,
                CredEdge: oorEdge,
                CredRules: VleiCredentialHelper.BuildVleiRules()
            ), "Step 26a", "OOR credential");
            if (oorIssued.IsFailed) return FailResponse(oorIssued.Errors[0].Message);

            // Step 26b: QVI grants OOR credential to Person via IPEX
            var oorGrantSaid = await GrantCredentialStep(new IpexGrantSubmitArgs(
                SenderNameOrPrefix: qviName,
                RecipientPrefix: personResult.Value.Prefix,
                Acdc: oorIssued.Value.Acdc,
                Anc: oorIssued.Value.Anc,
                Iss: oorIssued.Value.Iss
            ), "Step 26b", "OOR credential");
            if (oorGrantSaid.IsFailed) return FailResponse(oorGrantSaid.Errors[0].Message);
            generatedExchangeSaids.Add(oorGrantSaid.Value);

            // Step 27: Person admits OOR credential
            var step27 = await AdmitCredentialStep(new IpexAdmitSubmitArgs(
                SenderNameOrPrefix: personName,
                RecipientPrefix: qviResult.Value.Prefix,
                GrantSaid: oorGrantSaid.Value
            ), "Step 27", "OOR credential");
            if (step27.IsFailed) return FailResponse(step27.Errors[0].Message);
            generatedExchangeSaids.Add(step27.Value);

            // Step 28: Person presents OOR credential to Verifier
            var step28 = await PresentCredentialStep(personName, oorIssued.Value.Said, verifierResult.Value.Prefix, "Step 28", "OOR credential");
            if (step28.IsFailed) return FailResponse(step28.Errors[0].Message);
            generatedExchangeSaids.Add(step28.Value);

            // Step 29a: LE issues ECR Auth credential to QVI
            var ecrAuthCredData = new RecursiveDictionary();
            ecrAuthCredData["AID"] = new RecursiveValue { StringValue = personResult.Value.Prefix };
            ecrAuthCredData["LEI"] = new RecursiveValue { StringValue = "254900OPPU84GM83MG36" };
            ecrAuthCredData["personLegalName"] = new RecursiveValue { StringValue = "John Smith" };
            ecrAuthCredData["engagementContextRole"] = new RecursiveValue { StringValue = "Project Manager" };

            var ecrAuthEdge = new RecursiveDictionary();
            ecrAuthEdge["d"] = new RecursiveValue { StringValue = "" };
            var ecrAuthLeEdge = new RecursiveDictionary();
            ecrAuthLeEdge["n"] = new RecursiveValue { StringValue = leCredIssued.Value.Said };
            ecrAuthLeEdge["s"] = new RecursiveValue { StringValue = LeSchemaSaid };
            ecrAuthEdge["le"] = new RecursiveValue { Dictionary = ecrAuthLeEdge };

            var ecrAuthIssued = await IssueCredentialStep(new IssueAndGetCredentialArgs(
                IssuerAidNameOrPrefix: leName,
                RegistryName: leRegistryName,
                Schema: EcrAuthSchemaSaid,
                HolderPrefix: qviResult.Value.Prefix,
                CredData: ecrAuthCredData,
                CredEdge: ecrAuthEdge,
                CredRules: VleiCredentialHelper.BuildVleiRules(VleiCredentialHelper.EcrAuthPrivacyDisclaimer)
            ), "Step 29a", "ECR Auth credential");
            if (ecrAuthIssued.IsFailed) return FailResponse(ecrAuthIssued.Errors[0].Message);

            // Step 29b: LE grants ECR Auth to QVI via IPEX
            var ecrAuthGrantSaid = await GrantCredentialStep(new IpexGrantSubmitArgs(
                SenderNameOrPrefix: leName,
                RecipientPrefix: qviResult.Value.Prefix,
                Acdc: ecrAuthIssued.Value.Acdc,
                Anc: ecrAuthIssued.Value.Anc,
                Iss: ecrAuthIssued.Value.Iss
            ), "Step 29b", "ECR Auth credential");
            if (ecrAuthGrantSaid.IsFailed) return FailResponse(ecrAuthGrantSaid.Errors[0].Message);
            generatedExchangeSaids.Add(ecrAuthGrantSaid.Value);

            // Step 30: QVI admits ECR Auth credential
            var step30 = await AdmitCredentialStep(new IpexAdmitSubmitArgs(
                SenderNameOrPrefix: qviName,
                RecipientPrefix: leResult.Value.Prefix,
                GrantSaid: ecrAuthGrantSaid.Value
            ), "Step 30", "ECR Auth credential");
            if (step30.IsFailed) return FailResponse(step30.Errors[0].Message);
            generatedExchangeSaids.Add(step30.Value);

            // Step 31a: QVI issues ECR credential to Person (private)
            var ecrCredData = VleiCredentialHelper.BuildEcrCredentialData("Project Manager");
            var ecrEdge = VleiCredentialHelper.BuildEcrAuthEdge(ecrAuthIssued.Value.Said);

            var ecrIssued = await IssueCredentialStep(new IssueAndGetCredentialArgs(
                IssuerAidNameOrPrefix: qviName,
                RegistryName: qviRegistryName,
                Schema: EcrSchemaSaid,
                HolderPrefix: personResult.Value.Prefix,
                CredData: ecrCredData,
                CredEdge: ecrEdge,
                CredRules: VleiCredentialHelper.BuildVleiRules(VleiCredentialHelper.EcrPrivacyDisclaimer),
                Private: true
            ), "Step 31a", "ECR credential");
            if (ecrIssued.IsFailed) return FailResponse(ecrIssued.Errors[0].Message);

            // Step 31b: QVI grants ECR credential to Person via IPEX
            var ecrGrantSaid = await GrantCredentialStep(new IpexGrantSubmitArgs(
                SenderNameOrPrefix: qviName,
                RecipientPrefix: personResult.Value.Prefix,
                Acdc: ecrIssued.Value.Acdc,
                Anc: ecrIssued.Value.Anc,
                Iss: ecrIssued.Value.Iss
            ), "Step 31b", "ECR credential");
            if (ecrGrantSaid.IsFailed) return FailResponse(ecrGrantSaid.Errors[0].Message);
            generatedExchangeSaids.Add(ecrGrantSaid.Value);

            // Step 32: Person admits ECR credential
            var step32 = await AdmitCredentialStep(new IpexAdmitSubmitArgs(
                SenderNameOrPrefix: personName,
                RecipientPrefix: qviResult.Value.Prefix,
                GrantSaid: ecrGrantSaid.Value
            ), "Step 32", "ECR credential");
            if (step32.IsFailed) return FailResponse(step32.Errors[0].Message);
            generatedExchangeSaids.Add(step32.Value);

            // Step 33: Wait for KERIA to propagate notifications, then mark as read
            await WaitForNotificationsAndMarkAsReadStep(generatedExchangeSaids, "Step 33");

            _logger.LogInformation("PrimeData Go completed successfully");
            return Result.Ok(new PrimeDataGoResponse(true));
        }

        private const string SchemaOobiBaseUrl = VleiCredentialHelper.SchemaOobiBaseUrl;
        private const string QviSchemaSaid = VleiCredentialHelper.QviSchemaSaid;
        private const string LeSchemaSaid = VleiCredentialHelper.LeSchemaSaid;
        private const string OorAuthSchemaSaid = VleiCredentialHelper.OorAuthSchemaSaid;
        private const string OorSchemaSaid = VleiCredentialHelper.OorSchemaSaid;
        private const string EcrAuthSchemaSaid = VleiCredentialHelper.EcrAuthSchemaSaid;
        private const string EcrSchemaSaid = VleiCredentialHelper.EcrSchemaSaid;

        /// <summary>
        /// Returns AID prefixes eligible to be the Discloser for a given IPEX workflow.
        /// For Apply-only workflows, no credential is required so all AIDs are eligible.
        /// For issuance workflows with Offer/Grant, the Discloser must hold an ECR Auth credential.
        /// For presentation workflows with Offer/Grant, the Discloser must hold an ECR credential.
        /// </summary>
        public async Task<Result<List<string>>> GetEligibleDiscloserPrefixes(bool isPresentation, IpexWorkflow workflow) {
            var requiresCredential = workflow != IpexWorkflow.Apply;

            if (!requiresCredential) {
                // Apply-only: any AID can be the Discloser (they don't act in Apply-only)
                var idsResult = await _signifyClient.GetIdentifiers();
                if (idsResult.IsFailed) return Result.Fail<List<string>>(idsResult.Errors[0].Message);
                return Result.Ok(idsResult.Value.Aids.Select(a => a.Prefix).ToList());
            }

            var credsResult = await _signifyClient.GetCredentials();
            if (credsResult.IsFailed) return Result.Fail<List<string>>(credsResult.Errors[0].Message);

            var targetSchema = isPresentation ? EcrSchemaSaid : EcrAuthSchemaSaid;
            var eligiblePrefixes = credsResult.Value
                .Where(c => c.GetValueByPath("sad.s")?.Value?.ToString() == targetSchema)
                .Select(c => isPresentation
                    ? c.GetValueByPath("sad.a.i")?.Value?.ToString()   // ECR holder (issuee)
                    : c.GetValueByPath("sad.a.i")?.Value?.ToString())  // ECR Auth holder (issuee)
                .Where(p => p is not null)
                .Cast<string>()
                .Distinct()
                .ToList();

            return Result.Ok(eligiblePrefixes);
        }

        public async Task<Result<PrimeDataIpexResponse>> GoIpexAsync(PrimeDataIpexPayload payload) {
            _logger.LogInformation("PrimeData IPEX starting: workflow={Workflow}, isPresentation={IsPresentation}, discloser={Discloser}, disclosee={Disclosee}, role={Role}",
                payload.Workflow, payload.IsPresentation, payload.DiscloserPrefix, payload.DiscloseePrefix, payload.EcrRole);

            var generatedExchangeSaids = new HashSet<string>();

            // Resolve ECR schema OOBI
            _logger.LogInformation("Resolving ECR schema OOBI...");
            var schemaResolve = await _signifyClient.ResolveOobi(SchemaOobiBaseUrl + EcrSchemaSaid);
            if (schemaResolve.IsFailed) {
                return FailIpexResponse($"Failed to resolve ECR schema OOBI: {schemaResolve.Errors[0].Message}");
            }

            var discloserPrefix = payload.DiscloserPrefix;
            var discloseePrefix = payload.DiscloseePrefix;
            var workflow = payload.Workflow;

            // Determine credential SAID for presentation or issue credential for issuance
            string? credentialSaid = null;
            IssuedCredential? issuedCred = null;

            var workflowIncludesOfferOrGrant = workflow is not IpexWorkflow.Apply;

            if (workflowIncludesOfferOrGrant) {
                if (payload.IsPresentation) {
                    // Presentation: find existing ECR credential held by Discloser
                    var credsResult = await _signifyClient.GetCredentials();
                    if (credsResult.IsFailed) return FailIpexResponse($"Failed to get credentials: {credsResult.Errors[0].Message}");

                    var ecrCred = credsResult.Value.FirstOrDefault(c =>
                        c.GetValueByPath("sad.s")?.Value?.ToString() == EcrSchemaSaid &&
                        c.GetValueByPath("sad.a.i")?.Value?.ToString() == discloserPrefix);

                    if (ecrCred is null) {
                        return FailIpexResponse($"Discloser {discloserPrefix} does not hold an ECR credential for presentation");
                    }
                    credentialSaid = ecrCred.GetValueByPath("sad.d")?.Value?.ToString();
                    _logger.LogInformation("Found ECR credential for presentation: said={Said}", credentialSaid);
                }
                else {
                    // Issuance: validate Discloser has ECR Auth, then issue ECR credential
                    var credsResult = await _signifyClient.GetCredentials();
                    if (credsResult.IsFailed) return FailIpexResponse($"Failed to get credentials: {credsResult.Errors[0].Message}");

                    var ecrAuthCred = VleiCredentialHelper.FindEcrAuthCredential(credsResult.Value, discloserPrefix);

                    if (ecrAuthCred is null) {
                        return FailIpexResponse($"Discloser {discloserPrefix} does not hold an ECR Auth credential for issuance");
                    }
                    var ecrAuthSaid = ecrAuthCred.GetValueByPath("sad.d")?.Value?.ToString()!;
                    _logger.LogInformation("Found ECR Auth credential: said={Said}", ecrAuthSaid);

                    // Auto-create registry for Discloser (Issuer)
                    var discloserName = await GetAidName(discloserPrefix);
                    if (discloserName is null) return FailIpexResponse($"Could not resolve AID name for prefix {discloserPrefix}");

                    var registryName = $"{discloserName}_ecr_ipex_registry";
                    var registryResult = await CreateRegistryStep(discloserName, registryName, "IPEX registry");
                    if (registryResult.IsFailed) return FailIpexResponse(registryResult.Errors[0].Message);

                    // Issue ECR credential with ECR Auth edge
                    var ecrCredData = VleiCredentialHelper.BuildEcrCredentialData(payload.EcrRole);
                    var ecrEdge = VleiCredentialHelper.BuildEcrAuthEdge(ecrAuthSaid);

                    issuedCred = (await IssueCredentialStep(new IssueAndGetCredentialArgs(
                        IssuerAidNameOrPrefix: discloserName,
                        RegistryName: registryName,
                        Schema: EcrSchemaSaid,
                        HolderPrefix: discloseePrefix,
                        CredData: ecrCredData,
                        CredEdge: ecrEdge,
                        CredRules: VleiCredentialHelper.BuildVleiRules(VleiCredentialHelper.EcrPrivacyDisclaimer),
                        Private: true
                    ), "IPEX issue", "ECR credential")).ValueOrDefault;

                    if (issuedCred is null) return FailIpexResponse("Failed to issue ECR credential");

                    credentialSaid = issuedCred.Said;
                    _logger.LogInformation("ECR credential issued: said={Said}", credentialSaid);
                }
            }

            // Execute IPEX workflow steps
            // Track all generated SAIDs, and separately track which to mark as read.
            // The last step's notification should remain unread (it represents a pending action).
            string? applySaid = null;
            string? offerSaid = null;
            string? agreeSaid = null;
            string? grantSaid = null;
            string? lastStepSaid = null;

            // Apply step (Disclosee sends Apply to Discloser)
            if (workflow is IpexWorkflow.Apply or IpexWorkflow.ApplyOffer or IpexWorkflow.ApplyOfferAgree
                or IpexWorkflow.ApplyOfferAgreeGrant or IpexWorkflow.ApplyOfferAgreeGrantAdmit) {

                var attributes = new RecursiveDictionary();
                attributes["engagementContextRole"] = new RecursiveValue { StringValue = payload.EcrRole };

                _logger.LogInformation("IPEX Apply: Disclosee {Disclosee} applying to Discloser {Discloser}...", discloseePrefix, discloserPrefix);
                var applyResult = await _signifyClient.IpexApplyAndSubmit(new IpexApplySubmitArgs(
                    SenderNameOrPrefix: discloseePrefix,
                    RecipientPrefix: discloserPrefix,
                    SchemaSaid: EcrSchemaSaid,
                    Attributes: attributes
                ));
                if (applyResult.IsFailed) return FailIpexResponse($"Apply failed: {applyResult.Errors[0].Message}");
                applySaid = applyResult.Value["applySaid"].StringValue!;
                generatedExchangeSaids.Add(applySaid);
                lastStepSaid = applySaid;
                _logger.LogInformation("Apply submitted: applySaid={ApplySaid}", applySaid);
            }

            // Offer step (Discloser sends Offer to Disclosee)
            if (workflow is IpexWorkflow.Offer or IpexWorkflow.ApplyOffer or IpexWorkflow.ApplyOfferAgree
                or IpexWorkflow.ApplyOfferAgreeGrant or IpexWorkflow.ApplyOfferAgreeGrantAdmit
                or IpexWorkflow.OfferAgreeGrantAdmit) {

                _logger.LogInformation("IPEX Offer: Discloser {Discloser} offering to Disclosee {Disclosee}...", discloserPrefix, discloseePrefix);
                var offerResult = await _signifyClient.IpexOfferAndSubmit(new IpexOfferSubmitArgs(
                    SenderNameOrPrefix: discloserPrefix,
                    RecipientPrefix: discloseePrefix,
                    CredentialSaid: credentialSaid!,
                    ApplySaid: applySaid
                ));
                if (offerResult.IsFailed) return FailIpexResponse($"Offer failed: {offerResult.Errors[0].Message}");
                offerSaid = offerResult.Value["offerSaid"].StringValue!;
                generatedExchangeSaids.Add(offerSaid);
                lastStepSaid = offerSaid;
                _logger.LogInformation("Offer submitted: offerSaid={OfferSaid}", offerSaid);
            }

            // Agree step (Disclosee sends Agree to Discloser)
            if (workflow is IpexWorkflow.ApplyOfferAgree
                or IpexWorkflow.ApplyOfferAgreeGrant or IpexWorkflow.ApplyOfferAgreeGrantAdmit
                or IpexWorkflow.OfferAgreeGrantAdmit) {

                _logger.LogInformation("IPEX Agree: Disclosee {Disclosee} agreeing with Discloser {Discloser}...", discloseePrefix, discloserPrefix);
                var agreeResult = await _signifyClient.IpexAgreeAndSubmit(new IpexAgreeSubmitArgs(
                    SenderNameOrPrefix: discloseePrefix,
                    RecipientPrefix: discloserPrefix,
                    OfferSaid: offerSaid!
                ));
                if (agreeResult.IsFailed) return FailIpexResponse($"Agree failed: {agreeResult.Errors[0].Message}");
                agreeSaid = agreeResult.Value["agreeSaid"].StringValue!;
                generatedExchangeSaids.Add(agreeSaid);
                lastStepSaid = agreeSaid;
                _logger.LogInformation("Agree submitted: agreeSaid={AgreeSaid}", agreeSaid);
            }

            // Grant step (Discloser sends Grant to Disclosee)
            if (workflow is IpexWorkflow.ApplyOfferAgreeGrant or IpexWorkflow.ApplyOfferAgreeGrantAdmit
                or IpexWorkflow.Grant or IpexWorkflow.GrantAdmit
                or IpexWorkflow.OfferAgreeGrantAdmit) {

                _logger.LogInformation("IPEX Grant: Discloser {Discloser} granting to Disclosee {Disclosee}...", discloserPrefix, discloseePrefix);

                if (payload.IsPresentation) {
                    // Presentation: grant received credential
                    var grantResult = await _signifyClient.GrantReceivedCredential(discloserPrefix, credentialSaid!, discloseePrefix);
                    if (grantResult.IsFailed) return FailIpexResponse($"Grant (presentation) failed: {grantResult.Errors[0].Message}");
                    grantSaid = grantResult.Value["grantSaid"].StringValue!;
                }
                else {
                    // Issuance: grant newly issued credential
                    var grantResult = await GrantCredentialStep(new IpexGrantSubmitArgs(
                        SenderNameOrPrefix: discloserPrefix,
                        RecipientPrefix: discloseePrefix,
                        Acdc: issuedCred!.Acdc,
                        Anc: issuedCred.Anc,
                        Iss: issuedCred.Iss
                    ), "IPEX grant", "ECR credential");
                    if (grantResult.IsFailed) return FailIpexResponse(grantResult.Errors[0].Message);
                    grantSaid = grantResult.Value;
                }
                generatedExchangeSaids.Add(grantSaid!);
                lastStepSaid = grantSaid;
                _logger.LogInformation("Grant submitted: grantSaid={GrantSaid}", grantSaid);
            }

            // Admit step (Disclosee sends Admit to Discloser)
            if (workflow is IpexWorkflow.ApplyOfferAgreeGrantAdmit or IpexWorkflow.GrantAdmit
                or IpexWorkflow.OfferAgreeGrantAdmit) {

                _logger.LogInformation("IPEX Admit: Disclosee {Disclosee} admitting from Discloser {Discloser}...", discloseePrefix, discloserPrefix);
                var admitResult = await AdmitCredentialStep(new IpexAdmitSubmitArgs(
                    SenderNameOrPrefix: discloseePrefix,
                    RecipientPrefix: discloserPrefix,
                    GrantSaid: grantSaid!
                ), "IPEX admit", "ECR credential");
                if (admitResult.IsFailed) return FailIpexResponse(admitResult.Errors[0].Message);
                generatedExchangeSaids.Add(admitResult.Value);
                lastStepSaid = admitResult.Value;
                _logger.LogInformation("Admit submitted: admitSaid={AdmitSaid}", admitResult.Value);
            }

            // Mark generated notifications as read, except the last step's notification
            // (the last step represents a pending action for the recipient to act on).
            // For complete workflows (ending in Admit), mark everything as read.
            var isCompleteWorkflow = workflow is IpexWorkflow.ApplyOfferAgreeGrantAdmit or IpexWorkflow.GrantAdmit
                or IpexWorkflow.OfferAgreeGrantAdmit;
            var saidsToMarkAsRead = isCompleteWorkflow
                ? generatedExchangeSaids
                : new HashSet<string>(generatedExchangeSaids.Where(s => s != lastStepSaid));
            await WaitForNotificationsAndMarkAsReadStep(saidsToMarkAsRead, "IPEX cleanup");

            _logger.LogInformation("PrimeData IPEX completed successfully: workflow={Workflow}", workflow);
            return Result.Ok(new PrimeDataIpexResponse(true));
        }

        private async Task<string?> GetAidName(string prefix) {
            var idsResult = await _signifyClient.GetIdentifiers();
            if (idsResult.IsFailed) return null;
            return idsResult.Value.Aids.FirstOrDefault(a => a.Prefix == prefix)?.Name;
        }

        // ===================== Helper Types =====================

        private record IssuedCredential(RecursiveDictionary Acdc, RecursiveDictionary Anc, RecursiveDictionary Iss, string Said);

        // ===================== Helper Methods =====================

        private static Result<PrimeDataGoResponse> FailResponse(string error) =>
            Result.Ok(new PrimeDataGoResponse(false, Error: error));

        private static Result<PrimeDataIpexResponse> FailIpexResponse(string error) =>
            Result.Ok(new PrimeDataIpexResponse(false, Error: error));

        private async Task<Result<AidWithOobi>> CreateAidStep(string name, string stepLabel) {
            _logger.LogInformation("{Step}: Creating AID '{Name}'...", stepLabel, name);
            var result = await _signifyClient.CreateAidWithEndRole(name);
            if (result.IsFailed) {
                var err = $"Failed to create AID '{name}': {result.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Fail<AidWithOobi>(err);
            }
            _logger.LogInformation("AID created: prefix={Prefix}, oobi={Oobi}", result.Value.Prefix, result.Value.Oobi);
            return result;
        }

        private async Task<Result> ResolveOobiPairsStep((string resolver, string oobi, string alias)[] pairs, string stepLabel) {
            _logger.LogInformation("{Step}: Resolving OOBIs...", stepLabel);
            foreach (var (resolver, oobi, alias) in pairs) {
                _logger.LogInformation("  {Resolver} resolving OOBI for {Alias}...", resolver, alias);
                var result = await _signifyClient.ResolveOobi(oobi, alias);
                if (result.IsFailed) {
                    var err = $"Failed OOBI resolve ({resolver} -> {alias}): {result.Errors[0].Message}";
                    _logger.LogError("{Error}", err);
                    return Result.Fail(err);
                }
                _logger.LogInformation("  OOBI resolved: {Resolver} -> {Alias}", resolver, alias);
            }
            return Result.Ok();
        }

        private async Task<Result> StoreConnectionsStep(
            (string resolver, string oobi, string alias)[] pairs,
            Dictionary<string, string> nameToPrefix,
            string stepLabel) {
            _logger.LogInformation("{Step}: Storing {Count} connections...", stepLabel, pairs.Length);
            var existingResult = await _storageService.GetItem<Connections>();
            var existingItems = existingResult.IsSuccess && existingResult.Value is not null
                ? existingResult.Value.Items
                : new List<Connection>();

            var newConnections = new List<Connection>(existingItems);
            foreach (var (resolver, _, alias) in pairs) {
                newConnections.Add(new Connection {
                    Name = alias,
                    SenderPrefix = nameToPrefix[resolver],
                    ReceiverPrefix = nameToPrefix[alias],
                    ConnectionDate = DateTime.UtcNow
                });
            }

            var setResult = await _storageService.SetItem(new Connections { Items = newConnections });
            if (setResult.IsFailed) {
                var err = $"Failed to store connections: {setResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Fail(err);
            }
            _logger.LogInformation("{Count} connections stored successfully", pairs.Length);
            return Result.Ok();
        }

        private async Task<Result> ChallengeExchangeStep(
            string challengerName, string challengerPrefix,
            string responderName, string responderPrefix,
            string stepGenerate, string stepRespond, string stepVerify) {
            // Generate challenge
            _logger.LogInformation("{Step}: {Challenger} generating challenge for {Responder}...", stepGenerate, challengerName, responderName);
            var challengeResult = await _signifyClient.GenerateChallenge(128);
            if (challengeResult.IsFailed) {
                var err = $"Failed to generate challenge: {challengeResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Fail(err);
            }
            _logger.LogInformation("Challenge generated: {WordCount} words", challengeResult.Value.Words.Count);

            // Respond
            _logger.LogInformation("{Step}: {Responder} responding to {Challenger}'s challenge...", stepRespond, responderName, challengerName);
            var respondResult = await _signifyClient.RespondToChallenge(responderName, challengerPrefix, challengeResult.Value.Words);
            if (respondResult.IsFailed) {
                var err = $"Failed {responderName} respond to {challengerName} challenge: {respondResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Fail(err);
            }
            _logger.LogInformation("{Responder} responded to {Challenger}'s challenge", responderName, challengerName);

            // Verify + wait for operation + mark responded
            _logger.LogInformation("{Step}: {Challenger} verifying {Responder}'s challenge response...", stepVerify, challengerName, responderName);
            var verifyOp = await _signifyClient.VerifyChallenge(responderPrefix, challengeResult.Value.Words);
            if (verifyOp.IsFailed) {
                var err = $"Failed {challengerName} verify {responderName} response: {verifyOp.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Fail(err);
            }
            var completedOp = await _signifyClient.WaitForOperation(verifyOp.Value);
            if (completedOp.IsFailed) {
                var err = $"Failed waiting for verify operation: {completedOp.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Fail(err);
            }
            var responseEl = (JsonElement)completedOp.Value.Response!;
            var said = responseEl.GetProperty("exn").GetProperty("d").GetString()!;
            var respondedResult = await _signifyClient.ChallengeResponded(responderPrefix, said);
            if (respondedResult.IsFailed) {
                var err = $"Failed {challengerName} mark {responderName} challenge responded: {respondedResult.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Fail(err);
            }
            _logger.LogInformation("{Challenger} verified {Responder}'s challenge response (SAID={Said})", challengerName, responderName, said);
            return Result.Ok();
        }

        private async Task<Result<RegistryCheckResult>> CreateRegistryStep(string aidName, string registryName, string stepLabel) {
            _logger.LogInformation("{Step}: Creating credential registry '{Name}' for '{AidName}'...", stepLabel, registryName, aidName);
            var result = await _signifyClient.CreateRegistryIfNotExists(aidName, registryName);
            if (result.IsFailed) {
                var err = $"Failed to create registry '{registryName}': {result.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Fail<RegistryCheckResult>(err);
            }
            _logger.LogInformation("Registry: regk={Regk}, created={Created}", result.Value.Regk, result.Value.Created);
            return result;
        }

        private async Task<Result<IssuedCredential>> IssueCredentialStep(IssueAndGetCredentialArgs args, string stepLabel, string credLabel) {
            _logger.LogInformation("{Step}: Issuing {CredLabel}...", stepLabel, credLabel);
            var result = await _signifyClient.IssueAndGetCredential(args);
            if (result.IsFailed) {
                var err = $"Failed to issue {credLabel}: {result.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Fail<IssuedCredential>(err);
            }
            var issued = new IssuedCredential(
                result.Value["acdc"].Dictionary!,
                result.Value["anc"].Dictionary!,
                result.Value["iss"].Dictionary!,
                result.Value["said"].StringValue!
            );
            _logger.LogInformation("{CredLabel} issued: said={Said}", credLabel, issued.Said);
            return Result.Ok(issued);
        }

        private async Task<Result<string>> GrantCredentialStep(IpexGrantSubmitArgs args, string stepLabel, string credLabel) {
            _logger.LogInformation("{Step}: Granting {CredLabel} via IPEX...", stepLabel, credLabel);
            var result = await _signifyClient.IpexGrantAndSubmit(args);
            if (result.IsFailed) {
                var err = $"Failed to grant {credLabel}: {result.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Fail<string>(err);
            }
            var grantSaid = result.Value["grantSaid"].StringValue!;
            _logger.LogInformation("{CredLabel} granted: grantSaid={GrantSaid}", credLabel, grantSaid);
            return Result.Ok(grantSaid);
        }

        private async Task<Result<string>> AdmitCredentialStep(IpexAdmitSubmitArgs args, string stepLabel, string credLabel) {
            _logger.LogInformation("{Step}: Admitting {CredLabel}...", stepLabel, credLabel);
            var result = await _signifyClient.IpexAdmitAndSubmit(args);
            if (result.IsFailed) {
                var err = $"Failed to admit {credLabel}: {result.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Fail<string>(err);
            }
            var admitSaid = result.Value["admitSaid"].StringValue!;
            _logger.LogInformation("{CredLabel} admitted: admitSaid={AdmitSaid}", credLabel, admitSaid);
            return Result.Ok(admitSaid);
        }

        private async Task<Result<string>> PresentCredentialStep(string senderNameOrPrefix, string credSaid, string recipientPrefix, string stepLabel, string credLabel) {
            _logger.LogInformation("{Step}: {Sender} presenting {CredLabel}...", stepLabel, senderNameOrPrefix, credLabel);
            var result = await _signifyClient.GrantReceivedCredential(senderNameOrPrefix, credSaid, recipientPrefix);
            if (result.IsFailed) {
                var err = $"Failed to present {credLabel}: {result.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Fail<string>(err);
            }
            var grantSaid = result.Value["grantSaid"].StringValue!;
            _logger.LogInformation("{CredLabel} presented: grantSaid={GrantSaid}", credLabel, grantSaid);
            return Result.Ok(grantSaid);
        }

        private async Task WaitForNotificationsAndMarkAsReadStep(HashSet<string> expectedExchangeSaids, string stepLabel) {
            _logger.LogInformation("{Step}: Waiting for {Count} notifications to propagate...",
                stepLabel, expectedExchangeSaids.Count);

            var timeout = TimeSpan.FromSeconds(30);
            var interval = TimeSpan.FromSeconds(1);
            var deadline = DateTime.UtcNow + timeout;
            var foundSaids = new HashSet<string>();

            while (DateTime.UtcNow < deadline) {
                var notifsResult = await _signifyClient.ListNotifications();
                if (notifsResult.IsSuccess) {
                    foreach (var n in notifsResult.Value) {
                        var exchangeSaid = n.GetValueByPath("a.d")?.Value?.ToString();
                        if (exchangeSaid is not null && expectedExchangeSaids.Contains(exchangeSaid)) {
                            foundSaids.Add(exchangeSaid);
                        }
                    }
                    if (foundSaids.Count >= expectedExchangeSaids.Count) break;
                }
                await Task.Delay(interval);
            }

            _logger.LogInformation("{Step}: Found {Found}/{Expected} notifications",
                stepLabel, foundSaids.Count, expectedExchangeSaids.Count);

            await MarkGeneratedNotificationsAsReadStep(expectedExchangeSaids, stepLabel);
        }

        private async Task MarkGeneratedNotificationsAsReadStep(HashSet<string> exchangeSaids, string stepLabel) {
            _logger.LogInformation("{Step}: Marking generated notifications as read ({Count} exchange SAIDs tracked)...", stepLabel, exchangeSaids.Count);
            try {
                var notifsResult = await _signifyClient.ListNotifications();
                if (notifsResult.IsFailed) {
                    _logger.LogWarning("Failed to list notifications for cleanup: {Error}", notifsResult.Errors[0].Message);
                    return;
                }
                var marked = 0;
                foreach (var n in notifsResult.Value) {
                    var notifId = n.GetValueByPath("i")?.Value?.ToString();
                    var exchangeSaid = n.GetValueByPath("a.d")?.Value?.ToString();
                    var isRead = n.GetValueByPath("r")?.Value is true;
                    if (notifId is not null && exchangeSaid is not null && !isRead && exchangeSaids.Contains(exchangeSaid)) {
                        var markResult = await _signifyClient.MarkNotification(notifId);
                        if (markResult.IsSuccess) marked++;
                        else _logger.LogWarning("Failed to mark notification {Id} as read", notifId);
                    }
                }
                _logger.LogInformation("{Marked} notifications marked as read", marked);
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "Error during notification cleanup");
            }
        }
    }
}
