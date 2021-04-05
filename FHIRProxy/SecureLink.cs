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

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Security.Claims;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Formatters.Internal;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace FHIRProxy
{
    public static class SecureLink
    {
        public static string _bearerToken;
        private static object _lock = new object();
        private static string[] allowedresources = { "Patient", "Practitioner", "RelatedPerson" };
        private static string[] validcmds = { "link", "unlink", "list" };
        [FHIRProxyAuthorization]
        [FunctionName("SecureLink")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "manage/{cmd}/{res}/{id}/{name}")] HttpRequest req,
            ILogger log, ClaimsPrincipal principal, string cmd, string res, string id,string name)
        {
            log.LogInformation("SecureLink Function Invoked");
            //Is the principal authenticated
            if (!Utils.isServerAccessAuthorized(req))
            {
                return new ContentResult() { Content = "User is not Authenticated", StatusCode = (int)System.Net.HttpStatusCode.Unauthorized };
            }
            
            if (!Utils.inServerAccessRole(req,"A")) 
            {
                return new ContentResult() { Content = "User does not have suffiecient rights (Administrator required)", StatusCode = (int)System.Net.HttpStatusCode.Unauthorized };
            }
            if (string.IsNullOrEmpty(cmd) || !validcmds.Any(cmd.Contains))
            {
                return new BadRequestObjectResult("Invalid Command....Valid commands are link, unlink and list");
            }
            //Are we linking the correct resource type
            if (string.IsNullOrEmpty(res) || !allowedresources.Any(res.Contains))
            {
                return new BadRequestObjectResult("Resource must be Patient,Practitioner or RelatedPerson");
            }
            
            ClaimsIdentity ci = (ClaimsIdentity)principal.Identity;
            string aadten = (string.IsNullOrEmpty(ci.Tenant()) ? "Unknown" : ci.Tenant());
            FhirJsonParser _parser = new FhirJsonParser();
            _parser.Settings.AcceptUnknownMembers = true;
            _parser.Settings.AllowUnrecognizedEnums = true;
            //Get a FHIR Client so we can talk to the FHIR Server
            log.LogInformation($"Instanciating FHIR Client Proxy");
            FHIRClient fhirClient = FHIRClientFactory.getClient(log);
            int i_link_days = 0;
            int.TryParse(System.Environment.GetEnvironmentVariable("FP-LINK-DAYS"), out i_link_days);
            if (i_link_days == 0) i_link_days = 365;
            //Load the resource to Link
            var fhirresp = await fhirClient.LoadResource(res + "/" + id, null, false, req.Headers);
            var lres = _parser.Parse<Resource>((string)fhirresp.Content);
            if (lres.TypeName.Equals("OperationOutcome"))
            {

                return new BadRequestObjectResult(lres.ToString());

            }
            CloudTable table = Utils.getTable();
            switch (cmd)
            {
                case "link":
                    LinkEntity linkentity = new LinkEntity(res, aadten + "-" + name);
                    linkentity.ValidUntil = DateTime.Now.AddDays(i_link_days);
                    linkentity.LinkedResourceId = id;
                    Utils.setLinkEntity(table, linkentity);
                    return new OkObjectResult($"Identity: {name} in directory {aadten} is now linked to {res}/{id}");
                case "unlink":
                    LinkEntity delentity = Utils.getLinkEntity(table, res, aadten + "-" + name);
                    if (delentity==null) return new OkObjectResult($"Resource {res}/{id} has no links to Identity {name} in directory {aadten}");
                    Utils.deleteLinkEntity(table,delentity);
                    return new OkObjectResult($"Identity: {name} in directory {aadten} has been unlinked from {res}/{id}");
                case "list":
                    LinkEntity entity = Utils.getLinkEntity(table, res, aadten + "-" + name);
                    if (entity != null)
                        return new OkObjectResult($"Resource {res}/{id} is linked to Identity: {name} in directory {aadten}");
                    else
                        return new OkObjectResult($"Resource {res}/{id} has no links to Identity {name} in directory {aadten}");
            }
            return new OkObjectResult($"No action taken Identity: {name}");

        }
      

    }
}
