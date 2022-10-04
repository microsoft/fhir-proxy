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
using System.Security.Claims;
using StackExchange.Redis;
using System.Threading.Tasks;

namespace FHIRProxy.postprocessors
{
    class ConsentOptOutFilter : IProxyPostProcess
    {
        private static string ASSOCIATION_CACHE_PREFIX = "consent-opt-out-assc-";
        private static string PATIENT_DENY_ACTORS_PREFIX = "consent-opt-out-patdeny-";
       
       
        /* Opt-out: Default is for health information of patients to be included automatically, but the patient can opt out completely.
           Note: This is an access only policy, it does not prevent updates to the medical record as these could be valid */
        public async Task<ProxyProcessResult> Process(FHIRResponse response, HttpRequest req, ILogger log, ClaimsPrincipal principal)
        {
            if (!req.Method.Equals("GET") || !response.IsSuccess()) return new ProxyProcessResult(true, "", "", response);
            FHIRParsedPath pp = req.parsePath();
            FHIRResponse fr = response;
            //Load the consent category code from settings
            string consent_category = System.Environment.GetEnvironmentVariable("FP-MOD-CONSENT-OPTOUT-CATEGORY");
            if (string.IsNullOrEmpty(consent_category))
            {
                log.LogWarning("ConsentOptOutFilter: No value for FP-MOD-CONSENT-OPTOUT-CATEGORY in settings...Filter will not execute");
                return new ProxyProcessResult(true, "", "", fr);
            }
            if (fr == null || fr.Content == null || string.IsNullOrEmpty(fr.Content.ToString()))
            {
                log.LogInformation("ConsentOptOutFilter: No FHIR Response found in context...Nothing to filter");
                return new ProxyProcessResult(true, "", "", fr);
            }
            JObject result = JObject.Parse(fr.ToString());
            //Administrator is allowed access
            if (Utils.inServerAccessRole(req, "A")) return new ProxyProcessResult(true, "", "", fr);
            ClaimsIdentity ci = (ClaimsIdentity)principal.Identity;
            string aadten = ci.Tenant();
            string name = ci.ObjectId();
            var cache = Utils.RedisConnection.GetDatabase();
            List<string> associations = ((string)cache.StringGet($"{ASSOCIATION_CACHE_PREFIX}{aadten}-{name}")).DeSerializeList<string>();
            if (associations == null)
            {
                //Load Associations if not in cache
                associations = new List<string>();
                var table = Utils.getTable();
                //Practioner
                var practitioner = Utils.getEntity<LinkEntity>(table, "Practitioner", aadten + "-" + name);
                if (practitioner != null)
                {
                    associations.Add(practitioner.PartitionKey + "/" + practitioner.LinkedResourceId);
                    //Load organization from PractionerRoles
                    var prs = await FHIRClient.CallFHIRServer($"PractitionerRole?practitioner={practitioner.LinkedResourceId}", "","GET",req.Headers,log);
                    var ro = prs.toJToken();
                    if (ro.FHIRResourceType().Equals("Bundle"))
                    {
                        JArray entries = (JArray)ro["entry"];
                        if (!entries.IsNullOrEmpty())
                        {
                            foreach (JToken tok in entries)
                            {
                                associations.Add(tok["resource"].FHIRReferenceId());
                                if (!tok["resource"]["organization"].IsNullOrEmpty() && !tok["resource"]["organization"]["reference"].IsNullOrEmpty())
                                {
                                    associations.Add((string)tok["resource"]["organization"]["reference"]);
                                }
                            }
                        }
                    }
                }
                //RealtedPerson
                var related = Utils.getEntity<LinkEntity>(table, "RelatedPerson", aadten + "-" + name);
                if (related != null)
                {
                    associations.Add(related.PartitionKey + "/" + related.LinkedResourceId);
                }

                cache.StringSet($"{ASSOCIATION_CACHE_PREFIX}{aadten}-{name}", associations.Distinct().ToList().SerializeList(),TimeSpan.FromMinutes(Utils.GetIntEnvironmentVariable("CONSENT_CACHE_TTL_MINUTES","60")));
            }
           
            //Loop through results load any consent records and validate no opt-out default is to allow
            if (result.FHIRResourceType().Equals("Bundle"))
            {
                JArray entries = (JArray)result["entry"];
                if (!entries.IsNullOrEmpty())
                {
                    foreach (JToken tok in entries)
                    {
                        if (await denyAccess(tok["resource"], cache, associations, consent_category, req.Headers,log))
                        {
                            JObject denyObj = new JObject();
                            denyObj["resourceType"] = tok["resource"].FHIRResourceType();
                            denyObj["id"] = tok["resource"].FHIRResourceId();
                            denyObj["text"] = new JObject();
                            denyObj["text"]["status"] = "generated";
                            denyObj["text"]["div"] = "<div xmlns =\"http://www.w3.org/1999/xhtml\"><p>Patient has withheld access to this resource</p></div>";
                            tok["resource"] = denyObj;
                        }
                    }

                }
            }
            else if (!result.FHIRResourceType().Equals("OperationalOutcome"))
            {
                    if (await denyAccess(result, cache, associations, consent_category, req.Headers,log))
                    {
                            fr.Content = Utils.genOOErrResponse("access-denied", $"The patient has withheld access to this resource: {pp.ResourceType + (pp.ResourceId == null ? "" : "/" + pp.ResourceId)}");
                            fr.StatusCode = System.Net.HttpStatusCode.Unauthorized;
                            return new ProxyProcessResult(false, "access-denied", "", fr);
                    }
            }
            fr.Content = result.ToString();
            return new ProxyProcessResult(true, "", "", fr);
        }


        private async Task<bool> denyAccess(JToken resource, IDatabase cache, List<string> associations, string consentcat, IHeaderDictionary headers,ILogger log)
        {
            string patientId = null;
            string rt = resource.FHIRResourceType();
            //Check for Patient resource or load patient resource id from subject/patient member
            if (rt.Equals("Patient"))
            {
                patientId = rt + "/" + (string)resource["id"];
            }
            if (patientId == null)
            {
                patientId = (string)resource?["subject"]?["reference"];
                if (string.IsNullOrEmpty(patientId) || !patientId.StartsWith("Patient")) patientId = (string)resource?["patient"]?["reference"];
            }
            //If no patient id present assume not tied to patient do not filter;
            if (string.IsNullOrEmpty(patientId)) return false;
            //Load Cache if needed
            List<string> denyactors = ((string)cache.StringGet($"{PATIENT_DENY_ACTORS_PREFIX}{patientId}")).DeSerializeList<string>();
            if (denyactors == null)
            {
                //Fetch and Cache Deny access Consent Information
                var pid = patientId.Split("/")[1];
                var consentrecs = await FHIRClient.CallFHIRServer($"Consent?patient={pid}&category={consentcat}","","GET",headers,log);
                var result = consentrecs.toJToken();
                if (result.FHIRResourceType().Equals("Bundle"))
                {
                    JArray entries = (JArray)result["entry"];
                    if (!entries.IsNullOrEmpty())
                    {
                        denyactors = new List<string>();
                        foreach (JToken tok in entries)
                        {
                            var r = tok["resource"];
                            if (!r["provision"].IsNullOrEmpty())
                            {
                                //Check enforceemnt period
                                if (isEnforced(r["provision"]["period"]))
                                {

                                    string type = (string)r["provision"]["type"];
                                    //Load deny provisions only

                                    if (type != null && type.Equals("deny"))
                                    {
                                        //Load actor references to deny
                                        JArray actors = (JArray)r["provision"]["actors"];
                                        if (!actors.IsNullOrEmpty())
                                        {
                                            foreach (JToken actor in actors)
                                            {
                                                denyactors.Add((string)actor["reference"]);
                                            }
                                        }
                                        else
                                        {
                                            //Nobody specified so everybody is denied access this trumps all opt out advise
                                            denyactors.Clear();
                                            denyactors.Add("*");
                                            break;
                                        }
                                    }
                                }
                            }

                        }
                       
                    }
                    else
                    {
                        denyactors = new List<string>();
                    }
                    cache.StringSet($"{PATIENT_DENY_ACTORS_PREFIX}{patientId}", denyactors.Distinct().ToList().SerializeList(), TimeSpan.FromHours(Utils.GetIntEnvironmentVariable("CONSENT_CACHE_TTL_MINUTES", "60")));

                }

            }
            //If there is an empty actor array that means there is no deny provision specified so return false to allow access
            if (denyactors.Count == 0) return false;
            //It there is a wildcard in first entry then everyone is denied return true
            if (denyactors.First().Equals("*")) return true;
            //Check for intersection of denied actors and associations of current user if found access will be denied
            return associations.Select(x => x)
                             .Intersect(denyactors)
                             .Any();


        }
        private bool isEnforced(JToken period)
        {
            //Check Enforecement Period for Provision in Consent
            //no enforcement period specified so it's valid
            if (period.IsNullOrEmpty()) return true;
            //See if we are within the enforcement period
            //If start or end is not specified the lowest/greatest dates are assumed.
            DateTime? start = (DateTime)period["start"];
            if (!start.HasValue) start = DateTime.MinValue;
            DateTime? end = (DateTime)period["end"];
            if (!end.HasValue) end = DateTime.MaxValue;
            DateTime now = DateTime.Now;
            if (now >= start && now <= end) return true;
            return false;
        }

    }
    
}
