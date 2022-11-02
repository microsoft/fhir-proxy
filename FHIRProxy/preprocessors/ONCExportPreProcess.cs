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
                    QueryString newquery = new QueryString();
                    foreach (var s in req.Query)
                    {
                        if (!s.Key.Equals("_container")) newquery = newquery.Add(s.Key, s.Value);
                    }
                    newquery = newquery.Add("_container",ci.ObjectId());
                    req.QueryString = newquery;

                    FHIRParsedPath pp = req.parsePath();
                    List<string> overrideExportUrls = new();

                    if (pp.ResourceType == "Group")
                    {
                        // Handle device export
                        var patientsInGroup = await GetPatientIdsForGroupId(req.Path, log);
                        var deviceRequestStrings = BuildDeviceExportRequests(patientsInGroup, ci.ObjectId());

                        // Add system level export for all devices for patients in group
                        overrideExportUrls.AddRange(deviceRequestStrings);

                        // Add system level export for all reference data
                        overrideExportUrls.Add($"$export?_container={ci.ObjectId()}&_type=Medication,Practitioner,Location,Organization");

                        // Add current group export
                        overrideExportUrls.Add($"Group/$export?_container={ci.ObjectId()}");
                    }

                    if (overrideExportUrls.Count > 0)
                    {
                        FHIRResponse resp = await ProcessExportAggregate(overrideExportUrls, req.Host, log);
                        return new ProxyProcessResult(false, string.Empty, string.Empty, resp);
                    }
                }
            }

            return new ProxyProcessResult(true, "", requestBody, null);
        }
      
        public async Task<IEnumerable<string>> GetPatientIdsForGroupId(PathString requestPath, ILogger log)
        {
            // Group id is right before $export
            var pathSplit = requestPath.Value.Split("/");
            string groupId = pathSplit[pathSplit.Length - 1];

            FHIRResponse groupResult = await FHIRClient.CallFHIRServer($"Group/{groupId}", body: "", "GET", log);
            if (groupResult.StatusCode != HttpStatusCode.OK)
            {
                return Enumerable.Empty<string>();
            }

            return groupResult
                .toJToken()
                .SelectToken("member.entity")
                .Where(x => x.Value<string?>("reference").Contains("Patient/"))
                .Select(x => x.ToString().Split("/").Last());
        }

        public IEnumerable<string> BuildDeviceExportRequests(IEnumerable<string> patientIds, string oid)
        {

            // Encode the patient id into the type filter
            IEnumerable<string> patientIdsTypeFilters = patientIds.Select(x => HttpUtility.UrlEncode($"Device.patient={x}").Replace(".", "%3F"));

            int baseUrlLength = Utils.GetEnvironmentVariable("FS-URL", "").Length;

            string baseDeviceRequest = $"$export?_container={oid}&_type=Device&_typefilter=";
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

        public async Task<FHIRResponse> ProcessExportAggregate(List<string> overrideExportUrls, HostString host, ILogger log)
        {
            List<string> exportContentLocations = new();
            foreach (var path in overrideExportUrls)
            {
                FHIRResponse groupResult = await FHIRClient.CallFHIRServer(path, body: "", "GET", log);

                if (!groupResult.IsSuccess())
                {
                    return groupResult;
                }

                if (!groupResult.Headers.ContainsKey("Content-Location"))
                {
                    groupResult.StatusCode = HttpStatusCode.InternalServerError;
                    JObject content = new();
                    content["error"] = "Content-Location header not found for export";
                    groupResult.Content = content;
                    return groupResult;
                }

                exportContentLocations.Add(groupResult.Headers["Content-Location"].Value);
            }

            ExportAggregate ea = new(exportContentLocations);

            CloudTable eaTable = Utils.getTable(ProxyConstants.EXPORT_AGGREGATE_TABLE);
            Utils.setEntity(eaTable, ea);

            FHIRResponse resp = new();
            resp.StatusCode = HttpStatusCode.Accepted;
            resp.Headers.Add("Content-Location", new HeaderParm("Content-Location", $"https://{host}/fhir/_operations/aggexport/{ea.ExportId}"));
            return resp;
        }
    }
}
