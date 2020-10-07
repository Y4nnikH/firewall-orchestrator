﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FWO.Ui.Data.Api
{
    public class Rule
    {
        [JsonPropertyName("rule_id")]
        public int Id { get; set; }

        [JsonPropertyName("rule_num")]
        public int Number { get; set; }  

        [JsonPropertyName("rule_disabled")]
        public bool Disabled { get; set; }

        [JsonPropertyName("rule_services")]
        public ServiceWrapper[] Services { get; set; }

        [JsonPropertyName("rule_svc_neg")]
        public bool ServiceNegated { get; set; }

        [JsonPropertyName("rule_svc")]
        public string Service { get; set; }

        [JsonPropertyName("rule_src_neg")]
        public bool SourceNegated { get; set; }

        [JsonPropertyName("rule_src")]
        public string Source { get; set; }

        [JsonPropertyName("rule_froms")]
        public NetworkObjectWrapper[] Froms { get; set; }

        [JsonPropertyName("rule_dst_neg")]
        public bool DestinationNegated { get; set; }

        [JsonPropertyName("rule_dst")]
        public string Destination { get; set; }

        [JsonPropertyName("rule_tos")]
        public NetworkObjectWrapper[] Tos { get; set; }

        [JsonPropertyName("rule_action")]
        public string Action { get; set; }

        [JsonPropertyName("rule_track")]
        public string Track { get; set; }
    }
}