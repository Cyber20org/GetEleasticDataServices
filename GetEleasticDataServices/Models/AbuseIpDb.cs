using System;

namespace Models
{
    public class AbuseIpDb
    {
        public Guid id { get; set; }
        public string ipAddress { get; set; }
        public bool? isPublic { get; set; }
        public int? ipVersion { get; set; }
        public bool isWhitelisted { get; set; }
        public int abuseConfidenceScore { get; set; }
        public string? usageType { get; set; }
        public string? isp { get; set; }
        public string? domain { get; set; }
        public int totalReports { get; set; }
        public int numDistinctUsers { get; set; }
    }
}