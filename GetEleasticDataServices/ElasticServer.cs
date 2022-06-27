using Bubbles.Models.ViewModel;
using GetEleasticDataServices.Models;
using Nest;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Text;
using System.Timers;
using System.Web.Script.Serialization;

namespace Models.Server
{
    public class ElasticServer
    {
        private readonly Timer _timer, _alertTime;

        private readonly string _ConnectionElastic, _AbuseDbIpKey,
            _ServerName, _logsFilePath, _LastMailsPath,
            _RecentLogsFolderPath, _EmailsFolderPath;

        private readonly string[] _Emails;
        private readonly int _MinScore, _MailInterval, _Interval;
        private readonly List<string> listOfIps = new List<string>();
        private List<ElasticDocIndex> listOfElasticDoc = new List<ElasticDocIndex>();
        private readonly List<SaveToFile> listOfMails = new List<SaveToFile>();
        private readonly List<SaveToFile> filesToExcel = new List<SaveToFile>();
        private IEnumerable<AbuseIpDb> _AbuseIpdbSet;

        public ElasticServer(string ConnectionElastic, string Emails, string MinScore, string Path, string AbuseDbIpKey, string interval, string ServerName)
        {
            _ConnectionElastic = ConnectionElastic;
            _ServerName = ServerName;
            _AbuseDbIpKey = AbuseDbIpKey;

            _Emails = Emails.Split(',');
            _RecentLogsFolderPath = Path + $"\\RecentLogs";
            _LastMailsPath = _RecentLogsFolderPath + "\\LastMails";
            _EmailsFolderPath = _RecentLogsFolderPath + "\\Emails";
            _logsFilePath = _RecentLogsFolderPath + "\\Logs";

            try
            {
                _MinScore = Int32.Parse(MinScore);
                _Interval = 10000;
                _MailInterval = Int32.Parse(interval) * 60000;
            }
            catch
            {
                _MinScore = 5;
                _Interval = 60000;
                _MailInterval = _Interval * 5;
            }

            _timer = new Timer(_Interval) { AutoReset = true };
            _timer.Elapsed += TimerElapsed;

            _alertTime = new Timer(_MailInterval) { AutoReset = true };
            _alertTime.Elapsed += AlertElapsed;
        }

        private void TimerElapsed(object sender, ElapsedEventArgs e)
        {
            var TimerElapsedstopwatch = new Stopwatch();
            TimerElapsedstopwatch.Start();
            _timer.Enabled = false;
            try
            {
                GetAllAbuseIps();
                CreateAllFolders();
                CreateElasticDocIndexList();
                SaveElasticDocIndexListToJson();
                DistinctAllRuntimeIps();
                CheckAllRuntimeIps();
                SaveMailsTemplesFiles();

                GC.Collect(2, GCCollectionMode.Optimized);
                TimerElapsedstopwatch.Stop();
                long elapsed_time = TimerElapsedstopwatch.ElapsedMilliseconds;
                if (elapsed_time > 0 && elapsed_time < 60000)
                {
                    System.Threading.Thread.Sleep((int)(60000 - elapsed_time));
                }
                _timer.Enabled = true;
            }
            catch (Exception ex)
            {
                _timer.Enabled = true;
                ExceptionToLog(ex);
                GC.Collect();
            }
        }

        private void AlertElapsed(object sender, ElapsedEventArgs e)
        {
            int fCount = Directory.GetFiles(_EmailsFolderPath, "*", SearchOption.TopDirectoryOnly).Length;

            try
            {
                DirectoryInfo info = new DirectoryInfo(_EmailsFolderPath);
                FileInfo[] files = info.GetFiles().OrderBy(p => p.CreationTime).ToArray();
                for (int i = 0; i < fCount; i++)
                {
                    if (File.Exists($"{files[i].FullName}"))
                    {
                        string fileData = File.ReadAllText(files[i].FullName);
                        var result = JsonConvert.DeserializeObject<List<SaveToFile>>(fileData);
                        filesToExcel.AddRange(result);
                        File.Delete($"{files[i].FullName}");
                    }
                }
                if (!File.Exists(_LastMailsPath + "\\SendMail.txt"))
                {
                    // Create a file to write to.
                    using (StreamWriter sw = File.CreateText(_LastMailsPath + "\\SendMail.txt"))
                    {
                        sw.WriteLine("Create File...");
                    }
                }
            }
            catch (Exception ex)
            {
                ExceptionToLog(ex);
            }
            try
            {
                List<SaveToFile> noDupes = filesToExcel.Distinct().OrderBy(x => -x.abuseIp.abuseConfidenceScore).ToList();
                for (int i = 0; i < _Emails.Length; i++)
                {
                    Console.WriteLine($"Send mails to {_Emails[i]}");

                    List<SaveToFile> specficMail = (from x in noDupes
                                                    where _Emails[i].Contains(x.email)
                                                    select x).ToList();
                    sendMailsFunction(specficMail);
                }
            }
            catch (Exception ex)
            {
                GC.Collect();
                ExceptionToLog(ex);
            }
        }

        public void Start()
        {
            _timer.Start();
            _alertTime.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            _alertTime.Stop();
        }

        /// <summary>
        /// Get all AbuseIp from Bubbles DB
        /// </summary>
        /// <returns></returns>
        public bool GetAllAbuseIps()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                _AbuseIpdbSet = CreateListFromTable<AbuseIpDb>(DatabaseConnection("SELECT " +
                                                               $"ipAddress ,id ,abuseConfidenceScore ,totalReports" +
                                                               $" FROM [Bubbles].[dbo].[AbuseIpsDb]"));

                stopwatch.Stop();
                long elapsed_time = stopwatch.ElapsedMilliseconds;
                Console.WriteLine($"getAlAbuseIps : {elapsed_time}");
                return true;
            }
            catch (Exception ex)
            {
                ExceptionToLog(ex);
                return false;
            }
        }

        private List<ElasticDocIndex> GetAllDocumentsInIndex(ElasticClient client, ClientsMonitor clientsMonitor, int monitorTime, string scrollTimeout = "2m", int scrollSize = 4000)
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
                                    .TimeZone("+03:00"))
                           //  &&
                           //  q

                           //  .Range(dg => dg
                           //  .Field(x => x.MogCounter)
                           //  .GreaterThan(1))

                           //)
                           ).Scroll(scrollTimeout));

            List<ElasticDocIndex> results = new List<ElasticDocIndex>();

            if (!initialResponse.IsValid || string.IsNullOrEmpty(initialResponse.ScrollId))
                throw new Exception(initialResponse.ServerError.Error.Reason);

            if (initialResponse.Documents.Any())
            {
                results.AddRange(initialResponse.Documents);
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
                ExceptionToLog(ex);
            }
            client.ClearScroll(new ClearScrollRequest(scrollid));

            return results.Where(x => x.MogCounter <= 1 || x.MogCounter == null)
                    .Where(ax => ax.DestinationIp != null && ax.SourceIp != null)
                    .Where(s =>
                          (s.SourceIp.Contains("10.")
                        || s.SourceIp.Contains("192.168")
                        || s.SourceIp.Contains("172.16")
                        || s.DestinationIp.Contains("10.")
                        || s.DestinationIp.Contains("192.168")
                        || s.DestinationIp.Contains("172.16")))
                    .Where(sx => !sx.DestinationIp.Contains("Error")
                                || !sx.SourceIp.Contains("Error"))
                    //.GroupBy(x=> new {x.SourceIp,x.DestinationIp,x.DestinationPort,x.SourcePort, x.ProcessName})
                    //.Select(x=> x.First())
                    .ToList()
                    .Distinct()
                    .ToList();
        }

        private DataTable DatabaseConnection(string query, bool save = false)
        {
            try
            {
                string connectionString = $"data source=" + _ServerName + ";initial catalog=Bubbles;User Id=sa;Password=Cyber@123;MultipleActiveResultSets=True;";
                using (var conn = new SqlConnection(connectionString))
                using (var cmd = new SqlCommand(query, conn))
                {
                    conn.Open();

                    if (!save)
                    {
                        using (SqlDataReader dr = cmd.ExecuteReader())
                        {
                            var tb = new DataTable();
                            tb.Load(dr);
                            return tb;
                        }
                    }
                    cmd.ExecuteNonQuery();
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                using (StreamWriter sw = File.AppendText(_logsFilePath + "\\LogsFile.txt"))
                {
                    sw.WriteLine(ex.Message + " - " + ex.StackTrace + " - " + ex.Source + " - " + ex.GetType());
                    sw.WriteLine(DateTime.Now.ToString());
                    sw.WriteLine(query);
                    sw.WriteLine("\n\n\n");
                }
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

        public bool sendMailsFunction(List<SaveToFile> spesficMail)
        {
            List<SaveToFile> getOffDuplicate = new List<SaveToFile>();
            foreach (SaveToFile saveToFile in spesficMail)
            {
                bool condition_1 = getOffDuplicate.Any(x => x.abuseIp.id == saveToFile.abuseIp.id);
                bool condition_2 = getOffDuplicate.Any(x => x.docIndex.SourceIp == saveToFile.docIndex.SourceIp);
                if (!condition_1 && !condition_2)
                {
                    getOffDuplicate.Add(saveToFile);
                }
            }
            if (getOffDuplicate.Count > 0)
            {
                string textBody = " <table border=" + 1 + " cellpadding=" + 0
                + " cellspacing=" + 0 + " width = " + 800 + ">" +
                "<tr bgcolor='#4da6ff'>" +
                "<td><b>Alert Status</b></td> " +
                "<td><b>Computer</b> </td>" +
                "<td><b>Source</b> </td>" +
                "<td><b>Destination</b> </td>" +
                "<td><b>Process</b> </td>" +
                "<td><b>Groups</b> </td>" +
                "<td><b>More Details</b> </td>" +
                "<td><b>Time</b> </td>" +
                "</tr>";

                foreach (var item in getOffDuplicate)
                {
                    string status;

                    if (item.abuseIp.abuseConfidenceScore < 25)
                    {
                        status = "Yellow";
                    }
                    else if (item.abuseIp.abuseConfidenceScore < 50)
                    {
                        status = "Orange";
                    }
                    else
                    {
                        status = "Red";
                    }
                    textBody += $"<tr style='background-color:{status};'>" +
                        "<td>" + status + "</td>" +
                        "<td> " + item.docIndex.ReportingComputer + "</td> " +
                        "<td> " + item.docIndex.SourceIp + "/" + item.docIndex.SourcePort + "</td> " +
                        "<td> " + item.docIndex.DestinationIp + "/" + item.docIndex.DestinationPort + "</td> " +
                        "<td> " + item.docIndex.ProcessName + "</td> " +
                        "<td> " + item.docIndex.ClientGroup + "</td> " +
                        "<td> " + "https://www.abuseipdb.com/check/" + item.abuseIp.ipAddress + "</td> " +
                        "<td> " + item.docIndex.TimesTamp + "</td> " +
                        "</tr>";
                    //Msg.From = new MailAddress("cyber@cyber20.com");
                }

                using (System.Net.Mail.MailMessage Msg = new System.Net.Mail.MailMessage())
                {
                    Console.WriteLine(getOffDuplicate[0].email);
                    Msg.To.Add(new MailAddress(getOffDuplicate[0].email, "Report", Encoding.UTF8));
                    Msg.Subject = $"Cyber 2.0 Abuse ips report" + DateTime.UtcNow.ToString();
                    Msg.IsBodyHtml = true;

                    Msg.Body = textBody;
                    Msg.From = new MailAddress("Cyber@cyber20.com");

                    SmtpClient smtp = new SmtpClient
                    {
                        Host = "smtp.gmail.com",
                        EnableSsl = true,
                        DeliveryMethod = SmtpDeliveryMethod.Network,
                        DeliveryFormat = SmtpDeliveryFormat.International,
                        UseDefaultCredentials = false,
                        Port = 587,
                        Timeout = 10000,
                        Credentials = new NetworkCredential("cyber@cyber20.com", "Cyber@909"),
                    };
                    try
                    {
                        smtp.Send(Msg);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message + " - " + ex.StackTrace + " - " + ex.Source + " - " + ex.GetType());
                        using (StreamWriter sw = File.AppendText(_logsFilePath + "\\LogsFile.txt"))
                        {
                            sw.WriteLine(ex.Message + " - " + ex.StackTrace + " - " + ex.Source + " - " + ex.GetType());
                            sw.WriteLine(DateTime.Now.ToString());
                            sw.WriteLine("\n\n\n");
                        }
                        return false;
                    }
                }
            }
            Console.WriteLine("empty");
            return false;
        }

        public bool AlertToUser(AbuseIpDb abuseIp, ElasticDocIndex docIndex, string email)
        {
            string status;

            if (abuseIp.abuseConfidenceScore < 25)
            {
                status = "Yellow";
            }
            else if (abuseIp.abuseConfidenceScore < 50)
            {
                status = "Orange";
            }
            else
            {
                status = "Red";
            }

            string body = $"Alert Status :  {status} <br />" +
                    $"Alert from Computer : {docIndex.ReportingComputer} <br />" +
                    $" process : {docIndex.ProcessName} <br />" +
                    $" AbuseIpDb report that {abuseIp.ipAddress} contained in AbuseIPDB" +
                    $" and have abuse confidence score : {abuseIp.abuseConfidenceScore}  <br />" +
                    $" from {abuseIp.totalReports} reporters <br />" +
                    $" {docIndex.SourceIp}/{docIndex.SourcePort} to {docIndex.DestinationIp}/{docIndex.DestinationPort} <br />" +
                    $"Groups {docIndex.ClientGroup} <br />" +
                    $"https://www.abuseipdb.com/check/{abuseIp.ipAddress}";

            using (System.Net.Mail.MailMessage Msg = new System.Net.Mail.MailMessage())
            {
                if (email.Contains("@"))
                {
                    Msg.To.Add(new MailAddress(email, "Report", Encoding.UTF8));
                }

                Msg.Subject = $"Cyber 2.0 {status} Alert info - " + docIndex.TimesTamp;
                //Msg.From = new MailAddress("cyber@cyber20.com");
                Msg.IsBodyHtml = true;
                Msg.Body = body;
                Msg.From = new MailAddress("Cyber@cyber20.com");
                SmtpClient smtp = new SmtpClient
                {
                    Host = "smtp.gmail.com",
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    DeliveryFormat = SmtpDeliveryFormat.International,
                    UseDefaultCredentials = false,
                    Port = 587,
                    Timeout = 10000,
                    Credentials = new NetworkCredential("cyber@cyber20.com", "Cyber@909"),
                };
                try
                {
                    smtp.Send(Msg);
                    return true;
                }
                catch (Exception ex)
                {
                    using (StreamWriter sw = File.AppendText(_logsFilePath + "\\LogsFile.txt"))
                    {
                        sw.WriteLine(ex.Message + " - " + ex.StackTrace + " - " + ex.Source + " - " + ex.GetType());
                        sw.WriteLine(DateTime.Now.ToString());
                        sw.WriteLine("\n\n\n");
                    }
                    return false;
                }
            }
        }

        public AbuseIpDb CheckIfInDb(string ipAddress)
        {
            if (!_AbuseIpdbSet.Any()) return null;
            var item = _AbuseIpdbSet.FirstOrDefault(item => item.ipAddress == ipAddress);
            if (item != null)
            {
                return item;
            }
            return null;
        }

        /// <summary>
        /// get an ip address and get info from ABUSEDBIP.COM about that traffic
        /// </summary>
        /// <param name="ipAddress">check (string)ipAddress in the API</param>
        /// <returns></returns>
        public AbuseIpDb CheckInAPI(string ipAddress)
        {
            string key = _AbuseDbIpKey;
            var client = new RestClient("https://api.abuseipdb.com/api/v2/check");
            var request = new RestRequest(Method.GET);
            request.AddHeader("Key", key);
            request.AddHeader("Accept", "application/json");
            request.AddParameter("ipAddress", ipAddress);
            request.AddParameter("maxAgeInDays", "90");
            request.AddParameter("verbose", "");

            IRestResponse response = client.Execute(request);

            var parsedJson = JsonConvert.DeserializeObject<AbuseDbIpModelView.Root>(response.Content, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            if (parsedJson.data != null)
            {
                try
                {
                    AbuseIpDb abuseIp = new AbuseIpDb
                    {
                        id = Guid.NewGuid(),
                        ipAddress = parsedJson.data.ipAddress,
                        isPublic = parsedJson.data.isPublic,
                        ipVersion = parsedJson.data.ipVersion,
                        isWhitelisted = parsedJson.data.isWhitelisted,
                        abuseConfidenceScore = parsedJson.data.abuseConfidenceScore,
                        usageType = parsedJson.data.usageType,
                        isp = parsedJson.data.isp,
                        domain = parsedJson.data.domain,
                        totalReports = parsedJson.data.totalReports,
                        numDistinctUsers = parsedJson.data.numDistinctUsers,
                    };

                    int pub = (bool)abuseIp.isPublic ? 1 : 0;
                    int white = abuseIp.isWhitelisted ? 1 : 0;
                    string query = "INSERT INTO dbo.AbuseIpsDb (id, ipAddress, isPublic, ipVersion, isWhitelisted, abuseConfidenceScore," +
                        "usageType,isp,domain,totalReports,numDistinctUsers)"
                        + $" VALUES('{abuseIp.id}', '{abuseIp.ipAddress}', {pub}, {abuseIp.ipVersion},{white},{abuseIp.abuseConfidenceScore},'{abuseIp.usageType}'," +
                        $"'{abuseIp.isp}','{abuseIp.domain}',{abuseIp.totalReports},{abuseIp.numDistinctUsers})";

                    DatabaseConnection(query, true);

                    return abuseIp;
                }
                catch (Exception ex)
                {
                    ExceptionToLog(ex);
                }
            }
            return null;
        }

        /// <summary>
        /// get ExceptionToLog and append it to the log file
        /// </summary>
        /// <param name="ex"></param>
        public void ExceptionToLog(Exception ex)
        {
            Console.WriteLine(ex.Message);
            using (StreamWriter sw = File.AppendText(_logsFilePath + "\\LogsFile.txt"))
            {
                sw.WriteLine(ex.Message + " - " + ex.StackTrace + " - " + ex.Source + " - " + ex.GetType());
                sw.WriteLine(DateTime.Now.ToString());
                sw.WriteLine("\n\n\n");
            }
        }

        /// <summary>
        /// initial creation for all service folders
        /// </summary>
        /// <returns></returns>
        public bool CreateAllFolders()
        {
            try
            {
                if (!Directory.Exists(_RecentLogsFolderPath))
                {
                    DirectoryInfo di = Directory.CreateDirectory(_RecentLogsFolderPath);
                    DirectoryInfo LogsFile = Directory.CreateDirectory(_logsFilePath);
                    DirectoryInfo LastMails = Directory.CreateDirectory(_LastMailsPath);
                    DirectoryInfo EmailsDi = Directory.CreateDirectory(_EmailsFolderPath);
                }

                // This text is added only once to the file.
                if (!File.Exists(_logsFilePath + "\\LogsFile.txt"))
                {
                    // Create a file to write to.
                    using (StreamWriter sw = File.CreateText(_logsFilePath + "\\LogsFile.txt"))
                    {
                        sw.WriteLine("Create File...");
                    }
                }

                // This text is added only once to the file.
                if (!File.Exists(_LastMailsPath + "\\SendMail.txt"))
                {
                    // Create a file to write to.
                    using (StreamWriter sw = File.CreateText(_LastMailsPath + "\\SendMail.txt"))
                    {
                        sw.WriteLine("Create File...");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                ExceptionToLog(ex);
                return false;
            }
        }

        /// <summary>
        /// get FolderPath info and set numberOfFile that the folder can keep
        /// keep most recent file
        /// </summary>
        /// <param name="FolderPath">The folder Path </param>
        /// <param name="numberOfFile">The maximum files that folder contains</param>
        /// <returns></returns>
        public bool DeleteFilesFromFolder(string FolderPath, int numberOfFile)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                int fCount = Directory.GetFiles(FolderPath, "*", SearchOption.TopDirectoryOnly).Length;

                if (fCount >= numberOfFile)
                {
                    DirectoryInfo info = new DirectoryInfo(FolderPath);
                    FileInfo[] files = info.GetFiles().OrderBy(p => p.CreationTime).ToArray();
                    if (File.Exists($"{files[0].FullName}"))
                    {
                        File.Delete($"{files[0].FullName}");
                    }
                }

                stopwatch.Stop();
                long elapsed_time = stopwatch.ElapsedMilliseconds;
                Console.WriteLine($"getAlAbuseIps : {elapsed_time}");
                return true;
            }
            catch (Exception ex)
            {
                ExceptionToLog(ex);
                return false;
            }
        }

        /// <summary>
        /// get list of ElasticDocIndexs Obj save at _RecentFolder path Json File
        /// </summary>
        public void SaveElasticDocIndexListToJson()
        {
            DeleteFilesFromFolder(_RecentLogsFolderPath, 10);

            string fileName = DateTime.Now.ToString()
                                            .Replace(" ", "-")
                                            .Replace(":", "-")
                                            .Replace("/", "-");

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            string fileFullPath = _RecentLogsFolderPath + "\\" + fileName + ".Json";
            try
            {
                var translatedData = from x in listOfElasticDoc

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
                                         x.SourcePort,
                                         x.TimesTamp
                                     };
                var json = new JavaScriptSerializer();
                json.MaxJsonLength = int.MaxValue;
                var ss = json.Serialize(translatedData);

                File.AppendAllText(@fileFullPath, ss);

                stopwatch.Stop();
                long elapsed_time = stopwatch.ElapsedMilliseconds;
                Console.WriteLine($"SaveElasticSearchData : {elapsed_time}");
            }
            catch (Exception ex)
            {
                ExceptionToLog(ex);
            }
        }

        /// <summary>
        /// get the last min traffic from the elastic search and convert it
        /// to an readable object
        /// </summary>
        public void CreateElasticDocIndexList()
        {
            List<ElasticDocIndex> connectionList = new List<ElasticDocIndex>();
            try
            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();

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
                    string ElasticDocIndexKey = "";
                    if (doc != null)
                    {
                        foreach (var item in doc)
                        {
                            string itemKey = item.ReportingComputer + item.ProcessName + item.SourceIp
                                + item.DestinationIp + item.SourcePort + item.DestinationPort;
                            if (ElasticDocIndexKey.Contains(itemKey)) continue;
                            ElasticDocIndex _ealsticView = new ElasticDocIndex
                            {
                                UserName = clients.ClientName,
                                ClientGroup = clients.ClientGroup.Replace(" ", "-"),
                                IsBroadcast = item.MogCounter >= 1,
                                Direction = item.Direction,
                                SourceIp = item.SourceIp,
                                ReportingComputer = item.ReportingComputer,
                                ProcessName = item.ProcessName,
                                Status = item.Status,
                                DestinationIp = item.DestinationIp,
                                UserPName = item.UserName,
                                DestinationPort = item.DestinationPort,
                                SourcePort = item.SourcePort,
                                TimesTamp = item.TimesTamp
                            };
                            connectionList.Add(_ealsticView);
                            ElasticDocIndexKey += itemKey;
                        }
                        ElasticDocIndexKey = null;
                    }
                }

                stopwatch.Stop();
                long elapsed_time = stopwatch.ElapsedMilliseconds;
                Console.WriteLine($"CreateElasticDocIndexList : {elapsed_time}");
                listOfElasticDoc = connectionList;
            }
            catch (Exception ex)
            {
                ExceptionToLog(ex);
            }
        }

        /// <summary>
        /// Create a list of ips without from the elastic and remove all duplicate
        /// </summary>
        public void DistinctAllRuntimeIps()
        {
            try
            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();
                var unknownIp = listOfElasticDoc.Where(x => !x.IsBroadcast);
                unknownIp = unknownIp.Distinct().ToList();
                foreach (var elasticDocIndex in unknownIp)
                {
                    //TODO ADD GROUP FILTER
                    string ip = elasticDocIndex.DestinationIp;
                    if (ip.StartsWith("10.") || ip.StartsWith("172.16") || ip.StartsWith("192.168"))
                    {
                        ip = elasticDocIndex.SourceIp;
                    }
                    if (!elasticDocIndex.IsBroadcast)
                    {
                        if (ip.StartsWith("10.") || ip.StartsWith("172.16") || ip.StartsWith("192.168")) continue;
                        if (InRunTimeCache(ip, elasticDocIndex)) continue;
                    }
                }
                stopwatch.Stop();
                long elapsed_time = stopwatch.ElapsedMilliseconds;
                Console.WriteLine($"DistinctAllRuntimeIps : {elapsed_time}");
            }
            catch (Exception ex)
            {
                ExceptionToLog(ex);
            }
        }

        /// <summary>
        /// get an ip and ElasticDocIndex intance and check if its already exists.
        /// call from CreateElasticDocIndexList()
        /// </summary>
        /// <param name="ipAddress">(string)ipAddress</param>
        /// <param name="docIndex">(ElasticDocIndex)docIndex</param>
        /// <returns>bool if alredy appears return true</returns>
        public bool InRunTimeCache(string ipAddress, ElasticDocIndex docIndex)
        {
            if (listOfIps.Any())
            {
                if (listOfIps.Contains(ipAddress))
                {
                    return true;
                }
            }
            listOfIps.Add(ipAddress);
            listOfElasticDoc.Add(docIndex);
            return false;
        }

        public void CheckAllRuntimeIps()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            try
            {
                if (File.Exists(_LastMailsPath + "\\SendMail.txt"))
                {
                    string s = "";
                    using (StreamReader sr = File.OpenText(_LastMailsPath + "\\SendMail.txt"))
                    {
                        s = sr.ReadToEnd();
                    }
                    for (int i = 0; i < listOfIps.Count(); i++)
                    {
                        AbuseIpDb item = CheckIfInDb(listOfIps[i]) ?? CheckInAPI(listOfIps[i]);
                        if (item == null) continue;
                        for (int j = 0; j < _Emails.Length; j++)
                        {
                            if (_Emails[j].ToLower().Split('=')[1].Replace(" ", "-")
                                .Contains(listOfElasticDoc[i].ClientGroup.ToLower())
                                || _Emails[j].Split('=')[1] == "all") continue;

                            if (item.abuseConfidenceScore >= _MinScore)
                            {
                                SaveToFile dataToFile = new SaveToFile(item, listOfElasticDoc[i], _Emails[j].Split('=')[0]);
                                listOfMails.Add(dataToFile);
                                if (item.abuseConfidenceScore >= 60)
                                {
                                    string SaveToFileKey = $"{listOfElasticDoc[i].TimesTamp}=={item.ipAddress}=={listOfElasticDoc[i].ReportingComputer}=={_Emails[j].Split('=')[0]}\n";
                                    try
                                    {
                                        if (!s.Contains(SaveToFileKey))
                                        {
                                            AlertToUser(item, listOfElasticDoc[i], _Emails[j].Split('=')[0]);
                                            using (StreamWriter sw = File.AppendText(_LastMailsPath + "\\SendMail.txt"))
                                            {
                                                sw.WriteLine(SaveToFileKey);
                                                s += SaveToFileKey;
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        ExceptionToLog(ex);
                                    }
                                }
                            }
                        }
                    }
                }

                stopwatch.Stop();
                long elapsed_time = stopwatch.ElapsedMilliseconds;
                Console.WriteLine($"CheckAllRuntimeIps : {elapsed_time}");
            }
            catch (Exception ex)
            {
                ExceptionToLog(ex);
            }
        }

        public void SaveMailsTemplesFiles()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            try
            {
                var translatedData = from x in listOfMails
                                     select new
                                     {
                                         x.abuseIp,
                                         x.docIndex,
                                         x.email
                                     };

                string Email = DateTime.Now.ToString()
                                                .Replace(" ", "-")
                                                .Replace(":", "-")
                                                .Replace("/", "-");
                string fileFullPath = $"{_EmailsFolderPath}\\{Email}-Emails.Json";

                using (StreamWriter sw = new StreamWriter(fileFullPath))
                using (JsonWriter writer = new JsonTextWriter(sw))
                {
                    new JsonSerializer().Serialize(writer, translatedData);
                }
                stopwatch.Stop();
                long elapsed_time = stopwatch.ElapsedMilliseconds;
                Console.WriteLine($"SaveMailsTemplesFiles : {elapsed_time}");
            }
            catch (Exception ex)
            {
                ExceptionToLog(ex);
            }
        }
    }
}