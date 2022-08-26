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
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using System.Linq;

namespace FHIRProxy.postprocessors
{
    /* Proxy Post Process to publish events for CUD events to FHIR Server */
    class FHIRCDSSyncAgentPostProcess2 : IProxyPostProcess
    {
        private ServiceBusClient _queueClient = null;
        private ServiceBusSender _sender = null;

        private string _qname = null;
        private Object lockobj = new object();
        private string[] _fhirSupportedResources = null;
        private bool initializationfailed = false;
        private string _updateAction = null;
        private bool _bulkLoadMode = false;

        public FHIRCDSSyncAgentPostProcess2()
        {

        }
        public void InitQueueClient(ILogger log)
        {

            if (initializationfailed || _queueClient != null) return;
            lock (lockobj)
            {
                if (_queueClient == null)
                {
                    try
                    {
                        _updateAction = Utils.GetEnvironmentVariable("FP-BULK-OVERRIDE-ACTION", "Update");
                        _bulkLoadMode = Utils.GetBoolEnvironmentVariable("FP-SA-BULKLOAD");
                        string _sbcfhirupdates = Utils.GetEnvironmentVariable("SA-SERVICEBUSNAMESPACEFHIRUPDATES");
                        if (_bulkLoadMode)
                        {
                            _qname = Utils.GetEnvironmentVariable("SA-SERVICEBUSQUEUENAMEFHIRBULK");
                        }
                        else
                        {
                            _qname = Utils.GetEnvironmentVariable("SA-SERVICEBUSQUEUENAMEFHIRUPDATES");
                        }
                        string fsr = Utils.GetEnvironmentVariable("SA-FHIRMAPPEDRESOURCES");
                        if (!string.IsNullOrEmpty(fsr)) _fhirSupportedResources = fsr.Split(",");
                        if (string.IsNullOrEmpty(_sbcfhirupdates) || string.IsNullOrEmpty(_qname))
                        {
                            log.LogError($"FHIRCDSSyncAgentPostProcess2: Failed to initialize SA-SERVICEBUSNAMESPACEFHIRUPDATES and/or Queue name is/are not defined...Check Configuration");
                            initializationfailed = true;
                            return;
                        }
                        _queueClient = new ServiceBusClient(Utils.GetEnvironmentVariable("SA-SERVICEBUSNAMESPACEFHIRUPDATES"));
                        _sender = _queueClient.CreateSender(_qname);


                    }
                    catch (Exception e)
                    {
                        log.LogError($"FHIRCDSSyncAgentPostProcess2: Failed to initialize ServiceBusClient:{e.Message}->{e.StackTrace}");
                        initializationfailed = true;
                    }
                }
            }


        }
        public async Task<ProxyProcessResult> Process(FHIRResponse response, HttpRequest req, ILogger log, ClaimsPrincipal principal)
        {

            try
            {
                FHIRParsedPath pp = req.parsePath();
                //Do we need to send on to CDS? Don't send for GET/PATCH, errors, X-MS-FHIRCDSSynAgent Header is present
                if (req.Method.Equals("GET") || (int)response.StatusCode > 299 ||
                    req.Method.Equals("PATCH") || req.Headers["X-MS-FHIRCDSSynAgent"] == "true")
                    return new ProxyProcessResult(true, "", "", response);
                InitQueueClient(log);
                if (_queueClient == null)
                {
                    log.LogWarning($"FHIRCDSSyncAgentPostProcess2: Service Bus Queue Client not initialized will not publish....Check Environment Configuration");
                    return new ProxyProcessResult(true, "", "", response);
                }
                if (_fhirSupportedResources == null)
                {
                    log.LogWarning($"FHIRCDSSyncAgentPostProcess2: No mapped resources configured (SA-FHIRMAPPEDRESOURCES) will not publish....Check Environment Configuration");
                    return new ProxyProcessResult(true, "", "", response);
                }
                JArray entries = null;
                if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Created)
                {
                    var fhirresp = response.toJToken();
                    if (!fhirresp.IsNullOrEmpty() && (fhirresp.FHIRResourceType().Equals("Bundle") && ((string)fhirresp["type"]).EndsWith("-response")))
                    {

                        entries = (JArray)fhirresp["entry"];

                    }
                    else
                    {
                        entries = new JArray();
                        JObject stub = new JObject();
                        stub["response"] = new JObject();
                        stub["response"]["status"] = (int)response.StatusCode + " " + response.StatusCode.ToString();
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
                    stub["resource"]["meta"] = new JObject();
                    stub["resource"]["meta"]["versionId"] = "1";
                    entries.Add(stub);
                }
                await publishFHIREvents(entries, log);



            }

            catch (Exception exception)
            {
                log.LogError(exception, $"FHIRCDSSyncAgentPostProcess2 Exception: {exception.Message}");

            }

            return new ProxyProcessResult(true, "", "", response);

        }
        private async Task publishFHIREvents(JArray entries, ILogger log)
        {

            if (!entries.IsNullOrEmpty())
            {
                using ServiceBusMessageBatch messageBatch = await _sender.CreateMessageBatchAsync();
                foreach (JToken tok in entries)
                {
                    //Don't queue if no supported
                    if (!Array.Exists(_fhirSupportedResources, element => element == tok["resource"].FHIRResourceType()))
                        continue;

                    string entrystatus = (string)tok["response"]["status"];
                    ServiceBusMessage dta = createMsg(entrystatus, tok["resource"]);
                    if (!messageBatch.TryAddMessage(dta))
                    {
                        throw new Exception("FHIRCDSSyncAgentPostProcess2:Message Batch is too large");
                    }

                }
                // Send the message batch to the queue.
                await _sender.SendMessagesAsync(messageBatch);

            }

        }
        private ServiceBusMessage createMsg(string status, JToken resource)
        {
            if (resource.IsNullOrEmpty()) return null;
            string action = "Unknown";
            if (status.StartsWith("200")) action = _updateAction;
            if (status.StartsWith("201")) action = "Create";
            if (status.Contains("DELETE")) action = "Delete";
            string _msgBody = resource.FHIRResourceType() + "/" + resource.FHIRResourceId() + "," + action + ",";
            // Get CDS Id
            if (!resource["identifier"].IsNullOrEmpty() && !action.Equals("Create"))
            {
                var _srchToken = resource["identifier"].SelectToken("$.[?(@.system=='CDSEntityID')]");
                if (_srchToken != null)
                    _msgBody += _srchToken["value"].ToString();
            }
            ServiceBusMessage dta = new ServiceBusMessage(Encoding.UTF8.GetBytes(_msgBody));
            //For duplicate message sending hash together 
            dta.MessageId = Guid.NewGuid().ToString();
            if (!_bulkLoadMode)
            {
                //Partioning and Session locks are defaulted to resource type, if the resource is patient/subject based the key will be the reference
                string partitionkey = resource.FHIRResourceType();
                if (resource.FHIRResourceType().Equals("Patient"))
                {
                    partitionkey = resource.FHIRReferenceId();
                }
                else if (!resource["patient"].IsNullOrEmpty() && !resource["patient"]["reference"].IsNullOrEmpty())
                {
                    partitionkey = (string)resource["patient"]["reference"];

                }
                else if (!resource["subject"].IsNullOrEmpty() && !resource["subject"]["reference"].IsNullOrEmpty())
                {
                    partitionkey = (string)resource["subject"]["reference"];
                }
                dta.PartitionKey = partitionkey;
                //Set session to be the same as partition key
                dta.SessionId = partitionkey;
            }
            return dta;
        }
        public string hashMessage(string s)
        {
            string hash;
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
            {
                hash = BitConverter.ToString(
                  md5.ComputeHash(Encoding.UTF8.GetBytes(s))
                ).Replace("-", String.Empty);
            }
            return hash;
        }
    }
}