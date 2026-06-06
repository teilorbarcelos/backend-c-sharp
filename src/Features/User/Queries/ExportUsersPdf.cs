using MageBackend.Web;
using MageBackend.Shared;
using MageBackend.Database;
using MageBackend.Infrastructure.Pdf;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MageBackend.Features.User.Queries
{
    public record ExportUsersPdfQuery(SearchRequest Request) : IRequest<Stream>;

    public class ExportUsersPdfHandler : IRequestHandler<ExportUsersPdfQuery, Stream>
    {
        private readonly ApplicationDbContext _context;
        private readonly IPdfProvider _pdfProvider;

        public ExportUsersPdfHandler(ApplicationDbContext context, IPdfProvider pdfProvider)
        {
            _context = context;
            _pdfProvider = pdfProvider;
        }

        public async Task<Stream> Handle(ExportUsersPdfQuery query, CancellationToken cancellationToken)
        {
            var allUsers = new List<Database.User>();
            int page = 0;
            int size = 100;
            int total = 0;

            var baseQuery = _context.User
                .Include(u => u.Role)
                .Where(u => !u.IsDeleted)
                .ApplyActiveFilter(query.Request.Active);

            do
            {
                var pageReq = new SearchRequest
                {
                    Page = page,
                    Size = size,
                    SearchWord = query.Request.SearchWord,
                    SearchFields = query.Request.SearchFields,
                    CreatedAtStart = query.Request.CreatedAtStart,
                    CreatedAtEnd = query.Request.CreatedAtEnd,
                    OrderBy = query.Request.OrderBy,
                    OrderDirection = query.Request.OrderDirection
                };

                var searchResult = await baseQuery.ExecuteSearchAsync(pageReq, u => u);
                allUsers.AddRange(searchResult.Items);
                total = searchResult.Total;
                page++;
            } while (allUsers.Count < total);

            var pdfData = new
            {
                title = "Relatório de Usuários",
                generatedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
                users = allUsers.Select(u => new
                {
                    id = u.Id,
                    name = u.Name,
                    email = u.Email,
                    phone = u.Phone,
                    roleName = u.Role?.Name,
                    active = u.Active
                }).ToList()
            };

            return await _pdfProvider.GeneratePdfAsync("user-list", pdfData);
        }
    }
}
