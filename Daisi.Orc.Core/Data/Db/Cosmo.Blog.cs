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
    }
}
