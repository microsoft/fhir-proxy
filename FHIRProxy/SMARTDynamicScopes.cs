using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Reflection.Emit;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Amqp.Framing;
using System.Reflection.Metadata;

namespace FHIRProxy
{
    public static class SMARTDynamicScopes
    {
        public const string STYLE_STRING= "#permissions {   font-family: Arial, Helvetica, sans-serif;   border-collapse: collapse;   width: 50%; }  #permissions td, #permissions th {   border: 1px solid #ddd;   padding: 8px; }  #permissions tr:nth-child(even){background-color: #f2f2f2;}  #permissions th {   padding-top: 12px;   padding-bottom: 12px;   text-align: left;   background-color: #3933FF;   color: white; } body {   margin:40px; } * {   box-sizing: border-box; }  #username {   display:block;   width:50%;   margin:10px 0;   padding:10px;   border:1px solid #111;   transition: .3s background-color; } #username:hover {   background-color: #fafafa; } #continue {   display:block;   width:10%;   margin:10px 0;   padding:10px;   border:1px solid #111;   transition: .3s background-color; }";
        [FunctionName("SMARTDynamicScopes")]
        public static IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "oauth2/dynamicscopes")] HttpRequest req,
         ILogger log)
        {
          
            string scopeString = req.Query["scope"];
            StringBuilder web = new StringBuilder();
            web.AppendLine("<html>")
            .AppendLine("<head>")
            .AppendLine("<style>")
            .AppendLine(STYLE_STRING)
            .AppendLine("</style>")
            .AppendLine("<script language=\"javascript\">")
            .AppendLine("function toggle(source) {")
            .AppendLine("var x = document.getElementsByName(\"chkscope[]\"), i;")
            .AppendLine("   for (i = 0; i < x.length; i++)")
            .AppendLine("   {")
            .AppendLine("       if (x[i].type == \"checkbox\")")
            .AppendLine("       {")
            .AppendLine("           x[i].checked = source.checked;")
            .AppendLine("       }")
            .AppendLine("   }")
            .AppendLine("}")
            .AppendLine("function redirect() {")
            .AppendLine("   var restrictedscopes=\"\"")
            .AppendLine("   var username=document.getElementById(\"username\")")
            .AppendLine("   if (username.value.length < 1) {")
            .AppendLine("       alert(\"You must enter a username (your login name)\");")
            .AppendLine("       username.focus()") 
            .AppendLine("       return;")
            .AppendLine("   }")
            .AppendLine("   var x = document.getElementsByName(\"chkscope[]\"), i;")
            .AppendLine("   for (i = 0; i < x.length; i++)")
            .AppendLine("   {")
            .AppendLine("       if (x[i].type == \"checkbox\")")
            .AppendLine("       {")
            .AppendLine("           if (x[i].checked)")
            .AppendLine("           {")
            .AppendLine("             if (i==0) restrictedscopes=x[i].value; else restrictedscopes=restrictedscopes + \" \" + x[i].value;")
            .AppendLine("           }")
            .AppendLine("       }")
            .AppendLine("   }")
            .AppendLine($" var rurl = \"{req.Scheme}://{req.Host}/oauth2/authorize{req.QueryString.Value}&sessionscopes=\" + encodeURIComponent(restrictedscopes) + \"&login_hint=\" + username.value;")
            .AppendLine("  window.location.replace(rurl);")
            .AppendLine("}")
            .AppendLine("</script>")
            .AppendLine("</head>")
            .AppendLine("<body style=\"{zoom: 100%;}\">")
            .AppendLine("<table id=\"permissions\">")
            .AppendLine("<tr><th colspan=2 style=\"width:100%\">Application Access</th></tr>")
            .AppendLine("<tr><td colspan=2 style=\"width:100%\">")
            .AppendLine("<label for=\"username\">User Name: </label>")
            .AppendLine("<input type=\"text\" id=\"username\" name=\"username\" placeholder=\"Login...\"></td></tr>")
            .AppendLine("<tr><th colspan=2 style=\"width:100%\">Session Permissions</th></tr>");
            var s = scopeString.Split(" ");
            int x = 0;
            web.AppendLine($"<tr><td style=\"width:5%\"><input id=\"selectall\" name=\"selectall\" type=\"checkbox\" onClick=\"toggle(this)\" checked></td><td style=\"width:95%\"></td></tr>");
            foreach (string t in s)
            {
                if (t.StartsWith("patient/"))
                {
                    var u = t.Split('/');
                    var v = u[1].Split('.');
                    var resource = v[0];
                    var perms = v[1];
                    string msg = "";
                    if (perms.ToLower().Equals("read")) perms = "rs";
                    if (perms.ToLower().Equals("write")) perms = "cud";
                    if (perms.Equals("*")) perms = "cruds";
                    bool canread = (perms.Contains("r", StringComparison.InvariantCultureIgnoreCase));
                    bool cancreate = (perms.Contains("c", StringComparison.InvariantCultureIgnoreCase));
                    bool canupdate = (perms.Contains("u", StringComparison.InvariantCultureIgnoreCase));
                    bool candelete = (perms.Contains("d", StringComparison.InvariantCultureIgnoreCase));
                    bool cansearch = (perms.Contains("s", StringComparison.InvariantCultureIgnoreCase));
                    if (canread || cansearch) msg = "Read";
                    if (cancreate || canupdate || candelete) msg = msg + (msg.Length == 0 ? "" : "/") + "Write";
                    msg = msg + " " + resource + " resources";
                    web.Append($"<tr><td style=\"width:5%\"><input id=\"chkscope{x}\" name=\"chkscope[]\" type=\"checkbox\" value=\"{t}\" checked></td>");
                    web.AppendLine($"<td style=\"width:95%\">{msg}</td></tr>");
                    x++;
                } 
                
            }
            web.AppendLine("</table><br/>");
            web.AppendLine("<p width=50%>");
            web.AppendLine("<input id=\"continue\" type=\"button\" onClick=\"redirect()\"value=\"Continue\"/></p>");
            web.AppendLine("</html>");
            return new ContentResult { Content = $"{web.ToString()}", ContentType = "text/html", StatusCode = (int)HttpStatusCode.OK };
        }
    }
}
