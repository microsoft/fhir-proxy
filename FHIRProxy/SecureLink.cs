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
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Security.Claims;
using System.Collections.Generic;
using System.Linq;
using Microsoft.WindowsAzure.Storage.Table;
using System.Text;

namespace FHIRProxy
{
    public static class SecureLink
    {
        public static string _bearerToken;
        private static object _lock = new object();
        private static string[] allowedresources = { "Patient", "Practitioner", "RelatedPerson" };
        private static string[] validcmds = { "find","link", "unlink", "list","usage" };
        private static string htmltemplatehead = "<html><head><style>body {font-family: arial, sans-serif;} table {font-family: arial, sans-serif;border-collapse: collapse;width: 100%;}td, th {border: 1px solid #dddddd;text-align: left;padding: 8px;}tr:nth-child(even){background-color: #dddddd;}</style></head><body>";
        private static string htmltemplatefoot = "</body></html>";
        [FHIRProxyAuthorization]
        [FunctionName("SecureLink")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "manage/{cmd}/{res?}/{id?}/{tid?}/{oid?}")] HttpRequest req,
            ILogger log, string cmd, string res, string id, string tid, string oid)
        {

            
            if (string.IsNullOrEmpty(cmd) || !validcmds.Any(cmd.Contains))
            {
                return new BadRequestObjectResult($"Invalid Command....Valid commands are: {String.Join(",",validcmds)}");
            }
            //Are we linking the correct resource type
            if (!cmd.ToLower().Equals("usage") && (string.IsNullOrEmpty(res) || !allowedresources.Any(res.Contains)))
            {
                return new BadRequestObjectResult($"Resource must be one of: {String.Join(",", allowedresources)}");
            }
            int i_link_days = 0;
            int.TryParse(System.Environment.GetEnvironmentVariable("FP-LINK-DAYS"), out i_link_days);
            if (i_link_days == 0) i_link_days = 365;
            CloudTable table = Utils.getTable();
            switch (cmd)
            {
                case "usage":
                    StringBuilder sb0 = new StringBuilder();
                    sb0.Append(htmltemplatehead);
                    sb0.Append($"<h2>SecureLink Usage</h2>");
                    sb0.Append($"<p align=left>{req.Scheme + "://" + req.Host.Value}/manage/[{String.Join("|", validcmds)}]/[{String.Join("|", allowedresources)}]/[fhir logical id | name to find]/[tenant id | OIDC Issuer]/[oid | unique user id]</br>");
                    sb0.Append($"This proxy instance is connect to FHIR Server: {Utils.GetEnvironmentVariable("FS-URL", "")}</p>");
                    sb0.Append(htmltemplatefoot);
                    return new ContentResult() { Content = sb0.ToString(), StatusCode = 200, ContentType = "text/html" };
                case "find":
                    
                    StringBuilder sb = new StringBuilder();
                    var fhirresp = await FHIRClient.CallFHIRServer($"{res}?name={id}", null, "GET", req.Headers, log);
                    if (!fhirresp.IsSuccess())
                    {
                        return ProxyFunction.genContentResult(fhirresp, log);
                    }
                    sb.Append(htmltemplatehead);
                    sb.Append($"<h2>{res} resources matching {id} on FHIR Server {Utils.GetEnvironmentVariable("FS-URL","")} </h2>");
                    var o = fhirresp.toJToken();
                    JArray entries = (JArray) o["entry"];
                    if (entries != null)
                    {
                        LinkEntity alreadylink = Utils.getEntity<LinkEntity>(table, res, tid + "-" + oid);
                        sb.Append("<table>");
                        sb.Append("<tr><th>FHIR Id</th><th>Name</th><th>DOB</th><th>Gender</th><th>Link URL</th></tr>");
                        foreach (JToken tok in entries)
                        {
                            string fhirid = tok["resource"]["id"].ToString();
                            sb.Append("<tr>");
                            sb.Append($"<td>{fhirid}</td>");
                            sb.Append(getDemog(tok["resource"]));
                            if (alreadylink!=null && alreadylink.LinkedResourceId.Equals(fhirid))
                            {
                                string unlinkurl = $"{req.Scheme}://{req.Host.Value}/manage/unlink/{res}/{fhirid}/{tid}/{oid}";
                                sb.Append($"<td>Already Linked! <a href='{unlinkurl}' target='_blank'>Unlink</a></td>");
                            } else
                            {
                                string linkurl = $"{req.Scheme}://{req.Host.Value}/manage/link/{res}/{fhirid}/{tid}/{oid}";
                                sb.Append($"<td><a href='{linkurl}' target='_blank'> Link</a></td>");
                            }
                            sb.Append("</tr>");
                        }
                        sb.Append("</table>");
                    }
                    sb.Append(htmltemplatefoot);
                    return new ContentResult() { Content = sb.ToString(), StatusCode = 200, ContentType = "text/html" };
                case "link":
                    LinkEntity linkentity = new LinkEntity(res, tid + "-" + oid);
                    linkentity.ValidUntil = DateTime.Now.AddDays(i_link_days);
                    linkentity.LinkedResourceId = id;
                    Utils.setEntity(table, linkentity);
                    return new OkObjectResult($"Identity: {oid} in directory {tid} is now linked to FHIR {res}/{id} on FHIR Server: {Utils.GetEnvironmentVariable("FS-URL", "")}");
                case "unlink":
                    LinkEntity delentity = Utils.getEntity<LinkEntity>(table, res, tid + "-" + oid);
                    if (delentity==null) return new OkObjectResult($"Resource {res}/{id} in FHIR has no links to Identity {oid} in directory {tid}");
                    Utils.deleteEntity(table,delentity);
                    return new OkObjectResult($"Identity: {oid} in directory {tid} has been unlinked from FHIR {res}/{id} on FHIR Server: {Utils.GetEnvironmentVariable("FS-URL", "")}");
                case "list":
                    LinkEntity entity = Utils.getEntity<LinkEntity>(table, res, tid + "-" + oid);
                    if (entity != null)
                        return new OkObjectResult($"Resource {res}/{id} on FHIR Server: {Utils.GetEnvironmentVariable("FS-URL", "")} is linked to Identity: {oid} in directory {tid}");
                    else
                        return new OkObjectResult($"Resource {res}/{id} on FHIR Server: {Utils.GetEnvironmentVariable("FS-URL", "")} has no links to Identity {oid} in directory {tid}");
            }
            return new OkObjectResult($"No action taken Identity: {oid}");

        }
        private static string getDemog(JToken tok)
        {
            if (tok == null) return "";
            StringBuilder sb = new StringBuilder();
            //Name
            sb.Append("<td>");
            if (!tok["name"].IsNullOrEmpty())
            {
                JArray name = (JArray)tok["name"];
                if (!name.IsNullOrEmpty())
                {
                    JArray given = (JArray)name[0]["given"];
                    if (!given.IsNullOrEmpty())
                    {
                        sb.Append(given[0].ToString());
                        if (given.Count > 1)
                        {
                            sb.Append($" {given[1]}");
                        }
                                          }
                    if (!name[0]["family"].IsNullOrEmpty())
                    {
                        sb.Append($" {name[0]["family"].ToString()}");
                    }
                }
            }
            sb.Append("</td>");
            //DOB
            sb.Append("<td>");
            if (!tok["birthDate"].IsNullOrEmpty())
            {
                DateTime bd = (DateTime)tok["birthDate"];
                sb.Append($"{bd.ToLongDateString()}");
            }
            sb.Append("</td>");
            sb.Append("<td>");
            if (!tok["gender"].IsNullOrEmpty())
            {
                sb.Append($"{tok["gender"].ToString()}");
            }
            sb.Append("</td>");
            return sb.ToString();
        }
      

    }
}
