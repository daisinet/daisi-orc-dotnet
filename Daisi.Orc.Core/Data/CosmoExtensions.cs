using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace Daisi.Orc.Core.Data
{
    public static class CosmoExtensions
    {
        public static async Task<List<T>> GetAllItemsInContainerAsync<T>(this Container container)
        {
            var queryDefinition = new QueryDefinition("SELECT * FROM c");
            using FeedIterator<T> feedIterator = container.GetItemQueryIterator<T>(queryDefinition);

            List<T> items = new List<T>();
            while (feedIterator.HasMoreResults)
            {
                FeedResponse<T> response = await feedIterator.ReadNextAsync();
                items.AddRange(response.Resource);
            }
            return items;
        }

        public static async Task<PagedResult<T>> ToPagedResultAsync<T>(this IQueryable<T> query, int? pageSize = 10, int? pageIndex = 0)
        {
            PagedResult<T> result = new();

            result.TotalCount = await query.CountAsync();


            if (pageSize != null)
            {
                if (pageIndex != null)
                    query = query.Skip(pageIndex.Value * pageSize.Value);

                query = query.Take(pageSize.Value);
            }


            var iterator = query.ToFeedIterator();
            
            while (iterator.HasMoreResults)
            {
                foreach (var host in await iterator.ReadNextAsync())
                {
                    result.Items.Add(host);
                }
            }

            return result;
        }
    }

    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
       
        public int TotalCount { get; set; }


    }

}
