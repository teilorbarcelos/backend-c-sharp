using MageBackend.Web;
using MageBackend.Shared;
using MageBackend.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MageBackend.Features.Role.Queries
{
    public record ListRolesQuery(SearchRequest Request) : IRequest<SearchResult<RoleResponseDto>>;

    public class ListRolesHandler : IRequestHandler<ListRolesQuery, SearchResult<RoleResponseDto>>
    {
        private readonly ApplicationDbContext _context;

        public ListRolesHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<SearchResult<RoleResponseDto>> Handle(ListRolesQuery query, CancellationToken cancellationToken)
        {
            var searchResult = await _context.Role
                .Where(r => !r.IsDeleted)
                .ApplyActiveFilter(query.Request.Active, forceDefaultTrue: true)
                .ExecuteSearchAsync(query.Request, r => r);

            var roleIds = searchResult.Items.Select(r => r.Id).ToList();
            var allRoleFeatures = await _context.RoleFeature
                .Where(rf => roleIds.Contains(rf.IdRole))
                .ToListAsync(cancellationToken);

            var dtos = searchResult.Items.Select(r => RoleMapper.MapToDto(r, allRoleFeatures
                .Where(rf => rf.IdRole == r.Id)
                .Select(rf => new RoleFeatureDto
                {
                    IdFeature = rf.IdFeature,
                    Create = rf.Create,
                    View = rf.View,
                    Delete = rf.Delete,
                    Activate = rf.Activate
                }).ToList())).ToList();

            return new SearchResult<RoleResponseDto>(dtos, searchResult.Total, searchResult.Page, searchResult.Size);
        }
    }

    public record ListAllRolesQuery(SearchRequest Request) : IRequest<SearchResult<RoleResponseDto>>;

    public class ListAllRolesHandler : IRequestHandler<ListAllRolesQuery, SearchResult<RoleResponseDto>>
    {
        private readonly ApplicationDbContext _context;

        public ListAllRolesHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<SearchResult<RoleResponseDto>> Handle(ListAllRolesQuery query, CancellationToken cancellationToken)
        {
            var searchResult = await _context.Role
                .Where(r => !r.IsDeleted)
                .ApplyActiveFilter(query.Request.Active)
                .ExecuteSearchAsync(query.Request, r => r);

            var roleIds = searchResult.Items.Select(r => r.Id).ToList();
            var allRoleFeatures = await _context.RoleFeature
                .Where(rf => roleIds.Contains(rf.IdRole))
                .ToListAsync(cancellationToken);

            var dtos = searchResult.Items.Select(r => RoleMapper.MapToDto(r, allRoleFeatures
                .Where(rf => rf.IdRole == r.Id)
                .Select(rf => new RoleFeatureDto
                {
                    IdFeature = rf.IdFeature,
                    Create = rf.Create,
                    View = rf.View,
                    Delete = rf.Delete,
                    Activate = rf.Activate
                }).ToList())).ToList();

            return new SearchResult<RoleResponseDto>(dtos, searchResult.Total, searchResult.Page, searchResult.Size);
        }
    }

    public record GetRoleByIdQuery(string Id) : IRequest<RoleResponseDto?>;

    public class GetRoleByIdHandler : IRequestHandler<GetRoleByIdQuery, RoleResponseDto?>
    {
        private readonly ApplicationDbContext _context;

        public GetRoleByIdHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<RoleResponseDto?> Handle(GetRoleByIdQuery query, CancellationToken cancellationToken)
        {
            var role = await _context.Role.Where(r => r.Id == query.Id && !r.IsDeleted).FirstOrDefaultAsync(cancellationToken);
            if (role == null) return null;

            var roleFeatures = await _context.RoleFeature
                .Where(rf => rf.IdRole == query.Id)
                .Select(rf => new RoleFeatureDto
                {
                    IdFeature = rf.IdFeature,
                    Create = rf.Create,
                    View = rf.View,
                    Delete = rf.Delete,
                    Activate = rf.Activate
                }).ToListAsync(cancellationToken);

            return RoleMapper.MapToDto(role, roleFeatures);
        }
    }

    public record ListFeaturesQuery : IRequest<List<Database.Feature>>;

    public class ListFeaturesHandler : IRequestHandler<ListFeaturesQuery, List<Database.Feature>>
    {
        private readonly ApplicationDbContext _context;

        public ListFeaturesHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<Database.Feature>> Handle(ListFeaturesQuery query, CancellationToken cancellationToken)
        {
            return await _context.Feature.Where(f => f.Active).ToListAsync(cancellationToken);
        }
    }
}
