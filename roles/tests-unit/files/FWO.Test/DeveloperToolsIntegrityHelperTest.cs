using FWO.Data;
using FWO.Ui.Services;
using NUnit.Framework;

namespace FWO.Test
{
    [TestFixture]
    [Parallelizable]
    internal class DeveloperToolsIntegrityHelperTest
    {
        [Test]
        public void BuildRulebaseLinkIssues_MapsGenericIssueAndJsonPayload()
        {
            RulebaseLinkIntegrityRow row = new()
            {
                Id = 42,
                GatewayId = 11,
                FromRuleId = 9,
                FromRulebaseId = 7,
                ToRulebaseId = 8,
                Rule = new IntegrityRemovedReference { Removed = 300 },
                Rulebase = new IntegrityRemovedReference { Removed = 200 }
            };

            List<DataIntegrityIssue> issues = DeveloperToolsIntegrityHelper.BuildRulebaseLinkIssues([row]);

            Assert.That(issues.Count, Is.EqualTo(1));
            Assert.That(issues[0].TableName, Is.EqualTo(IntegrityTableName.RulebaseLink));
            Assert.That(issues[0].RowId, Is.EqualTo(42));
            Assert.That(issues[0].InconsistencyType, Is.EqualTo(IntegrityInconsistencyType.RemoveInconsistency));
            Assert.That(issues[0].JsonObject, Does.Contain("\"gw_id\": 11"));
            Assert.That(issues[0].JsonObject, Does.Contain("\"removed\": 300"));
        }

        [Test]
        public void GetResolvedRemoved_ReturnsMinimumNonNullRelatedRemovedValue()
        {
            RulebaseLinkIntegrityRow row = new()
            {
                Rule = new IntegrityRemovedReference { Removed = 300 },
                Rulebase = new IntegrityRemovedReference { Removed = 200 },
                RulebaseByFromRulebaseId = new IntegrityRemovedReference { Removed = 250 }
            };

            long? removed = DeveloperToolsIntegrityHelper.GetResolvedRemoved(row);

            Assert.That(removed, Is.EqualTo(200));
        }

        [Test]
        public void GetResolvedRemoved_ReturnsNull_WhenNoRelatedRemovedValueExists()
        {
            RulebaseLinkIntegrityRow row = new();

            long? removed = DeveloperToolsIntegrityHelper.GetResolvedRemoved(row);

            Assert.That(removed, Is.Null);
        }

        [Test]
        public void BuildRulebaseLinkResolveUpdates_SkipsRowsWithoutResolvableRemovedValue()
        {
            RulebaseLinkIntegrityRow resolvable = new()
            {
                Id = 1,
                Rule = new IntegrityRemovedReference { Removed = 100 }
            };
            RulebaseLinkIntegrityRow unresolved = new()
            {
                Id = 2
            };

            List<RulebaseLinkResolveUpdate> updates =
                DeveloperToolsIntegrityHelper.BuildRulebaseLinkResolveUpdates([resolvable, unresolved]);

            Assert.That(updates.Count, Is.EqualTo(1));
            Assert.That(updates[0].Id, Is.EqualTo(1));
            Assert.That(updates[0].Removed, Is.EqualTo(100));
        }

        [Test]
        public void TryRemoveResolvedIssues_RemovesSelectedIssues_OnFullSuccess()
        {
            List<DataIntegrityIssue> issues =
            [
                new()
                {
                    TableName = IntegrityTableName.RulebaseLink,
                    RowId = 1,
                    InconsistencyType = IntegrityInconsistencyType.RemoveInconsistency
                },
                new()
                {
                    TableName = IntegrityTableName.RulebaseLink,
                    RowId = 2,
                    InconsistencyType = IntegrityInconsistencyType.RemoveInconsistency
                }
            ];

            bool success = DeveloperToolsIntegrityHelper.TryRemoveResolvedIssues(issues, [issues[0]], 1);

            Assert.That(success, Is.True);
            Assert.That(issues.Select(issue => issue.RowId), Is.EquivalentTo([2L]));
        }

        [Test]
        public void TryRemoveResolvedIssues_LeavesIssuesUntouched_OnPartialSuccess()
        {
            List<DataIntegrityIssue> issues =
            [
                new()
                {
                    TableName = IntegrityTableName.RulebaseLink,
                    RowId = 1,
                    InconsistencyType = IntegrityInconsistencyType.RemoveInconsistency
                },
                new()
                {
                    TableName = IntegrityTableName.RulebaseLink,
                    RowId = 2,
                    InconsistencyType = IntegrityInconsistencyType.RemoveInconsistency
                }
            ];

            bool success = DeveloperToolsIntegrityHelper.TryRemoveResolvedIssues(issues, [issues[0], issues[1]], 1);

            Assert.That(success, Is.False);
            Assert.That(issues.Select(issue => issue.RowId), Is.EquivalentTo([1L, 2L]));
        }
    }
}
