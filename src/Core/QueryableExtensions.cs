using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MageBackend.Core
{
    public static class QueryableExtensions
    {
        public static IQueryable<T> ApplyActiveFilter<T>(this IQueryable<T> query, bool? active, bool forceDefaultTrue = false)
        {
            var propInfo = typeof(T).GetProperties()
                .FirstOrDefault(p => p.Name.Equals("Active", StringComparison.OrdinalIgnoreCase));
            if (propInfo == null) return query;

            if (!active.HasValue && !forceDefaultTrue) return query;

            var parameter = Expression.Parameter(typeof(T), "x");
            var property = Expression.Property(parameter, propInfo);
            var value = active ?? true;

            var compare = Expression.Equal(property, Expression.Constant(value));
            var lambda = Expression.Lambda<Func<T, bool>>(compare, parameter);
            return query.Where(lambda);
        }

        public static IQueryable<T> ApplyDateRange<T>(this IQueryable<T> query, DateTime? start, DateTime? end)
        {
            var propInfo = typeof(T).GetProperties()
                .FirstOrDefault(p => p.Name.Equals("CreatedAt", StringComparison.OrdinalIgnoreCase));
            if (propInfo == null) return query;

            var parameter = Expression.Parameter(typeof(T), "x");
            var property = Expression.Property(parameter, propInfo);

            if (start.HasValue)
            {
                var startVal = Expression.Constant(start.Value);
                var compare = Expression.GreaterThanOrEqual(property, startVal);
                var lambda = Expression.Lambda<Func<T, bool>>(compare, parameter);
                query = query.Where(lambda);
            }

            if (end.HasValue)
            {
                var endVal = Expression.Constant(end.Value);
                var compare = Expression.LessThanOrEqual(property, endVal);
                var lambda = Expression.Lambda<Func<T, bool>>(compare, parameter);
                query = query.Where(lambda);
            }

            return query;
        }

        public static IQueryable<T> ApplySearch<T>(this IQueryable<T> query, string? searchWord, string? searchFields)
        {
            if (string.IsNullOrEmpty(searchWord) || string.IsNullOrEmpty(searchFields))
                return query;

            var parameter = Expression.Parameter(typeof(T), "x");
            Expression? body = null;

            var fields = searchFields.Split(',').Select(f => f.Trim()).ToList();
            var containsMethod = typeof(string).GetMethod("Contains", new[] { typeof(string) })!;

            foreach (var field in fields)
            {
                Expression property = parameter;
                bool skipField = false;

                foreach (var member in field.Split('.'))
                {
                    var propInfo = property.Type.GetProperties()
                        .FirstOrDefault(p => p.Name.Equals(member, StringComparison.OrdinalIgnoreCase));
                    if (propInfo == null)
                    {
                        skipField = true;
                        break;
                    }
                    property = Expression.Property(property, propInfo);
                }

                if (skipField) continue;

                if (property.Type != typeof(string)) continue;

                var searchVal = Expression.Constant(searchWord);
                var containsCall = Expression.Call(property, containsMethod, searchVal);

                if (body == null)
                {
                    body = containsCall;
                }
                else
                {
                    body = Expression.OrElse(body, containsCall);
                }
            }

            if (body == null) return query;

            var lambda = Expression.Lambda<Func<T, bool>>(body, parameter);
            return query.Where(lambda);
        }

        public static IQueryable<T> ApplyOrdering<T>(this IQueryable<T> query, string? orderBy, string orderDirection)
        {
            if (string.IsNullOrEmpty(orderBy))
            {
                var createdAtProp = typeof(T).GetProperty("CreatedAt");
                if (createdAtProp != null)
                {
                    return ApplyOrdering(query, "CreatedAt", "desc");
                }
                return query;
            }

            var parameter = Expression.Parameter(typeof(T), "x");
            Expression property = parameter;

            foreach (var member in orderBy.Split('.'))
            {
                var propInfo = property.Type.GetProperties()
                    .FirstOrDefault(p => p.Name.Equals(member, StringComparison.OrdinalIgnoreCase));
                if (propInfo == null) return query;
                property = Expression.Property(property, propInfo);
            }

            var lambda = Expression.Lambda(property, parameter);

            var methodName = orderDirection.Equals("desc", StringComparison.OrdinalIgnoreCase) ? "OrderByDescending" : "OrderBy";
            var resultExpression = Expression.Call(
                typeof(Queryable),
                methodName,
                new Type[] { typeof(T), property.Type },
                query.Expression,
                Expression.Quote(lambda)
            );

            return query.Provider.CreateQuery<T>(resultExpression);
        }

        public static async Task<SearchResult<TResponse>> ExecuteSearchAsync<TEntity, TResponse>(
            this IQueryable<TEntity> query,
            SearchRequest req,
            Func<TEntity, TResponse> mapper)
        {
            query = query
                .ApplySearch(req.SearchWord, req.SearchFields)
                .ApplyDateRange(req.CreatedAtStart, req.CreatedAtEnd);

            var total = await query.CountAsync();

            query = query.ApplyOrdering(req.OrderBy, req.OrderDirection);

            var items = await query.Skip(req.Page * req.Size).Take(req.Size).ToListAsync();

            var dtos = items.Select(mapper).ToList();

            return new SearchResult<TResponse>(dtos, total, req.Page, req.Size);
        }
    }
}
