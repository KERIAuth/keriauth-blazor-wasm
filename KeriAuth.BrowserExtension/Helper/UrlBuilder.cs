using System.Web;
using System;
using System.Collections.Generic;

namespace KeriAuth.BrowserExtension.Helper
{
    public class UrlBuilder
    {
        // See also the similar typescript implementation, currently in service-worker.ts
        public static string CreateUrlWithEncodedQueryStrings(string baseUrl, List<KeyValuePair<string, string>> queryParams)
        {
            var uriBuilder = new UriBuilder(baseUrl);
            var query = HttpUtility.ParseQueryString(string.Empty);

            foreach (var param in queryParams)
            {
                query[HttpUtility.UrlEncode(param.Key)] = HttpUtility.UrlEncode(param.Value);
            }

            uriBuilder.Query = query.ToString();
            return uriBuilder.ToString();
        }

        public static Dictionary<string, string> DecodeUrlQueryString(string url)
        {
            var uri = new Uri(url);
            var query = HttpUtility.ParseQueryString(uri.Query);
            var decodedParams = new Dictionary<string, string>();

            foreach (string key in query)
            {
                var param = HttpUtility.UrlDecode(query[key]);
                if (param is null)
                {
                    throw new Exception($"Failed to decode query string with key: {key}");
                }
                decodedParams[HttpUtility.UrlDecode(key)] = param;
            }
            return decodedParams;
        }
    }
}
