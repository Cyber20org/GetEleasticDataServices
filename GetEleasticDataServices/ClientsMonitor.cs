using System;

namespace Models
{
    public class ClientsMonitor
    {
        public string ClientName { get; set; }
        public DateTime TimeStamp { get; set; }
        public string ClientGroup { get; set; }
        public string LogedInUser { get; set; }
    }
}