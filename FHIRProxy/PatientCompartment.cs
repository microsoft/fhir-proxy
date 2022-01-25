using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;

namespace FHIRProxy
{

    public class PatientCompartment
    {
        private static object _lock = new object();
        private static PatientCompartment _instance = null;
        private Dictionary<string, List<string>> _compresources = new Dictionary<string, List<string>>();

        private PatientCompartment()
        {
            try
            {
#nullable enable
                Stream? stream1 = Assembly.GetExecutingAssembly().GetManifestResourceStream("FHIRProxy.patient_comp.json");
#nullable disable
                StreamReader reader = new StreamReader(stream1);
                JObject _compobj = JObject.Parse(reader.ReadToEnd());
                JArray arr = (JArray)_compobj["resource"];
                if (arr != null)
                {
                    foreach (JToken t in arr)
                    {
                        string key = t["code"].ToString();
                        List<string> p = new List<string>();
                        JArray arr2 = (JArray)t["param"];
                        if (arr2 != null)
                        {
                            foreach (JToken t1 in arr2)
                            {
                                p.Add(t1.ToString());
                            }
                        }
                        //Special cases for actor/subject resource queries to check for patient as well
                        if (p.Contains("actor") && !p.Contains("patient")) p.Add("patient");
                        if (p.Contains("subject") && !p.Contains("patient")) p.Add("patient");
                        _compresources.TryAdd(key, p);
                    }
                }
            }
            catch (Exception)
            {

            }
        }
        public static PatientCompartment Instance()
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new PatientCompartment();
                    }
                }
            }
            return _instance;
        }
        public bool isPatientCompartmentResource(string resourceType)
        {
            if (string.IsNullOrEmpty(resourceType)) return false;
            return _compresources.ContainsKey(resourceType);
        }
        public string[] GetPatientParametersForResourceType(string resourceType)
        {
            if (string.IsNullOrEmpty(resourceType)) return null;
            List<string> parms = null;
            if (_compresources.TryGetValue(resourceType, out parms))
            {
                return parms.ToArray();
            }
            return null;
        }
        
    }
}
