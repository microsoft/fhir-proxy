using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace FHIRProxy
{
    public class UserScopeResult
    {
        public UserScopeResult(bool result, JToken content, string message="")
        {
            this.Result = result;
            this.ResponseContent = content;
            this.Message = message;
        }
        public bool Result { get; set;}
        public string Message { get; set; }
        public JToken ResponseContent { get; set; }
    }
}
