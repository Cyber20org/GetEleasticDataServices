using Nest;
using System;
using System.ComponentModel;

namespace Models
{
    public class ElasticDocIndex
    {
        [Text(Name = "@timestamp")]
        public DateTime TimesTamp { get; set; }

        //[Text(Name = "cast_type")]
        //public string CastType { get; set; }

        [Text(Name = "destination_ip")]
        public string DestinationIp { get; set; }

        [Text(Name = "client_group")]
        public string ClientGroup { get; set; }

        [Number(DocValues = false, IgnoreMalformed = true, Coerce = true, Name = "destination_port")]
        public int? DestinationPort { get; set; }

        [Text(Name = "direction")]
        public string Direction { get; set; }

        //[Text(Name = "direction_low")]
        //public byte DirectionFlow { get; set; }

        //[Text(Name = "flow_handle")]
        //public string FlowHandle { get; set; }

        //[Text(Name = "flow_state")]
        //public string FlowState { get; set; }

        //[Text(Name = "log_type")]
        //public string LogType { get; set; }

        [Number(DocValues = false, IgnoreMalformed = true, Coerce = true, Name = "mog_counter")]
        [DisplayName("mog_counter")]
        public long? MogCounter { get; set; }

        [DisplayName("is_broadcast")]
        public bool IsBroadcast { get; set; }

        //[Text(Name = "os")]
        //public string Os { get; set; }

        [Number(DocValues = false, IgnoreMalformed = true, Coerce = true, Name = "port")]
        public int? Port { get; set; }

        [Text(Name = "process_name")]
        public string ProcessName { get; set; }

        //[Number(DocValues = false, IgnoreMalformed = true, Coerce = true, Name = "process_id")]
        //public int? ProcessID { get; set; }

        //[Text(Name = "process_path")]
        //public string ProcessPath { get; set; }

        //[Text(Name = "protocol")]
        //public string Protocol { get; set; }

        //[Text(Name = "reason")]
        //public string Reason { get; set; }

        [Text(Name = "reporting_computer")]
        public string ReportingComputer { get; set; }

        //[Text(Name = "scramble_state")]
        //public string ScrambleState { get; set; }

        [Text(Name = "destination_path")]
        public string DestinationPath { get; set; }

        [Text(Name = "source_ip")]
        public string SourceIp { get; set; }

        [Number(DocValues = false, IgnoreMalformed = true, Coerce = true, Name = "source_port")]
        public int? SourcePort { get; set; }

        [Text(Name = "status")]
        public string Status { get; set; }

        [Text(Name = "user_name")]
        public string UserName { get; set; }

        ////<-----
        //[Text(Name = "client_time")]
        //public string ClientTime { get; set; }

        //[Text(Name = "@version")]
        //public string Version { get; set; }

        ////<-----
        ////[Number(IgnoreMalformed = true, Coerce = true, Name = "sub_sequance_number")]
        //[Text(Name = "sub_sequance_number")]
        //public string SubSequanceNumber { get; set; }

        ////[Text(Name = "source_port")]
        ////[Number(DocValues = false, IgnoreMalformed = true, Coerce = true, Name = "source_port")]
        ////public int SourcePort { get; set; }
        //[Text(Name = "message")]
        //public string Message { get; set; }

        //[Text(Name = "host")]
        //public string Host { get; set; }

        //[Text(Name = "full_server_time")]
        //public string FullServerTime { get; set; }

        //[Text(Name = "chain_array")]
        //public string ChainArray { get; set; }
        //[Text(Name = "User_PName")]
        public string UserPName { get; set; }

        //public int ServerID { get; set; }

        //public bool IsActive { get; set; }
    }
}