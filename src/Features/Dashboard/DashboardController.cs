using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MageBackend.Database;
using MageBackend.Core.Filters;
using MageBackend.Core;

namespace MageBackend.Features.Dashboard
{
    [ApiController]
    [Route("v1/dashboard")]
    public class DashboardController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public DashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("stats")]
        [CheckPermission("dashboard", "view")]
        [ProducesResponseType(typeof(DashboardStatsResponseDto), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> GetStats(
            [FromQuery(Name = "createdAt_start")] string? createdAtStart,
            [FromQuery(Name = "createdAt_end")] string? createdAtEnd)
        {
            var start = DateTimeHelper.ParseStartDate(createdAtStart);
            var end = DateTimeHelper.ParseEndDate(createdAtEnd);

            var userStats = await _context.Database
                .SqlQueryRaw<TimeSeriesStatDto>(@"
                    SELECT 
                        CONVERT(VARCHAR(10), created_at, 120) AS [Date],
                        COUNT(*) AS [Count]
                    FROM [User]
                    WHERE created_at >= {0} AND created_at <= {1} AND is_deleted = 0
                    GROUP BY CONVERT(VARCHAR(10), created_at, 120)
                    ORDER BY [Date] ASC", start, end)
                .ToListAsync();

            var productStats = await _context.Database
                .SqlQueryRaw<TimeSeriesStatDto>(@"
                    SELECT 
                        CONVERT(VARCHAR(10), created_at, 120) AS [Date],
                        COUNT(*) AS [Count]
                    FROM Product
                    WHERE created_at >= {0} AND created_at <= {1} AND is_deleted = 0
                    GROUP BY CONVERT(VARCHAR(10), created_at, 120)
                    ORDER BY [Date] ASC", start, end)
                .ToListAsync();

            var productsPerUser = await _context.Database
                .SqlQueryRaw<UserProductStatDto>(@"
                    SELECT 
                        p.id_user AS UserId,
                        COALESCE(u.name, 'Anonymous') AS UserName,
                        COUNT(*) AS [Count]
                    FROM Product p
                    LEFT JOIN [User] u ON p.id_user = u.id
                    WHERE p.created_at >= {0} 
                      AND p.created_at <= {1}
                      AND p.is_deleted = 0
                    GROUP BY p.id_user, u.name
                    ORDER BY [Count] DESC", start, end)
                .ToListAsync();

            return Ok(new DashboardStatsResponseDto
            {
                UserCreationStats = userStats,
                ProductCreationStats = productStats,
                ProductsPerUser = productsPerUser
            });
        }
    }
}
