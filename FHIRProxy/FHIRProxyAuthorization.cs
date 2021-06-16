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
            FHIRParsedPath pp = req.parsePath();
            string id = pp.ResourceId;
            string res = pp.ResourceType;
            ILogger log = executingContext.Arguments["log"] as ILogger;
            if (id == null) id = "";
            ClaimsPrincipal principal = executingContext.Arguments["principal"] as ClaimsPrincipal;
            ClaimsIdentity ci = (ClaimsIdentity)principal.Identity;
           
            bool admin = ci.IsInFHIRRole(Environment.GetEnvironmentVariable("FP-ADMIN-ROLE"));
            bool reader = ci.IsInFHIRRole(Environment.GetEnvironmentVariable("FP-READER-ROLE"));
            bool writer = ci.IsInFHIRRole(Environment.GetEnvironmentVariable("FP-WRITER-ROLE"));
            bool ispostcommand = req.Method.Equals("POST") && id.Equals("_search") && reader;
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
            }
            //Claims Trump Role Access if scope claims are present then request must pass scope check
            if (hasScopeClaim(ci))
            {
                if (!PassedScopeCheck(req, ci, res, id, log))
                {
                    req.Headers.Add(Utils.AUTH_STATUS_HEADER, ((int)System.Net.HttpStatusCode.Unauthorized).ToString());
                    req.Headers.Add(Utils.AUTH_STATUS_MSG_HEADER, "Principal did not pass claims scope for this request.");
                    goto leave;
                }
            } else {
                //No claims present use role access
                if (!PassedRoleCheck(req, reader, writer, admin, ispostcommand))
                {
                    req.Headers.Add(Utils.AUTH_STATUS_HEADER, ((int)System.Net.HttpStatusCode.Unauthorized).ToString());
                    req.Headers.Add(Utils.AUTH_STATUS_MSG_HEADER, "Principal is not in an authorized role");
                    goto leave;
                }
            }
            string passheader = req.Headers["X-MS-AZUREFHIR-AUDIT-PROXY"];
            if (string.IsNullOrEmpty(passheader)) passheader = executingContext.FunctionName;
            //Since we are proxying with service client need to ensure authenticated proxy principal is audited
            req.Headers.Add(Utils.AUTH_STATUS_HEADER, ((int)System.Net.HttpStatusCode.OK).ToString());
            req.Headers.Add(Utils.FHIR_PROXY_ROLES, inroles);
            req.Headers.Add("X-MS-AZUREFHIR-AUDIT-USERID", ci.ObjectId());
            req.Headers.Add("X-MS-AZUREFHIR-AUDIT-TENANT", ci.Tenant());
            req.Headers.Add("X-MS-AZUREFHIR-AUDIT-SOURCE", req.HttpContext.Connection.RemoteIpAddress.ToString());
            req.Headers.Add("X-MS-AZUREFHIR-AUDIT-PROXY", passheader);
          leave:
            return base.OnExecutingAsync(executingContext, cancellationToken);
        }
        private bool PassedRoleCheck(HttpRequest req,bool reader, bool writer, bool admin, bool ispostcommand)
        {
            if (req.Method.Equals("GET") && !admin && !reader) return false;
            if (!req.Method.Equals("GET") && !admin && !writer && !ispostcommand) return false;
            return true;
        }
        private bool hasScopeClaim(ClaimsIdentity ci)
        {
            IEnumerable<Claim> claims = ci.Claims.Where(x => x.Type == "http://schemas.microsoft.com/identity/claims/scope");
            if (claims == null || claims.Count() == 0) claims = ci.Claims.Where(x => x.Type == "scp");
            return (claims != null && claims.Count() > 0);
        }
        private bool PassedScopeCheck(HttpRequest req, ClaimsIdentity ci,string res,string resid, ILogger log)
        {
            //Check for SMART Scopes (e.g. <patient/user>.<resource>|*.Read|Write|*)
            IEnumerable<Claim> claims = ci.Claims.Where(x => x.Type == "http://schemas.microsoft.com/identity/claims/scope");
            if (claims == null || claims.Count() == 0) claims = ci.Claims.Where(x => x.Type == "scp");
            if (claims==null || claims.Count()==0)
            {
                log.LogWarning($"FHIRProxyAuthorization:Claims check. There are no scope claims in the access token.!");
                return false;
            }
            log.LogInformation($"FHIRProxyAuthorization:Begin Claims check. There are {claims.Count()} scope claims entries");
            foreach (Claim c in claims)
            {
                string[] claimsinentry = c.Value.Split(" ");
                foreach (string claim in claimsinentry)
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
            }

            
            return false;
           
        }
        private bool PassedContextScope(string scope, ClaimsIdentity ci,string res,string id,HttpRequest req, ILogger log)
        {
            //For Patient Scope we will see if there is a patient claim in the token with a FHIR Logical Id or External Id
            //and the id matches and the query is scoped down to the matching patient.
            if (scope.StartsWith("patient", StringComparison.InvariantCultureIgnoreCase))
            {
                IEnumerable<Claim> claims = ci.Claims;
                string fhirid = claims.Where(c => c.Type == Utils.GetEnvironmentVariable("FP-PATIENT-FHIR-ID-CLAIM", "fhirpatientid")).Select(c => c.Value).SingleOrDefault();
                string fhirextid = claims.Where(c => c.Type == Utils.GetEnvironmentVariable("FP-PATIENT-FHIR-EXTID-CLAIM", "fhirpatientextid")).Select(c => c.Value).SingleOrDefault();
                if (string.IsNullOrEmpty(fhirid) && string.IsNullOrEmpty(fhirextid))
                {
                    //See if OID has been linked to a patient if no persisted FHIR ID claim specified
                    fhirid = GetFHIRIdFromOID(ci, "Patient", log);
                    if (string.IsNullOrEmpty(fhirid))
                    {
                        log.LogWarning("FHIRProxyAuthorization: Scope context is for Patient but no Patient Identity Claim or Link found");
                        return false;
                    }
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
                            log.LogInformation($"FHIRProxyAuthorization: PassedContextScope: Checking _id={fhirid} in {req.QueryString.Value}");
                            return req.QueryString.Value.Contains($"_id={fhirid}");
                        }
                    }
                    log.LogWarning($"FHIRProxyAuthorization: PassedContextScope: For patient resource must have FHIR Id claim or link and must match the id resource of request {id}-{fhirid}");
                    return false;
                } else
                {
                    log.LogInformation($"FHIRProxyAuthorization: PassedContextScope: Looking for Patient scope in query string {req.QueryString.Value}");
                    IQueryCollection querycol = req.Query;
                    //See if this resource query is constrained by Patient in Subject or Patient parameters
                    string qextid = querycol.Get<string>("patient:Patient.Identifier", @default: "");
                    if (string.IsNullOrEmpty(qextid)) querycol.Get<string>("subject:Patient.Identifier", @default: "");
                    string qid = querycol.Get<string>("patient", @default: "");
                    if (string.IsNullOrEmpty(qid)) qid = querycol.Get<string>("subject", @default: "");
                    if (!string.IsNullOrEmpty(qextid) && !string.IsNullOrEmpty(fhirextid) && qextid.Contains(fhirextid)) return true;
                    if (!string.IsNullOrEmpty(qid) && !string.IsNullOrEmpty(fhirid) && qid.Contains(fhirid)) return true;
                    log.LogWarning("FHIRProxyAuthorization: PassedContextScope not match internal or external identifier from link/claim in query parms or request is not constrained to patient context...");
                    return false;
                }
            } else if (scope.StartsWith("user",StringComparison.InvariantCultureIgnoreCase))
            {
                //Will pass on user context scope but will need to be filtered by Pre/Post Module Logic
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
