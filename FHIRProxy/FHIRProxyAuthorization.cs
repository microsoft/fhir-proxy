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
            req.Headers.Remove(Utils.PATIENT_CONTEXT_FHIRID);
          
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
                bool ispostcommand = req.Method.Equals("POST") && id.Equals("_search") && reader;
                //Claims Trump Role Access if scope claims are present then request must pass scope check
                List<string> smartClaims = ExtractSmartScopeClaims(ci);
                if (smartClaims.Count > 0)
                {
                    if (!PassedScopeCheck(req, ci, res, id, log))
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
                    if (!PassedRoleCheck(req, reader, writer, admin, ispostcommand))
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
                //Since we are proxying with service client need to ensure authenticated proxy principal is audited
               
                req.Headers.Add("X-MS-AZUREFHIR-AUDIT-USERID", ci.ObjectId());
                req.Headers.Add("X-MS-AZUREFHIR-AUDIT-TENANT", ci.Tenant());
                req.Headers.Add("X-MS-AZUREFHIR-AUDIT-SOURCE", req.HttpContext.Connection.RemoteIpAddress.ToString());
                req.Headers.Add("X-MS-AZUREFHIR-AUDIT-PROXY", passheader);
            }
           
          leave:
            return base.OnExecutingAsync(executingContext, cancellationToken);
        }
        private bool PassedRoleCheck(HttpRequest req,bool reader, bool writer, bool admin, bool ispostcommand)
        {
            if (req.Method.Equals("GET") && !admin && !reader) return false;
            if (!req.Method.Equals("GET") && !admin && !writer && !ispostcommand) return false;
            return true;
        }
        private List<string> ExtractSmartScopeClaims(ClaimsIdentity ci)
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
        private bool PassedScopeCheck(HttpRequest req, ClaimsIdentity ci,string res,string resid, ILogger log)
        {
            //Check for SMART Scopes (e.g. <patient/user>.<resource>|*.Read|Write|*)
                List<string> smartClaims = ExtractSmartScopeClaims(ci);
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
                        if ((s[1].Equals(res) || s[1].Equals("*")) && req.Method.Equals("GET") && canread && PassedContextScope(s[0], ci, res, resid, req, log))
                        {
                            return true;
                        }
                        else if ((s[1].Equals(res) || s[1].Equals("*")) && !req.Method.Equals("GET") && canwrite && PassedContextScope(s[0], ci, res, resid, req, log))
                        {
                            return true;
                        }
                    }
                    else
                    {
                        log.LogWarning($"FHIRProxyAuthorization:Claim {claim} is not a SMART claim. Will not match a scope. Expected format is [patient|user].[resourceType|*].[read|write|*]");
                    }
                }
            
                //didn't pass scope checks
            
            return false;
           
        }
        private bool PassedContextScope(string scope, ClaimsIdentity ci,string res,string id,HttpRequest req, ILogger log)
        {
            //Load Patient Compartment Resources 
            PatientCompartment comp = PatientCompartment.Instance();
            //For Patient Scope we will see if there is a patient claim in the token with a FHIR Logical Id or External Id
            //and the id matches and the query is scoped down to the matching patient.
            if (scope.StartsWith("patient", StringComparison.InvariantCultureIgnoreCase))
            {
                //See if resource is patient compartment to check for query scope no access for non-patient compartment resources...
                if (!comp.isPatientCompartmentResource(res)) return false;
                
                //Check if patient is in context already
                string fhirid = null;
                if (req.Headers.ContainsKey(Utils.PATIENT_CONTEXT_FHIRID))
                {
                    fhirid = req.Headers[Utils.PATIENT_CONTEXT_FHIRID].FirstOrDefault();
                }
                else
                {
                    IEnumerable<Claim> claims = ci.Claims;
                    fhirid = claims.Where(c => c.Type == Utils.GetEnvironmentVariable("FP-PATIENT-FHIR-ID-CLAIM", "fhirpatientid")).Select(c => c.Value).SingleOrDefault();
                    if (string.IsNullOrEmpty(fhirid))
                    {
                        //See if OID has been linked to a patient if no persisted FHIR ID claim specified
                        fhirid = GetFHIRIdFromOID(ci, "Patient", log);
                        if (string.IsNullOrEmpty(fhirid))
                        {
                            log.LogWarning("FHIRProxyAuthorization: Scope context is for Patient but no Patient Identity Claim or Link found");
                            return false;
                        }
                    }
                    //Set fhirid in context for post filtering
                    req.Headers.Add(Utils.PATIENT_CONTEXT_FHIRID, fhirid);
                }
                //If Patient resource must have Patient Identity Claim for FHIR Logical Id in Token and must match id parameter
                if (res.Equals("Patient"))
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        log.LogInformation($"FHIRProxyAuthorization: PassedContextScope: Checking {id} and {fhirid}");
                        if (!string.IsNullOrEmpty(fhirid) && fhirid.Equals(id)) return true;
                    } else {
                        if (!string.IsNullOrEmpty(fhirid)) {
                            log.LogInformation($"FHIRProxyAuthorization: PassedContextScope: Checking for {fhirid} in {req.QueryString.Value}");
                            return (req.QueryString.Value.Contains($"_id={fhirid}") || req.QueryString.Value.Contains($"link=Patient/{fhirid}"));
                        }
                    }
                    log.LogWarning($"FHIRProxyAuthorization: PassedContextScope: For patient resource must have FHIR Id claim or link and must match the id resource of request {id}-{fhirid}");
                    return false;
                } else
                {
                    //Check queries if id is not specified
                    if (string.IsNullOrEmpty(id))
                    {
                        log.LogInformation($"FHIRProxyAuthorization: PassedContextScope: Looking for Patient scope in query string {req.QueryString.Value}");
                        IQueryCollection querycol = req.Query;
                        //Get the list of parms to check for patient scope
                        string[] parms = comp.GetPatientParametersForResourceType(res);
                        //See if this resource query has expected parm that is constrained by Patient
                        foreach (string p in parms)
                        {
                            string qid = querycol.Get<string>($"{p}", @default: "");
                            if (!string.IsNullOrEmpty(qid) && !string.IsNullOrEmpty(fhirid) && qid.Contains(fhirid)) return true;
                        }
                    } else
                    {
                        //Allow individual retrieve will be checked against patient context on response
                        return true;
                    }
                    log.LogWarning("FHIRProxyAuthorization: PassedContextScope not match internal or external identifier from link/claim in query parms or request is not constrained to patient context...");
                    return false;
                }
            } else if (scope.StartsWith("user",StringComparison.InvariantCultureIgnoreCase))
            {
                //Will pass on user context scope but will need to be filtered by Pre/Post Module Logic
                return true;
            } else if (scope.StartsWith("system", StringComparison.InvariantCultureIgnoreCase))
            {
                //Will pass on system scope but will need to be filtered by Pre/Post Module Logic
                return true;
            }
            return false;
        }
        public static string GetFHIRIdFromOID(ClaimsIdentity ci, string res,ILogger log)
        {
            string aadten = (string.IsNullOrEmpty(ci.Tenant()) ? "Unknown" : ci.Tenant());
            string oid = ci.ObjectId();
            if (string.IsNullOrEmpty(oid))
            {
                log.LogWarning("FHIRProxyAuthorization: No OID claim found in Claims Identity!");
                return null;
            }
            var table = Utils.getTable();
            var entity = Utils.getLinkEntity(table, res, aadten + "-" + oid);
            if (entity != null)
            {
                return entity.LinkedResourceId;
            }
            log.LogInformation($"FHIRProxyAuthorization: No linked FHIR {res} Resource for oid:{oid}");
            return null;
        }

    }
}
