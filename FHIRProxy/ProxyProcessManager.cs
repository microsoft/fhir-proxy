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

using LazyCache;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
namespace FHIRProxy
{
    class ProxyProcessManager
    {
        /* To enable Pre/Post Processors environment variables must be specified with valida configurations.
         * The Configuration format is FULLY_QUALIFIED_CLASS_NAME:CACHE_EXPIRATION_IN_MINUTES, comma separated entries.
         * The Expiration is optional and defaults to 1440 minutes cache expiration time.
         * Processors will be evicted from cache at configured expiration time from creation. To allow for garbage collection and management.  
         * For example to instanciate SamplePreProcessor with Cache Expiration of 30 minutes and ProfileValidationPreProcessor with default expiration the environment vairable FP-PRE-PROCESSOR-TYPES
         * would be set as follows:
         * FP-PRE-PROCESSOR-TYPES="FHIRProxy.preprocessors.SamplePreProcessor:30,FHIRProxy.preprocessors.ProfileValidationPreProcessor"
         * 
         * FP-POST-PROCESSOR-TYPES Follows the same convention and rules
         */
        private static IAppCache cache = new CachingService();

        private static readonly int DEF_EXP_MINS = 1440;
        public static async Task<ProxyProcessResult> RunPostProcessors(FHIRResponse response, HttpRequest req, ILogger log, ClaimsPrincipal principal)
        {
            ProxyProcessResult rslt = new ProxyProcessResult();
            //Default to server response 
            rslt.Response = response;
            //Get Configured PostProcessors
            string pps = System.Environment.GetEnvironmentVariable("FP-POST-PROCESSOR-TYPES");
            if (string.IsNullOrEmpty(pps)) return rslt;
            string[] types = pps.Split(",");
            foreach (string cls in types)
            {
                try
                {
                    string ic = cls;
                    DateTimeOffset os = DateTimeOffset.Now;
                    if (cls.Contains(":"))
                    {
                        string[] x = cls.Split(":");
                        ic = x[0];
                        int exp = DEF_EXP_MINS;
                        int.TryParse(x[1], out exp);
                        os = os.AddMinutes(exp);
                    }
                    else
                    {
                        os = os.AddMinutes(DEF_EXP_MINS);

                    }
                    IProxyPostProcess ip = (IProxyPostProcess)cache.GetOrAdd(cls, () => GetInstance(ic),os);
                    log.LogInformation($"ProxyProcessManager is running {cls} post-process...");
                    rslt = await ip.Process(rslt.Response,req, log, principal);
                    if (!rslt.Continue) return rslt;
                }
                catch (InvalidCastException ece)
                {
                    log.LogWarning($"{cls} does not seem to implement IProxyPostProcess and will not be executed:{ece.Message}");
                }
                catch (Exception e)
                {
                    log.LogError(e, $"Error trying to instanciate/execute post-process {cls}: {e.Message}");
                    FHIRResponse fhirresp = new FHIRResponse();
                    fhirresp.StatusCode = System.Net.HttpStatusCode.InternalServerError;
                    fhirresp.Content = Utils.genOOErrResponse("internalerror", $"Error trying to instanciate/execute post-process {cls}: {e.Message}");
                    return new ProxyProcessResult(false, "internalerror", "",fhirresp);
                }
            }
            return rslt;
        }
        public static async Task<ProxyProcessResult> RunPreProcessors(string requestBody, HttpRequest req, ILogger log, ClaimsPrincipal principal)
        {
            ProxyProcessResult rslt = new ProxyProcessResult();
            rslt.Request = requestBody;
            //Get Configured PreProcessors
            string pps = System.Environment.GetEnvironmentVariable("FP-PRE-PROCESSOR-TYPES");
            if (string.IsNullOrEmpty(pps)) return rslt;
            string[] types = pps.Split(",");
            foreach(string cls in types)
            {
                try
                {
                    string ic = cls;
                    DateTimeOffset os = DateTimeOffset.Now;
                    if (cls.Contains(":"))
                    {
                        string[] x = cls.Split(":");
                        ic = x[0];
                        int exp = DEF_EXP_MINS;
                        int.TryParse(x[1], out exp);
                        os = os.AddMinutes(exp);
                    } else
                    {
                        os = os.AddMinutes(DEF_EXP_MINS);

                    }
                    IProxyPreProcess ip =  (IProxyPreProcess) cache.GetOrAdd(cls, () => GetInstance(ic),os);
                    log.LogInformation($"ProxyProcessManager is running {cls} pre-process...");
                    rslt = await ip.Process(rslt.Request, req, log, principal);
                    if (!rslt.Continue) return rslt;
                }
                catch (InvalidCastException ece)
                {
                    log.LogWarning($"{cls} does not seem to implement IProxyPreProcess and will not be executed:{ece.Message}");
                }
                catch (Exception e)
                {
                    log.LogError(e,$"Error trying to instanciate/execute pre-process {cls}: {e.Message}");
                    FHIRResponse fhirresp = new FHIRResponse();
                    fhirresp.StatusCode = System.Net.HttpStatusCode.InternalServerError;
                    fhirresp.Content = Utils.genOOErrResponse("internalerror", $"Error trying to instanciate/execute post-process {cls}: {e.Message}");
                    return new ProxyProcessResult(false, "internalerror", "",fhirresp);
                }
            }
            return rslt;
        }
        public static object GetInstance(string strFullyQualifiedName)
        {
            Type t = Type.GetType(strFullyQualifiedName);
            return Activator.CreateInstance(t);
        }
    }
}
