using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OpenTavu.Plugins.Tests.Fakes
{
    /// <summary>The fake itself: paging, so the jobs' paging loops are tested against something that pages.</summary>
    public class InMemoryOrganizationServiceTests
    {
        [Fact]
        public void A_query_with_PageInfo_returns_every_row_once_across_pages()
        {
            var svc = new InMemoryOrganizationService();
            for (int i = 0; i < 7; i++) svc.Seed(new Entity("tavu_lead") { ["tavu_order"] = i });

            var q = new QueryExpression("tavu_lead") { ColumnSet = new ColumnSet("tavu_order"), PageInfo = new PagingInfo { Count = 3, PageNumber = 1 } };
            q.AddOrder("tavu_order", OrderType.Ascending);
            var seen = new System.Collections.Generic.List<int>();
            int pages = 0;
            while (true)
            {
                EntityCollection page = svc.RetrieveMultiple(q);
                pages++;
                seen.AddRange(page.Entities.Select(e => e.GetAttributeValue<int>("tavu_order")));
                if (!page.MoreRecords) break;
                q.PageInfo.PageNumber++;
                q.PageInfo.PagingCookie = page.PagingCookie;
            }

            Assert.Equal(3, pages);
            Assert.Equal(Enumerable.Range(0, 7), seen);
        }

        [Fact]
        public void An_unsupported_operator_throws_instead_of_matching_silently()
        {
            var svc = new InMemoryOrganizationService();
            var q = new QueryExpression("tavu_lead");
            q.Criteria.AddCondition("tavu_name", ConditionOperator.Like, "%x%");

            Assert.Throws<System.NotSupportedException>(() => svc.RetrieveMultiple(q));
        }
    }
}
