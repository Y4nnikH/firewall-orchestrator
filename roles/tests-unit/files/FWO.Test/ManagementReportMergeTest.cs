using System.Collections.Generic;
using System.Linq;
using FWO.Data;
using FWO.Data.Report;
using NUnit.Framework;

namespace FWO.Test
{
    [TestFixture]
    internal class ManagementReportMergeTest
    {
        [Test]
        public void Merge_AppendsArraysAndRulesByRulebaseId()
        {
            ManagementReport target = ManagementReport(
                objectIds: [1],
                serviceIds: [10],
                userIds: [20],
                rulebases:
                [
                    Rulebase(1, 100),
                    Rulebase(2, 200)
                ]);

            ManagementReport source = ManagementReport(
                objectIds: [2, 3],
                serviceIds: [11],
                userIds: [21],
                rulebases:
                [
                    Rulebase(2, 201, 202),
                    Rulebase(1, 101)
                ]);

            (bool newObjects, Dictionary<string, int> addedCounts) = target.Merge(source);

            Assert.That(newObjects, Is.True);
            Assert.That(target.Objects.Select(obj => obj.Id), Is.EqualTo(new long[] { 1, 2, 3 }));
            Assert.That(target.Services.Select(service => service.Id), Is.EqualTo(new long[] { 10, 11 }));
            Assert.That(target.Users.Select(user => user.Id), Is.EqualTo(new long[] { 20, 21 }));
            Assert.That(target.Rulebases.Single(rulebase => rulebase.Id == 1).Rules.Select(rule => rule.Id), Is.EqualTo(new long[] { 100, 101 }));
            Assert.That(target.Rulebases.Single(rulebase => rulebase.Id == 2).Rules.Select(rule => rule.Id), Is.EqualTo(new long[] { 200, 201, 202 }));
            Assert.That(addedCounts["NetworkObjects"], Is.EqualTo(2));
            Assert.That(addedCounts["Rules"], Is.EqualTo(2));
        }

        [Test]
        public void Merge_DifferentRulebaseIds_Throws()
        {
            ManagementReport target = ManagementReport([], [], [], [Rulebase(1, 100)]);
            ManagementReport source = ManagementReport([], [], [], [Rulebase(2, 200)]);

            Assert.That(() => target.Merge(source), Throws.TypeOf<NotSupportedException>());
        }

        private static ManagementReport ManagementReport(long[] objectIds, long[] serviceIds, long[] userIds, RulebaseReport[] rulebases)
        {
            return new ManagementReport
            {
                Objects = objectIds.Select(id => new NetworkObject { Id = id }).ToArray(),
                Services = serviceIds.Select(id => new NetworkService { Id = id }).ToArray(),
                Users = userIds.Select(id => new NetworkUser { Id = id }).ToArray(),
                Rulebases = rulebases
            };
        }

        private static RulebaseReport Rulebase(int id, params long[] ruleIds)
        {
            return new RulebaseReport
            {
                Id = id,
                Rules = ruleIds.Select(ruleId => new Rule { Id = ruleId, RulebaseId = id }).ToArray()
            };
        }
    }
}
