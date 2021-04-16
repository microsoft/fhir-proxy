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
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;

namespace FHIRProxy.preprocessors
{
    class ProfileValidationPreProcess : IProxyPreProcess
    {
        private bool init = false;
        private JObject enforcement = null;
        private object lockObj = new object();
        private HttpClient _client = new HttpClient();
        public async Task<ProxyProcessResult> Process(string requestBody, HttpRequest req, ILogger log, ClaimsPrincipal principal)
        {
            FHIRParsedPath pp = req.parsePath();
            string url = Environment.GetEnvironmentVariable("FP-MOD-FHIRVALIDATION-URL");
            if (string.IsNullOrEmpty(url))
            {
                log.LogWarning("ProfileValidationPreProcess: The validation URL is not configured....Validation will not be run");
                return new ProxyProcessResult(true, "", requestBody, null);
            }
            /*Call Resource and Profile Validation server*/
            if (string.IsNullOrEmpty(requestBody) || req.Method.Equals("GET") || req.Method.Equals("DELETE") || string.IsNullOrEmpty(pp.ResourceType)) return new ProxyProcessResult(true, "", requestBody, null);
            try
            {
                var test = JObject.Parse(requestBody);
            }
            catch(Exception)
            {
                //Not Valid JSON Object return
                return new ProxyProcessResult(true, "", requestBody, null);
            }
           
            /* Load Profile Enforcement Policy */
            if (!init)
            {
                lock (lockObj)
                {
                    if (!init)
                    {
                        try
                        {
                            _client.BaseAddress = new Uri(url);
                            _client.Timeout = new TimeSpan(0, 0, Utils.GetIntEnvironmentVariable("FS_TIMEOUT_SECS", "30"));
                            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(Utils.GetEnvironmentVariable("FP-STORAGEACCT"));
                            // Connect to the blob storage
                            CloudBlobClient serviceClient = storageAccount.CreateCloudBlobClient();
                            // Connect to the blob container
                            CloudBlobContainer container = serviceClient.GetContainerReference(Utils.GetEnvironmentVariable("FP-MOD-FHIRVALIDATION-POLICY-CONTAINER","fhirvalidator"));
                            // Connect to the blob file
                            CloudBlockBlob blob = container.GetBlockBlobReference(Utils.GetEnvironmentVariable("FP-MOD-FHIRVALIDATION-POLICY-FILE", "profile_enforce_policy.json"));
                            // Get the blob file as text
                            string contents = blob.DownloadTextAsync().Result;
                            enforcement = JObject.Parse(contents);
                        }
                        catch (Exception e)
                        {
                            log.LogWarning($"ProfileValidationPreProcess: Unable to load profile enforcement policy file: {e.Message} Validation against R4 structure only");
                        }
                        finally
                        {
                            init = true;
                        }
                    }
                }

            }
            string queryString = "";
            if (enforcement != null)
            {
                var enforce = enforcement["enforce"];
                var tok = enforce.SelectToken($"[?(@.resource=='{pp.ResourceType}')]");
                if (!tok.IsNullOrEmpty())
                {
                    JArray arr = (JArray)tok["profiles"];
                    if (!arr.IsNullOrEmpty())
                    {
                        foreach (JToken t in arr)
                        {
                            if (queryString.Length == 0)
                                queryString += $"?profile={t}";
                            else
                                queryString += $"&profile={t}";
                        }
                    }
                }
            }
            string result = "";
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, queryString);
                request.Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
                var response = await _client.SendAsync(request);
                result = await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException we)
            {
                FHIRResponse r = new FHIRResponse();
                r.StatusCode = HttpStatusCode.InternalServerError;
                r.Content = Utils.genOOErrResponse("web-exception", we.Message);
                return new ProxyProcessResult(false, "web-exception", requestBody, r);
            }
            
            JObject obj = JObject.Parse(result);
            //The validator should return an OperationOutcome resource.  In The presence of issues indicates a errors/warnings so we will send it back to the client for 
            //corrective action
            JArray issues = (JArray)obj["issue"];
            if (!issues.IsNullOrEmpty())
            {
                FHIRResponse resp = new FHIRResponse();
                resp.Content = result;
                resp.StatusCode = HttpStatusCode.BadRequest;
                return new ProxyProcessResult(false, "Validation Error", requestBody, resp);
            }
               
            return new ProxyProcessResult(true,"",requestBody,null);
        }
    }
}
