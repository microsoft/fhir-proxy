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
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace FHIRProxy
{
 
    class FHIRClientFactory
    {
        private static string _bearerToken = null;
        private static FHIRClient _fhirclient = null;
        private static object _lock = new object();
        public static FHIRClient getClient(ILogger log)
        {
            /* Get/update/check current bearer token to authenticate the proxy to the FHIR Server
             * The following parameters must be defined in environment variables:
             * To use Manged Service Identity or Service Client:
             * FS-URL = the fully qualified URL to the FHIR Server
             * FS-RESOURCE = the audience or resource for the FHIR Server for Azure API for FHIR should be https://azurehealthcareapis.com
             * To use a Service Client Principal the following must also be specified:
             * FS-TENANT-NAME = the GUID or UPN of the AAD tenant that is hosting FHIR Server Authentication
             * FS-CLIENT-ID = the client or app id of the private client authorized to access the FHIR Server
             * FS-SECRET = the client secret to pass to FHIR Server Authentication
            */
            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("FS-RESOURCE")) && FHIRClient.isTokenExpired(_bearerToken))
            {
                lock (_lock)
                {
                    if (FHIRClient.isTokenExpired(_bearerToken))
                    {
                        log.LogInformation("Token is expired...Obtaining new bearer token...");
                         _bearerToken = FHIRClient.GetOAUTH2BearerToken(System.Environment.GetEnvironmentVariable("FS-RESOURCE"), System.Environment.GetEnvironmentVariable("FS-TENANT-NAME"),
                                                                  System.Environment.GetEnvironmentVariable("FS-CLIENT-ID"), System.Environment.GetEnvironmentVariable("FS-SECRET")).GetAwaiter().GetResult();
                        _fhirclient = new FHIRClient(System.Environment.GetEnvironmentVariable("FS-URL"), _bearerToken);
                    }
                }
            }
            /* Get a FHIR Client instance to talk to the FHIR Server */
            return _fhirclient;

        }
        public static async Task<FHIRResponse> callFHIRServer(string requestBody, HttpRequest req,ILogger log, string res,string id,string hist,string vid)
        {
            

            FHIRClient fhirClient = FHIRClientFactory.getClient(log);
            
            FHIRResponse fhirresp = null;
            if (req.Method.Equals("GET"))
            {
                var qs = req.QueryString.HasValue ? req.QueryString.Value : null;
                StringBuilder sb = new StringBuilder();
                sb.Append(res);
                if (!string.IsNullOrEmpty(id))
                {
                    sb.Append("/" + id);
                    if (!string.IsNullOrEmpty(hist))
                    {
                        sb.Append("/" + hist);
                        if (!string.IsNullOrEmpty(vid))
                        {
                            sb.Append("/" + vid);
                        }
                    }
                }
                fhirresp = await fhirClient.LoadResource(sb.ToString(), qs, false, req.Headers);
            }
            else
            {
                if (req.Method.Equals("DELETE"))
                {
                    fhirresp = await fhirClient.DeleteResource(res + (id == null ? "" : "/" + id), req.Headers);
                }
                else if (req.Method.Equals("POST") && !string.IsNullOrEmpty(id) && id.StartsWith("_search"))
                {
                    var qs = req.QueryString.HasValue ? req.QueryString.Value : null;
                    fhirresp = await fhirClient.PostCommand(res + "/" + id, requestBody, qs,req.Headers);         
                }
                else 
                {
                    fhirresp = await fhirClient.SaveResource(res, requestBody, req.Method, req.Headers);
                }
            }
            return fhirresp;
        }
    }
}
