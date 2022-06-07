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
using System.Text;
using System.Threading.Tasks;

namespace FHIRProxy.postprocessors
{
    class ParticipantFilterPostProcess : IProxyPostProcess
    {
        public async Task<ProxyProcessResult> Process(FHIRResponse response, HttpRequest req, ILogger log, ClaimsPrincipal principal)
        {
           
            if (!req.Method.Equals("GET") || !response.IsSuccess()) return new ProxyProcessResult(true,"","", response);
            FHIRResponse fr = response;
            FHIRParsedPath pp = req.parsePath();
            JObject result = (fr == null ? null : JObject.Parse(fr.ToString()));
            if (fr == null)
            {
                log.LogInformation("ParticipantFilter: No FHIR Response found in context");
                return new ProxyProcessResult();
            }
           
            //Needed Variables
            ClaimsIdentity ci = (ClaimsIdentity)principal.Identity;
            string aadten = ci.Tenant();
            string name = ci.ObjectId();

            bool admin = Utils.inServerAccessRole(req,"A");
            List<string> resourceidentities = new List<string>();
            List<string> inroles = ci.Roles();
            List<string> fhirresourceroles = new List<string>();
            fhirresourceroles.AddRange(Environment.GetEnvironmentVariable("FP-PARTICIPANT-ACCESS-ROLES").Split(","));
            fhirresourceroles.AddRange(Environment.GetEnvironmentVariable("FP-PATIENT-ACCESS-ROLES").Split(","));
            Dictionary<string, bool> porcache = new Dictionary<string,bool>();
            var table = Utils.getTable();
            //Load linked Resource Identifiers from linkentities table for each known role the user is in
            foreach (string r in inroles)
            {
                if (fhirresourceroles.Any(r.Equals))
                {
                    var entity = Utils.getEntity<LinkEntity>(table, r, aadten + "-" + name);
                    if (entity != null)
                    {
                        resourceidentities.Add(r + "/" + entity.LinkedResourceId);
                    }
                }
            }
            if (!admin && !ci.IsInFHIRRole(Environment.GetEnvironmentVariable("FP-GLOBAL-ACCESS-ROLES")))
            {
                if (((string)result["resourceType"]).Equals("Bundle"))
                {
                    JArray entries = (JArray)result["entry"];
                    if (!entries.IsNullOrEmpty())
                    {
            
                        foreach (JToken entry in entries)
                        {
                            if (!await IsAParticipantOrPatient(entry["resource"], resourceidentities, porcache, req.Headers,log))
                            {
                                JObject denyObj = new JObject();
                                denyObj["resourceType"] = entry["resource"].FHIRResourceType();
                                denyObj["id"] = entry["resource"].FHIRResourceId();
                                denyObj["text"] = new JObject();
                                denyObj["text"]["status"] = "generated";
                                denyObj["text"]["div"] = "<div xmlns =\"http://www.w3.org/1999/xhtml\"><p>You do not have access to data contained in this resource</p></div>";
                                entry["resource"] = denyObj;
                            }

                        }
                    }
                }
                else if (!((string)result["resourceType"]).Equals("OperationalOutcome"))
                {
                    if (!await IsAParticipantOrPatient(result, resourceidentities, porcache, req.Headers,log))
                    {
                        fr.Content = Utils.genOOErrResponse("access-denied", $"You are not an authorized Paticipant in care and cannot access this resource: {pp.ResourceType + (pp.ResourceId == null ? "" : "/" + pp.ResourceId)}");
                        fr.StatusCode = System.Net.HttpStatusCode.Unauthorized;
                        return new ProxyProcessResult(false, "access-denied", "",fr);
                    }
                }
            }
            fr.Content = result.ToString();
            return new ProxyProcessResult(true, "", "",fr);
        }
        private static async Task<bool> IsAParticipantOrPatient(JToken resource, IEnumerable<string> knownresourceIdentities, Dictionary<string, bool> porcache, IHeaderDictionary auditheaders,ILogger log)
        {

            string patientId = null;
            string encounterId = null;
            JToken patient = null;
            JToken encounter = null;

            string rt = (string)resource["resourceType"];
            //Check for Patient resource or load patient resource from subject member
            if (rt.Equals("Patient"))
            {
                patient = resource;
                patientId = rt + "/" + (string)resource["id"];
            }
            if (patient == null)
            {
                patientId = (string)resource?["subject"]?["reference"];
                if (string.IsNullOrEmpty(patientId)) patientId = (string)resource?["patient"]?["reference"];
            }
            if (rt.Equals("Encounter"))
            {
                encounter = resource;
                encounterId = rt + "/" + (string)resource["id"];
                patientId = (string)resource?["subject"]?["reference"];
            }
            if (encounter == null)
            {
                encounterId = (string)resource?["encounter"]?["reference"];
            }
            //If no patient or encounter records present assume not tied to patient do not filter;
            if (patientId == null && encounterId == null) return true;
            //See if patientId is in POR Cache
            if (!string.IsNullOrEmpty(patientId) && porcache.ContainsKey(patientId)) return porcache[patientId];
            if (!string.IsNullOrEmpty(encounterId) && porcache.ContainsKey(encounterId)) return porcache[encounterId];
            //Load the patient if needed
            if (patient == null)
            {
                if (!string.IsNullOrEmpty(patientId))
                {
                    var pat = await FHIRClient.CallFHIRServer(patientId, null, "GET", auditheaders,log);
                    JObject temp = JObject.Parse((string)pat.Content);
                    if (temp != null && ((string)temp["resourceType"]).Equals("Patient"))
                    {
                        patient = temp;
                    }
                    else
                    {
                        porcache[patientId] = false;
                        return false;
                    }

                }
                else if (!string.IsNullOrEmpty(encounterId) && patient == null)
                {
                    var enc = await FHIRClient.CallFHIRServer(encounterId, null, "GET", auditheaders,log);
                    if (enc != null)
                    {
                        JObject temp = JObject.Parse((string)enc.Content);
                        if (temp != null && ((string)temp["resourceType"]).Equals("Encounter") && (string)temp["subject"]?["reference"] != null)
                        {
                            patientId = (string)temp["subject"]?["reference"];
                            var pat = await FHIRClient.CallFHIRServer(patientId, null, "GET", auditheaders,log);
                            JObject temp1 = JObject.Parse((string)pat.Content);
                            if (temp1 != null && ((string)temp1["resourceType"]).Equals("Patient"))
                            {
                                patient = temp1;
                            }
                            else
                            {
                                porcache[patientId] = false;
                                return false;
                            }
                        }
                        else
                        {
                            porcache[encounterId] = false;
                            return false;
                        }
                    }

                }
                else
                {
                    //Cannot Determine/Find a Patient or Encounter reference assume it's not a patient reference
                    return true;
                }
            }


            foreach (string rid in knownresourceIdentities)
            {
                if (rid.StartsWith("Patient"))
                {
                    string pid = rid;
                    if (pid.Equals(patientId))
                    {
                        porcache[patientId] = true;
                        if (!string.IsNullOrEmpty(encounterId)) porcache[encounterId] = true;
                        return true;
                    }
                }
                else if (rid.StartsWith("Practitioner"))
                {
                    if (patient["generalPractitioner"] != null)
                    {
                        var gp_s = from gp in patient["generalPractitioner"]
                                   where (string)gp["reference"] == rid
                                   select (string)gp["reference"];
                        if (gp_s != null && gp_s.Count() > 0)
                        {
                            porcache[patientId] = true;
                            if (!string.IsNullOrEmpty(encounterId)) porcache[encounterId] = true;
                            return true;
                        }

                    }
                    string pid = rid.Split("/")[1];
                    string patid = (string)patient["id"];
                    var porencs = await FHIRClient.CallFHIRServer($"Encounter?patient={patid}&participant={pid}", null,"GET", auditheaders,log);
                    if (porencs != null)
                    {
                        JObject temp2 = JObject.Parse((string)porencs.Content);
                        if (temp2 != null && ((string)temp2["resourceType"]).Equals("Bundle"))
                        {
                            JArray entries = (JArray)temp2["entry"];
                            if (entries != null && entries.Count > 0)
                            {
                                porcache[patientId] = true;
                                if (!string.IsNullOrEmpty(encounterId)) porcache[encounterId] = true;
                                return true;
                            }
                        }
                    }
                }
            }
            porcache[patientId] = false;
            if (!string.IsNullOrEmpty(encounterId)) porcache[encounterId] = false;
            return false;
        }
    }
    
}
