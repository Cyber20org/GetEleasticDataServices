using System;
using System.Configuration;
using Models.Server;
using Topshelf;

namespace GetEleasticDataServices
{
    internal class Program
    {
        static void Main(string[] args)
        {

            string ConnectionElastic, Emails, MinScore, Path;
            ConnectionElastic = ConfigurationManager.AppSettings.Get("ConnectionElastic");
            Emails = ConfigurationManager.AppSettings.Get("Emails");
            MinScore = ConfigurationManager.AppSettings.Get("MinScore");
            Path = ConfigurationManager.AppSettings.Get("Path");

            var exitCode = HostFactory.Run(x =>
            {
                x.Service<ElasticServer>(s =>
                {
                    s.ConstructUsing(elasticSearch => new ElasticServer(ConnectionElastic, Emails, MinScore, Path));
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

