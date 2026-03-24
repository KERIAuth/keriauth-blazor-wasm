using Extension.Models.Messages.AppBw;
using Extension.Services.SignifyService.Models;
using FluentResults;

namespace Extension.Services.PrimeDataService {
    public interface IPrimeDataService {
        Task<Result<PrimeDataGoResponse>> GoAsync(PrimeDataGoPayload? payload = null);
        Task<Result<PrimeDataIpexResponse>> GoIpexAsync(PrimeDataIpexPayload payload);
        Task<Result<List<string>>> GetEligibleDiscloserPrefixes(bool isPresentation, IpexWorkflow workflow);

        // IPEX step helpers — shared by PrimeDataService workflows and BackgroundWorker one-step actions
        Task<Result<string>> ApplyStep(IpexApplySubmitArgs args, string stepLabel);
        Task<Result<string>> OfferStep(IpexOfferSubmitArgs args, string stepLabel);
        Task<Result<string>> AgreeStep(IpexAgreeSubmitArgs args, string stepLabel);
        Task<Result<string>> AdmitStep(IpexAdmitSubmitArgs args, string stepLabel);
        Task<Result<string>> GrantStep(IpexGrantSubmitArgs args, string stepLabel);
        Task<Result<string>> PresentStep(string senderNameOrPrefix, string credSaid, string recipientPrefix, string stepLabel);
    }
}
