using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    public class BlogsRPC(ILogger<BlogsRPC> logger, Cosmo cosmo) : BlogsProto.BlogsProtoBase
    {
        [Authorize]
        public async override Task<GetBlogsResponse> GetBlogs(GetBlogsRequest request, ServerCallContext context)
        {
            var response = new GetBlogsResponse();

            int page = request.Paging?.PageIndex ?? 1;
            int pageSize = request.Paging?.PageSize ?? 20;

            var (blogs, totalCount) = await cosmo.GetBlogsPagedAsync(page, pageSize);
            response.TotalCount = totalCount;
            response.Blogs.AddRange(blogs.Select(b => b.ConvertToProto()));

            return response;
        }

        [Authorize]
        public async override Task<GetBlogResponse> GetBlog(GetBlogRequest request, ServerCallContext context)
        {
            var blog = await cosmo.GetBlogAsync(request.Id);
            if (blog == null)
                throw new RpcException(new Status(StatusCode.NotFound, "Blog not found."));

            return new GetBlogResponse { Blog = blog.ConvertToProto() };
        }

        [Authorize]
        public async override Task<CreateBlogResponse> CreateBlog(CreateBlogRequest request, ServerCallContext context)
        {
            var dbBlog = request.Blog.ConvertToDb();
            dbBlog.DateCreated = DateTime.UtcNow;
            dbBlog = await cosmo.CreateBlogAsync(dbBlog);

            return new CreateBlogResponse { Blog = dbBlog.ConvertToProto() };
        }

        [Authorize]
        public async override Task<UpdateBlogResponse> UpdateBlog(UpdateBlogRequest request, ServerCallContext context)
        {
            var existing = await cosmo.GetBlogAsync(request.Blog.Id);
            if (existing == null)
                throw new RpcException(new Status(StatusCode.NotFound, "Blog not found."));

            var dbBlog = request.Blog.ConvertToDb();
            dbBlog.DateCreated = existing.DateCreated;
            dbBlog = await cosmo.UpdateBlogAsync(dbBlog);

            return new UpdateBlogResponse { Blog = dbBlog.ConvertToProto() };
        }

        [Authorize]
        public async override Task<DeleteBlogResponse> DeleteBlog(DeleteBlogRequest request, ServerCallContext context)
        {
            var success = await cosmo.DeleteBlogAsync(request.Id);
            return new DeleteBlogResponse { Success = success };
        }
    }

    public static class BlogExtensions
    {
        extension(Blog blog)
        {
            public BlogArticle ConvertToProto()
            {
                var proto = new BlogArticle
                {
                    Id = blog.Id ?? string.Empty,
                    Title = blog.Title ?? string.Empty,
                    Author = blog.Author ?? string.Empty,
                    AuthorLink = blog.AuthorLink ?? string.Empty,
                    BodyMarkdown = blog.BodyMarkdown ?? string.Empty,
                    ImageUrl = blog.ImageUrl ?? string.Empty,
                    DateCreated = blog.DateCreated.ToUniversalTime().ToTimestamp(),
                    LikeCount = blog.LikeCount,
                    ViewCount = blog.ViewCount
                };

                if (blog.Tags is not null)
                {
                    foreach (var tag in blog.Tags)
                    {
                        proto.Tags.Add(tag.Name ?? tag.Id ?? string.Empty);
                    }
                }

                return proto;
            }
        }

        extension(BlogArticle proto)
        {
            public Blog ConvertToDb()
            {
                var blog = new Blog
                {
                    Id = string.IsNullOrWhiteSpace(proto.Id) ? Cosmo.GenerateId(Cosmo.BlogsIdPrefix) : proto.Id,
                    Title = proto.Title,
                    Author = proto.Author,
                    AuthorLink = proto.AuthorLink,
                    BodyMarkdown = proto.BodyMarkdown,
                    ImageUrl = proto.ImageUrl,
                    DateCreated = proto.DateCreated?.ToDateTime() ?? DateTime.UtcNow,
                    LikeCount = proto.LikeCount,
                    ViewCount = proto.ViewCount,
                    Tags = proto.Tags.Select(t => new Stub { Id = t, Name = t }).ToArray()
                };

                return blog;
            }
        }
    }
}
