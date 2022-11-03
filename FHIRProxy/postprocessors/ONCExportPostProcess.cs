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
using Azure.Core;
using Azure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace FHIRProxy.postprocessors
{
    class ONCExportPostProcess : IProxyPostProcess
    {
        public async Task<ProxyProcessResult> Process(FHIRResponse response, HttpRequest req, ILogger log, ClaimsPrincipal principal)
        {
            await Task.Delay(0);
            if (req.Method.Equals("GET") && req.Path.HasValue)
            {
                if (req.Path.Value.StartsWith("/fhir/_operations/export") || req.Path.Value.StartsWith("/fhir/_operations/aggexport"))
                {
                    ClaimsIdentity ci = (ClaimsIdentity)principal.Identity;
                    if (response.StatusCode==System.Net.HttpStatusCode.OK && response.Content != null)
                    {
                        try
                        {
                            JObject exportresp = JObject.Parse(response.ToString());
                            exportresp["requiresAccessToken"] = true;
                            JArray respfiles = (JArray)exportresp["output"];
                            foreach(JToken tok in respfiles)
                            {
                                string url = (string)tok["url"];
                                string[] pathrw = url.Replace("https://", "").Split("/");
                                StringBuilder sb = new StringBuilder();
                                sb.Append("https://" + req.Host.Value);
                                sb.Append("/fhir/_exportfile");
                                for (int x=1; x < pathrw.Length; x++)
                                {
                                    sb.Append("/" + pathrw[x]);
                                }
                                tok["url"] = sb.ToString();
                            }
                            response.Content = exportresp;
                        }
                        catch (Exception e)
                        {
                            log.LogError($"Invalid JSON from export response:{e.Message}");
                        }
                    }
                }              
            }
            return new ProxyProcessResult(true, "", "", response);
        }
      
       
    }
}
