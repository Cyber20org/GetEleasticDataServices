using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using System.Collections.Specialized;
using Models.Server;
using Topshelf;

namespace GetEleasticDataServices
{
    internal class Program
    {
        static void Main(string[] args)
        {

            string ConnectionElastic;
            ConnectionElastic = ConfigurationManager.AppSettings.Get("ConnectionElastic");
            var exitCode = HostFactory.Run(x =>
            {
                x.Service<ElasticServer>(s =>
                {
                    s.ConstructUsing(elasticSearch => new ElasticServer(ConnectionElastic));
                    s.WhenStarted(elasticSearch => elasticSearch.Start());
                    s.WhenStopped(elasticSearch => elasticSearch.Stop());
                });

                x.RunAsLocalService();
                x.SetServiceName("ElasticSearchExportDataService");
                x.SetDisplayName("Elastic Search Export Data Service");
                x.SetDescription("export each min data from elastic and save it as a file for the last 15 min");
            });

            int exitCodeValue = (int)Convert.ChangeType(exitCode,exitCode.GetTypeCode());
            Environment.ExitCode = exitCodeValue;
        }
    }
}

