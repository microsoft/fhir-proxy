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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FHIRProxy.preprocessors
{
    //Implements a non-pageable patient level $everything with the first 5000 related entries 
    class EverythingPatientPreProcess : IProxyPreProcess
    {
        private static int MAX_ARRAY_SIZE = 5000;
        public async Task<ProxyProcessResult> Process(string requestBody, HttpRequest req, ILogger log, ClaimsPrincipal principal)
        {
            FHIRParsedPath pp = req.parsePath();
            if (req.Method.Equals("GET") && pp.ResourceType.SafeEquals("Patient") && !string.IsNullOrEmpty(pp.ResourceId) && pp.Operation.SafeEquals("$everything"))
            {
                ConcurrentBag<JToken> ss = new ConcurrentBag<JToken>();
                var nextresult = await FHIRClient.CallFHIRServer($"Patient?_id={pp.ResourceId}",null,"GET",req.Headers,log);
                var fhirresp = nextresult.toJToken();
                if (fhirresp.IsNullOrEmpty() || fhirresp["entry"].IsNullOrEmpty()) return new ProxyProcessResult(false, "Patient not found or server error", "", null);
                addEntries((JArray)fhirresp["entry"], ss, log);
                nextresult = await FHIRClient.CallFHIRServer($"Patient/{pp.ResourceId}/*?_count=1000",null,"GET",req.Headers,log);
                fhirresp = JObject.Parse(nextresult.Content.ToString());

                if (!fhirresp.IsNullOrEmpty() && !fhirresp["entry"].IsNullOrEmpty())
                {
                    addEntries((JArray)fhirresp["entry"], ss, log);
                    bool nextlink = !fhirresp["link"].IsNullOrEmpty() && ((string)fhirresp["link"].getFirstField()["relation"]).Equals("next");
                    while (nextlink && ss.Count < MAX_ARRAY_SIZE)
                    {
                        string nextpage = (string)fhirresp["link"].getFirstField()["url"];
                        nextresult = await FHIRClient.CallFHIRServer(nextpage,null,"GET",log);
                        fhirresp = JObject.Parse(nextresult.Content.ToString());
                        if (fhirresp.IsNullOrEmpty() || !fhirresp.FHIRResourceType().Equals("Bundle") || !((string)fhirresp["type"]).Equals("searchset")) return new ProxyProcessResult(false, "Next Page not Returned or server error", "", null);
                        addEntries((JArray)fhirresp["entry"], ss, log);
                        nextlink = !fhirresp["link"].IsNullOrEmpty() && ((string)fhirresp["link"].getFirstField()["relation"]).Equals("next");
                    }
                }
                fhirresp["entry"] = new JArray(ss.ToArray());
                fhirresp["link"] = new JArray();
                nextresult.Content = fhirresp;
                return new ProxyProcessResult(false, "", requestBody, nextresult);

                
            }
            return new ProxyProcessResult(true,"",requestBody,null);
        }
        private void addEntries(JArray entries, ConcurrentBag<JToken> ss, ILogger log)
        {
            if (!entries.IsNullOrEmpty())
            {
                log.LogInformation($"EverythingPatientPreProcess: Adding {entries.Count} bundle entries to everything array...");
                foreach(JToken tok in entries)
                {
                    ss.Add(tok);
                }
            }
        }
    }
}
