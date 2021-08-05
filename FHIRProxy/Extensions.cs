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

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Http;

namespace FHIRProxy
{
    public class FHIRParsedPath
    {
        public string ResourceType { get; set; }
        public string ResourceId { get; set; }
        public string Operation { get; set; }
        public string VersionId { get; set; }
        public IEnumerable<string> PathElements { get; set; }
    }
    public static class Extensions
    {
        public static FHIRParsedPath parsePath(this HttpRequest req)
        {
            var retVal = new FHIRParsedPath();
            //Remove proxy route
                      
            if (req.Path.HasValue)
            {
                string path = req.Path.Value;
                if (path.StartsWith("/fhir/")) path = path.Substring(6);
                if (path.StartsWith("/fhir")) path = path.Substring(5);
                string[] p = path.Split("/");
                if (p.Count() > 0) retVal.ResourceType = p[0];
                if (p.Count() > 1) retVal.ResourceId = p[1];
                if (p.Count() > 2) retVal.Operation = p[2];
                if (p.Count() > 3) retVal.VersionId = p[3];
                retVal.PathElements = p;
            }
            return retVal;
        }
        public static bool SafeEquals(this string source, string compare)
        {
            
            if (compare == null || source==null) return false;
            return source.Equals(compare);
        }
        public static string SerializeList<T>(this List<T> thelist)
        {
            if (thelist == null) return null;
            return JsonConvert.SerializeObject(thelist);
        }
        public static List<T> DeSerializeList<T>(this string str)
        {
            if (string.IsNullOrEmpty(str)) return null;
            return JsonConvert.DeserializeObject<List<T>>(str);
        }
        public static string FHIRResourceId(this JToken token)
        {
            if (!token.IsNullOrEmpty()) return (string)token["id"];
            return "";
        }
        public static string FHIRResourceType(this JToken token)
        {
            if (!token.IsNullOrEmpty()) return (string)token["resourceType"];
            return "";
        }
        public static string FHIRVersionId(this JToken token)
        {
            if (!token.IsNullOrEmpty() && !token["meta"].IsNullOrEmpty())
            {
                return (string)token["meta"]?["versionId"];
            }
            return "";
        }
        public static string FHIRLastUpdated(this JToken token)
        {
            if (!token.IsNullOrEmpty() && !token["meta"].IsNullOrEmpty() && !token["meta"]["lastUpdated"].IsNullOrEmpty())
            {
                return JsonConvert.SerializeObject(token["meta"]?["lastUpdated"]).Replace("\"", "");
            }
            return "";
        }
        public static string FHIRReferenceId(this JToken token)
        {
            if (!token.IsNullOrEmpty() && !token["resourceType"].IsNullOrEmpty() && !token["id"].IsNullOrEmpty())
            {
                return (string)token["resourceType"] + "/" + (string)token["id"];
            }
            return "";
        }
        public static bool IsNullOrEmpty(this JToken token)
        {
            return (token == null) ||
                   (token.Type == JTokenType.Array && !token.HasValues) ||
                   (token.Type == JTokenType.Object && !token.HasValues) ||
                   (token.Type == JTokenType.String && token.ToString() == String.Empty) ||
                   (token.Type == JTokenType.Null);
        }
        public static JToken getFirstField(this JToken o)
        {
            if (o == null) return null;
            if (o.Type == JTokenType.Array)
            {
                if (o.HasValues) return ((JArray)o)[0];
                return null;
            }
            return o;
        }
        public static string fhirUser(this ClaimsIdentity ci)
        {
            IEnumerable<Claim> claims = ci.Claims;
            string fhiruser = claims.Where(c => c.Type == Utils.GetEnvironmentVariable("FP-FHIR-USER-CLAIM", "fhirUser")).Select(c => c.Value).SingleOrDefault();
            if (string.IsNullOrEmpty(fhiruser))
            {
                return null;
            }
            return fhiruser;
        }
        public static bool HasScope(this ClaimsIdentity identity, string scope)
        {
            if (string.IsNullOrEmpty(scope)) return false;
            IEnumerable<Claim> claims = identity.Claims.Where(x => x.Type == "http://schemas.microsoft.com/identity/claims/scope");
            if (claims==null || claims.Count()==0) claims = identity.Claims.Where(x => x.Type == "scp");
            foreach (Claim c in claims)
            {
                if (c.Value.Contains(scope, StringComparison.InvariantCultureIgnoreCase)) return true;
            }
            return false;
        }
        public static bool IsInFHIRRole(this ClaimsIdentity identity, string rolestring)
        {
            if (string.IsNullOrEmpty(rolestring)) return false;
            string[] roles = rolestring.Split(",");
            foreach (string r in roles)
            {
                if (identity.Roles().Exists(s => s.Equals(r)))
                {
                    return true;
                }
            }
            return false;
        }
        public static List<string> Roles(this ClaimsIdentity identity)
        {

            return identity.Claims
                           .Where(c => c.Type == "roles")
                           .Select(c => c.Value)
                           .ToList();
        }
        public static string ObjectId(this ClaimsIdentity identity)
        {
            var tid = identity.Claims
                          .Where(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier");
                        
            if (!tid.Any())
            {
                tid = identity.Claims
                          .Where(c => c.Type == "oid");
            }
            if (tid.Any())
            {
                return tid.Single().Value.ToAzureKeyString();
            }
            else
            {
                return "";
            }

        }
        public static string Tenant(this ClaimsIdentity identity)
        {
            var tid = identity.Claims
                           .Where(c => c.Type == "http://schemas.microsoft.com/identity/claims/tenantid");
            if (!tid.Any())
            {
                tid = identity.Claims
                           .Where(c => c.Type == "tid");
               
            }
            if (tid.Any())
            {
                return tid.Single().Value.ToAzureKeyString();
            }
            else
            {
                return "";
            }


        }
        public static string ToAzureKeyString(this string str)
        {
            var sb = new StringBuilder();
            foreach (var c in str
                .Where(c => c != '/'
                            && c != '\\'
                            && c != '#'
                            && c != '/'
                            && c != '?'
                            && !char.IsControl(c)))
                sb.Append(c);
            return sb.ToString();
        }
        public static IEnumerable<T> All<T>(
        this IQueryCollection collection,
        string key)
        {
            var values = new List<T>();

            if (collection.TryGetValue(key, out var results))
            {
                foreach (var s in results)
                {
                    try
                    {
                        var result = (T)Convert.ChangeType(s, typeof(T));
                        values.Add(result);
                    }
                    catch (Exception)
                    {
                        // conversion failed
                        // skip value
                    }
                }
            }

            // return an array with at least one
            return values;
        }

        public static T Get<T>(
            this IQueryCollection collection,
            string key,
            T @default = default,
            ParameterPick option = ParameterPick.First)
        {
            var values = All<T>(collection, key);
            var value = @default;

            if (values.Any())
            {
                value = option switch
                {
                    ParameterPick.First => values.FirstOrDefault(),
                    ParameterPick.Last => values.LastOrDefault(),
                    _ => value
                };
            }

            return value ?? @default;
        }
    }    
    public enum ParameterPick
    {
        First,
        Last
    }
}

