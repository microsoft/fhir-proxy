using System;
using System.Collections.Generic;
using System.Text;

namespace FHIRProxy
{
    public class UserScopeResult
    {
        public UserScopeResult(bool result, string message="")
        {
            this.Result = result;
            this.Message = message;
        }
        public bool Result { get; set;}
        public string Message { get; set; }
    }
}
