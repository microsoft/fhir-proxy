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
    class CovidVaccineStatusCDSHook : IProxyPreProcess
    {
        public async Task<ProxyProcessResult> Process(string requestBody, HttpRequest req, ILogger log, ClaimsPrincipal principal)
        {
            FHIRParsedPath pp = req.parsePath();
            if (req.Method.Equals("POST") && pp.ResourceType.SafeEquals("cds-services") && pp.ResourceId.SafeEquals("covid-vaccine-history"))
            {
                JObject hookbody = JObject.Parse(requestBody);
                ClaimsIdentity ci = (ClaimsIdentity)principal.Identity;
                //TODO: Add Scope Check based on FHIR User
                string fhirUser = ci.fhirUser();
                //Check which Patient
                string patientid = (string)hookbody["context"]["patientId"];
                //Load COVID Vaccinations
                var nextresult = await FHIRClient.CallFHIRServer($"Immunization?patient={patientid}&vaccine-code=http://snomed.info/sct|840534001","","GET",req.Headers,log);
                if (nextresult.IsSuccess())
                {
                    JObject resp = (JObject)nextresult.toJToken();
                    if (resp.FHIRResourceType().Equals("Bundle"))
                    {
                        JArray arr = (JArray)resp["entry"];
                        JObject cdshookresp = new JObject();
                        cdshookresp["cards"] = new JArray();
                        ProxyProcessResult ppr = null;
                        if (arr != null && arr.Count > 0)
                        {

                            int dn = 0;
                            foreach (JToken vaccineresp in arr)
                            {
                                JToken v = vaccineresp["resource"];
                                string manref = (string)v["manufacturer"]["reference"];
                                //Load Vaccine Dates and details
                                var org = await FHIRClient.CallFHIRServer($"{manref}", "", "GET", req.Headers, log);
                                string manname = "";
                                if (org.IsSuccess())
                                {
                                    JToken t = org.toJToken();
                                    manname = (t["name"].IsNullOrEmpty() ? "Unknown" : t["name"].ToString());
                                }
                                string lotNumber = (string)v["lotNumber"];
                                DateTime admindt = (DateTime)v["occurrenceDateTime"];
                                dn++;
                                JObject covidcard = new JObject();
                                covidcard["summary"] = "Patient Received a Dose of Covid-19 Vaccine";
                                covidcard["detail"] = $"Patient received dose number {dn} of {manname} vaccine Lot# {lotNumber} on {admindt.ToString("d")}";
                                covidcard["indicator"] = "info";
                                covidcard["source"] = new JObject();
                                covidcard["source"]["label"] = $"FHIR Server {req.Host}";
                                ((JArray)cdshookresp["cards"]).Add(covidcard);
                            }

                        }
                        else
                        {
                            JObject covidcard = new JObject();
                            covidcard["summary"] = "Patient has not received any covid vaccine doses";
                            covidcard["detail"] = $"Patient has not received any covid vaccine doses";
                            covidcard["indicator"] = "warning";
                            covidcard["source"] = new JObject();
                            covidcard["source"]["label"] = $"FHIR Server {req.Host}";
                            ((JArray)cdshookresp["cards"]).Add(covidcard);
                        }
                        JArray ta = (JArray)cdshookresp["cards"];
                        if (ta.Count < 2)
                        {
                            JObject covidcard = new JObject();
                            covidcard["summary"] = "Patient is not fully vaccinated";
                            covidcard["detail"] = $"Patient has only received first dose";
                            covidcard["indicator"] = "warning";
                            covidcard["source"] = new JObject();
                            covidcard["source"]["label"] = $"FHIR Server {req.Host}";
                            ((JArray)cdshookresp["cards"]).Add(covidcard);
                        } else if (ta.Count < 3)
                        {
                            JObject covidcard = new JObject();
                            covidcard["summary"] = "Patient is overdue for COVID-19 booster";
                            covidcard["detail"] = $"Patient has only received first two doses";
                            covidcard["indicator"] = "warning";
                            covidcard["source"] = new JObject();
                            covidcard["source"]["label"] = $"FHIR Server {req.Host}";
                            ((JArray)cdshookresp["cards"]).Add(covidcard);
                        }
                        FHIRResponse fr1 = new FHIRResponse();
                        fr1.Content = cdshookresp.ToString();
                        ppr = new ProxyProcessResult(false, "", requestBody, fr1);
                        ppr.DirectReply = true;
                        return ppr;
                    }
                    else
                    {
                        return new ProxyProcessResult(false, $"Unexpected result searching for vaccination records:{resp.ToString()}", requestBody, null);
                    }
                } else
                {
                    return new ProxyProcessResult(false, $"Error searching for vaccination records:{nextresult.ToString()}", requestBody, null);
                }
                
            }
            return new ProxyProcessResult(true, "", requestBody, null);
        }
      
       
    }
}
