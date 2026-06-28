using FWO.Logging;

namespace FWO.Api.Client.Queries
{
    public class IntegrityQueries : Queries
    {
        public static readonly string getInconsistentRulebaseLinks;
        public static readonly string resolveInconsistentRulebaseLinks;

        static IntegrityQueries()
        {
            try
            {
                getInconsistentRulebaseLinks = GetQueryText("integrity/getInconsistentRulebaseLinks.graphql");
                resolveInconsistentRulebaseLinks = GetQueryText("integrity/resolveInconsistentRulebaseLinks.graphql");
            }
            catch (Exception exception)
            {
                Log.WriteError("Initialize Api Queries", "Api Integrity Queries could not be loaded.", exception);
#if RELEASE
                Environment.Exit(-1);
#else
                throw;
#endif
            }
        }
    }
}
