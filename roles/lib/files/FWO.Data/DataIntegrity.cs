using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace FWO.Data
{
    public enum IntegrityTableName
    {
        RulebaseLink
    }

    public enum IntegrityInconsistencyType
    {
        RemoveInconsistency
    }

    public sealed class DataIntegrityIssue
    {
        public IntegrityTableName TableName { get; set; }

        public long RowId { get; set; }

        public IntegrityInconsistencyType InconsistencyType { get; set; }

        public string JsonObject { get; set; } = "";
    }

    public sealed class IntegrityRemovedReference
    {
        [JsonProperty("removed"), JsonPropertyName("removed")]
        public long? Removed { get; set; }
    }

    public sealed class RulebaseLinkIntegrityRow
    {
        [JsonProperty("id"), JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonProperty("gw_id"), JsonPropertyName("gw_id")]
        public int GatewayId { get; set; }

        [JsonProperty("from_rule_id"), JsonPropertyName("from_rule_id")]
        public int? FromRuleId { get; set; }

        [JsonProperty("from_rulebase_id"), JsonPropertyName("from_rulebase_id")]
        public int? FromRulebaseId { get; set; }

        [JsonProperty("to_rulebase_id"), JsonPropertyName("to_rulebase_id")]
        public int ToRulebaseId { get; set; }

        [JsonProperty("removed"), JsonPropertyName("removed")]
        public long? Removed { get; set; }

        [JsonProperty("rule"), JsonPropertyName("rule")]
        public IntegrityRemovedReference? Rule { get; set; }

        [JsonProperty("rulebase"), JsonPropertyName("rulebase")]
        public IntegrityRemovedReference? Rulebase { get; set; }

        [JsonProperty("rulebaseByFromRulebaseId"), JsonPropertyName("rulebaseByFromRulebaseId")]
        public IntegrityRemovedReference? RulebaseByFromRulebaseId { get; set; }
    }

    public sealed class RulebaseLinkResolveUpdate
    {
        public long Id { get; set; }

        public long Removed { get; set; }
    }
}
