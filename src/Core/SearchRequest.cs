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

            if (!ValidateAllowedKeys(query, out errorMessage))
                return request;

            ParsePagination(query, request, out errorMessage);
            if (errorMessage != null) return request;

            ParseSearch(query, request, allowedFields, out errorMessage);
            if (errorMessage != null) return request;

            ParseOrdering(query, request, allowedFields, out errorMessage);
            if (errorMessage != null) return request;

            ParseDates(query, request, out errorMessage);
            if (errorMessage != null) return request;

            ParseActive(query, request);

            return request;
        }

        private static bool ValidateAllowedKeys(IQueryCollection query, out string? errorMessage)
        {
            errorMessage = null;
            var knownParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "page", "size", "searchWord", "searchFields", "orderBy", "orderDirection", "createdAt_start", "createdAt_end", "active"
            };

            var invalidKey = query.Keys.FirstOrDefault(k => !knownParams.Contains(k));
            if (invalidKey != null)
            {
                errorMessage = $"Query parameter '{invalidKey}' is not allowed.";
                return false;
            }
            return true;
        }

        private static void ParsePagination(IQueryCollection query, SearchRequest request, out string? errorMessage)
        {
            errorMessage = null;
            if (query.TryGetValue("page", out var pageVal) && int.TryParse(pageVal, out var page))
            {
                request.Page = page;
            }

            if (query.TryGetValue("size", out var sizeVal) && int.TryParse(sizeVal, out var size))
            {
                if (size > 100)
                {
                    errorMessage = "Page size cannot exceed 100.";
                    return;
                }
                request.Size = size;
            }
        }

        private static void ParseSearch(IQueryCollection query, SearchRequest request, string[] allowedFields, out string? errorMessage)
        {
            errorMessage = null;
            if (query.TryGetValue("searchWord", out var sw)) request.SearchWord = sw.ToString();
            if (query.TryGetValue("searchFields", out var sf)) request.SearchFields = sf.ToString();

            if (!string.IsNullOrEmpty(request.SearchWord) && string.IsNullOrEmpty(request.SearchFields))
            {
                errorMessage = "searchFields is required when searchWord is provided.";
                return;
            }

            if (!string.IsNullOrEmpty(request.SearchFields))
            {
                var fields = request.SearchFields.Split(',');
                foreach (var field in fields)
                {
                    var normalized = field.Trim();
                    if (!allowedFields.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    {
                        errorMessage = $"Search on field '{field}' is not allowed or invalid.";
                        return;
                    }
                }
            }
        }

        private static void ParseOrdering(IQueryCollection query, SearchRequest request, string[] allowedFields, out string? errorMessage)
        {
            errorMessage = null;
            if (query.TryGetValue("orderBy", out var ob))
            {
                request.OrderBy = ob.ToString();
                if (!allowedFields.Contains(request.OrderBy.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    errorMessage = $"Order by field '{ob}' is not allowed or invalid.";
                    return;
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
        }

        private static void ParseDates(IQueryCollection query, SearchRequest request, out string? errorMessage)
        {
            errorMessage = null;
            if (query.TryGetValue("createdAt_start", out var cs))
            {
                if (!DateTime.TryParseExact(cs.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dStart))
                {
                    errorMessage = "Invalid format for createdAt_start. Use yyyy-MM-dd.";
                    return;
                }
                request.CreatedAtStart = dStart;
            }

            if (query.TryGetValue("createdAt_end", out var ce))
            {
                if (!DateTime.TryParseExact(ce.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dEnd))
                {
                    errorMessage = "Invalid format for createdAt_end. Use yyyy-MM-dd.";
                    return;
                }
                request.CreatedAtEnd = dEnd.Date.AddDays(1).AddSeconds(-1);
            }
        }

        private static void ParseActive(IQueryCollection query, SearchRequest request)
        {
            if (query.TryGetValue("active", out var act))
            {
                var actStr = act.ToString();
                if (bool.TryParse(actStr, out var activeBool))
                {
                    request.Active = activeBool;
                }
                else if (actStr.Equals("1") || actStr.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    request.Active = true;
                }
                else if (actStr.Equals("0") || actStr.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    request.Active = false;
                }
            }
        }
    }

    public class SearchResult<T>
    {
        public List<T> Items { get; set; }
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
