
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
        public ExportAggregate(List<string> childContentLocationUrls)
        {
            this.PartitionKey = "exportaggregate";
            this.RowKey = Guid.NewGuid().ToString();
        }
        public string ExportId {
            get { return this.RowKey; }
        }

        public List<string> ExportUrlList => ExportUrls.Split(",").ToList();


        public string ExportUrls { get; set; }
    }
}
