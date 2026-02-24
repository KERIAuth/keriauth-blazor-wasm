using Extension.Models.Messages.AppBw;
using FluentResults;

namespace Extension.Services.PrimeDataService {
    public interface IPrimeDataService {
        Task<Result<PrimeDataGoResponse>> GoAsync(PrimeDataGoPayload payload);
    }
}
