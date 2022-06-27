using Models;

namespace GetEleasticDataServices.Models
{
    public class SaveToFile
    {
        public SaveToFile(AbuseIpDb abuseIp, ElasticDocIndex docIndex, string email)
        {
            this.abuseIp = abuseIp;
            this.docIndex = docIndex;
            this.email = email;
        }

        public string email { get; set; }
        public AbuseIpDb abuseIp { get; set; }
        public ElasticDocIndex docIndex { get; set; }
    }
}