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
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace FHIRProxy.postprocessors
{
    class DateSortPostProcessor : IProxyPostProcess
    {
        private static readonly int MAX_ARRAY_SIZE = 5000;
        private static string DATE_SORT_RESOURCES_SUPPORTED = "Observation:DiagnosticReport:Encounter:CarePlan:CareTeam:EpisodeOfCare:Claim";
        public async Task<ProxyProcessResult> Process(FHIRResponse response, HttpRequest req, ILogger log, ClaimsPrincipal principal)
        {
            if (!req.Method.Equals("GET") || response.StatusCode != HttpStatusCode.OK || response.Content == null) return new ProxyProcessResult(true, "", "", response);
            List<JToken> ss = null;
            FHIRParsedPath pp = req.parsePath();
            if (DATE_SORT_RESOURCES_SUPPORTED.Contains(pp.ResourceType) && req.Query.ContainsKey("_sort") && req.Query["_sort"].First().Contains("date"))
            {
                var fhirresp = response.toJToken();
                if (fhirresp.IsNullOrEmpty() || !((string)fhirresp["resourceType"]).Equals("Bundle") || !((string)fhirresp["type"]).Equals("searchset")) return new ProxyProcessResult(true, "", "", response);
                ss = new List<JToken>();
                addEntries((JArray)fhirresp["entry"],ss,log);
                //Process Next Pages until out or max array
                bool nextlink = !fhirresp["link"].IsNullOrEmpty() && ((string)fhirresp["link"].getFirstField()["relation"]).Equals("next");
                while (nextlink && ss.Count < MAX_ARRAY_SIZE)
                {
                    string nextpage = (string)fhirresp["link"].getFirstField()["url"];

                    var nextresult = await FHIRClient.CallFHIRServer(nextpage, "", "GET", log);
                    fhirresp = nextresult.toJToken();
                    if (fhirresp.IsNullOrEmpty() || !fhirresp.FHIRResourceType().Equals("Bundle") || !((string)fhirresp["type"]).Equals("searchset")) return new ProxyProcessResult(false, "Next Page not Returned or server error", "", nextresult);
                    addEntries((JArray)fhirresp["entry"], ss, log);
                    nextlink = !fhirresp["link"].IsNullOrEmpty() && ((string)fhirresp["link"].getFirstField()["relation"]).Equals("next");
                }
                ss.Sort(new DateSortedIndexComparar(req.Query["_sort"].First().Contains("-date")));
                fhirresp["entry"] = new JArray(ss.ToArray());
                fhirresp["link"] = new JArray();
              
                response.Content = fhirresp.ToString();
            }
            var retVal = new ProxyProcessResult();
            if (ss !=null && ss.Count >= MAX_ARRAY_SIZE)
            {
                retVal.ErrorMsg="Warning: _Sort exceeed or met MAX_ARRAY_SIZE results may not be accurate!";
                log.LogWarning("_Sort exceeed or met MAX_ARRAY_SIZE results may not be accurate!");
            }
            retVal.Response = response;
            return retVal;
        }
        private void addEntries(JArray entries,List<JToken> ss,ILogger log)
        {
            if (!entries.IsNullOrEmpty())
            {
                log.LogInformation($"Adding {entries.Count} bundle entries to sorted array...");
                ss.AddRange(entries);
            }
        }
    }
   
    internal class DateSortedIndexComparar : IComparer<JToken>
    {
        private bool isReverse = false;
        public DateSortedIndexComparar(bool reverse=false)
        {
            isReverse = reverse;
        }
        public int Compare(JToken x, JToken y)
        {
            string rt = x["resource"].FHIRResourceType();

            switch (rt)
            {
                case "Observation":
                case "DiagnosticReport":
                    DateTime? t1 = (DateTime?)x["resource"]["effectiveDateTime"];
                    if (!t1.HasValue && !x["resource"]["effectivePeriod"].IsNullOrEmpty()) t1 = (DateTime?)x["resource"]["effectivePeriod"]["start"];
                    DateTime? t2 = (DateTime?)y["resource"]["effectiveDateTime"];
                    if (!t2.HasValue && !y["resource"]["effectivePeriod"].IsNullOrEmpty()) t2 = (DateTime?)y["resource"]["effectivePeriod"]["start"];
                    if (!t1.HasValue) t1= DateTime.MinValue;
                    if (!t2.HasValue) t2 = DateTime.MinValue;
                    if (isReverse) return -(t1.Value.CompareTo(t2.Value));
                    return t1.Value.CompareTo(t2.Value);
                case "Encounter":
                case "CarePlan":
                case "CareTeam":
                case "EpisodeOfCare":
                    DateTime? t3 = (x["resource"]["period"].IsNullOrEmpty() || x["resource"]["period"]["start"].IsNullOrEmpty() ? DateTime.MinValue : (DateTime?)x["resource"]["period"]["start"]);
                    DateTime? t4 = (y["resource"]["period"].IsNullOrEmpty() || y["resource"]["period"]["start"].IsNullOrEmpty() ? DateTime.MinValue : (DateTime?)y["resource"]["period"]["start"]);
                    if (isReverse) return -(t3.Value.CompareTo(t4.Value));
                    return t3.Value.CompareTo(t4.Value);
                case "Claim":
                    DateTime? t5 = (x["resource"]["created"].IsNullOrEmpty() ? DateTime.MinValue : (DateTime?)x["resource"]["created"]);
                    DateTime? t6 = (y["resource"]["created"].IsNullOrEmpty() ? DateTime.MinValue : (DateTime?)y["resource"]["created"]);
                    if (isReverse) return -(t5.Value.CompareTo(t6.Value));
                    return t5.Value.CompareTo(t6.Value);
                default:
                    return 0;
                  

            }
           
        }
    }
}
