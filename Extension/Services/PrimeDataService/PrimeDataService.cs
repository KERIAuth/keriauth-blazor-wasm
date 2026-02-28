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

        public async Task<Result<PrimeDataGoResponse>> GoAsync(PrimeDataGoPayload payload) {
            var prepend = payload.Prepend;
            _logger.LogInformation("PrimeData Go starting with prepend '{Prepend}'", prepend);

            var generatedExchangeSaids = new HashSet<string>();

            // Steps 1-4: Create AIDs
            var nameToPrefix = new Dictionary<string, string>();

            var gedaName = $"{prepend}_geda";
            var gedaResult = await CreateAidStep(gedaName, "Step 1");
            if (gedaResult.IsFailed) return FailResponse(gedaResult.Errors[0].Message);
            nameToPrefix[gedaName] = gedaResult.Value.Prefix;

            var qviName = $"{prepend}_qvi";
            var qviResult = await CreateAidStep(qviName, "Step 2");
            if (qviResult.IsFailed) return FailResponse(qviResult.Errors[0].Message);
            nameToPrefix[qviName] = qviResult.Value.Prefix;

            var leName = $"{prepend}_le";
            var leResult = await CreateAidStep(leName, "Step 3");
            if (leResult.IsFailed) return FailResponse(leResult.Errors[0].Message);
            nameToPrefix[leName] = leResult.Value.Prefix;

            var personName = $"{prepend}_person";
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
            var gedaRegistryName = $"{prepend}_geda_registry";
            var registryResult = await CreateRegistryStep(gedaName, gedaRegistryName, "Step 12");
            if (registryResult.IsFailed) return FailResponse(registryResult.Errors[0].Message);

            // Step 12b: Resolve credential schema OOBIs
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
                    return FailResponse(err);
                }
                _logger.LogInformation("  Schema OOBI resolved: {SchemaSaid}", schemaSaid);
            }

            // Step 13a: GEDA issues QVI credential
            var qviCredData = new RecursiveDictionary();
            qviCredData["LEI"] = new RecursiveValue { StringValue = "5493001KJTIIGC8Y1R17" };
            var qviCredIssued = await IssueCredentialStep(new IssueAndGetCredentialArgs(
                IssuerAidNameOrPrefix: gedaName,
                RegistryName: gedaRegistryName,
                Schema: "EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao",
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
            var verifierName = $"{prepend}_verifier";
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
                SchemaSaid: "EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao"
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
            var qviRegistryName = $"{prepend}_qvi_le_registry";
            var qviRegistryResult = await CreateRegistryStep(qviName, qviRegistryName, "Step 17");
            if (qviRegistryResult.IsFailed) return FailResponse(qviRegistryResult.Errors[0].Message);

            // Step 18a: QVI issues LE credential
            var leCredData = new RecursiveDictionary();
            leCredData["LEI"] = new RecursiveValue { StringValue = "254900OPPU84GM83MG36" };

            var leCredEdge = new RecursiveDictionary();
            leCredEdge["d"] = new RecursiveValue { StringValue = "" };
            var qviEdge = new RecursiveDictionary();
            qviEdge["n"] = new RecursiveValue { StringValue = qviCredIssued.Value.Said };
            qviEdge["s"] = new RecursiveValue { StringValue = "EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao" };
            leCredEdge["qvi"] = new RecursiveValue { Dictionary = qviEdge };

            var leCredIssued = await IssueCredentialStep(new IssueAndGetCredentialArgs(
                IssuerAidNameOrPrefix: qviName,
                RegistryName: qviRegistryName,
                Schema: "ENPXp1vQzRF6JwIuS-mp2U8Uf1MoADoP_GqQ62VsDZWY",
                HolderPrefix: leResult.Value.Prefix,
                CredData: leCredData,
                CredEdge: leCredEdge,
                CredRules: BuildVleiRules()
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
            var leRegistryName = $"{prepend}_le_oor_registry";
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
            oorAuthLeEdge["s"] = new RecursiveValue { StringValue = "ENPXp1vQzRF6JwIuS-mp2U8Uf1MoADoP_GqQ62VsDZWY" };
            oorAuthEdge["le"] = new RecursiveValue { Dictionary = oorAuthLeEdge };

            var oorAuthIssued = await IssueCredentialStep(new IssueAndGetCredentialArgs(
                IssuerAidNameOrPrefix: leName,
                RegistryName: leRegistryName,
                Schema: "EKA57bKBKxr_kN7iN5i7lMUxpMG-s19dRcmov1iDxz-E",
                HolderPrefix: qviResult.Value.Prefix,
                CredData: oorAuthCredData,
                CredEdge: oorAuthEdge,
                CredRules: BuildVleiRules()
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
            oorAuthRef["s"] = new RecursiveValue { StringValue = "EKA57bKBKxr_kN7iN5i7lMUxpMG-s19dRcmov1iDxz-E" };
            oorAuthRef["o"] = new RecursiveValue { StringValue = "I2I" };
            oorEdge["auth"] = new RecursiveValue { Dictionary = oorAuthRef };

            var oorIssued = await IssueCredentialStep(new IssueAndGetCredentialArgs(
                IssuerAidNameOrPrefix: qviName,
                RegistryName: qviRegistryName,
                Schema: "EBNaNu-M9P5cgrnfl2Fvymy4E_jvxxyjb70PRtiANlJy",
                HolderPrefix: personResult.Value.Prefix,
                CredData: oorCredData,
                CredEdge: oorEdge,
                CredRules: BuildVleiRules()
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
            ecrAuthLeEdge["s"] = new RecursiveValue { StringValue = "ENPXp1vQzRF6JwIuS-mp2U8Uf1MoADoP_GqQ62VsDZWY" };
            ecrAuthEdge["le"] = new RecursiveValue { Dictionary = ecrAuthLeEdge };

            var ecrAuthIssued = await IssueCredentialStep(new IssueAndGetCredentialArgs(
                IssuerAidNameOrPrefix: leName,
                RegistryName: leRegistryName,
                Schema: "EH6ekLjSr8V32WyFbGe1zXjTzFs9PkTYmupJ9H65O14g",
                HolderPrefix: qviResult.Value.Prefix,
                CredData: ecrAuthCredData,
                CredEdge: ecrAuthEdge,
                CredRules: BuildVleiRules("Privacy Considerations are applicable to QVI ECR AUTH vLEI Credentials.  It is the sole responsibility of QVIs as Issuees of QVI ECR AUTH vLEI Credentials to present these Credentials in a privacy-preserving manner using the mechanisms provided in the Issuance and Presentation Exchange (IPEX) protocol specification and the Authentic Chained Data Container (ACDC) specification.  https://github.com/WebOfTrust/IETF-IPEX and https://github.com/trustoverip/tswg-acdc-specification.")
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
            var ecrCredData = new RecursiveDictionary();
            ecrCredData["LEI"] = new RecursiveValue { StringValue = "254900OPPU84GM83MG36" };
            ecrCredData["personLegalName"] = new RecursiveValue { StringValue = "John Smith" };
            ecrCredData["engagementContextRole"] = new RecursiveValue { StringValue = "Project Manager" };

            var ecrEdge = new RecursiveDictionary();
            ecrEdge["d"] = new RecursiveValue { StringValue = "" };
            var ecrAuthRef = new RecursiveDictionary();
            ecrAuthRef["n"] = new RecursiveValue { StringValue = ecrAuthIssued.Value.Said };
            ecrAuthRef["s"] = new RecursiveValue { StringValue = "EH6ekLjSr8V32WyFbGe1zXjTzFs9PkTYmupJ9H65O14g" };
            ecrAuthRef["o"] = new RecursiveValue { StringValue = "I2I" };
            ecrEdge["auth"] = new RecursiveValue { Dictionary = ecrAuthRef };

            var ecrIssued = await IssueCredentialStep(new IssueAndGetCredentialArgs(
                IssuerAidNameOrPrefix: qviName,
                RegistryName: qviRegistryName,
                Schema: "EEy9PkikFcANV1l7EHukCeXqrzT1hNZjGlUk7wuMO5jw",
                HolderPrefix: personResult.Value.Prefix,
                CredData: ecrCredData,
                CredEdge: ecrEdge,
                CredRules: BuildVleiRules("It is the sole responsibility of Holders as Issuees of an ECR vLEI Credential to present that Credential in a privacy-preserving manner using the mechanisms provided in the Issuance and Presentation Exchange (IPEX) protocol specification and the Authentic Chained Data Container (ACDC) specification. https://github.com/WebOfTrust/IETF-IPEX and https://github.com/trustoverip/tswg-acdc-specification."),
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

        // ===================== Helper Types =====================

        private record IssuedCredential(RecursiveDictionary Acdc, RecursiveDictionary Anc, RecursiveDictionary Iss, string Said);

        // ===================== Helper Methods =====================

        private static Result<PrimeDataGoResponse> FailResponse(string error) =>
            Result.Ok(new PrimeDataGoResponse(false, Error: error));

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

        private static RecursiveDictionary BuildVleiRules(string? privacyText = null) {
            var rules = new RecursiveDictionary();
            rules["d"] = new RecursiveValue { StringValue = "" };
            var usage = new RecursiveDictionary();
            usage["l"] = new RecursiveValue { StringValue = "Usage of a valid, unexpired, and non-revoked vLEI Credential, as defined in the associated Ecosystem Governance Framework, does not assert that the Legal Entity is trustworthy, honest, reputable in its business dealings, safe to do business with, or compliant with any laws or that an implied or expressly intended purpose will be fulfilled." };
            rules["usageDisclaimer"] = new RecursiveValue { Dictionary = usage };
            var issuance = new RecursiveDictionary();
            issuance["l"] = new RecursiveValue { StringValue = "All information in a valid, unexpired, and non-revoked vLEI Credential, as defined in the associated Ecosystem Governance Framework, is accurate as of the date the validation process was complete. The vLEI Credential has been issued to the legal entity or person named in the vLEI Credential as the subject; and the qualified vLEI Issuer exercised reasonable care to perform the validation process set forth in the vLEI Ecosystem Governance Framework." };
            rules["issuanceDisclaimer"] = new RecursiveValue { Dictionary = issuance };
            if (privacyText is not null) {
                var privacy = new RecursiveDictionary();
                privacy["l"] = new RecursiveValue { StringValue = privacyText };
                rules["privacyDisclaimer"] = new RecursiveValue { Dictionary = privacy };
            }
            return rules;
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
