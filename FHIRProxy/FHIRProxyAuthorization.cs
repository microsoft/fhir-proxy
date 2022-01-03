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
using Microsoft.Azure.WebJobs.Host;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json.Linq;

namespace FHIRProxy
{
    class FHIRProxyAuthorization : FunctionInvocationFilterAttribute
    {
        public override Task OnExecutingAsync(FunctionExecutingContext executingContext, CancellationToken cancellationToken)
        {
            var req = executingContext.Arguments.First().Value as HttpRequest;
          
            ILogger log = executingContext.Arguments["log"] as ILogger;
           
            ClaimsPrincipal principal = executingContext.Arguments["principal"] as ClaimsPrincipal;
            ClaimsIdentity ci = (ClaimsIdentity)principal.Identity;
           
            bool admin = ci.IsInFHIRRole(Environment.GetEnvironmentVariable("FP-ADMIN-ROLE"));
            bool reader = ci.IsInFHIRRole(Environment.GetEnvironmentVariable("FP-READER-ROLE"));
            bool writer = ci.IsInFHIRRole(Environment.GetEnvironmentVariable("FP-WRITER-ROLE"));
            
            string inroles = "";
            if (admin) inroles += "A";
            if (reader) inroles += "R";
            if (writer) inroles += "W";
            req.Headers.Remove(Utils.AUTH_STATUS_HEADER);
            req.Headers.Remove(Utils.AUTH_STATUS_MSG_HEADER);
            req.Headers.Remove(Utils.FHIR_PROXY_ROLES);
            req.Headers.Remove(Utils.FHIR_PROXY_SMART_SCOPE);
   
          
            if (!principal.Identity.IsAuthenticated)
            {
                req.Headers.Add(Utils.AUTH_STATUS_HEADER, ((int)System.Net.HttpStatusCode.Unauthorized).ToString());
                req.Headers.Add(Utils.AUTH_STATUS_MSG_HEADER, "Principal is not Authenticated");
                goto leave;
            }//
            //Set Authentication Status/Roles
            req.Headers.Add(Utils.AUTH_STATUS_HEADER, ((int)System.Net.HttpStatusCode.OK).ToString());
            req.Headers.Add(Utils.FHIR_PROXY_ROLES, inroles);
            //Access checks for /fhir proxy endpoint
            if (executingContext.FunctionName.Equals("ProxyFunction"))
            {
                FHIRParsedPath pp = req.parsePath();
                string id = pp.ResourceId;
                string res = pp.ResourceType;
                if (id == null) id = "";
               
                //Claims Trump Role Access if scope claims are present then request must pass scope check
                List<string> smartClaims = ExtractSmartScopeClaims(ci);
                if (smartClaims.Count > 0)
                {
                    if (!PassedScopeCheck(req, smartClaims, res, id, log))
                    {
                        req.Headers.Remove(Utils.AUTH_STATUS_HEADER);
                        req.Headers.Remove(Utils.AUTH_STATUS_MSG_HEADER);
                        req.Headers.Add(Utils.AUTH_STATUS_HEADER, ((int)System.Net.HttpStatusCode.Unauthorized).ToString());
                        req.Headers.Add(Utils.AUTH_STATUS_MSG_HEADER, "Principal did not pass claims scope for this request.");
                        goto leave;
                    }
                }
                else
                {
                    //No smart claims present use role access
                    if (!PassedRoleCheck(isFHIRGet(req,id), reader, writer, admin))
                    {
                        req.Headers.Remove(Utils.AUTH_STATUS_HEADER);
                        req.Headers.Remove(Utils.AUTH_STATUS_MSG_HEADER);
                        req.Headers.Add(Utils.AUTH_STATUS_HEADER, ((int)System.Net.HttpStatusCode.Unauthorized).ToString());
                        req.Headers.Add(Utils.AUTH_STATUS_MSG_HEADER, "Principal is not in an authorized role");
                        goto leave;
                    }
                }
                string passheader = req.Headers["X-MS-AZUREFHIR-AUDIT-PROXY"];
                if (string.IsNullOrEmpty(passheader)) passheader = "fhir-proxy";
                string auditsource = req.Headers["X-MS-AZUREFHIR-AUDIT-SOURCE"];
                if (string.IsNullOrEmpty(auditsource)) auditsource = req.HttpContext.Connection.RemoteIpAddress.ToString();
                //Since we are proxying with service client need to ensure authenticated proxy principal is audited
               
                req.Headers.Add("X-MS-AZUREFHIR-AUDIT-USERID", ci.ObjectId());
                req.Headers.Add("X-MS-AZUREFHIR-AUDIT-TENANT", ci.Tenant());
                req.Headers.Add("X-MS-AZUREFHIR-AUDIT-SOURCE", auditsource);
                req.Headers.Add("X-MS-AZUREFHIR-AUDIT-PROXY", passheader);
            }
           
          leave:
            return base.OnExecutingAsync(executingContext, cancellationToken);
        }
        public static bool isFHIRGet(HttpRequest req,string resourceid)
        {
            string r = "";
            if (!string.IsNullOrEmpty(resourceid)) r = resourceid;
            return req.Method.Equals("GET") || (req.Method.Equals("POST") && r.Equals("_search"));
        }
        private bool PassedRoleCheck(bool isGet,bool reader, bool writer, bool admin)
        {
            if (isGet && (admin || reader)) return true;
            if (!isGet && (admin || writer)) return true;
            return false;
        }
        public static List<string> ExtractSmartScopeClaims(ClaimsIdentity ci)
        {
            List<string> retVal = new List<string>();
            IEnumerable<Claim> claims = ci.Claims.Where(x => x.Type == "http://schemas.microsoft.com/identity/claims/scope");
            if (claims == null || claims.Count() == 0) claims = ci.Claims.Where(x => x.Type == "scp");
            if (claims == null || claims.Count() == 0)
            {
                return retVal;
            }
            foreach (Claim c in claims)
            {
                string[] claimsinentry = c.Value.Split(" ");
                foreach (string claim in claimsinentry)
                {
                    string[] s = claim.Split(".");
                    if (s.Length == 3 && (s[0].Equals("patient") || s[0].Equals("user") || s[0].Equals("system")))
                    {
                        retVal.Add(claim);
                    }
                }
            }
            return retVal;
        }
        private bool PassedScopeCheck(HttpRequest req, List<string> smartClaims,string res,string resid, ILogger log)
        {
                bool isGet = isFHIRGet(req, resid);
                //Check for SMART Scopes (e.g. <system/patient/user>.<resource>|*.Read|Write|*)
                foreach (string claim in smartClaims)
                {
                    string[] s = claim.Split(".");
                    log.LogInformation($"FHIRProxyAuthorization: Checking scope: {claim}");
                    if (s.Length > 2 && !string.IsNullOrEmpty(s[0]) && !string.IsNullOrEmpty(s[1]) && !string.IsNullOrEmpty(s[2]))
                    {
                        if (s[0].StartsWith("launch")) continue; //Getting to access claims
                        bool canread = (s[2].Equals("read", StringComparison.InvariantCultureIgnoreCase) || s[2].Equals("*", StringComparison.InvariantCultureIgnoreCase));
                        bool canwrite = (s[2].Equals("write", StringComparison.InvariantCultureIgnoreCase) || s[2].Equals("*", StringComparison.InvariantCultureIgnoreCase));
                        log.LogInformation($"FHIRProxyAuthorization: Checking request {res} against claim scope {s[1]} CanRead:{canread} CanWrite{canwrite}");
                        if ((s[1].Equals(res) || s[1].Equals("*")) && isGet && canread)
                        {
                            return true;
                        }
                        else if ((s[1].Equals(res) || s[1].Equals("*")) && !isGet && canwrite)
                        {
                            return true;
                        } else if (s[1].Equals("Bundle") && string.IsNullOrEmpty(res) && req.Method.Equals("POST") && canwrite)
                        {
                            return true;
                        }
                    }
                    else
                    {
                        log.LogWarning($"FHIRProxyAuthorization:Claim {claim} is not a SMART claim. Will not match a scope. Expected format is [patient|user].[resourceType|*].[read|write|*]");
                    }
                }
            
                //didn't pass basic scope checks
            
            return false;
           
        }
        public static UserScopeResult ResultAllowedForUserScope(JToken tok, ClaimsPrincipal principal, ILogger log)
        {
            ClaimsIdentity ci = (ClaimsIdentity)principal.Identity;
            return ResultAllowedForUserScope(tok, ci, log);
        }
        public static UserScopeResult ResultAllowedForUserScope(JToken tok, ClaimsIdentity ci, ILogger log)
        {
            if (tok == null) return new UserScopeResult(true);
            if (tok.FHIRResourceType().Equals("OperationOutcome")) return new UserScopeResult(true);
            List<string> smartClaims = ExtractSmartScopeClaims(ci);
            //No Claims in token then pass scope context by default
            if (smartClaims == null || smartClaims.Count == 0) return new UserScopeResult(true);
            string claimstring = String.Join(" ", smartClaims);
            //Load fhirUser placed in cache by SMARTProxyToken issuer.
            string fhiruser = GetCachedFHIRUser(ci);
            if (string.IsNullOrEmpty(fhiruser))
            {
                return new UserScopeResult(false, $"fhirUser is not in context...SMART Scopes require a fhirUser claim see fhir proxy documentation");
            }
            PatientCompartment comp = PatientCompartment.Instance();
            if (!tok.FHIRResourceType().Equals("Bundle"))
            {
                return CheckResourceUserScope(tok, ci, comp, claimstring, log);
            }
            else
            {
                var resourcesToCheck = (JArray)tok["entry"];
                if (!resourcesToCheck.IsNullOrEmpty())
                {
                    //Any entry not related to patient in bundle will cause scope reject for entire query
                    foreach (JToken t in resourcesToCheck)
                    {
                        JToken resource = t["resource"];
                        var rslt = CheckResourceUserScope(resource, ci, comp, claimstring, log);
                        if (!rslt.Result) return rslt;
                    }
                    
                }
                return new UserScopeResult(true);
            }
        }
        public static UserScopeResult CheckResourceUserScope(JToken resource,ClaimsIdentity ci, PatientCompartment comp,string claimstring,  ILogger log)
        {
            string fhiruser = GetCachedFHIRUser(ci);
            if (string.IsNullOrEmpty(fhiruser))
            {
                return new UserScopeResult(false, $"fhirUser is not in access_token claims...SMART Scopes require a fhirUser claim see fhir proxy documentation");
            }
            string resourceType = resource.FHIRResourceType();
                if (fhiruser.StartsWith("Patient/"))
                {
                    //TODO: Is restricting to patient compartment results enough if not recheck search bundle results against scopes.
                    //Patient Scope for resource can they read and see resource
                    /*if (!claimstring.Contains($"patient.{resourceType}.read") &&
                        !claimstring.Contains($"patient.{resourceType}.*") &&
                        !claimstring.Contains($"patient.*.*"))
                    {
                        return new UserScopeResult(false, $"User in context {fhiruser} does not have scope permission to read resource {resourceType} contained in this result");
                    }*/
                    //See if resource is patient compartment to check for query scope no access for non-patient compartment resources...
                    if (!comp.isPatientCompartmentResource(resourceType)) return new UserScopeResult(false, $"Resource type {resourceType} is not in the Patient Compartment definition");
                    //If Patient resource must have Patient Identity Claim for FHIR Logical Id in Token and must match id parameter
                    if (resourceType.Equals("Patient"))
                    {
                        if (fhiruser.Contains(resource.FHIRResourceId())) return new UserScopeResult(true);
                        else return new UserScopeResult(false, $"Patient resource does not match patient context scope Context: {fhiruser} Resource: {resource.FHIRResourceId()}");
                    }
                    //Get the list of parms to check for patient scope
                    string[] parms = comp.GetPatientParametersForResourceType(resourceType);
                    //See if this resource has a reference to the patient in user context
                    if (parms != null && parms.Length > 0)
                    {
                        bool hasPatientRef = false;
                        foreach (string p in parms)
                        {
                            string rslt = (resource[p].IsNullOrEmpty() ? "" : resource[p].ToString());
                            if (!string.IsNullOrEmpty(rslt) && rslt.Contains(fhiruser)) hasPatientRef = true;
                        }
                        if (!hasPatientRef)
                        {
                            return new UserScopeResult(false, $"Could not find Patient Reference {fhiruser} in reference fields {String.Join(",", parms)} of resource {resource.FHIRResourceType()}/{resource.FHIRResourceId()}");
                        }
                    }
                }
                else if (fhiruser.StartsWith("Practitioner/"))
                {
                    //Will pass on user context scope but will need to be filtered by Pre/Post Module Logic
                    if (!claimstring.Contains($"user.{resourceType}.read") &&
                       !claimstring.Contains($"user.{resourceType}.*") &&
                       !claimstring.Contains($"user.*.*"))
                    {
                        return new UserScopeResult(false, $"User in context {fhiruser} does not have scope permission to read resource {resourceType} contained in this result");
                    }
                }
                else if (fhiruser.StartsWith("System/"))
                {
                    //Will pass on system scope but will need to be filtered by Pre/Post Module Logic
                    if (!claimstring.Contains($"system.{resourceType}.read") &&
                      !claimstring.Contains($"system.{resourceType}.*") &&
                      !claimstring.Contains($"system.*.*"))
                    {
                        return new UserScopeResult(false, $"User in context {fhiruser} does not have scope permission to read resource {resourceType} contained in this result");
                    }
                }
                //All Checks Passed
                return new UserScopeResult(true);
        }
        public static string GetFHIRIdFromFHIRUser(string fhiruser)
        {
            if (fhiruser == null || !fhiruser.Contains("/")) return fhiruser;
            return fhiruser.Substring(fhiruser.LastIndexOf("/") +1);
        }
        public static string GetCachedFHIRUser(ClaimsIdentity ci)
        {
            var cache = Utils.RedisConnection.GetDatabase();
            string aadten = ci.Tenant();
            string oid = ci.ObjectId();
            if (string.IsNullOrEmpty(aadten) || string.IsNullOrEmpty(oid)) return null;
            return cache.StringGet($"usermap-{aadten}-{oid}");
        }
        public static string GetMappedFHIRUser(ClaimsIdentity ci,ILogger log)
        {
            //Check the fhirUser claim
            string aadten = (string.IsNullOrEmpty(ci.Tenant()) ? "Unknown" : ci.Tenant());
            string oid = ci.ObjectId();
            if (string.IsNullOrEmpty(oid))
            {
                log.LogWarning("GetMappedFHIRUser: No OID claim found in Claims Identity!");
                return null;
            }
            var table = Utils.getTable();
            //Check for Patient Association
            var entity = Utils.getLinkEntity(table, "Patient", aadten + "-" + oid);
            if (entity != null)
            {
                return $"Patient/{entity.LinkedResourceId}";
            }
            //Check for Practitioner Association
            entity = Utils.getLinkEntity(table, "Practitioner", aadten + "-" + oid);
            if (entity != null)
            {
                return $"Practitioner/{entity.LinkedResourceId}";
            }
            //Check for Practitioner Association
             entity = Utils.getLinkEntity(table, "RelatedPerson", aadten + "-" + oid);
            if (entity != null)
            {
                return $"RelatedPerson/{entity.LinkedResourceId}";
            }
            log.LogInformation($"FHIRProxyAuthorization: No linked FHIR Resource for oid:{oid}");
            return null;
        }

    }
}
