using System.Text;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using System;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Http;
using System.Net;
using Polly;
using System.Collections.Concurrent;

namespace FHIRProxy
{
    public static class FHIRClient
    {
        private static ConcurrentDictionary<string, string> _tokendict = new ConcurrentDictionary<string, string>();
        private static object lockobj = new object();
        private static HttpClient _fhirClient =null;
        private static async Task<string> GetFHIRToken(ILogger log)
        {
            string tok = null;
            if (_tokendict.TryGetValue("fhirtoken", out tok) && !ADUtils.isTokenExpired(tok)) return tok;
            //renew
            string resource = System.Environment.GetEnvironmentVariable("FS-RESOURCE");
            string tenant = System.Environment.GetEnvironmentVariable("FS-TENANT-NAME");
            string clientid = System.Environment.GetEnvironmentVariable("FS-CLIENT-ID");
            string secret = System.Environment.GetEnvironmentVariable("FS-SECRET");
            string authority = Utils.GetEnvironmentVariable("FS-AUTHORITY", "https://login.microsoftonline.com");
            bool isMsi = ADUtils.isMSI(resource, tenant, clientid, secret);
            log.LogInformation($"Obtaining new FHIR Access Token...Using MSI=({isMsi.ToString()})...");
            string newtok = await ADUtils.GetAADAccessToken($"{authority}/{tenant}", clientid, secret, resource, isMsi,log);
            if (newtok != null)
            {
                if (tok==null)
                {
                    _tokendict.TryAdd("fhirtoken", newtok);
                } else { 
                    _tokendict.TryUpdate("fhirtoken", newtok, tok);
                }
                log.LogInformation($"GetFHIRToken: fhir token renewed...");
            }
            return newtok;
        }
        private static TimeSpan GetServerRetryAfter(HttpResponseMessage resp, ILogger log)
        {
            TimeSpan? retryafter = null;
            try
            {
                retryafter = resp.Headers.RetryAfter?.Delta;
                if (retryafter.HasValue)
                {
                    return retryafter.Value;
                }
                if (resp.Headers.Contains("x-ms-retry-after-ms"))
                {
                    int rtms = int.Parse(resp.Headers.GetValues("x-ms-retry-after-ms").FirstOrDefault());
                    return TimeSpan.FromMilliseconds(rtms);
                }
            }
            catch (Exception e)
            {
                log.LogWarning($"GetServerRetryAfter: Exception getting header {e.Message}....");
            }
            return TimeSpan.FromMilliseconds(Utils.GetIntEnvironmentVariable("FP-POLLY-RETRYMS", "200"));

        }
        private static void InititalizeHttpClient(ILogger log)
        {
            if (_fhirClient == null)
            {
                lock (lockobj)
                {
                    if (_fhirClient == null)
                    {
                        log.LogInformation("Initializing FHIR Client...");
                        SocketsHttpHandler socketsHandler = new SocketsHttpHandler
                        {
                            ResponseDrainTimeout = TimeSpan.FromSeconds(Utils.GetIntEnvironmentVariable("FP-POOLEDCON-RESPONSEDRAINSECS", "30")),
                            PooledConnectionLifetime = TimeSpan.FromMinutes(Utils.GetIntEnvironmentVariable("FP-POOLEDCON-LIFETIME", "5")),
                            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(Utils.GetIntEnvironmentVariable("FP-POOLEDCON-IDLETO", "2")),
                            MaxConnectionsPerServer = Utils.GetIntEnvironmentVariable("FP-POOLEDCON-MAXCONNECTIONS", "20")
                        };
                        _fhirClient = new HttpClient(socketsHandler);
                    }
                }
            }
           
        }
        public static async System.Threading.Tasks.Task<FHIRResponse> CallFHIRServer(HttpRequest req, string path, string body, ILogger log)
        {
            path += (req.QueryString.HasValue ? req.QueryString.Value : "");
            return await FHIRClient.CallFHIRServer(path, body, req.Method, req.Headers, log);
        }
        public static async System.Threading.Tasks.Task<FHIRResponse> CallFHIRServer(string path, string body, string method, ILogger log)
        {
            HeaderDictionary dict = new HeaderDictionary();
            dict.Add("Content-Type", "application/json");
            dict.Add("Accept", "application/json");
            return await FHIRClient.CallFHIRServer(path, body, method, dict, log);
        }
        public static async System.Threading.Tasks.Task<FHIRResponse> CallFHIRServer(string path, string body, string method, IHeaderDictionary headers, ILogger log)
        {
            InititalizeHttpClient(log);
            HttpMethod rm = HttpMethod.Put;
            switch (method)
            {
                case "GET":
                    rm = HttpMethod.Get;
                    break;
                case "POST":
                    rm = HttpMethod.Post;
                    break;
                case "PUT":
                    rm = HttpMethod.Put;
                    break;
                case "PATCH":
                    rm = HttpMethod.Patch;
                    break;
                case "DELETE":
                    rm = HttpMethod.Delete;
                    break;
                default:
                    throw new Exception($"{method} is not supported");

            }
            var retryPolicy = Policy
                    .Handle<HttpRequestException>()
                    .OrResult<HttpResponseMessage>(r => ServiceCommunicationException.httpStatusCodesWorthRetrying.Contains(r.StatusCode))
                    .WaitAndRetryAsync(retryCount: Utils.GetIntEnvironmentVariable("FP-POLLY-MAXRETRIES", "3"),
                                         sleepDurationProvider: (retryCount, response, context) =>
                                         {
                                             return GetServerRetryAfter(response.Result, log);
                                         },
                                         onRetryAsync: async (response, timespan, retryCount, context) => {
                                             log.LogWarning($"FHIR Request failed. Waiting {timespan} before next retry. Retry attempt {retryCount}");
                                         }
                   );
            HttpResponseMessage _fhirResponse = 
                await retryPolicy.ExecuteAsync(async () =>
                {
                    HttpRequestMessage _fhirRequest;
                    string fsurl = Utils.GetEnvironmentVariable("FS-URL", "");
                    if (!path.StartsWith(fsurl, StringComparison.InvariantCultureIgnoreCase)) path = fsurl + "/" + path;
                    _fhirRequest = new HttpRequestMessage(rm, path);
                    string ct = "application/json";
                    if (headers.TryGetValue("Content-Type", out Microsoft.Extensions.Primitives.StringValues ctvalues))
                    {
                        ct = ctvalues.First();
                        if (!string.IsNullOrEmpty(ct)) ct = ct.Split(";")[0];
                    }
                    //Add Authorization
                    string _bearerToken = await GetFHIRToken(log);
                    _fhirRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
                    foreach (string headerKey in headers.Keys)
                    {
                        try
                        {
                            if (headerKey.StartsWith("x-ms", StringComparison.InvariantCultureIgnoreCase) ||
                                headerKey.StartsWith("Prefer", StringComparison.InvariantCultureIgnoreCase) ||
                                headerKey.StartsWith("etag", StringComparison.InvariantCultureIgnoreCase) ||
                                headerKey.StartsWith("If-", StringComparison.InvariantCultureIgnoreCase) ||
                                headerKey.StartsWith("Accept",StringComparison.InvariantCultureIgnoreCase))
                            {
                                _fhirRequest.Headers.Add(headerKey, headers[headerKey].FirstOrDefault());
                            }
                        }
                        catch (Exception ex)
                        {
                            log.LogError($"Error Adding Headers to FHIR Request {headerKey}:{ex.Message}");
                        }
                    }
                    if (!headers.TryGetValue("Accept", out Microsoft.Extensions.Primitives.StringValues acvalue))
                    {
                        _fhirRequest.Headers.Add("Accept", "application/json");
                    }
                    if (!string.IsNullOrEmpty(body))
                    {
                        _fhirRequest.Content = new StringContent(body, Encoding.UTF8);
                        _fhirRequest.Content.Headers.Remove("Content-Type");
                        _fhirRequest.Content.Headers.Add("Content-Type", ct);
                    }
                    return await _fhirClient.SendAsync(_fhirRequest);
                });
            // Read Response Content (this will usually be JSON content)
            var content = await _fhirResponse.Content.ReadAsStringAsync();
            return new FHIRResponse(content, _fhirResponse.Headers, _fhirResponse.Content.Headers,_fhirResponse.StatusCode);
            

        }
       
    }
    public class FHIRResponse
    {
        public FHIRResponse()
        {
            Headers = new Dictionary<string, HeaderParm>();
        }
        public FHIRResponse(string content, HttpResponseHeaders respheaders,HttpContentHeaders contentHeaders, HttpStatusCode status, bool parse = false) : this()
        {
            string[] filterheaders = Utils.GetEnvironmentVariable("FS-RESPONSE-HEADER-NAME", "x-ms-retry-after-ms,x-ms-session-token,x-ms-request-charge,Retry-After,Date,Last-Modified,ETag,Location,Content-Location").Split(",");
            if (parse) this.Content = JObject.Parse(content);
            else this.Content = content;
            foreach (string head in filterheaders)
            {
                IEnumerable<string> values = null;
                if (respheaders.TryGetValues(head, out values))
                {
                    this.Headers.Add(head, new HeaderParm(head, values.First()));

                }
            }
            IEnumerable<string> contentloc = null;
            if (contentHeaders.TryGetValues("Content-Location", out contentloc))
            {
                this.Headers.Add("Content-Location", new HeaderParm("Content-Location",contentloc.First()));
            }
            this.StatusCode = status;
        }
        public IDictionary<string, HeaderParm> Headers { get; set; }
        public object Content { get; set; }
        public HttpStatusCode StatusCode { get; set; }
        public bool IsSuccess()
        {
            int s = (int)StatusCode;
            return (s > 199 && s < 300);
        }
        public override string ToString()
        {
            if (Content == null) return "";
            if (Content is string) return (string)Content;
            if (Content is JToken)
            {
                return ((JToken)Content).ToString();
            }
            return base.ToString();
        }
        public JToken toJToken()
        {
            if (Content is string)
            {
                var c = (string)Content;
                if (string.IsNullOrEmpty(c)) return null;
                return JObject.Parse(c);
            }
            if (Content == null) return new JObject();
            return (JToken)Content;
        }

    }
    public class HeaderParm
    {
        public HeaderParm()
        {

        }
        public HeaderParm(string name, string value)
        {
            this.Name = name;
            this.Value = value;
        }
        public string Name { get; set; }
        public string Value { get; set; }
    }

}
