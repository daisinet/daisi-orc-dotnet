using Daisi.Orc.Core.Data.Models;
using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Daisi.Orc.Core.Data.Db
{
    public partial class Cosmo
    {
        public const string BlogsIdPrefix = "blog";
        public const string BlogsContainerName = "Blogs";
        public const string BlogsPartitionKeyName = "BlogId";

        public async Task<List<Blog>> GetBlogsAsync(int count = 10)
        {
            var container = await GetContainerAsync(BlogsContainerName);
            var it = container.GetItemQueryIterator<Blog>($"SELECT TOP {count} * FROM c ORDER BY c._ts DESC");
            var blogs = new List<Blog>();
            while (it.HasMoreResults)
            {
                var response = await it.ReadNextAsync();
                blogs.AddRange(response.Resource);
            }
            return blogs;
        }

        public async Task<(List<Blog> Blogs, int TotalCount)> GetBlogsPagedAsync(int page = 1, int pageSize = 9)
        {
            var container = await GetContainerAsync(BlogsContainerName);

            var countQuery = container.GetItemQueryIterator<int>("SELECT VALUE COUNT(1) FROM c");
            int totalCount = 0;
            while (countQuery.HasMoreResults)
            {
                var response = await countQuery.ReadNextAsync();
                totalCount = response.FirstOrDefault();
            }

            int offset = (page - 1) * pageSize;
            var query = new QueryDefinition($"SELECT * FROM c ORDER BY c.DateCreated DESC OFFSET @offset LIMIT @limit")
                .WithParameter("@offset", offset)
                .WithParameter("@limit", pageSize);

            var it = container.GetItemQueryIterator<Blog>(query);
            var blogs = new List<Blog>();
            while (it.HasMoreResults)
            {
                var response = await it.ReadNextAsync();
                blogs.AddRange(response.Resource);
            }

            return (blogs, totalCount);
        }

        public async Task<Blog> CreateBlogAsync(Blog blog)
        {
            var container = await GetContainerAsync(BlogsContainerName);
            blog.Id = Regex.Replace(blog.Title, "[^a-zA-Z0-9 ]", "").Replace(" ", "_");
            var result = await container.CreateItemAsync<Blog>(blog);
            return result.Resource;
        }

        public PartitionKey GetPartitionKey(Blog blog)
        {
            return new PartitionKey(blog.BlogId);
        }

        public async Task<Blog> GetBlogAsync(string blogId)
        {
            var container = await GetContainerAsync(BlogsContainerName);
            var blog = await container.ReadItemAsync<Blog>(blogId, new PartitionKey(blogId));
            return blog.Resource;
        }

        public async Task<Blog> UpdateBlogAsync(Blog blog)
        {
            var container = await GetContainerAsync(BlogsContainerName);
            var item = await container.ReplaceItemAsync(blog, blog.Id, new PartitionKey(blog.BlogId));
            return item.Resource;
        }

        public async Task<bool> DeleteBlogAsync(string blogId)
        {
            var container = await GetContainerAsync(BlogsContainerName);
            try
            {
                await container.DeleteItemAsync<Blog>(blogId, new PartitionKey(blogId));
                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
        }
    }
}
