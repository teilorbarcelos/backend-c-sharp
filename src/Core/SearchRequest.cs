using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MageBackend.Core
{
    public class SearchRequest
    {
        public int Page { get; set; } = 0;
        public int Size { get; set; } = 10;
        public string? SearchWord { get; set; }
        public string? SearchFields { get; set; }
        public string? OrderBy { get; set; }
        public string OrderDirection { get; set; } = "asc";
        public DateTime? CreatedAtStart { get; set; }
        public DateTime? CreatedAtEnd { get; set; }
        public bool? Active { get; set; }

        public static SearchRequest Parse(IQueryCollection query, string[] allowedFields, out string? errorMessage)
        {
            errorMessage = null;
            var request = new SearchRequest();

            var knownParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "page", "size", "searchWord", "searchFields", "orderBy", "orderDirection", "createdAt_start", "createdAt_end", "active"
            };

            foreach (var key in query.Keys)
            {
                if (!knownParams.Contains(key))
                {
                    errorMessage = $"Query parameter '{key}' is not allowed.";
                    return request;
                }
            }

            if (query.TryGetValue("page", out var pageVal) && int.TryParse(pageVal, out var page))
            {
                request.Page = page;
            }

            if (query.TryGetValue("size", out var sizeVal) && int.TryParse(sizeVal, out var size))
            {
                if (size > 100)
                {
                    errorMessage = "Page size cannot exceed 100.";
                    return request;
                }
                request.Size = size;
            }

            if (query.TryGetValue("searchWord", out var sw))
            {
                request.SearchWord = sw.ToString();
            }

            if (query.TryGetValue("searchFields", out var sf))
            {
                request.SearchFields = sf.ToString();
            }

            if (!string.IsNullOrEmpty(request.SearchWord) && string.IsNullOrEmpty(request.SearchFields))
            {
                errorMessage = "searchFields is required when searchWord is provided.";
                return request;
            }

            if (!string.IsNullOrEmpty(request.SearchFields))
            {
                var fields = request.SearchFields.Split(',');
                foreach (var field in fields)
                {
                    var normalized = field.Trim();
                    // Allow mapping Role.name or Role.Name
                    if (!allowedFields.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    {
                        errorMessage = $"Search on field '{field}' is not allowed or invalid.";
                        return request;
                    }
                }
            }

            if (query.TryGetValue("orderBy", out var ob))
            {
                request.OrderBy = ob.ToString();
                if (!allowedFields.Contains(request.OrderBy.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    errorMessage = $"Order by field '{ob}' is not allowed or invalid.";
                    return request;
                }
            }

            if (query.TryGetValue("orderDirection", out var od))
            {
                var odStr = od.ToString().ToLower();
                if (odStr == "asc" || odStr == "desc")
                {
                    request.OrderDirection = odStr;
                }
            }

            if (query.TryGetValue("createdAt_start", out var cs))
            {
                if (!DateTime.TryParseExact(cs.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dStart))
                {
                    errorMessage = "Invalid format for createdAt_start. Use yyyy-MM-dd.";
                    return request;
                }
                request.CreatedAtStart = dStart;
            }

            if (query.TryGetValue("createdAt_end", out var ce))
            {
                if (!DateTime.TryParseExact(ce.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dEnd))
                {
                    errorMessage = "Invalid format for createdAt_end. Use yyyy-MM-dd.";
                    return request;
                }
                request.CreatedAtEnd = dEnd.Date.AddDays(1).AddSeconds(-1); // include the entire day
            }

            if (query.TryGetValue("active", out var act))
            {
                if (bool.TryParse(act.ToString(), out var activeBool))
                {
                    request.Active = activeBool;
                }
                else if (act.ToString().Equals("1") || act.ToString().Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    request.Active = true;
                }
                else if (act.ToString().Equals("0") || act.ToString().Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    request.Active = false;
                }
            }

            return request;
        }
    }

    public class SearchResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int Size { get; set; }

        public SearchResult(List<T> items, int total, int page, int size)
        {
            Items = items;
            Total = total;
            Page = page;
            Size = size;
        }
    }
}
