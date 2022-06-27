using Models.Server;
using System;
using System.Configuration;
using System.Text.RegularExpressions;
using Topshelf;

namespace GetEleasticDataServices
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            string ConnectionElastic, Emails, MinScore, Path, AbuseDbIpKey, interval, ServerName;
            ConnectionElastic = Regex.Replace(ConfigurationManager.AppSettings.Get("ConnectionElastic"), @"\t|\n|\r", "");
            Emails = Regex.Replace(ConfigurationManager.AppSettings.Get("Emails"), @"\t|\n|\r", "");
            MinScore = Regex.Replace(ConfigurationManager.AppSettings.Get("MinScore"), @"\t|\n|\r", "");
            interval = Regex.Replace(ConfigurationManager.AppSettings.Get("Interval"), @"\t|\n|\r", "");
            Path = Regex.Replace(ConfigurationManager.AppSettings.Get("Path"), @"\t|\n|\r", "");
            ServerName = Regex.Replace(ConfigurationManager.AppSettings.Get("ServerName"), @"\t|\n|\r", "");
            AbuseDbIpKey = Regex.Replace(ConfigurationManager.AppSettings.Get("AbuseDbIpKey"), @"\t|\n|\r", "");

            var exitCode = HostFactory.Run(x =>
            {
                x.Service<ElasticServer>(s =>
                {
                    s.ConstructUsing(elasticSearch => new ElasticServer(ConnectionElastic, Emails, MinScore, Path, AbuseDbIpKey, interval, ServerName));
                    s.WhenStarted(elasticSearch => elasticSearch.Start());
                    s.WhenStopped(elasticSearch => elasticSearch.Stop());
                });

                x.RunAsLocalService();
                x.SetServiceName("ElasticSearchExportDataService");
                x.SetDisplayName("Elastic Search Export Data Service");
                x.SetDescription("export each min data from elastic and save it as a file for the last 15 min");
            });

            int exitCodeValue = (int)Convert.ChangeType(exitCode, exitCode.GetTypeCode());
            Environment.ExitCode = exitCodeValue;
        }
    }
}