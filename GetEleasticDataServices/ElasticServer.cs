using Models;
using Nest;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Timers;
using System.Web.Script.Serialization;

namespace Models.Server
{
    public class ElasticServer
    {
        private readonly Timer _timer;
        private readonly string _ConnectionElastic, _Path;

        public ElasticServer(string ConnectionElastic)
        {

            _ConnectionElastic = ConnectionElastic;
            string filePath = Assembly.GetExecutingAssembly().Location;
            _Path = Path.GetDirectoryName(filePath);
            _timer = new Timer(60000) { AutoReset = true };
            _timer.Elapsed += TimerElapsed;
        }

        private void TimerElapsed(object sender, ElapsedEventArgs e)
        {

            string RecentLogsFolder = _Path + $"\\RecentLogs";
            DirectoryInfo di = Directory.CreateDirectory(RecentLogsFolder);

            var connectionList = ElasticInit().Where(s => (s.SourceIp != null && (s.SourceIp.Contains("10.")
                                                               || s.SourceIp.Contains("192.168")
                                                               || s.SourceIp.Contains("172.16")) && s.DestinationIp != null)
                                                               || (s.DestinationIp != null && (s.DestinationIp.Contains("10.")
                                                               || s.DestinationIp.Contains("192.168")
                                                               || s.DestinationIp.Contains("172.16")) && s.SourceIp != null))
                                                               .Distinct();

            int fCount = Directory.GetFiles(RecentLogsFolder, "*", SearchOption.TopDirectoryOnly).Length;
            DateTime dateTime = DateTime.Now;
            string fileName = dateTime.ToString().Replace(" ", "-").Replace(":", "-").Replace("/", "-");
            Console.WriteLine(fileName);
            if (fCount >= 15)
            {
                DirectoryInfo info = new DirectoryInfo(RecentLogsFolder);
                FileInfo[] files = info.GetFiles().OrderBy(p => p.CreationTime).ToArray();
                if (File.Exists($"{files[0].FullName}"))
                {
                    File.Delete($"{files[0].FullName}");
                }

            }
            string fileFullPath = RecentLogsFolder + "\\" + fileName + ".txt";
            var translatedData = from x in connectionList
                                 select new
                                 {
                                     x.UserName,
                                     x.ClientGroup,
                                     x.IsBroadcast,
                                     x.Direction,
                                     x.SourceIp,
                                     x.ReportingComputer,
                                     x.ProcessName,
                                     x.Status,
                                     x.DestinationIp,
                                     x.UserPName,
                                     x.DestinationPort,
                                     x.SourcePort
                                 };
            var json = new JavaScriptSerializer();
            json.MaxJsonLength = int.MaxValue;
            var ss = json.Serialize(translatedData);
            File.AppendAllText(@fileFullPath, ss);
        }

        public void Start()
        {
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        public List<ElasticDocIndex> ElasticInit()
        {
            List<ElasticDocIndex> connectionList = new List<ElasticDocIndex>();
            try
            {
                var connectionSettings = new ConnectionSettings(new Uri($"http://{_ConnectionElastic}:9200/"))
                    .DisableAutomaticProxyDetection()
                    .EnableHttpCompression()
                    .DisableDirectStreaming()
                    .PrettyJson()
                    .RequestTimeout(TimeSpan.FromMinutes(1));

                var client = new ElasticClient(connectionSettings);

                IEnumerable<ClientsMonitor> clientsMonitors = CreateListFromTable<ClientsMonitor>(DatabaseConnection($"SELECT " +
                    $"ClientName,ClientGroup,LogedInUser FROM [Cyber20DB].[dbo].[ClientsMonitor]"));

                foreach (ClientsMonitor clients in clientsMonitors.ToList())
                {
                    List<ElasticDocIndex> doc = GetAllDocumentsInIndex(client, clients, 1);
                    if (doc != null)
                    {
                        int id = 0;
                        foreach (var item in doc)
                        {
                            Console.WriteLine("GET DATA.");
                            Console.WriteLine("GET DATA..");
                            Console.WriteLine("GET DATA...");
                            ElasticDocIndex _ealsticView = new ElasticDocIndex
                            {
                                UserName = clients.ClientName,
                                ClientGroup = clients.ClientGroup.Replace(" ", "-"),
                                IsBroadcast = item.MogCounter != null,
                                Direction = item.Direction,
                                SourceIp = item.SourceIp,
                                ReportingComputer = item.ReportingComputer,
                                ProcessName = item.ProcessName,
                                Status = item.Status,
                                DestinationIp = item.DestinationIp,
                                UserPName = item.UserName,
                                DestinationPort = item.DestinationPort,
                                SourcePort = item.SourcePort
                            };
                            connectionList.Add(_ealsticView);
                        }
                    }
                }
                return connectionList;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message + " - " + ex.StackTrace + " - " + ex.Source + " - " + ex.GetType());
                return null;
            }
        }

        private static List<ElasticDocIndex> GetAllDocumentsInIndex(ElasticClient client, ClientsMonitor clientsMonitor, int monitorTime, string scrollTimeout = "2m", int scrollSize = 4000)
        {
            DateTime from = DateTime.Now.AddMinutes(-1 * monitorTime);
            DateTime doDay = DateTime.Now;

            ISearchResponse<ElasticDocIndex> initialResponse = client.Search<ElasticDocIndex>
                (scr => scr
                        .AllIndices()
                        .Index("logs*")
                        .From(0)
                        .Take(scrollSize)
                        .Sort(ss => ss.Descending("@timestamp"))
                        .Source(sr => sr
                            .Includes(fi => fi
                                 .Field(p => p.ClientGroup)
                                 .Field(p => p.DestinationIp)
                                 .Field(p => p.DestinationPort)
                                 .Field(p => p.Direction)
                                 .Field(p => p.IsBroadcast)
                                 .Field(p => p.MogCounter)
                                 .Field(p => p.Port)
                                 .Field(p => p.ProcessName)
                                 .Field(p => p.ReportingComputer)
                                 .Field(p => p.SourceIp)
                                 .Field(p => p.SourcePort)
                                 .Field(p => p.UserName)
                                 .Field(p => p.UserPName)))
                        .Query(q =>
                             q.Bool(b => b
                                .Must(m => m
                                    .MatchPhrasePrefix(a => a
                                        .Field(x => x.ReportingComputer)
                                        .Query(clientsMonitor.ClientName))))
                               &&
                            q.DateRange(c => c
                                    .Name("@timesTamp")
                                    .Field(f => f.TimesTamp)
                                    .GreaterThanOrEquals(from)
                                    .LessThanOrEquals(doDay)
                                    .TimeZone("+01:00")))
                           .Scroll(scrollTimeout));

            List<ElasticDocIndex> results = new List<ElasticDocIndex>();

            if (!initialResponse.IsValid || string.IsNullOrEmpty(initialResponse.ScrollId))
                throw new Exception(initialResponse.ServerError.Error.Reason);

            if (initialResponse.Documents.Any())
            {
                results.AddRange(initialResponse.Documents.ToList());
            }

            string scrollid = initialResponse.ScrollId;
            bool isScrollSetHasData = true;
            try
            {
                while (isScrollSetHasData)
                {
                    ISearchResponse<object> loopingResponse = client.Scroll<object>(scrollTimeout, scrollid);

                    if (loopingResponse.IsValid)
                    {
                        results.AddRange(initialResponse.Documents.ToList());
                        scrollid = loopingResponse.ScrollId;
                    }
                    isScrollSetHasData = loopingResponse.Documents.Any();
                }
            }
            catch (Exception ex)
            {
                throw;
            }
            client.ClearScroll(new ClearScrollRequest(scrollid));

            return results;
        }

        private DataTable DatabaseConnection(string query)
        {
            try
            {
                string connectionString = $"data source=" + _ConnectionElastic + ";initial catalog=Cyber20CyberAnalyzerDB;User Id=sa;Password=Cyber@123;MultipleActiveResultSets=True;";
                using (var conn = new SqlConnection(connectionString))
                using (var cmd = new SqlCommand(query, conn))
                {
                    conn.Open();
                    using (SqlDataReader dr = cmd.ExecuteReader())
                    {
                        var tb = new DataTable();
                        tb.Load(dr);
                        return tb;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        public List<T> CreateListFromTable<T>(DataTable tbl) where T : new()
        {
            // define return list
            List<T> lst = new List<T>();

            // go through each row
            foreach (DataRow r in tbl.Rows)
            {
                // add to the list
                lst.Add(CreateItemFromRow<T>(r));
            }

            // return the list
            return lst;
        }

        public T CreateItemFromRow<T>(DataRow row) where T : new()
        {
            // create a new object
            T item = new T();

            // set the item
            SetItemFromRow(item, row);

            // return
            return item;
        }

        public void SetItemFromRow<T>(T item, DataRow row) where T : new()
        {
            // go through each column
            foreach (DataColumn c in row.Table.Columns)
            {
                // find the property for the column
                PropertyInfo p = item.GetType().GetProperty(c.ColumnName);

                // if exists, set the value
                if (p != null && row[c] != DBNull.Value)
                {
                    p.SetValue(item, row[c], null);
                }
            }
        }

        public IEnumerable<ClientsMonitor> getGroups()
        {
            var connectionSettings = new ConnectionSettings(new Uri($"http://" + _ConnectionElastic + ":9200/"))
                    .DisableAutomaticProxyDetection()
                    .EnableHttpCompression()
                    .DisableDirectStreaming()
                    .PrettyJson()
                    .RequestTimeout(TimeSpan.FromMinutes(1));

            var client = new ElasticClient(connectionSettings);

            IEnumerable<ClientsMonitor> clientsMonitors = CreateListFromTable<ClientsMonitor>(DatabaseConnection("SELECT ClientName,ClientGroup,LogedInUser FROM [Cyber20DB].[dbo].[ClientsMonitor]"));

            return clientsMonitors;
        }
    }
}