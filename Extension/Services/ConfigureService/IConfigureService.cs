using Extension.Models.Messages.AppBw;
using FluentResults;

namespace Extension.Services.ConfigureService;

public interface IConfigureService {
    Task<Result<ConfigureResponsePayload>> ConfigureAsync(ConfigureRequestPayload payload);
    Task<Result> ResetAsync();
}
