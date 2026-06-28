using FWO.Api.Client.Queries;
using NUnit.Framework;

namespace FWO.Test
{
    [TestFixture]
    [Parallelizable]
    internal class IntegrityQueriesTest
    {
        [Test]
        public void IntegrityQueries_LoadExpectedOperations()
        {
            Assert.That(IntegrityQueries.getInconsistentRulebaseLinks, Does.Contain("query getInconsistentRulebaseLinks"));
            Assert.That(IntegrityQueries.getInconsistentRulebaseLinks, Does.Contain("rulebase_link"));
            Assert.That(IntegrityQueries.resolveInconsistentRulebaseLinks, Does.Contain("mutation resolveInconsistentRulebaseLinks"));
            Assert.That(IntegrityQueries.resolveInconsistentRulebaseLinks, Does.Contain("update_rulebase_link_many"));
        }
    }
}
