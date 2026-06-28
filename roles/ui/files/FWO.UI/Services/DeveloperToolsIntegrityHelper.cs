using System.Text.Json;
using FWO.Data;

namespace FWO.Ui.Services
{
    public static class DeveloperToolsIntegrityHelper
    {
        private static readonly JsonSerializerOptions kJsonOptions = new()
        {
            WriteIndented = true
        };

        public static List<DataIntegrityIssue> BuildRulebaseLinkIssues(IEnumerable<RulebaseLinkIntegrityRow> rows)
        {
            return [.. rows.Select(row => new DataIntegrityIssue
            {
                TableName = IntegrityTableName.RulebaseLink,
                RowId = row.Id,
                InconsistencyType = IntegrityInconsistencyType.RemoveInconsistency,
                JsonObject = JsonSerializer.Serialize(row, kJsonOptions)
            })];
        }

        public static List<RulebaseLinkResolveUpdate> BuildRulebaseLinkResolveUpdates(IEnumerable<RulebaseLinkIntegrityRow> rows)
        {
            List<RulebaseLinkResolveUpdate> updates = [];

            foreach (RulebaseLinkIntegrityRow row in rows)
            {
                long? removed = GetResolvedRemoved(row);
                if (removed.HasValue)
                {
                    updates.Add(new RulebaseLinkResolveUpdate
                    {
                        Id = row.Id,
                        Removed = removed.Value
                    });
                }
            }

            return updates;
        }

        public static long? GetResolvedRemoved(RulebaseLinkIntegrityRow row)
        {
            List<long> removedCandidates =
            [
                .. new long?[]
                {
                    row.Rule?.Removed,
                    row.Rulebase?.Removed,
                    row.RulebaseByFromRulebaseId?.Removed
                }.Where(value => value.HasValue).Select(value => value!.Value)
            ];

            return removedCandidates.Count == 0 ? null : removedCandidates.Min();
        }

        public static bool TryRemoveResolvedIssues(List<DataIntegrityIssue> issues, IReadOnlyCollection<DataIntegrityIssue> selectedIssues, int affectedRows)
        {
            if (affectedRows != selectedIssues.Count)
            {
                return false;
            }

            HashSet<string> selectedKeys = [.. selectedIssues.Select(GetIssueKey)];
            issues.RemoveAll(issue => selectedKeys.Contains(GetIssueKey(issue)));
            return true;
        }

        private static string GetIssueKey(DataIntegrityIssue issue)
        {
            return $"{issue.TableName}:{issue.InconsistencyType}:{issue.RowId}";
        }
    }
}
