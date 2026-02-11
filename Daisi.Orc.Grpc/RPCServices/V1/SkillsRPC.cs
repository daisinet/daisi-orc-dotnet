using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models.Skills;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    [Authorize]
    public class SkillsRPC(ILogger<SkillsRPC> logger, Cosmo cosmo) : SkillsProto.SkillsProtoBase
    {
        public override async Task<GetSkillsByAccountResponse> GetSkillsByAccount(GetSkillsByAccountRequest request, ServerCallContext context)
        {
            var skills = await cosmo.GetSkillsByAccountAsync(request.AccountId);
            var response = new GetSkillsByAccountResponse();
            foreach (var skill in skills)
            {
                response.Skills.Add(MapToProto(skill));
            }
            return response;
        }

        public override async Task<GetSkillByIdResponse> GetSkillById(GetSkillByIdRequest request, ServerCallContext context)
        {
            var skill = await cosmo.GetSkillByIdAsync(request.Id);
            return new GetSkillByIdResponse
            {
                Skill = skill is not null ? MapToProto(skill) : null
            };
        }

        public override async Task<CreateSkillResponse> CreateSkill(CreateSkillRequest request, ServerCallContext context)
        {
            var skill = new Skill
            {
                AccountId = request.AccountId,
                Name = request.Name,
                Description = request.Description,
                ShortDescription = request.ShortDescription,
                Author = request.Author,
                Version = request.Version,
                IconUrl = request.IconUrl,
                RequiredToolGroups = request.RequiredToolGroups.ToList(),
                Tags = request.Tags.ToList(),
                Visibility = request.Visibility,
                SystemPromptTemplate = request.SystemPromptTemplate
            };

            skill = await cosmo.CreateSkillAsync(skill);
            logger.LogInformation("Created skill {SkillId} for account {AccountId}", skill.Id, skill.AccountId);

            return new CreateSkillResponse
            {
                Skill = MapToProto(skill)
            };
        }

        public override async Task<UpdateSkillResponse> UpdateSkill(UpdateSkillRequest request, ServerCallContext context)
        {
            var existing = await cosmo.GetSkillByIdAsync(request.Skill.Id);
            if (existing is null)
                throw new RpcException(new Status(StatusCode.NotFound, "Skill not found"));

            existing.Name = request.Skill.Name;
            existing.Description = request.Skill.Description;
            existing.ShortDescription = request.Skill.ShortDescription;
            existing.Author = request.Skill.Author;
            existing.Version = request.Skill.Version;
            existing.IconUrl = request.Skill.IconUrl;
            existing.RequiredToolGroups = request.Skill.RequiredToolGroups.ToList();
            existing.Tags = request.Skill.Tags.ToList();
            existing.Visibility = request.Skill.Visibility;
            existing.Status = request.Skill.Status;
            existing.SystemPromptTemplate = request.Skill.SystemPromptTemplate;
            existing.RejectionReason = request.Skill.RejectionReason;

            if (!string.IsNullOrEmpty(request.Skill.ReviewedBy))
            {
                existing.ReviewedBy = request.Skill.ReviewedBy;
                existing.ReviewedAt = request.Skill.ReviewedAt?.ToDateTime();
            }

            await cosmo.UpdateSkillAsync(existing);
            logger.LogInformation("Updated skill {SkillId}", existing.Id);

            return new UpdateSkillResponse
            {
                Skill = MapToProto(existing)
            };
        }

        public override async Task<GetPendingReviewSkillsResponse> GetPendingReviewSkills(GetPendingReviewSkillsRequest request, ServerCallContext context)
        {
            var skills = await cosmo.GetPendingReviewSkillsAsync();
            var response = new GetPendingReviewSkillsResponse();
            foreach (var skill in skills)
            {
                response.Skills.Add(MapToProto(skill));
            }
            return response;
        }

        public override async Task<CreateSkillReviewResponse> CreateSkillReview(CreateSkillReviewRequest request, ServerCallContext context)
        {
            var review = new SkillReview
            {
                SkillId = request.SkillId,
                ReviewerEmail = request.ReviewerEmail,
                Status = request.Status,
                Comment = request.Comment
            };

            review = await cosmo.CreateSkillReviewAsync(review);
            logger.LogInformation("Created review {ReviewId} for skill {SkillId}", review.Id, review.SkillId);

            return new CreateSkillReviewResponse
            {
                Review = MapReviewToProto(review)
            };
        }

        public override async Task<GetSkillReviewsResponse> GetSkillReviews(GetSkillReviewsRequest request, ServerCallContext context)
        {
            var reviews = await cosmo.GetSkillReviewsAsync(request.SkillId);
            var response = new GetSkillReviewsResponse();
            foreach (var review in reviews)
            {
                response.Reviews.Add(MapReviewToProto(review));
            }
            return response;
        }

        public override async Task<GetRequiredSkillsResponse> GetRequiredSkills(GetRequiredSkillsRequest request, ServerCallContext context)
        {
            var skills = await cosmo.GetRequiredSkillsAsync();
            var response = new GetRequiredSkillsResponse();
            foreach (var skill in skills)
            {
                response.Skills.Add(MapToProto(skill));
            }
            return response;
        }

        private static SkillInfo MapToProto(Skill skill)
        {
            var info = new SkillInfo
            {
                Id = skill.Id ?? string.Empty,
                AccountId = skill.AccountId ?? string.Empty,
                Name = skill.Name ?? string.Empty,
                Description = skill.Description ?? string.Empty,
                ShortDescription = skill.ShortDescription ?? string.Empty,
                Author = skill.Author ?? string.Empty,
                Version = skill.Version ?? string.Empty,
                IconUrl = skill.IconUrl ?? string.Empty,
                Visibility = skill.Visibility ?? string.Empty,
                Status = skill.Status ?? string.Empty,
                ReviewedBy = skill.ReviewedBy ?? string.Empty,
                RejectionReason = skill.RejectionReason ?? string.Empty,
                DownloadCount = skill.DownloadCount,
                SystemPromptTemplate = skill.SystemPromptTemplate ?? string.Empty,
                IsRequired = skill.IsRequired,
                CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(skill.CreatedAt, DateTimeKind.Utc)),
                UpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(skill.UpdatedAt, DateTimeKind.Utc))
            };

            if (skill.ReviewedAt.HasValue)
                info.ReviewedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(skill.ReviewedAt.Value, DateTimeKind.Utc));

            info.RequiredToolGroups.AddRange(skill.RequiredToolGroups ?? []);
            info.Tags.AddRange(skill.Tags ?? []);

            return info;
        }

        private static SkillReviewInfo MapReviewToProto(SkillReview review)
        {
            return new SkillReviewInfo
            {
                Id = review.Id ?? string.Empty,
                SkillId = review.SkillId ?? string.Empty,
                ReviewerEmail = review.ReviewerEmail ?? string.Empty,
                Status = review.Status ?? string.Empty,
                Comment = review.Comment ?? string.Empty,
                Timestamp = Timestamp.FromDateTime(DateTime.SpecifyKind(review.Timestamp, DateTimeKind.Utc))
            };
        }
    }
}
