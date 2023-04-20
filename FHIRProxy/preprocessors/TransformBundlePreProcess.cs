/* 
* 2020 Microsoft Corp
* 
* THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS “AS IS”
* AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO,
* THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
* ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE
* FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
* (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
* LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
* HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
* OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
* OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
namespace FHIRProxy.preprocessors
{
    /* Converts Transaction Bundles to Batch bundles subtable for API for FHIR ingestion preserving relationships. 
     * Caution: assumes UUID are assigned per spec.*/
    class TransformBundlePreProcess : IProxyPreProcess
    {
        public async Task<ProxyProcessResult> Process(string requestBody, HttpRequest req, ILogger log, ClaimsPrincipal principal)
        {
            FHIRParsedPath pp = req.parsePath();
            if (string.IsNullOrEmpty(requestBody) || !req.Method.Equals("POST") || !string.IsNullOrEmpty(pp.ResourceType)) return new ProxyProcessResult(true, "", requestBody, null);
            JObject result = null;
            try
            {
                result = JObject.Parse(requestBody);
            }
            catch (Exception e)
            {
                log.LogError($"TransformBundleProcess: Unable to parse JSON Object from request body:{e.Message}");
                result = null;
            }
            if (result == null || result["resourceType"] == null || result["type"]==null) return new ProxyProcessResult(true,"",requestBody,null);
            string rtt = result.FHIRResourceType();
            string bt = (string) result["type"];
            if (rtt.Equals("Bundle") && bt.Equals("transaction"))
            {
                log.LogInformation($"TransformBundleProcess: looks like a valid transaction bundle");
                JArray entries = (JArray)result["entry"];
                if (entries.IsNullOrEmpty()) return new ProxyProcessResult(true, "", requestBody, null);
                log.LogInformation($"TransformBundleProcess: Phase 1 searching for existing entries on FHIR Server...");
                foreach (JToken tok in entries)
                {
                    if (!tok.IsNullOrEmpty() && !tok["request"].IsNullOrEmpty())
                    { 
                        string requrl = (string)tok["request"]["url"];
                        string method = (string)tok["request"]["method"];
                        string ifnoneexist = (string)tok["request"]["ifNoneExist"];
                        string resource = "";
                        string query = "";
                        if (method.Equals("post",StringComparison.InvariantCultureIgnoreCase) && !string.IsNullOrEmpty(ifnoneexist))
                        {
                            resource = requrl;
                            query = Uri.UnescapeDataString(ifnoneexist);
                       
                        } else if (method.Equals("put",StringComparison.InvariantCultureIgnoreCase) && requrl.Contains("?"))
                        {
                            string[] parts = requrl.Split("?");
                            resource = parts[0];
                            query = parts[1];
                            query=Uri.UnescapeDataString(query);
                        }
                        if (!string.IsNullOrEmpty(resource) && !string.IsNullOrEmpty(query))
                        {
                            log.LogInformation($"TransformBundleProcess: Loading Resource {resource} with query {query}");
                            var r = await FHIRClient.CallFHIRServer($"{resource}?{query}", null, "GET", req.Headers, log);
                            if (r.StatusCode == System.Net.HttpStatusCode.OK)
                            {
                                var rs = r.toJToken();
                                if (!rs.IsNullOrEmpty() && ((string)rs["resourceType"]).Equals("Bundle") && !rs["entry"].IsNullOrEmpty())
                                {
                                    JArray respentries = (JArray)rs["entry"];
                                    string furl = (string)tok["fullUrl"];
                                    if (respentries.Count > 1)
                                    {
                                        var msg = $"Entry fullUrl: {furl} Resource query not selective enough: {resource}?{query}";
                                        log.LogError(msg);
                                        FHIRResponse fer = new FHIRResponse();
                                        fer.StatusCode = System.Net.HttpStatusCode.PreconditionFailed;
                                        fer.Content = Utils.genOOErrResponse("error", msg);
                                        return new ProxyProcessResult(false, msg, requestBody,fer);
                                    }
                                    string existingid = "urn:uuid:" + (string)respentries[0]["resource"]["id"];
                                    if (!string.IsNullOrEmpty(furl)) requestBody = requestBody.Replace(furl, existingid);
                                    
                                }
                            }
                        }
                    }
                }
                //reparse JSON with replacement of existing ids prepare to convert to Batch bundle with PUT to maintain relationships
                result = JObject.Parse(requestBody);
                Dictionary<string, string> convert = new Dictionary<string, string>();
                result["type"] = "batch";
                entries = (JArray)result["entry"];
                foreach (JToken tok in entries)
                {
                    string urn = (string)tok["fullUrl"];
                    if (!string.IsNullOrEmpty(urn) && urn.StartsWith("urn:uuid:") && !tok["resource"].IsNullOrEmpty())
                    {
                        string rt = (string)tok["resource"]["resourceType"];
                        string rid = (string)tok["resource"]["id"];
                        if (string.IsNullOrEmpty(rid))
                        {
                            rid = urn.Replace("urn:uuid:", "");
                            tok["resource"]["id"] = rid;
                        }
                        if (!convert.TryAdd(rid, rt))
                        {
                            log.LogWarning($"TransformBundleProcess: **** Duplicate GUID Detected {rid} already assigned to a resource type");
                        }
                        tok["request"]["method"] = "PUT";
                        tok["request"]["url"] = $"{rt}?_id={rid}";
                    }

                }
                log.LogInformation($"TransformBundleProcess: Phase 2 Localizing {convert.Count} resource entries...");
                IEnumerable<JToken> refs = result.SelectTokens("$..reference");
                foreach (JToken item in refs)
                {
                    string s = item.ToString();
                    string t = "";
                    s = s.Replace("urn:uuid:", "");

                    if (convert.TryGetValue(s, out t))
                    {
                        item.Replace(t + "/" + s);
                    }
                }
                if (Utils.GetBoolEnvironmentVariable("FP-BUNDLES-LOGCONVERTED"))
                {
                    log.LogInformation($"TransformBundleProcess: Transformed Bundle:\r\n{result.ToString()}");
                }
                return new ProxyProcessResult(true, "", result.ToString(), null);
            }
            return new ProxyProcessResult(true, "", requestBody, null);
        }
    }
    
}
