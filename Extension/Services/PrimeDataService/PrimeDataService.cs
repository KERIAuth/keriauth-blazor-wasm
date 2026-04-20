using System.Text.Json;
using Extension.Helper;
using Extension.Models;
using Extension.Models.Messages.AppBw;
using Extension.Models.Storage;
using Extension.Services.SignifyService;
using Extension.Services.SignifyService.Models;
using Extension.Services.Storage;
using FluentResults;

namespace Extension.Services.PrimeDataService {
    // TODO P2 move PrimeDataService so it is not a direct dependency of App program.cs;
    // rather, interactions should be via AppBw messages
    public class PrimeDataService : IPrimeDataService {
        private readonly ISignifyClientService _signifyClient;
        private readonly IStorageGateway _storageGateway;
        private readonly ISchemaService _schemaService;
        private readonly ILogger<PrimeDataService> _logger;
        private DateTime _lastProgressWriteUtc = DateTime.MinValue;
        private const int ProgressThrottleMs = 500;
        private PrimeDataOperation _currentOperation;

        public PrimeDataService(ISignifyClientService signifyClient, IStorageGateway storageGateway, ISchemaService schemaService, ILogger<PrimeDataService> logger) {
            _signifyClient = signifyClient;
            _storageGateway = storageGateway;
            _schemaService = schemaService;
            _logger = logger;
        }

        private async Task ReportProgress(int step, int totalSteps, string description) {
            _logger.LogInformation("Progress: Step {Step} of {Total}: {Description}", step, totalSteps, description);
            var now = DateTime.UtcNow;
            if ((now - _lastProgressWriteUtc).TotalMilliseconds < ProgressThrottleMs) return;
            _lastProgressWriteUtc = now;
            await _storageGateway.SetItem(new PrimeDataProgress {
                Operation = _currentOperation,
                Step = step,
                TotalSteps = totalSteps,
                Description = description
            }, StorageArea.Session);
        }

        private async Task ReportComplete() {
            await _storageGateway.SetItem(new PrimeDataProgress { Operation = _currentOperation, IsComplete = true }, StorageArea.Session);
            // Yield so the BW's background polling loop and any queued broker work can run before
            // this workflow task fully completes — without this, the post-workflow notification
            // cache can miss the /offer /agree /grant /admit notifications the workflow generated.
            await Task.Yield();
        }

        private async Task ReportError(string description) {
            await _storageGateway.SetItem(new PrimeDataProgress { Operation = _currentOperation, IsError = true, Description = description }, StorageArea.Session);
        }

        public async Task<Result<PrimeDataGoResponse>> GoAsync(PrimeDataGoPayload? payload = null) {
            _currentOperation = PrimeDataOperation.Go;
            var prepend = payload?.Prepend ?? string.Empty;
            _logger.LogInformation("PrimeData Go starting with prepend '{Prepend}'", prepend);

            const int goTotalSteps = 39;
            var generatedExchangeSaids = new HashSet<string>();

            // Steps 1-4: Create AIDs
            var nameToPrefix = new Dictionary<string, string>();

            await ReportProgress(1, goTotalSteps, "Creating GEDA profile");
            var gedaName = $"{prepend}geda";
            var gedaResult = await CreateAidStep(gedaName, "Step 1");
            if (gedaResult.IsFailed) return await FailResponseWithProgress(gedaResult.Errors[0].Message);
            nameToPrefix[gedaName] = gedaResult.Value.Prefix;

            await ReportProgress(2, goTotalSteps, "Creating QVI profile");
            var qviName = $"{prepend}qvi";
            var qviResult = await CreateAidStep(qviName, "Step 2");
            if (qviResult.IsFailed) return await FailResponseWithProgress(qviResult.Errors[0].Message);
            nameToPrefix[qviName] = qviResult.Value.Prefix;

            await ReportProgress(3, goTotalSteps, "Creating LE profile");
            var leName = $"{prepend}le";
            var leResult = await CreateAidStep(leName, "Step 3");
            if (leResult.IsFailed) return await FailResponseWithProgress(leResult.Errors[0].Message);
            nameToPrefix[leName] = leResult.Value.Prefix;

            await ReportProgress(4, goTotalSteps, "Creating Person profile");
            var personName = $"{prepend}person";
            var personResult = await CreateAidStep(personName, "Step 4");
            if (personResult.IsFailed) return await FailResponseWithProgress(personResult.Errors[0].Message);
            nameToPrefix[personName] = personResult.Value.Prefix;

            await RefreshCachedIdentifiersAsync();

            // Step 5: Resolve OOBIs between roles
            await ReportProgress(5, goTotalSteps, "Resolving OOBIs between roles");
            var step5Pairs = new[] {
                (gedaName, qviResult.Value.Oobi, qviName),
                (qviName, gedaResult.Value.Oobi, gedaName),
                (leName, qviResult.Value.Oobi, qviName),
                (qviName, leResult.Value.Oobi, leName),
            };
            var step5 = await ResolveOobiPairsStep(step5Pairs, "Step 5");
            if (step5.IsFailed) return await FailResponseWithProgress(step5.Errors[0].Message);
            var step5Store = await StoreConnectionsStep(step5Pairs, nameToPrefix, "Step 5");
            if (step5Store.IsFailed) return await FailResponseWithProgress(step5Store.Errors[0].Message);

            // Steps 6-8: GEDA challenges QVI
            await ReportProgress(6, goTotalSteps, "GEDA challenging QVI");
            var step6 = await ChallengeExchangeStep(
                gedaName, gedaResult.Value.Prefix,
                qviName, qviResult.Value.Prefix,
                "Step 6", "Step 7", "Step 8");
            if (step6.IsFailed) return await FailResponseWithProgress(step6.Errors[0].Message);

            // Steps 9-11: QVI challenges GEDA
            await ReportProgress(7, goTotalSteps, "QVI challenging GEDA");
            var step9 = await ChallengeExchangeStep(
                qviName, qviResult.Value.Prefix,
                gedaName, gedaResult.Value.Prefix,
                "Step 9", "Step 10", "Step 11");
            if (step9.IsFailed) return await FailResponseWithProgress(step9.Errors[0].Message);

            // Step 12: GEDA creates credential registry
            await ReportProgress(8, goTotalSteps, "Creating GEDA credential registry");
            var gedaRegistryName = $"{prepend}geda_registry";
            var registryResult = await CreateRegistryStep(gedaName, gedaRegistryName, "Step 12");
            if (registryResult.IsFailed) return await FailResponseWithProgress(registryResult.Errors[0].Message);

            // Step 12b: Resolve credential schema OOBIs
            await ReportProgress(9, goTotalSteps, "Resolving credential schema OOBIs");
            var schemaOobis = new[] {
                QviSchemaSaid, LeSchemaSaid, OorAuthSchemaSaid, OorSchemaSaid, EcrAuthSchemaSaid, EcrSchemaSaid, SediSchemaSaid
            };
            foreach (var said in schemaOobis) {
                var schemaResolveResult = await ResolveSchemaOobiWithFallbackAsync(said);
                if (schemaResolveResult.IsFailed) {
                    return await FailResponseWithProgress(schemaResolveResult.Errors[0].Message);
                }
            }

            // Step 13a: GEDA issues QVI credential
            await ReportProgress(10, goTotalSteps, "Issuing QVI credential");
            var qviCredData = new RecursiveDictionary();
            qviCredData["LEI"] = new RecursiveValue { StringValue = "5493001KJTIIGC8Y1R17" };
            var qviCredIssued = await IssueCredentialStep(new IssueAndGetCredentialArgs(
                IssuerAidNameOrPrefix: gedaName,
                RegistryName: gedaRegistryName,
                Schema: QviSchemaSaid,
                HolderPrefix: qviResult.Value.Prefix,
                CredData: qviCredData
            ), "Step 13a", "QVI credential");
            if (qviCredIssued.IsFailed) return await FailResponseWithProgress(qviCredIssued.Errors[0].Message);

            // Step 13b: GEDA grants QVI credential to QVI via IPEX
            await ReportProgress(11, goTotalSteps, "Granting QVI credential");
            var qviGrantSaid = await GrantStep(new IpexGrantSubmitArgs(
                SenderNameOrPrefix: gedaName,
                RecipientPrefix: qviResult.Value.Prefix,
                Acdc: qviCredIssued.Value.Acdc,
                Anc: qviCredIssued.Value.Anc,
                Iss: qviCredIssued.Value.Iss
            ), "Step 13b");
            if (qviGrantSaid.IsFailed) return await FailResponseWithProgress(qviGrantSaid.Errors[0].Message);
            generatedExchangeSaids.Add(qviGrantSaid.Value);
            await WaitForNotificationsAndMarkAsReadStep(new HashSet<string> { qviGrantSaid.Value }, "Step 13b propagation");

            // Step 14: QVI admits QVI credential
            await ReportProgress(12, goTotalSteps, "Admitting QVI credential");
            var step14 = await AdmitStep(new IpexAdmitSubmitArgs(
                SenderNameOrPrefix: qviName,
                RecipientPrefix: gedaResult.Value.Prefix,
                GrantSaid: qviGrantSaid.Value
            ), "Step 14");
            if (step14.IsFailed) return await FailResponseWithProgress(step14.Errors[0].Message);
            generatedExchangeSaids.Add(step14.Value);

            // Step 15a: Create Verifier AID
            await ReportProgress(13, goTotalSteps, "Creating Verifier profile");
            var verifierName = $"{prepend}verifier";
            var verifierResult = await CreateAidStep(verifierName, "Step 15a");
            if (verifierResult.IsFailed) return await FailResponseWithProgress(verifierResult.Errors[0].Message);
            nameToPrefix[verifierName] = verifierResult.Value.Prefix;
            await RefreshCachedIdentifiersAsync();

            // Step 15b: Resolve OOBIs between QVI and Verifier
            await ReportProgress(14, goTotalSteps, "Resolving OOBIs for Verifier");
            var step15bPairs = new[] {
                (qviName, verifierResult.Value.Oobi, verifierName),
                (verifierName, qviResult.Value.Oobi, qviName),
            };
            var step15b = await ResolveOobiPairsStep(step15bPairs, "Step 15b");
            if (step15b.IsFailed) return await FailResponseWithProgress(step15b.Errors[0].Message);
            var step15bStore = await StoreConnectionsStep(step15bPairs, nameToPrefix, "Step 15b");
            if (step15bStore.IsFailed) return await FailResponseWithProgress(step15bStore.Errors[0].Message);

            // Step 16a: Verifier requests QVI credential via IPEX apply
            await ReportProgress(15, goTotalSteps, "Requesting QVI credential");
            var applyStepResult = await ApplyStep(new IpexApplySubmitArgs(
                SenderNameOrPrefix: verifierName,
                RecipientPrefix: qviResult.Value.Prefix,
                SchemaSaid: QviSchemaSaid
            ), "Step 16a");
            if (applyStepResult.IsFailed) return await FailResponseWithProgress(applyStepResult.Errors[0].Message);
            var applySaid = applyStepResult.Value;
            generatedExchangeSaids.Add(applySaid);
            await WaitForNotificationsAndMarkAsReadStep(new HashSet<string> { applySaid }, "Step 16a propagation");

            // Step 16b: QVI offers credential to Verifier
            await ReportProgress(16, goTotalSteps, "Offering QVI credential to Verifier");
            var offerStepResult = await OfferStep(new IpexOfferSubmitArgs(
                SenderNameOrPrefix: qviName,
                RecipientPrefix: verifierResult.Value.Prefix,
                CredentialSaid: qviCredIssued.Value.Said,
                ApplySaid: applySaid
            ), "Step 16b");
            if (offerStepResult.IsFailed) return await FailResponseWithProgress(offerStepResult.Errors[0].Message);
            var offerSaid = offerStepResult.Value;
            generatedExchangeSaids.Add(offerSaid);
            await WaitForNotificationsAndMarkAsReadStep(new HashSet<string> { offerSaid }, "Step 16b propagation");

            // Step 16c: Verifier agrees to credential offer
            await ReportProgress(17, goTotalSteps, "Agreeing to QVI credential offer");
            var agreeStepResult = await AgreeStep(new IpexAgreeSubmitArgs(
                SenderNameOrPrefix: verifierName,
                RecipientPrefix: qviResult.Value.Prefix,
                OfferSaid: offerSaid
            ), "Step 16c");
            if (agreeStepResult.IsFailed) return await FailResponseWithProgress(agreeStepResult.Errors[0].Message);
            var agreeSaid = agreeStepResult.Value;
            generatedExchangeSaids.Add(agreeSaid);

            // Step 17: QVI creates credential registry for LE credentials
            await ReportProgress(18, goTotalSteps, "Creating QVI credential registry");
            var qviRegistryName = $"{prepend}qvi_le_registry";
            var qviRegistryResult = await CreateRegistryStep(qviName, qviRegistryName, "Step 17");
            if (qviRegistryResult.IsFailed) return await FailResponseWithProgress(qviRegistryResult.Errors[0].Message);

            // Step 18a: QVI issues LE credential
            await ReportProgress(19, goTotalSteps, "Issuing LE credential");
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
            if (leCredIssued.IsFailed) return await FailResponseWithProgress(leCredIssued.Errors[0].Message);

            // Step 18b: QVI grants LE credential to LE via IPEX
            await ReportProgress(20, goTotalSteps, "Granting LE credential");
            var leGrantSaid = await GrantStep(new IpexGrantSubmitArgs(
                SenderNameOrPrefix: qviName,
                RecipientPrefix: leResult.Value.Prefix,
                Acdc: leCredIssued.Value.Acdc,
                Anc: leCredIssued.Value.Anc,
                Iss: leCredIssued.Value.Iss
            ), "Step 18b");
            if (leGrantSaid.IsFailed) return await FailResponseWithProgress(leGrantSaid.Errors[0].Message);
            generatedExchangeSaids.Add(leGrantSaid.Value);
            await WaitForNotificationsAndMarkAsReadStep(new HashSet<string> { leGrantSaid.Value }, "Step 18b propagation");

            // Step 19: LE admits LE credential
            await ReportProgress(21, goTotalSteps, "Admitting LE credential");
            var step19 = await AdmitStep(new IpexAdmitSubmitArgs(
                SenderNameOrPrefix: leName,
                RecipientPrefix: qviResult.Value.Prefix,
                GrantSaid: leGrantSaid.Value
            ), "Step 19");
            if (step19.IsFailed) return await FailResponseWithProgress(step19.Errors[0].Message);
            generatedExchangeSaids.Add(step19.Value);

            // Step 20: Resolve OOBIs between LE and Verifier
            await ReportProgress(22, goTotalSteps, "Resolving OOBIs for LE and Verifier");
            var step20Pairs = new[] {
                (leName, verifierResult.Value.Oobi, verifierName),
                (verifierName, leResult.Value.Oobi, leName),
            };
            var step20 = await ResolveOobiPairsStep(step20Pairs, "Step 20");
            if (step20.IsFailed) return await FailResponseWithProgress(step20.Errors[0].Message);
            var step20Store = await StoreConnectionsStep(step20Pairs, nameToPrefix, "Step 20");
            if (step20Store.IsFailed) return await FailResponseWithProgress(step20Store.Errors[0].Message);

            // Step 21: LE presents LE credential to Verifier
            await ReportProgress(23, goTotalSteps, "Presenting LE credential");
            var step21 = await PresentStep(leName, leCredIssued.Value.Said, verifierResult.Value.Prefix, "Step 21");
            if (step21.IsFailed) return await FailResponseWithProgress(step21.Errors[0].Message);
            generatedExchangeSaids.Add(step21.Value);

            // Step 22: Resolve OOBIs for Person
            await ReportProgress(24, goTotalSteps, "Resolving OOBIs for Person");
            var step22Pairs = new[] {
                (personName, leResult.Value.Oobi, leName),
                (leName, personResult.Value.Oobi, personName),
                (qviName, personResult.Value.Oobi, personName),
                (personName, qviResult.Value.Oobi, qviName),
                (personName, verifierResult.Value.Oobi, verifierName),
                (verifierName, personResult.Value.Oobi, personName),
            };
            var step22 = await ResolveOobiPairsStep(step22Pairs, "Step 22");
            if (step22.IsFailed) return await FailResponseWithProgress(step22.Errors[0].Message);
            var step22Store = await StoreConnectionsStep(step22Pairs, nameToPrefix, "Step 22");
            if (step22Store.IsFailed) return await FailResponseWithProgress(step22Store.Errors[0].Message);

            // Step 23: LE creates credential registry
            await ReportProgress(25, goTotalSteps, "Creating LE OOR registry");
            var leRegistryName = $"{prepend}le_oor_registry";
            var leRegistryResult = await CreateRegistryStep(leName, leRegistryName, "Step 23");
            if (leRegistryResult.IsFailed) return await FailResponseWithProgress(leRegistryResult.Errors[0].Message);

            // Step 24a: LE issues OOR Auth credential to QVI
            await ReportProgress(26, goTotalSteps, "Issuing OOR Auth credential");
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
            if (oorAuthIssued.IsFailed) return await FailResponseWithProgress(oorAuthIssued.Errors[0].Message);

            // Step 24b: LE grants OOR Auth to QVI via IPEX
            await ReportProgress(27, goTotalSteps, "Granting OOR Auth credential");
            var oorAuthGrantSaid = await GrantStep(new IpexGrantSubmitArgs(
                SenderNameOrPrefix: leName,
                RecipientPrefix: qviResult.Value.Prefix,
                Acdc: oorAuthIssued.Value.Acdc,
                Anc: oorAuthIssued.Value.Anc,
                Iss: oorAuthIssued.Value.Iss
            ), "Step 24b");
            if (oorAuthGrantSaid.IsFailed) return await FailResponseWithProgress(oorAuthGrantSaid.Errors[0].Message);
            generatedExchangeSaids.Add(oorAuthGrantSaid.Value);
            await WaitForNotificationsAndMarkAsReadStep(new HashSet<string> { oorAuthGrantSaid.Value }, "Step 24b propagation");

            // Step 25: QVI admits OOR Auth credential
            await ReportProgress(28, goTotalSteps, "Admitting OOR Auth credential");
            var step25 = await AdmitStep(new IpexAdmitSubmitArgs(
                SenderNameOrPrefix: qviName,
                RecipientPrefix: leResult.Value.Prefix,
                GrantSaid: oorAuthGrantSaid.Value
            ), "Step 25");
            if (step25.IsFailed) return await FailResponseWithProgress(step25.Errors[0].Message);
            generatedExchangeSaids.Add(step25.Value);

            // Step 26a: QVI issues OOR credential to Person
            await ReportProgress(29, goTotalSteps, "Issuing OOR credential");
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
            if (oorIssued.IsFailed) return await FailResponseWithProgress(oorIssued.Errors[0].Message);

            // Step 26b: QVI grants OOR credential to Person via IPEX
            await ReportProgress(30, goTotalSteps, "Granting OOR credential");
            var oorGrantSaid = await GrantStep(new IpexGrantSubmitArgs(
                SenderNameOrPrefix: qviName,
                RecipientPrefix: personResult.Value.Prefix,
                Acdc: oorIssued.Value.Acdc,
                Anc: oorIssued.Value.Anc,
                Iss: oorIssued.Value.Iss
            ), "Step 26b");
            if (oorGrantSaid.IsFailed) return await FailResponseWithProgress(oorGrantSaid.Errors[0].Message);
            generatedExchangeSaids.Add(oorGrantSaid.Value);
            await WaitForNotificationsAndMarkAsReadStep(new HashSet<string> { oorGrantSaid.Value }, "Step 26b propagation");

            // Step 27: Person admits OOR credential
            await ReportProgress(31, goTotalSteps, "Admitting OOR credential");
            var step27 = await AdmitStep(new IpexAdmitSubmitArgs(
                SenderNameOrPrefix: personName,
                RecipientPrefix: qviResult.Value.Prefix,
                GrantSaid: oorGrantSaid.Value
            ), "Step 27");
            if (step27.IsFailed) return await FailResponseWithProgress(step27.Errors[0].Message);
            generatedExchangeSaids.Add(step27.Value);

            // Step 28: Person presents OOR credential to Verifier
            await ReportProgress(32, goTotalSteps, "Presenting OOR credential");
            var step28 = await PresentStep(personName, oorIssued.Value.Said, verifierResult.Value.Prefix, "Step 28");
            if (step28.IsFailed) return await FailResponseWithProgress(step28.Errors[0].Message);
            generatedExchangeSaids.Add(step28.Value);

            // Step 29a: LE issues ECR Auth credential to QVI
            await ReportProgress(33, goTotalSteps, "Issuing ECR Auth credential");
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
            if (ecrAuthIssued.IsFailed) return await FailResponseWithProgress(ecrAuthIssued.Errors[0].Message);

            // Step 29b: LE grants ECR Auth to QVI via IPEX
            await ReportProgress(34, goTotalSteps, "Granting ECR Auth credential");
            var ecrAuthGrantSaid = await GrantStep(new IpexGrantSubmitArgs(
                SenderNameOrPrefix: leName,
                RecipientPrefix: qviResult.Value.Prefix,
                Acdc: ecrAuthIssued.Value.Acdc,
                Anc: ecrAuthIssued.Value.Anc,
                Iss: ecrAuthIssued.Value.Iss
            ), "Step 29b");
            if (ecrAuthGrantSaid.IsFailed) return await FailResponseWithProgress(ecrAuthGrantSaid.Errors[0].Message);
            generatedExchangeSaids.Add(ecrAuthGrantSaid.Value);
            await WaitForNotificationsAndMarkAsReadStep(new HashSet<string> { ecrAuthGrantSaid.Value }, "Step 29b propagation");

            // Step 30: QVI admits ECR Auth credential
            await ReportProgress(35, goTotalSteps, "Admitting ECR Auth credential");
            var step30 = await AdmitStep(new IpexAdmitSubmitArgs(
                SenderNameOrPrefix: qviName,
                RecipientPrefix: leResult.Value.Prefix,
                GrantSaid: ecrAuthGrantSaid.Value
            ), "Step 30");
            if (step30.IsFailed) return await FailResponseWithProgress(step30.Errors[0].Message);
            generatedExchangeSaids.Add(step30.Value);

            // Step 31a: QVI issues ECR credential to Person (private)
            await ReportProgress(36, goTotalSteps, "Issuing ECR credential");
            var ecrCredData = VleiCredentialHelper.BuildEcrCredentialData("SEDI Issuance Approver");
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
            if (ecrIssued.IsFailed) return await FailResponseWithProgress(ecrIssued.Errors[0].Message);

            // Step 31b: QVI grants ECR credential to Person via IPEX
            await ReportProgress(37, goTotalSteps, "Granting ECR credential");
            var ecrGrantSaid = await GrantStep(new IpexGrantSubmitArgs(
                SenderNameOrPrefix: qviName,
                RecipientPrefix: personResult.Value.Prefix,
                Acdc: ecrIssued.Value.Acdc,
                Anc: ecrIssued.Value.Anc,
                Iss: ecrIssued.Value.Iss
            ), "Step 31b");
            if (ecrGrantSaid.IsFailed) return await FailResponseWithProgress(ecrGrantSaid.Errors[0].Message);
            generatedExchangeSaids.Add(ecrGrantSaid.Value);
            await WaitForNotificationsAndMarkAsReadStep(new HashSet<string> { ecrGrantSaid.Value }, "Step 31b propagation");

            // Step 32: Person admits ECR credential
            await ReportProgress(38, goTotalSteps, "Admitting ECR credential");
            var step32 = await AdmitStep(new IpexAdmitSubmitArgs(
                SenderNameOrPrefix: personName,
                RecipientPrefix: qviResult.Value.Prefix,
                GrantSaid: ecrGrantSaid.Value
            ), "Step 32");
            if (step32.IsFailed) return await FailResponseWithProgress(step32.Errors[0].Message);
            generatedExchangeSaids.Add(step32.Value);

            // Step 33: Wait for KERIA to propagate notifications, then mark as read
            await ReportProgress(39, goTotalSteps, "Finalizing notifications");
            await WaitForNotificationsAndMarkAsReadStep(generatedExchangeSaids, "Step 33");

            _logger.LogInformation("PrimeData Go completed successfully");
            await ReportComplete();
            return Result.Ok(new PrimeDataGoResponse(true));
        }

        private const string QviSchemaSaid = VleiCredentialHelper.QviSchemaSaid;
        private const string LeSchemaSaid = VleiCredentialHelper.LeSchemaSaid;
        private const string OorAuthSchemaSaid = VleiCredentialHelper.OorAuthSchemaSaid;
        private const string OorSchemaSaid = VleiCredentialHelper.OorSchemaSaid;
        private const string EcrAuthSchemaSaid = VleiCredentialHelper.EcrAuthSchemaSaid;
        private const string EcrSchemaSaid = VleiCredentialHelper.EcrSchemaSaid;
        private const string SediSchemaSaid = SediCredentialHelper.SediSchemaSaid;

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
            _currentOperation = PrimeDataOperation.Ipex;
            _logger.LogInformation("PrimeData IPEX starting: workflow={Workflow}, isPresentation={IsPresentation}, discloser={Discloser}, disclosee={Disclosee}, role={Role}",
                payload.Workflow, payload.IsPresentation, payload.DiscloserPrefix, payload.DiscloseePrefix, payload.EcrRole);

            var ipexTotalSteps = ComputeIpexStepCount(payload.Workflow);
            var ipexStep = 0;

            var generatedExchangeSaids = new HashSet<string>();

            // Resolve ECR schema OOBI
            await ReportProgress(++ipexStep, ipexTotalSteps, "Resolving schema OOBIs");
            var schemaResolve = await ResolveSchemaOobiWithFallbackAsync(EcrSchemaSaid);
            if (schemaResolve.IsFailed) {
                return await FailIpexResponseWithProgress(schemaResolve.Errors[0].Message);
            }

            var discloserPrefix = payload.DiscloserPrefix;
            var discloseePrefix = payload.DiscloseePrefix;
            var workflow = payload.Workflow;

            // Determine credential SAID for presentation or issue credential for issuance
            string? credentialSaid = null;
            IssuedCredential? issuedCred = null;

            var workflowIncludesOfferOrGrant = workflow is not IpexWorkflow.Apply;

            if (workflowIncludesOfferOrGrant) {
                await ReportProgress(++ipexStep, ipexTotalSteps, "Preparing credential");
                if (payload.IsPresentation) {
                    // Presentation: find existing ECR credential held by Discloser
                    var credsResult = await _signifyClient.GetCredentials();
                    if (credsResult.IsFailed) return await FailIpexResponseWithProgress($"Failed to get credentials: {credsResult.Errors[0].Message}");

                    var ecrCred = credsResult.Value.FirstOrDefault(c =>
                        c.GetValueByPath("sad.s")?.Value?.ToString() == EcrSchemaSaid &&
                        c.GetValueByPath("sad.a.i")?.Value?.ToString() == discloserPrefix);

                    if (ecrCred is null) {
                        return await FailIpexResponseWithProgress($"Discloser {discloserPrefix} does not hold an ECR credential for presentation");
                    }
                    credentialSaid = ecrCred.GetValueByPath("sad.d")?.Value?.ToString();
                    _logger.LogInformation("Found ECR credential for presentation: said={Said}", credentialSaid);
                }
                else {
                    // Issuance: validate Discloser has ECR Auth, then issue ECR credential
                    var credsResult = await _signifyClient.GetCredentials();
                    if (credsResult.IsFailed) return await FailIpexResponseWithProgress($"Failed to get credentials: {credsResult.Errors[0].Message}");

                    var ecrAuthCred = VleiCredentialHelper.FindEcrAuthCredential(credsResult.Value, discloserPrefix);

                    if (ecrAuthCred is null) {
                        return await FailIpexResponseWithProgress($"Discloser {discloserPrefix} does not hold an ECR Auth credential for issuance");
                    }
                    var ecrAuthSaid = ecrAuthCred.GetValueByPath("sad.d")?.Value?.ToString()!;
                    _logger.LogInformation("Found ECR Auth credential: said={Said}", ecrAuthSaid);

                    // Auto-create registry for Discloser (Issuer)
                    var discloserName = await GetAidName(discloserPrefix);
                    if (discloserName is null) return await FailIpexResponseWithProgress($"Could not resolve AID name for prefix {discloserPrefix}");

                    var registryName = $"{discloserName}_ecr_ipex_registry";
                    var registryResult = await CreateRegistryStep(discloserName, registryName, "IPEX registry");
                    if (registryResult.IsFailed) return await FailIpexResponseWithProgress(registryResult.Errors[0].Message);

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

                    if (issuedCred is null) return await FailIpexResponseWithProgress("Failed to issue ECR credential");

                    credentialSaid = issuedCred.Said;
                    _logger.LogInformation("ECR credential issued: said={Said}", credentialSaid);
                }
            }

            // Execute IPEX workflow steps with notification propagation waits between each step.
            // After each intermediate step, wait for KERIA to generate the notification and mark it as read.
            // The last step's notification remains unread (pending action for the recipient).
            string? applySaid = null;
            string? offerSaid = null;
            string? agreeSaid = null;
            string? grantSaid = null;
            string? lastStepSaid = null;

            // Apply step (Disclosee sends Apply to Discloser)
            if (workflow is IpexWorkflow.Apply or IpexWorkflow.ApplyOffer or IpexWorkflow.ApplyOfferAgree
                or IpexWorkflow.ApplyOfferAgreeGrant or IpexWorkflow.ApplyOfferAgreeGrantAdmit
                or IpexWorkflow.ApplyGrant or IpexWorkflow.ApplyGrantAdmit) {

                await ReportProgress(++ipexStep, ipexTotalSteps, "Applying for credential");
                var attributes = new RecursiveDictionary();
                attributes["engagementContextRole"] = new RecursiveValue { StringValue = payload.EcrRole };

                var applyResult = await ApplyStep(new IpexApplySubmitArgs(
                    SenderNameOrPrefix: discloseePrefix,
                    RecipientPrefix: discloserPrefix,
                    SchemaSaid: EcrSchemaSaid,
                    Attributes: attributes
                ), "IPEX apply");
                if (applyResult.IsFailed) return await FailIpexResponseWithProgress(applyResult.Errors[0].Message);
                applySaid = applyResult.Value;
                generatedExchangeSaids.Add(applySaid);
                lastStepSaid = applySaid;

                // Wait for Apply notification before proceeding to Offer
                if (workflow is not IpexWorkflow.Apply) {
                    await WaitForNotificationsAndMarkAsReadStep(
                        new HashSet<string> { applySaid }, "Apply propagation");
                }
            }

            // Offer step (Discloser sends Offer to Disclosee)
            if (workflow is IpexWorkflow.Offer or IpexWorkflow.ApplyOffer or IpexWorkflow.ApplyOfferAgree
                or IpexWorkflow.ApplyOfferAgreeGrant or IpexWorkflow.ApplyOfferAgreeGrantAdmit
                or IpexWorkflow.OfferAgreeGrantAdmit) {

                await ReportProgress(++ipexStep, ipexTotalSteps, "Offering credential");
                var offerResult = await OfferStep(new IpexOfferSubmitArgs(
                    SenderNameOrPrefix: discloserPrefix,
                    RecipientPrefix: discloseePrefix,
                    CredentialSaid: credentialSaid!,
                    ApplySaid: applySaid
                ), "IPEX offer");
                if (offerResult.IsFailed) return await FailIpexResponseWithProgress(offerResult.Errors[0].Message);
                offerSaid = offerResult.Value;
                generatedExchangeSaids.Add(offerSaid);
                lastStepSaid = offerSaid;

                // Wait for Offer notification before proceeding to Agree
                if (workflow is IpexWorkflow.ApplyOfferAgree or IpexWorkflow.ApplyOfferAgreeGrant
                    or IpexWorkflow.ApplyOfferAgreeGrantAdmit or IpexWorkflow.OfferAgreeGrantAdmit) {
                    await WaitForNotificationsAndMarkAsReadStep(
                        new HashSet<string> { offerSaid }, "Offer propagation");
                }
            }

            // Agree step (Disclosee sends Agree to Discloser)
            if (workflow is IpexWorkflow.ApplyOfferAgree
                or IpexWorkflow.ApplyOfferAgreeGrant or IpexWorkflow.ApplyOfferAgreeGrantAdmit
                or IpexWorkflow.OfferAgreeGrantAdmit) {

                await ReportProgress(++ipexStep, ipexTotalSteps, "Agreeing to offer");
                var agreeResult = await AgreeStep(new IpexAgreeSubmitArgs(
                    SenderNameOrPrefix: discloseePrefix,
                    RecipientPrefix: discloserPrefix,
                    OfferSaid: offerSaid!
                ), "IPEX agree");
                if (agreeResult.IsFailed) return await FailIpexResponseWithProgress(agreeResult.Errors[0].Message);
                agreeSaid = agreeResult.Value;
                generatedExchangeSaids.Add(agreeSaid);
                lastStepSaid = agreeSaid;

                // Wait for Agree notification before proceeding to Grant
                if (workflow is IpexWorkflow.ApplyOfferAgreeGrant or IpexWorkflow.ApplyOfferAgreeGrantAdmit
                    or IpexWorkflow.OfferAgreeGrantAdmit) {
                    await WaitForNotificationsAndMarkAsReadStep(
                        new HashSet<string> { agreeSaid }, "Agree propagation");
                }
            }

            // Grant step (Discloser sends Grant to Disclosee)
            if (workflow is IpexWorkflow.ApplyOfferAgreeGrant or IpexWorkflow.ApplyOfferAgreeGrantAdmit
                or IpexWorkflow.Grant or IpexWorkflow.GrantAdmit
                or IpexWorkflow.OfferAgreeGrantAdmit
                or IpexWorkflow.ApplyGrant or IpexWorkflow.ApplyGrantAdmit) {

                await ReportProgress(++ipexStep, ipexTotalSteps, "Granting credential");
                if (payload.IsPresentation) {
                    var grantResult = await PresentStep(discloserPrefix, credentialSaid!, discloseePrefix, "IPEX grant");
                    if (grantResult.IsFailed) return await FailIpexResponseWithProgress(grantResult.Errors[0].Message);
                    grantSaid = grantResult.Value;
                }
                else {
                    // Chain the grant to the preceding agree when the workflow actually went
                    // through agree — KERIA's IpexHandler.verify() requires `p = agreeSaid` for
                    // a grant that follows an agree (`PreviousRoutes[grant] = (agree,)`).
                    // For flows that skip agree (ApplyGrant, ApplyGrantAdmit, standalone Grant,
                    // GrantAdmit), agreeSaid stays null and the grant is an unsolicited initiator.
                    var grantAgreeSaid = workflow is IpexWorkflow.ApplyOfferAgreeGrant
                        or IpexWorkflow.ApplyOfferAgreeGrantAdmit
                        or IpexWorkflow.OfferAgreeGrantAdmit
                        ? agreeSaid
                        : null;
                    var grantResult = await GrantStep(new IpexGrantSubmitArgs(
                        SenderNameOrPrefix: discloserPrefix,
                        RecipientPrefix: discloseePrefix,
                        Acdc: issuedCred!.Acdc,
                        Anc: issuedCred.Anc,
                        Iss: issuedCred.Iss,
                        AgreeSaid: grantAgreeSaid
                    ), "IPEX grant");
                    if (grantResult.IsFailed) return await FailIpexResponseWithProgress(grantResult.Errors[0].Message);
                    grantSaid = grantResult.Value;
                }
                generatedExchangeSaids.Add(grantSaid!);
                lastStepSaid = grantSaid;

                // Wait for Grant notification before proceeding to Admit
                if (workflow is IpexWorkflow.ApplyOfferAgreeGrantAdmit or IpexWorkflow.GrantAdmit
                    or IpexWorkflow.OfferAgreeGrantAdmit or IpexWorkflow.ApplyGrantAdmit) {
                    await WaitForNotificationsAndMarkAsReadStep(
                        new HashSet<string> { grantSaid! }, "Grant propagation");
                }
            }

            // Admit step (Disclosee sends Admit to Discloser)
            if (workflow is IpexWorkflow.ApplyOfferAgreeGrantAdmit or IpexWorkflow.GrantAdmit
                or IpexWorkflow.OfferAgreeGrantAdmit or IpexWorkflow.ApplyGrantAdmit) {

                await ReportProgress(++ipexStep, ipexTotalSteps, "Admitting credential");
                var admitResult = await AdmitStep(new IpexAdmitSubmitArgs(
                    SenderNameOrPrefix: discloseePrefix,
                    RecipientPrefix: discloserPrefix,
                    GrantSaid: grantSaid!
                ), "IPEX admit");
                if (admitResult.IsFailed) return await FailIpexResponseWithProgress(admitResult.Errors[0].Message);
                generatedExchangeSaids.Add(admitResult.Value);
                lastStepSaid = admitResult.Value;
            }

            // Mark the last step's notification as read for complete workflows (ending in Admit).
            // For partial workflows, the last step's notification stays unread (pending action).
            var isCompleteWorkflow = workflow is IpexWorkflow.ApplyOfferAgreeGrantAdmit or IpexWorkflow.GrantAdmit
                or IpexWorkflow.OfferAgreeGrantAdmit or IpexWorkflow.ApplyGrantAdmit;
            if (isCompleteWorkflow && lastStepSaid is not null) {
                await WaitForNotificationsAndMarkAsReadStep(
                    new HashSet<string> { lastStepSaid }, "Final step cleanup");
            }

            _logger.LogInformation("PrimeData IPEX completed successfully: workflow={Workflow}", workflow);
            await ReportComplete();
            return Result.Ok(new PrimeDataIpexResponse(true));
        }

        private static int ComputeIpexStepCount(IpexWorkflow workflow) {
            var count = 1; // schema OOBI resolution
            var hasOfferOrGrant = workflow is not IpexWorkflow.Apply;
            if (hasOfferOrGrant) count++; // credential prep (find or issue)

            if (workflow is IpexWorkflow.Apply or IpexWorkflow.ApplyOffer or IpexWorkflow.ApplyOfferAgree
                or IpexWorkflow.ApplyOfferAgreeGrant or IpexWorkflow.ApplyOfferAgreeGrantAdmit
                or IpexWorkflow.ApplyGrant or IpexWorkflow.ApplyGrantAdmit) count++;
            if (workflow is IpexWorkflow.Offer or IpexWorkflow.ApplyOffer or IpexWorkflow.ApplyOfferAgree
                or IpexWorkflow.ApplyOfferAgreeGrant or IpexWorkflow.ApplyOfferAgreeGrantAdmit
                or IpexWorkflow.OfferAgreeGrantAdmit) count++;
            if (workflow is IpexWorkflow.ApplyOfferAgree
                or IpexWorkflow.ApplyOfferAgreeGrant or IpexWorkflow.ApplyOfferAgreeGrantAdmit
                or IpexWorkflow.OfferAgreeGrantAdmit) count++;
            if (workflow is IpexWorkflow.ApplyOfferAgreeGrant or IpexWorkflow.ApplyOfferAgreeGrantAdmit
                or IpexWorkflow.Grant or IpexWorkflow.GrantAdmit
                or IpexWorkflow.OfferAgreeGrantAdmit
                or IpexWorkflow.ApplyGrant or IpexWorkflow.ApplyGrantAdmit) count++;
            if (workflow is IpexWorkflow.ApplyOfferAgreeGrantAdmit or IpexWorkflow.GrantAdmit
                or IpexWorkflow.OfferAgreeGrantAdmit or IpexWorkflow.ApplyGrantAdmit) count++;

            return count;
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

        private async Task<Result<PrimeDataGoResponse>> FailResponseWithProgress(string error) {
            await ReportError(error);
            return FailResponse(error);
        }

        private static Result<PrimeDataIpexResponse> FailIpexResponse(string error) =>
            Result.Ok(new PrimeDataIpexResponse(false, Error: error));

        private async Task<Result<PrimeDataIpexResponse>> FailIpexResponseWithProgress(string error) {
            await ReportError(error);
            return FailIpexResponse(error);
        }

        private async Task<Result> ResolveSchemaOobiWithFallbackAsync(string schemaSaid) {
            var schemaEntry = _schemaService.GetSchema(schemaSaid);
            var schemaName = schemaEntry?.Name ?? schemaSaid;

            var oobiUrls = _schemaService.GetOobiUrls(schemaSaid);
            if (oobiUrls.Length == 0) {
                oobiUrls = [.. _schemaService.DefaultOobiHosts.Select(host => $"{host}/oobi/{schemaSaid}")];
                _logger.LogInformation("Schema '{SchemaName}' not in manifest, trying {Count} default host URLs", schemaName, oobiUrls.Length);
            }
            else {
                _logger.LogInformation("Resolving schema '{SchemaName}' ({Said}) with {Count} OOBI URLs", schemaName, schemaSaid, oobiUrls.Length);
            }

            var errors = new List<string>();
            foreach (var oobiUrl in oobiUrls) {
                _logger.LogInformation("  Trying OOBI: {Url}", oobiUrl);
                var result = await _signifyClient.ResolveOobi(oobiUrl);
                if (result.IsFailed) {
                    var errorMsg = result.Errors[0].Message;
                    _logger.LogWarning("  Failed to start OOBI resolve for {Url}: {Error}", oobiUrl, errorMsg);
                    errors.Add($"{oobiUrl} (start): {errorMsg}");
                    continue;
                }

                // ResolveOobi returns the long-running operation descriptor — we must wait for it
                // AND verify GetSchemaRaw before declaring success. Without these steps KERIA can
                // silently fail to fetch (unreachable URL, content-type rejection, SAID mismatch)
                // while this function reports the schema as resolved; downstream credential
                // issuance then fails with "Schema not found" only at the KERIA 400 stage.
                if (result.Value.TryGetValue("name", out var nameValue) && nameValue.StringValue is string opName && !string.IsNullOrEmpty(opName)) {
                    var waitResult = await _signifyClient.WaitForOperation(new Operation(opName));
                    if (waitResult.IsFailed) {
                        var errorMsg = waitResult.Errors[0].Message;
                        _logger.LogWarning("  OOBI resolve operation {OpName} failed/timed out for {Url}: {Error}", opName, oobiUrl, errorMsg);
                        errors.Add($"{oobiUrl} (wait): {errorMsg}");
                        continue;
                    }
                }

                var verifyResult = await _signifyClient.GetSchemaRaw(schemaSaid);
                if (verifyResult.IsFailed) {
                    var errorMsg = verifyResult.Errors[0].Message;
                    _logger.LogWarning("  OOBI resolve reported success for {Url}, but KERIA still does not have schema {Said}: {Error}", oobiUrl, schemaSaid, errorMsg);
                    errors.Add($"{oobiUrl} (verify): {errorMsg}");
                    continue;
                }

                _logger.LogInformation("  Schema '{SchemaName}' resolved and verified via {Url}", schemaName, oobiUrl);
                return Result.Ok();
            }

            var allErrors = string.Join("; ", errors);
            _logger.LogError("Failed to resolve schema '{SchemaName}' ({Said}) from any OOBI URL. Tried: {Errors}", schemaName, schemaSaid, allErrors);
            return Result.Fail($"Failed to resolve schema '{schemaName}' ({schemaSaid}): all OOBI URLs failed");
        }

        private async Task RefreshCachedIdentifiersAsync() {
            try {
                var identifiersResult = await _signifyClient.GetIdentifiers();
                if (identifiersResult.IsSuccess && identifiersResult.Value is not null) {
                    var psResult = await _storageGateway.GetItem<PollingState>(StorageArea.Session);
                    var ps = psResult.IsSuccess && psResult.Value is not null ? psResult.Value : new PollingState();
                    await _storageGateway.WriteTransaction(StorageArea.Session, tx => {
                        tx.SetItem(new CachedIdentifiers { IdentifiersList = [identifiersResult.Value] });
                        tx.SetItem(ps with { IdentifiersLastFetchedUtc = DateTime.UtcNow });
                    });
                    _logger.LogInformation("RefreshCachedIdentifiersAsync: Updated CachedIdentifiers ({Count} aids)",
                        identifiersResult.Value.Aids.Count);
                }
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "RefreshCachedIdentifiersAsync: Failed to refresh identifiers");
            }
        }

        // TODO P2: CreateAidWithEndRole is a composite non-idempotent operation.
        // On network error, the AID may have been partially created in KERIA.
        // Recovery should check if AID exists before retrying.
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

            // Get the current config digest from preferences
            var prefsResult = await _storageGateway.GetItem<Preferences>();
            if (prefsResult.IsFailed || prefsResult.Value is null) {
                return Result.Fail("Could not retrieve Preferences for storing connections");
            }
            var digest = prefsResult.Value.SelectedKeriaConnectionDigest;
            if (string.IsNullOrEmpty(digest)) {
                return Result.Fail("No KERIA configuration selected");
            }

            var configsResult = await _storageGateway.GetItem<KeriaConnectConfigs>();
            if (configsResult.IsFailed || configsResult.Value is null) {
                return Result.Fail("Could not retrieve KeriaConnectConfigs");
            }
            var configs = configsResult.Value;
            if (!configs.Configs.TryGetValue(digest, out var config)) {
                return Result.Fail($"Config not found for digest {digest}");
            }

            var existingItems = config.Connections;
            var newConnections = new List<Connection>(existingItems);
            foreach (var (resolver, _, alias) in pairs) {
                newConnections.Add(new Connection {
                    Name = alias,
                    SenderPrefix = nameToPrefix[resolver],
                    ReceiverPrefix = nameToPrefix[alias],
                    ConnectionDate = DateTime.UtcNow
                });
            }

            var updatedConfig = config with { Connections = newConnections };
            var updatedDict = new Dictionary<string, KeriaConnectConfig>(configs.Configs) { [digest] = updatedConfig };
            var setResult = await _storageGateway.SetItem(configs with { Configs = updatedDict });
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

        public async Task<Result<string>> ApplyStep(IpexApplySubmitArgs args, string stepLabel) {
            _logger.LogInformation("{Step}: IPEX Apply from {Sender} to {Recipient}...", stepLabel, args.SenderNameOrPrefix, args.RecipientPrefix);
            var result = await _signifyClient.IpexApplyAndSubmit(args);
            if (result.IsFailed) {
                var err = $"IPEX Apply failed: {result.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Fail<string>(err);
            }
            var applySaid = result.Value["applySaid"].StringValue!;
            _logger.LogInformation("{Step}: Apply submitted: applySaid={ApplySaid}", stepLabel, applySaid);
            return Result.Ok(applySaid);
        }

        public async Task<Result<string>> OfferStep(IpexOfferSubmitArgs args, string stepLabel) {
            _logger.LogInformation("{Step}: IPEX Offer from {Sender} to {Recipient}...", stepLabel, args.SenderNameOrPrefix, args.RecipientPrefix);
            var result = await _signifyClient.IpexOfferAndSubmit(args);
            if (result.IsFailed) {
                var err = $"IPEX Offer failed: {result.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Fail<string>(err);
            }
            var offerSaid = result.Value["offerSaid"].StringValue!;
            _logger.LogInformation("{Step}: Offer submitted: offerSaid={OfferSaid}", stepLabel, offerSaid);
            return Result.Ok(offerSaid);
        }

        public async Task<Result<string>> AgreeStep(IpexAgreeSubmitArgs args, string stepLabel) {
            _logger.LogInformation("{Step}: IPEX Agree from {Sender} to {Recipient}...", stepLabel, args.SenderNameOrPrefix, args.RecipientPrefix);
            var result = await _signifyClient.IpexAgreeAndSubmit(args);
            if (result.IsFailed) {
                var err = $"IPEX Agree failed: {result.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Fail<string>(err);
            }
            var agreeSaid = result.Value["agreeSaid"].StringValue!;
            _logger.LogInformation("{Step}: Agree submitted: agreeSaid={AgreeSaid}", stepLabel, agreeSaid);
            return Result.Ok(agreeSaid);
        }

        public async Task<Result<string>> GrantStep(IpexGrantSubmitArgs args, string stepLabel) {
            _logger.LogInformation("{Step}: IPEX Grant from {Sender} to {Recipient}...", stepLabel, args.SenderNameOrPrefix, args.RecipientPrefix);
            var result = await _signifyClient.IpexGrantAndSubmit(args);
            if (result.IsFailed) {
                var err = $"IPEX Grant failed: {result.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Fail<string>(err);
            }
            var grantSaid = result.Value["grantSaid"].StringValue!;
            _logger.LogInformation("{Step}: Grant submitted: grantSaid={GrantSaid}", stepLabel, grantSaid);
            return Result.Ok(grantSaid);
        }

        public async Task<Result<string>> AdmitStep(IpexAdmitSubmitArgs args, string stepLabel) {
            _logger.LogInformation("{Step}: IPEX Admit from {Sender} to {Recipient}...", stepLabel, args.SenderNameOrPrefix, args.RecipientPrefix);
            var result = await _signifyClient.IpexAdmitAndSubmit(args);
            if (result.IsFailed) {
                var err = $"IPEX Admit failed: {result.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Fail<string>(err);
            }
            var admitSaid = result.Value["admitSaid"].StringValue!;
            _logger.LogInformation("{Step}: Admit submitted: admitSaid={AdmitSaid}", stepLabel, admitSaid);
            return Result.Ok(admitSaid);
        }

        public async Task<Result<string>> PresentStep(string senderNameOrPrefix, string credSaid, string recipientPrefix, string stepLabel) {
            _logger.LogInformation("{Step}: IPEX Present from {Sender}...", stepLabel, senderNameOrPrefix);
            var result = await _signifyClient.GrantReceivedCredential(senderNameOrPrefix, credSaid, recipientPrefix);
            if (result.IsFailed) {
                var err = $"IPEX Present failed: {result.Errors[0].Message}";
                _logger.LogError("{Error}", err);
                return Result.Fail<string>(err);
            }
            var grantSaid = result.Value["grantSaid"].StringValue!;
            _logger.LogInformation("{Step}: Present submitted: grantSaid={GrantSaid}", stepLabel, grantSaid);
            return Result.Ok(grantSaid);
        }

        private Task WaitForNotificationsAndMarkAsReadStep(HashSet<string> expectedExchangeSaids, string stepLabel) =>
            WaitForNotificationsAndMarkAsReadStep(expectedExchangeSaids, stepLabel, TimeSpan.FromSeconds(10));

        private async Task WaitForNotificationsAndMarkAsReadStep(HashSet<string> expectedExchangeSaids, string stepLabel, TimeSpan timeout) {
            _logger.LogInformation("{Step}: Waiting for {Count} notifications to propagate (timeout={Timeout}s)...",
                stepLabel, expectedExchangeSaids.Count, timeout.TotalSeconds);

            var interval = TimeSpan.FromSeconds(2);
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
