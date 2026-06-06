using MageBackend.Web;
using MageBackend.Shared;
using MageBackend.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MageBackend.Features.Feature.Queries
{
    public record ListFeaturesQuery(SearchRequest Request) : IRequest<SearchResult<Database.Feature>>;

    public class ListFeaturesHandler : IRequestHandler<ListFeaturesQuery, SearchResult<Database.Feature>>
    {
        private readonly ApplicationDbContext _context;

        public ListFeaturesHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<SearchResult<Database.Feature>> Handle(ListFeaturesQuery query, CancellationToken cancellationToken)
        {
            return await _context.Feature
                .ApplyActiveFilter(query.Request.Active, forceDefaultTrue: true)
                .ExecuteSearchAsync(query.Request, f => f);
        }
    }

    public record ListAllFeaturesQuery(SearchRequest Request) : IRequest<SearchResult<Database.Feature>>;

    public class ListAllFeaturesHandler : IRequestHandler<ListAllFeaturesQuery, SearchResult<Database.Feature>>
    {
        private readonly ApplicationDbContext _context;

        public ListAllFeaturesHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<SearchResult<Database.Feature>> Handle(ListAllFeaturesQuery query, CancellationToken cancellationToken)
        {
            return await _context.Feature
                .ApplyActiveFilter(query.Request.Active)
                .ExecuteSearchAsync(query.Request, f => f);
        }
    }

    public record GetFeatureByIdQuery(string Id) : IRequest<Database.Feature?>;

    public class GetFeatureByIdHandler : IRequestHandler<GetFeatureByIdQuery, Database.Feature?>
    {
        private readonly ApplicationDbContext _context;

        public GetFeatureByIdHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Database.Feature?> Handle(GetFeatureByIdQuery query, CancellationToken cancellationToken)
        {
            return await _context.Feature.FindAsync(new object[] { query.Id }, cancellationToken);
        }
    }
}
