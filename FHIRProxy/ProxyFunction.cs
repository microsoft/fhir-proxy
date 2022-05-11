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
using System.Net;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Linq.Expressions;
using System.ComponentModel.Design;


namespace FHIRProxy
{
    /*The basic FHIR proxy by default only validates that the user principal is authenticated (AuthN).
          *You can clone this code and add your own authorization logic, pre and post processing logic to fit your business
          *use cases. This function will accept standard REST Verbs as used by HL7 FHIR
          * 
          * IMPORTANT:  Do not publish this function without Authentication (Easy Auth or APIM) you will compromise your FHIR server!
          * 
    */
    public static class ProxyFunction
    {
     
        [FHIRProxyAuthorization]
        [FunctionName("ProxyFunction")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "put", "patch", "delete", Route = "fhir/{*restOfPath}")] HttpRequest req, string restOfPath,
                         ILogger log)
        {
            //Parse Path
            FHIRParsedPath parsedPath = req.parsePath();
            string coid = null;
            if (req.Headers.ContainsKey("x-ms-service-request-id"))
            {
                coid = req.Headers["x-ms-service-request-id"].First();
            } else
            {
                coid = Guid.NewGuid().ToString();
            }
            using (log.BeginScope(
                    new Dictionary<string, object> { { "CorrelationId", coid } }))
            {
                if (!Utils.isServerAccessAuthorized(req))
                {
                    return new ContentResult() { Content = Utils.genOOErrResponse("security", req.Headers[Utils.AUTH_STATUS_MSG_HEADER].First()), StatusCode = (int)System.Net.HttpStatusCode.Unauthorized, ContentType = "application/json" };
                }
                ClaimsPrincipal principal = ADUtils.BearerToClaimsPrincipal(req);
                //Load Request Body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
              
                //Initialize Response 
                FHIRResponse serverresponse = null;
                //Call Configured Pre-Processor Modules
                ProxyProcessResult prerslt = await ProxyProcessManager.RunPreProcessors(requestBody, req, log, principal);

                if (!prerslt.Continue)
                {
                    //Pre-Processor didn't like something or exception was called so return 
                    FHIRResponse preresp = prerslt.Response;
                    if (preresp == null)
                    {
                        string errmsg = (string.IsNullOrEmpty(prerslt.ErrorMsg) ? "No message" : prerslt.ErrorMsg);
                        FHIRResponse fer = new FHIRResponse();
                        fer.StatusCode = System.Net.HttpStatusCode.InternalServerError;
                        fer.Content = Utils.genOOErrResponse("internalerror", $"A Proxy Pre-Processor halted execution for an unknown reason. Check logs. Message is {errmsg}");
                        return genContentResult(fer, log);
                    }
                    if (prerslt.DirectReply)
                    {
                        return genContentResult(preresp, log);
                    }
                    //Do not continue, so no call to the fhir server go directly to post processing with the response from the pre-preprocessor
                    serverresponse = preresp;
                    goto PostProcessing;
                }

                log.LogInformation($"Calling FHIR Server...Path {restOfPath}");

                //Proxy the call to the FHIR Server
                serverresponse = await FHIRClient.CallFHIRServer(req, restOfPath, prerslt.Request, log);

            PostProcessing:
                //Call Configured Post-Processor Modules
                ProxyProcessResult postrslt = await ProxyProcessManager.RunPostProcessors(serverresponse, req, log, principal);


                if (postrslt.Response == null)
                {

                    string errmsg = (string.IsNullOrEmpty(postrslt.ErrorMsg) ? "No message" : postrslt.ErrorMsg);
                    postrslt.Response = new FHIRResponse();
                    postrslt.Response.StatusCode = System.Net.HttpStatusCode.InternalServerError;
                    postrslt.Response.Content = Utils.genOOErrResponse("internalerror", $"A Proxy Post-Processor halted execution for an unknown reason. Check logs. Message is {errmsg}");

                }
                //Check for User Scoped allowed resources
                var scoperesult = FHIRProxyAuthorization.ResultAllowedForUserScope(postrslt.Response.toJToken(), principal, log);
                if (!scoperesult.Result)
                {
                    postrslt.Response = new FHIRResponse();
                    postrslt.Response.StatusCode = System.Net.HttpStatusCode.Forbidden;
                    postrslt.Response.Content = Utils.genOOErrResponse("forbidden", scoperesult.Message);
                } else
                {
                    postrslt.Response.Content = scoperesult.ResponseContent;
                }
                //Reverse Proxy Response
                postrslt.Response = Utils.reverseProxyResponse(postrslt.Response, req);
                //return ActionResult
                if (postrslt.Response.StatusCode == HttpStatusCode.NoContent)
                {
                    return null;
                }
                return genContentResult(postrslt.Response, log);
            }
        }
        public static ContentResult genContentResult(FHIRResponse resp,ILogger log)
        {
            string r = "";
            if (resp != null) r = resp.ToString();
            int sc = (int)resp.StatusCode;
            string ct = (r.isJSON() ? "application/json" : "text/html");
            return new ContentResult() { Content = r, StatusCode = sc, ContentType = ct};
           
        }

    }
}
