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
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Net.Http;

namespace FHIRProxy
{
    class FHIRProxyAuthorization : FunctionInvocationFilterAttribute
    {   
        
        public static ClaimsPrincipal ValidateFPToken(string authToken,ILogger log)
        {
                var tokenHandler = new JwtSecurityTokenHandler();
                var validationParameters = GetFPValidationParameters(log);
                SecurityToken validatedToken;
                var principal = tokenHandler.ValidateToken(authToken, validationParameters, out validatedToken);
                ClaimsIdentity ci = new ClaimsIdentity(principal.Claims,"ExternalOIDC");
                ClaimsPrincipal user = new ClaimsPrincipal(ci);
                return user;
         
        }
        private static TokenValidationParameters GetFPValidationParameters(ILogger log)
        {
            var secret = Utils.GetEnvironmentVariable("FP-ACCESS-TOKEN-SECRET");
            var key = Encoding.ASCII.GetBytes(secret);
            var retVal = new TokenValidationParameters()
            {
                
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidIssuer = "https://fhir-proxy.azurehealthcareapis.com",
                IssuerSigningKey = new SymmetricSecurityKey(key),
                RequireSignedTokens = true
            };
            return retVal;
        }
        public override Task OnExecutingAsync(FunctionExecutingContext executingContext, CancellationToken cancellationToken)
        {
            var req = executingContext.Arguments.First().Value as HttpRequest;
            ILogger log = executingContext.Arguments["log"] as ILogger;
            ClaimsPrincipal principal = null;
            ClaimsIdentity ci = null;
            bool admin = false;
            bool reader = false;
            bool writer = false;
            string inroles = "";
            //remove local headers 
            req.Headers.Remove(Utils.AUTH_STATUS_HEADER);
            req.Headers.Remove(Utils.AUTH_STATUS_MSG_HEADER);
            req.Headers.Remove(Utils.FHIR_PROXY_ROLES);
            req.Headers.Remove(Utils.FHIR_PROXY_SMART_SCOPE);
            //Allow use of trusted IDP must be configured
            var jwtstr = req.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(jwtstr))
            {
                string msg = $"No acccess token found in Authorization Header";
                log.LogError(msg);
                req.Headers.Add(Utils.AUTH_STATUS_HEADER, ((int)System.Net.HttpStatusCode.Unauthorized).ToString());
                req.Headers.Add(Utils.AUTH_STATUS_MSG_HEADER, msg);
                goto leave;
            }
            try
            {
                jwtstr = jwtstr.Split(" ")[1];
                principal = ValidateFPToken(jwtstr, log);
            }
            catch(Exception e)
            {
                string msg = $"Error validating Access Token: {e.Message}";
                log.LogError(msg + $" Stack:{e.StackTrace}");
                req.Headers.Add(Utils.AUTH_STATUS_HEADER, ((int)System.Net.HttpStatusCode.Unauthorized).ToString());
                req.Headers.Add(Utils.AUTH_STATUS_MSG_HEADER, msg);
                goto leave;
            }
            if (principal != null)
            {
                    ci = (ClaimsIdentity)principal.Identity;
                    admin = ci.IsInFHIRRole(Environment.GetEnvironmentVariable("FP-ADMIN-ROLE"));
                    reader = ci.IsInFHIRRole(Environment.GetEnvironmentVariable("FP-READER-ROLE"));
                    writer = ci.IsInFHIRRole(Environment.GetEnvironmentVariable("FP-WRITER-ROLE"));
                    if (admin) inroles += "A";
                    if (reader) inroles += "R";
                    if (writer) inroles += "W";
                   
            }
           
            if (principal==null)
            {
                req.Headers.Add(Utils.AUTH_STATUS_HEADER, ((int)System.Net.HttpStatusCode.Unauthorized).ToString());
                req.Headers.Add(Utils.AUTH_STATUS_MSG_HEADER, "Security Principal is invalid or not present in token");
                goto leave;
            }
            if (!principal.Identity.IsAuthenticated)
            {
                req.Headers.Add(Utils.AUTH_STATUS_HEADER, ((int)System.Net.HttpStatusCode.Unauthorized).ToString());
                req.Headers.Add(Utils.AUTH_STATUS_MSG_HEADER, "Security Principal is not Authenticated");
                goto leave;
            }
            //Set Authentication Status/Roles
            req.Headers.Add(Utils.AUTH_STATUS_HEADER, ((int)System.Net.HttpStatusCode.OK).ToString());
            req.Headers.Add(Utils.FHIR_PROXY_ROLES, inroles);
            //Access checks for /fhir proxy endpoint
            if (executingContext.FunctionName.Equals("ProxyFunction"))
            {
                //SMART Scope checking for FHIR Calls
                FHIRParsedPath pp = req.parsePath();
                string id = pp.ResourceId;
                string res = pp.ResourceType;
                if (id == null) id = "";
                //Claims Trump Role Access if scope claims are present then request must pass scope check
                List<string> smartClaims = ExtractSmartScopeClaims(ci);
                if (smartClaims.Count > 0)
                {
                    var message = "";
                    if (!PassedScopeCheck(req, smartClaims, res, id, out message, log))
                    {
                        req.Headers.Remove(Utils.AUTH_STATUS_HEADER);
                        req.Headers.Remove(Utils.AUTH_STATUS_MSG_HEADER);
                        req.Headers.Add(Utils.AUTH_STATUS_HEADER, ((int)System.Net.HttpStatusCode.Forbidden).ToString());
                        req.Headers.Add(Utils.AUTH_STATUS_MSG_HEADER, message);
                        goto leave;
                    }
                }
                else
                {
                    //No smart claims present use role access
                    if (!PassedRoleCheck(req, reader, writer, admin,id))
                    {
                        req.Headers.Remove(Utils.AUTH_STATUS_HEADER);
                        req.Headers.Remove(Utils.AUTH_STATUS_MSG_HEADER);
                        req.Headers.Add(Utils.AUTH_STATUS_HEADER, ((int)System.Net.HttpStatusCode.Unauthorized).ToString());
                        req.Headers.Add(Utils.AUTH_STATUS_MSG_HEADER, "Principal is not in an authorized role");
                        goto leave;
                    }
                }
                string passheader = req.Headers["X-MS-AZUREFHIR-AUDIT-PROXY"];
                if (string.IsNullOrEmpty(passheader))
                {
                    passheader = "fhir-proxy";
                    req.Headers.Add("X-MS-AZUREFHIR-AUDIT-PROXY", passheader);
                }
                string auditsource = req.Headers["X-MS-AZUREFHIR-AUDIT-SOURCE"];
                if (string.IsNullOrEmpty(auditsource))
                {
                    auditsource = Utils.GetRemoteIpAddress(req);
                    req.Headers.Add("X-MS-AZUREFHIR-AUDIT-SOURCE", auditsource);
                }
                //Since we are proxying with service client need to ensure authenticated proxy principal is audited
                req.Headers.Add("X-MS-AZUREFHIR-AUDIT-USERID", ci.ObjectId());
                req.Headers.Add("X-MS-AZUREFHIR-AUDIT-TENANT", ci.Tenant());
                
            }
           
          leave:
            return base.OnExecutingAsync(executingContext, cancellationToken);
        }
        public static bool isFHIRSearch(HttpRequest req,string resourceid)
        {
            string r = "";
            if (!string.IsNullOrEmpty(resourceid)) r = resourceid;
            return (req.Method.Equals("GET") && req.QueryString.HasValue) || (req.Method.Equals("POST") && r.Equals("_search"));
        }
        private bool PassedRoleCheck(HttpRequest req,bool reader, bool writer, bool admin,string id)
        {
            switch (req.Method)
            {
                case "PUT":
                case "PATCH":
                case "DELETE":
                    return (admin || writer);
                case "GET":
                case "POST":
                    if (isFHIRSearch(req, id)) return (reader || admin);
                    return (admin || writer);
                default:
                    return false;
            }
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
        private bool PassedScopeCheck(HttpRequest req, List<string> smartClaims,string res,string resid, out string message, ILogger log)
        {
            var arr = smartClaims.ToArray();
            message = "";
            var results = Array.FindAll(arr, s => s.Contains("." + res +"."));
            var wild = Array.FindAll(arr, s => s.Contains(".*."));
            List<string> prunedClaims = new List<string>(results);
            prunedClaims.AddRange(wild);
            //Resource not specified then check bundle write
            if (string.IsNullOrEmpty(res))
            {
                var ba = Array.FindAll(arr, s => s.Contains(".Bundle."));
                prunedClaims.AddRange(ba);
            }
            //Check for SMART Scopes (e.g. <system/patient/user>.<resource>|*.Read|Write|*)
            foreach (string claim in prunedClaims)
                {
                    string[] s = claim.Split(".");
                    log.LogInformation($"FHIRProxyAuthorization: Checking scope: {claim}");
                    if (s.Length > 2 && !string.IsNullOrEmpty(s[0]) && !string.IsNullOrEmpty(s[1]) && !string.IsNullOrEmpty(s[2]))
                    {
                    //Convert to v2 claims
                        if (s[2].ToLower().Equals("read")) s[2] = "rs";
                        if (s[2].ToLower().Equals("write")) s[2] = "cud";
                        if (s[2].Equals("*")) s[2] = "cruds"; 
                        if (s[0].StartsWith("launch")) continue; //Getting to access claims
                        bool canread =   (s[2].Contains("r", StringComparison.InvariantCultureIgnoreCase));
                        bool cancreate = (s[2].Contains("c", StringComparison.InvariantCultureIgnoreCase));
                        bool canupdate = (s[2].Contains("u", StringComparison.InvariantCultureIgnoreCase));
                        bool candelete = (s[2].Contains("d", StringComparison.InvariantCultureIgnoreCase));
                        bool cansearch = (s[2].Contains("s", StringComparison.InvariantCultureIgnoreCase));
                        log.LogInformation($"FHIRProxyAuthorization: Checking request {res} against claim scope {s[1]} CanRead:{canread} CanCreate:{cancreate} CanUpdate:{canupdate} CanDelete: {candelete}");
                        switch (req.Method)
                        {
                            case "PUT":
                            case "PATCH":
                                message = $"Must have Update or Write Permissions on resource type {res}";
                                return ((s[1].Equals(res) || s[1].Equals("*")) && canupdate);
                            case "GET":
                                if (req.QueryString.HasValue)
                                {
                                    message = $"Must have Read/Search Permissions on resource type {res}";
                                    return ((s[1].Equals(res) || s[1].Equals("*")) && cansearch && canread);
                                }
                                else
                                {
                                    message = $"Must have Read Permissions on resource type {res}";
                                    return ((s[1].Equals(res) || s[1].Equals("*")) && canread);
                                }
                            case "DELETE":
                                message = $"Must have Delete/Write Permissions on resource type {res}";
                                return ((s[1].Equals(res) || s[1].Equals("*")) && candelete);
                            case "POST":
                                if (string.IsNullOrEmpty(res) && s[1].Equals("Bundle"))
                                {
                                    message = $"Must have Create/Write Permissions on resource type Bundle";
                                    return cancreate;
                                }
                                if (isFHIRSearch(req, resid))
                                {
                                    message = $"Must have Read/Search Permissions on resource type {res}";
                                    return ((s[1].Equals(res) || s[1].Equals("*")) && cansearch && canread);
                                }
                                message = $"Must have Create/Write Permissions on resource type {res}";
                                return ((s[1].Equals(res) || s[1].Equals("*")) && cancreate);
                            default:
                                message = $"Unsupported HTTP Verb {req.Method}";
                                return false;
                        }
                       
                    }
                    else
                    {
                        message = $"FHIRProxyAuthorization: Scope {claim} is not a SMART scope. Will not match a scope.Expected format is [patient | user | system].[resourceType | *].[read | write | *] or [cruds | *]";
                        log.LogWarning(message);
                    }
                }

                //didn't pass basic scope checks
                message = $"Access scopes not defined or granted for resource {res}";
                return false;
           
        }
        public static UserScopeResult ResultAllowedForUserScope(JToken tok, ClaimsPrincipal principal, ILogger log)
        {
            ClaimsIdentity ci = (ClaimsIdentity)principal.Identity;
            return ResultAllowedForUserScope(tok, ci, log);
        }
        public static UserScopeResult ResultAllowedForUserScope(JToken tok, ClaimsIdentity ci, ILogger log)
        {
            if (tok == null || string.IsNullOrEmpty(tok.FHIRResourceType())) return new UserScopeResult(true,tok);
            if (tok.FHIRResourceType().Equals("OperationOutcome")) return new UserScopeResult(true,tok);
            List<string> smartClaims = ExtractSmartScopeClaims(ci);
            //No Claims in token then pass scope context by default
            if (smartClaims == null || smartClaims.Count == 0) return new UserScopeResult(true,tok);
            string claimstring = String.Join(" ", smartClaims);
            //Load fhirUser placed in cache by SMARTProxyToken issuer.
            string fhiruser = ci.fhirUser();
            if (string.IsNullOrEmpty(fhiruser))
            {
                return new UserScopeResult(false, tok, $"fhirUser is not in principal claims...SMART Scopes require a fhirUser claim see fhir proxy documentation");
            }
            PatientCompartment comp = PatientCompartment.Instance();
            if (!tok.FHIRResourceType().Equals("Bundle"))
            {
                return CheckResourceUserScope(tok, ci, comp, claimstring, log);
            }
            else
            {
                var resourcesToCheck = (JArray)tok["entry"];
                JArray resourcesToReturn = new JArray();
                if (!resourcesToCheck.IsNullOrEmpty())
                {
                    //Any entry not related to patient in bundle will not be redacted
                    foreach (JToken t in resourcesToCheck)
                    {
                        JToken resource = t["resource"];
                        if (resource != null)
                        {
                            var rslt = CheckResourceUserScope(resource, ci, comp, claimstring, log);
                            if (!rslt.Result) {
                                t["resource"] = JObject.Parse(Utils.genOOErrResponse("forbidden", rslt.Message,"warning"));
                                if (t["search"] != null)
                                {
                                    t["search"]["mode"] = "outcome";
                                }
                            }
                        }
                        resourcesToReturn.Add(t);
                    }
                    tok["entry"] = resourcesToReturn;
                    
                }
               
            }
            return new UserScopeResult(true, tok);
        }
        public static UserScopeResult CheckResourceUserScope(JToken resource,ClaimsIdentity ci, PatientCompartment comp,string claimstring,  ILogger log)
        {
            string fhiruser = ci.fhirUser();
            if (string.IsNullOrEmpty(fhiruser))
            {
                return new UserScopeResult(false, resource,$"fhirUser is not in access_token claims...SMART Scopes require a fhirUser claim see fhir proxy documentation");
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
                        if (fhiruser.Contains(resource.FHIRResourceId())) return new UserScopeResult(true,resource);
                        else return new UserScopeResult(false, resource, $"Patient resource does not match patient context scope Context: {fhiruser} Resource: {resource.FHIRResourceId()}");
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
                            return new UserScopeResult(false, resource,$"Could not find Patient Reference {fhiruser} in reference fields {String.Join(",", parms)} of resource {resource.FHIRResourceType()}/{resource.FHIRResourceId()}");
                        }
                    }
                }
                else if (fhiruser.StartsWith("Practitioner/"))
                {
                    //Will pass on user context scope but will need to be filtered by Pre/Post Module Logic
                    if (!claimstring.Contains($"user.{resourceType}"))
                    {
                        return new UserScopeResult(false, resource,$"User in context {fhiruser} does not have scope permission to resource {resourceType} contained in this result");
                    }
                }
                else if (fhiruser.StartsWith("System/"))
                {
                    //Will pass on system scope but will need to be filtered by Pre/Post Module Logic
                    if (!claimstring.Contains($"system.{resourceType}"))
                    {
                        return new UserScopeResult(false, resource, $"User in context {fhiruser} does not have scope permission to resource {resourceType} contained in this result");
                    }
                }
                //All Checks Passed
                return new UserScopeResult(true,resource);
        }
        public static string GetFHIRIdFromFHIRUser(string fhiruser)
        {
            if (fhiruser == null || !fhiruser.Contains("/")) return fhiruser;
            return fhiruser.Substring(fhiruser.LastIndexOf("/") +1);
        }
        public static string GetMappedFHIRUser(string tenant, string oid, ILogger log)
        {
            //Check the fhirUser claim
            if (string.IsNullOrEmpty(tenant))
            {
                log.LogWarning("GetMappedFHIRUser: No Tenant(tid) specified!");
                return null;
            }
            if (string.IsNullOrEmpty(oid))
            {
                log.LogWarning("GetMappedFHIRUser: No OID specified!");
                return null;
            }
            try
            {
                var table = Utils.getTable();
                //Check for Patient Association
                var entity = Utils.getEntity<LinkEntity>(table, "Patient", tenant + "-" + oid);
                if (entity != null)
                {
                    return $"Patient/{entity.LinkedResourceId}";
                }
                //Check for Practitioner Association
                entity = Utils.getEntity<LinkEntity>(table, "Practitioner", tenant + "-" + oid);
                if (entity != null)
                {
                    return $"Practitioner/{entity.LinkedResourceId}";
                }
                //Check for Practitioner Association
                entity = Utils.getEntity<LinkEntity>(table, "RelatedPerson", tenant + "-" + oid);
                if (entity != null)
                {
                    return $"RelatedPerson/{entity.LinkedResourceId}";
                }
            }
            catch (Exception e)
            {
                log.LogError($"FHIRProxyAuthorization:Cannot access linked FHIR Resources table:{e.Message}");
            }
            log.LogInformation($"FHIRProxyAuthorization: No linked FHIR Resource for tenant {tenant} and oid:{oid}");
            return null;
        }

    }
}
