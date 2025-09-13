using System.Net.Http.Headers;

namespace Extension.Services.SignifyService {
    public interface IAuthenticater {
        // implementation depends on a Signer and a Verifier

        bool Verify(HttpRequestHeaders headers, string method, string path);
        HttpRequestHeaders Sign(HttpRequestHeaders headers, string method, string path, List<string>? fields = null);
    }
}
