using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MageBackend.Features.Dashboard
{
    [ExcludeFromCodeCoverage]
    public class DashboardStatsResponseDto
    {
        public List<TimeSeriesStatDto> UserCreationStats { get; set; } = new();
        public List<TimeSeriesStatDto> ProductCreationStats { get; set; } = new();
        public List<UserProductStatDto> ProductsPerUser { get; set; } = new();
    }

    [ExcludeFromCodeCoverage]
    public class TimeSeriesStatDto
    {
        public string Date { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    [ExcludeFromCodeCoverage]
    public class UserProductStatDto
    {
        public string? UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public int Count { get; set; }
    }
}
