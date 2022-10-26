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


namespace FHIRProxy.preprocessors
{
    class ONCExportPreProcess : IProxyPreProcess
    {
        public async Task<ProxyProcessResult> Process(string requestBody, HttpRequest req, ILogger log, ClaimsPrincipal principal)
        {
            await Task.Delay(0);
            if (req.Method.Equals("GET") && req.Path.HasValue)
            {
                if (req.Path.Value.EndsWith("$export"))
                {
                    ClaimsIdentity ci = (ClaimsIdentity)principal.Identity;
                    QueryString newquery = new QueryString();
                    foreach (var s in req.Query)
                    {
                        if (!s.Key.Equals("_container")) newquery = newquery.Add(s.Key, s.Value);
                    }
                    newquery = newquery.Add("_container",ci.ObjectId());
                    req.QueryString = newquery;
                   
                }
     
            }
            return new ProxyProcessResult(true, "", requestBody, null);

        }
      
       
    }
}
