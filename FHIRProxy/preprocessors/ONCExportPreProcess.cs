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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web;
using Microsoft.WindowsAzure.Storage.Table;
using System.Web.Http;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Http.Extensions;

namespace FHIRProxy.preprocessors
{
    class ONCExportPreProcess : IProxyPreProcess
    {
        const int MAX_URL_LENGTH = 2048;

        public async Task<ProxyProcessResult> Process(string requestBody, HttpRequest req, ILogger log, ClaimsPrincipal principal)
        {
            if (req.Method.Equals("GET") && req.Path.HasValue)
            {
                if (req.Path.Value.EndsWith("$export"))
                {
                    ClaimsIdentity ci = (ClaimsIdentity)principal.Identity;
                    FHIRParsedPath pp = req.parsePath();
                    List<string> overrideExportUrls = new();

                    if (pp.ResourceType == "Group")
                    {
                        log.LogInformation("Starting aggregate group export for group {GroupId}", pp.ResourceId);

                        // Handle device export
                        var patientsInGroup = await GetPatientIdsForGroupId(pp.ResourceId, log);
                        var deviceRequestStrings = BuildDeviceExportRequests(patientsInGroup, ci.ObjectId());

                        // Add system level export for all devices for patients in group
                        overrideExportUrls.AddRange(deviceRequestStrings);

                        // Add system level export for all reference data
                        overrideExportUrls.Add($"$export?_container={ci.ObjectId()}&_type=Medication,Practitioner,Location,Organization");

                        // Add current group export
                        overrideExportUrls.Add($"Group/{pp.ResourceId}/$export?_container={ci.ObjectId()}");
                    }
                    else if (pp.ResourceType == "Patient")
                    {
                        log.LogInformation("Starting aggregate group export for patient {PatientId}", pp.ResourceId);
                        var deviceRequestStrings = BuildDeviceExportRequests(new List<string> { pp.ResourceId }, ci.ObjectId());

                        // Add system level export for all devices for patient
                        overrideExportUrls.AddRange(deviceRequestStrings);

                        // Add system level export for all reference data
                        overrideExportUrls.Add($"$export?_container={ci.ObjectId()}&_type=Medication,Practitioner,Location,Organization");

                        // Add current patient export
                        overrideExportUrls.Add($"Patient/{pp.ResourceId}/$export?_container={ci.ObjectId()}");
                    }
                    else
                    {
                        QueryString newquery = new QueryString();
                        foreach (var s in req.Query)
                        {
                            if (!s.Key.Equals("_container")) newquery = newquery.Add(s.Key, s.Value);
                        }
                        newquery = newquery.Add("_container", ci.ObjectId());
                        req.QueryString = newquery;
                    }

                    if (overrideExportUrls.Count > 0)
                    {
                        FHIRResponse resp = await ProcessExportAggregate(req.GetEncodedUrl(), overrideExportUrls, req.Host, req.Headers, log);
                        return new ProxyProcessResult(false, string.Empty, string.Empty, resp);
                    }
                }

                // Process Export Check requests
                if (req.Path.Value.StartsWith("/fhir/_operations/aggexport"))
                {
                    string exportId = req.Path.Value.Split("/").Last();
                    CloudTable eaTable = Utils.getTable(ProxyConstants.EXPORT_AGGREGATE_TABLE);
                    ExportAggregate ea = Utils.getEntity<ExportAggregate>(eaTable, ProxyConstants.EXPORT_AGGREGATE_TABLE, exportId);

                    FHIRResponse resp = await FetchExportAggregateResult(ea, req.Host, log);
                    return new ProxyProcessResult(false, "", requestBody, resp);
                }
            }

            return new ProxyProcessResult(true, "", requestBody, null);
        }
      
        public async Task<List<string>> GetPatientIdsForGroupId(string groupId, ILogger log)
        {
            FHIRResponse groupResult = await FHIRClient.CallFHIRServer($"Group/{groupId}", body: "", "GET", log);
            if (groupResult.StatusCode != HttpStatusCode.OK)
            {
                return new List<string>();
            }

            var result = groupResult
                .toJToken()
                .SelectTokens("member[*].entity.reference")
                .Where(x => x is not null)
                .Where(x => x is JValue)
                .Select(x => x.ToString())
                .Where(x => x.Contains("Patient/"))
                .Select(x => x.Split("/").Last())
                .ToList();

            log.LogInformation("Found patients {PatientIds} in group {GroupId}.", string.Join(",", result), groupId);

            return result;
        }

        public IEnumerable<string> BuildDeviceExportRequests(IEnumerable<string> patientIds, string oid)
        {

            // Encode the patient id into the type filter
            IEnumerable<string> patientIdsTypeFilters = patientIds.Select(x => HttpUtility.UrlEncode($"Device.patient={x}").Replace(".", "%3F"));

            int baseUrlLength = Utils.GetEnvironmentVariable("FS-URL", "").Length;

            string baseDeviceRequest = $"$export?_container={oid}&_type=Device&_typeFilter=";
            string currentRequestString = baseDeviceRequest;

            foreach (var typeFilter in patientIdsTypeFilters)
            {
                // If we are over the max URL Length, return
                if (baseUrlLength + "/".Length + currentRequestString.Length + ",".Length + typeFilter.Length > MAX_URL_LENGTH)
                {
                    yield return currentRequestString;
                    currentRequestString = baseDeviceRequest;
                    continue;
                }

                // Add separator if not first type filter
                if (currentRequestString.Last() != '=')
                {
                    currentRequestString += ",";
                }

                // Add current typeFilter
                currentRequestString += typeFilter;
            }

            if (currentRequestString != baseDeviceRequest)
            {
                yield return currentRequestString;
            }
        }

        public async Task<FHIRResponse> ProcessExportAggregate(string requestUrl, List<string> overrideExportUrls, HostString host, IHeaderDictionary reqHeaders, ILogger log)
        {
            List<string> exportContentLocations = new();
            foreach (var path in overrideExportUrls)
            {
                FHIRResponse groupResult = await FHIRClient.CallFHIRServer(path, body: "", "GET", reqHeaders, log);

                if (!groupResult.IsSuccess())
                {
                    log.LogWarning("Child export returned without success. {Path} {StatusCode} {Body}", path, groupResult.StatusCode, groupResult.Content);
                    return groupResult;
                }

                if (!groupResult.Headers.ContainsKey("Content-Location"))
                {
                    groupResult.StatusCode = HttpStatusCode.InternalServerError;
                    JObject content = new();
                    content["error"] = "Content-Location header not found for export";
                    groupResult.Content = content;

                    log.LogError("Child export returned without ContentLocationHeader. {Path} {StatusCode} {Body}", path, groupResult.StatusCode, groupResult.Content.ToString());

                    return groupResult;
                }

                exportContentLocations.Add(groupResult.Headers["Content-Location"].Value);
            }

            ExportAggregate ea = new(requestUrl, exportContentLocations);

            CloudTable eaTable = Utils.getTable(ProxyConstants.EXPORT_AGGREGATE_TABLE);
            Utils.setEntity(eaTable, ea);

            FHIRResponse resp = new();
            resp.StatusCode = HttpStatusCode.Accepted;
            resp.Headers.Add("Content-Location", new HeaderParm("Content-Location", $"https://{host}/fhir/_operations/aggexport/{ea.ExportId}"));
            return resp;
        }

        public async Task<FHIRResponse> FetchExportAggregateResult(ExportAggregate ea, HostString proxyHost, ILogger log)
        {
            JObject exportResult = new();
            exportResult["transactionTime"] = ea.Timestamp;
            exportResult["request"] = ea.ExportRequestUrl;
            exportResult["requiresAccessToken"] = true;

            JArray output = new();
            JArray error = new();
            JArray issues = new();

            foreach (var uri in ea.ExportUrlList.Select(x => new Uri(x)))
            {
                FHIRResponse currentResponse = await FHIRClient.CallFHIRServer(uri.LocalPath, body: "", "GET", log);

                // If any exports are still running wait
                if (currentResponse.StatusCode != HttpStatusCode.OK)
                {
                    if (!currentResponse.IsSuccess())
                    {
                        log.LogWarning("Child export operation returned unsucessful. {Path} {StatusCode} {Body}", uri.LocalPath, currentResponse.StatusCode, currentResponse.Content.ToString());
                    }

                    return currentResponse;
                }

                JToken current = currentResponse.toJToken();

                foreach (JToken singleOutput in current.SelectTokens("output[*]"))
                {
                    Uri outputUrl = new(singleOutput["url"].ToString());
                    singleOutput["url"] = $"https://{proxyHost.Value}/fhir/_exportFile{outputUrl.PathAndQuery}";
                    output.Add(singleOutput);
                }
                
                foreach (JToken singleError in current.SelectTokens("error[*]"))
                {
                    error.Add(singleError);
                }
                
                foreach (JToken singleIssue in current.SelectTokens("issues[*]"))
                {
                    issues.Add(singleIssue);
                }
            }

            exportResult["output"] = output;
            exportResult["error"] = error;
            exportResult["issues"] = issues;

            return new FHIRResponse()
            {
                Content = exportResult,
                StatusCode = HttpStatusCode.OK,
            };
        }
    }
}