using Extension.Models.Messages.AppBw;
using FluentResults;

namespace Extension.Services.PrimeDataService {
    public interface IPrimeDataService {
        Task<Result<PrimeDataGoResponse>> GoAsync(PrimeDataGoPayload payload);
        Task<Result<PrimeDataIpexResponse>> GoIpexAsync(PrimeDataIpexPayload payload);
        Task<Result<List<string>>> GetEligibleDiscloserPrefixes(bool isPresentation, IpexWorkflow workflow);
    }
}
