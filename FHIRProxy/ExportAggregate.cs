
using Microsoft.WindowsAzure.Storage.Table;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace FHIRProxy
{
    public class ExportAggregate : TableEntity
    {
        public ExportAggregate()
        {

        }
        public ExportAggregate(string exportRequestUrl, List<string> childContentLocationUrls)
        {
            PartitionKey = "exportaggregate";
            RowKey = Guid.NewGuid().ToString();
            ExportRequestUrl = exportRequestUrl;
            ExportUrls = string.Join(",", childContentLocationUrls);
        }
        public string ExportId => RowKey;

        public string ExportRequestUrl { get; set; }

        public List<string> ExportUrlList => ExportUrls.Split(",").ToList();


        public string ExportUrls { get; set; }
    }
}
