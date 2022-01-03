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
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace FHIRProxy.postprocessors
{
    /* Proxy Post Process to publish events for CUD events to FHIR Server */
    class PublishFHIREventPostProcess : IProxyPostProcess
    {
        public async Task<ProxyProcessResult> Process(FHIRResponse response, HttpRequest req, ILogger log, ClaimsPrincipal principal)
        {
            
            try
            {
                FHIRParsedPath pp = req.parsePath();
                if (req.Method.Equals("GET") || (int)response.StatusCode > 299) return new ProxyProcessResult(true, "", "", response);
                string source = req.Headers["X-MS-AZUREFHIR-AUDIT-SOURCE"];
                if (string.IsNullOrEmpty(source)) source = "";
                string ecs = Environment.GetEnvironmentVariable("FP-MOD-EVENTHUB-CONNECTION");
                string enm = Environment.GetEnvironmentVariable("FP-MOD-EVENTHUB-NAME");
                if (string.IsNullOrEmpty(ecs) || string.IsNullOrEmpty(enm))
                {
                    log.LogWarning($"PublishFHIREventPostProcess: EventHubConnection String or EventHub Name is not specified. Will not publish.");
                    return new ProxyProcessResult(true, "", "", response);
                }
                JArray entries = null;
                if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Created)
                {
                    var fhirresp = JObject.Parse(response.Content.ToString());
                    if (!fhirresp.IsNullOrEmpty() && ((string)fhirresp["resourceType"]).Equals("Bundle") && ((string)fhirresp["type"]).EndsWith("-response"))
                    {

                        entries = (JArray)fhirresp["entry"];

                    } else
                    {
                        entries = new JArray();
                        JObject stub = new JObject();
                        stub["response"] = new JObject();
                        stub["response"]["status"] = (int) response.StatusCode + " " + response.StatusCode.ToString();
                        stub["resource"] = fhirresp;
                        entries.Add(stub);
                    }
                }
                else if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    entries = new JArray();
                    JObject stub = new JObject();
                    stub["response"] = new JObject();
                    stub["response"]["status"] = req.Method;
                    stub["resource"] = new JObject();
                    stub["resource"]["id"] = pp.ResourceId;
                    stub["resource"]["resourceType"] = pp.ResourceType;
                    entries.Add(stub);
                }
                await publishBatchEvent(ecs, enm, source, entries,log);



            }

            catch (Exception exception)
            {
              log.LogError(exception,$"PublishFHIREventPostProcess Exception: {exception.Message}");
               
            }
           
            return new ProxyProcessResult(true, "", "", response);

        }
        private async Task publishBatchEvent(string eventHubConnectionString, string eventHubName, string source, JArray entries,ILogger log)
        {
            await using (var producerClient = new EventHubProducerClient(eventHubConnectionString, eventHubName))
            {
                // Create a batch of events 
                using Azure.Messaging.EventHubs.Producer.EventDataBatch eventBatch = await producerClient.CreateBatchAsync();

                if (!entries.IsNullOrEmpty())
                {
                    foreach (JToken tok in entries)
                    {
                        string entrystatus = (string)tok["response"]["status"];
                        EventData dta = createMsg(entrystatus, source,tok["resource"]);
                        if (dta != null) eventBatch.TryAdd(dta);
                        
                    }

                }
                // Use the producer client to send the batch of events to the event hub
                await producerClient.SendAsync(eventBatch);
              
            }
        }
        private EventData createMsg(string status,string source, JToken resource)
        {
            if (resource.IsNullOrEmpty()) return null;
            string action = "Unknown";
            if (status.StartsWith("200")) action = "Updated";
            if (status.StartsWith("201")) action = "Created";
            if (status.Contains("DELETE")) action = "Deleted";
            string msg = "{\"action\":\"" + action + "\",\"resourcetype\":\"" + resource.FHIRResourceType() + "\",\"id\":\"" + resource.FHIRResourceId() + "\",\"version\":\"" + resource.FHIRVersionId() + "\",\"lastupdated\":\"" + resource.FHIRLastUpdated() + "\",\"source\":\"" + source + "\"}";
            return new EventData(Encoding.UTF8.GetBytes(msg));
        }
    }
}
