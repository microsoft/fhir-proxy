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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FHIRProxy.preprocessors
{
    class CDSServicesAdvertisement : IProxyPreProcess
    {
        public async Task<ProxyProcessResult> Process(string requestBody, HttpRequest req, ILogger log, ClaimsPrincipal principal)
        {
            await Task.Delay(0);
            FHIRParsedPath pp = req.parsePath();
            if (req.Method.Equals("GET") && pp.ResourceType.SafeEquals("cds-services"))
            {
                JObject servicesadv = new JObject();
                JArray services = new JArray();
                JObject s = new JObject();
                s["hook"] = "patient-view";
                s["title"] = "Patient COVID-19 Vaccine History";
                s["description"] = "List COVID-19 vaccination history and status for specified patient";
                s["id"] = "covid-vaccine-history";
                services.Add(s);
                servicesadv["services"] = services;
                FHIRResponse fr1 = new FHIRResponse();
                fr1.Content = servicesadv.ToString();
                var ppr = new ProxyProcessResult(false, "", requestBody, fr1);
                ppr.DirectReply = true;
                return ppr;
            }
            return new ProxyProcessResult(true, "", requestBody, null);
        }
      
       
    }
}
