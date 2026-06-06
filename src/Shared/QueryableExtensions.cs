using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

using MageBackend.Web;

namespace MageBackend.Shared
{
    public static class QueryableExtensions
    {
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertiesCache = new();

        private static PropertyInfo[] GetCachedProperties(Type type)
        {
            return _propertiesCache.GetOrAdd(type, t => t.GetProperties());
        }

        private static PropertyInfo? GetCachedProperty(Type type, string name)
        {
            var props = GetCachedProperties(type);
            return Array.Find(props, p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public static IQueryable<T> ApplyActiveFilter<T>(this IQueryable<T> query, bool? active, bool forceDefaultTrue = false)
        {
            var propInfo = GetCachedProperty(typeof(T), "Active");
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
            var propInfo = GetCachedProperty(typeof(T), "CreatedAt");
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
                var property = GetNestedProperty(parameter, field);
                if (property == null || property.Type != typeof(string)) continue;

                var searchVal = Expression.Constant(searchWord);
                var containsCall = Expression.Call(property, containsMethod, searchVal);

                body = body == null ? containsCall : Expression.OrElse(body, containsCall);
            }

            if (body == null) return query;

            var lambda = Expression.Lambda<Func<T, bool>>(body, parameter);
            return query.Where(lambda);
        }

        public static IQueryable<T> ApplyOrdering<T>(this IQueryable<T> query, string? orderBy, string orderDirection)
        {
            if (string.IsNullOrEmpty(orderBy))
            {
                var createdAtProp = GetCachedProperty(typeof(T), "CreatedAt");
                if (createdAtProp != null)
                {
                    return ApplyOrdering(query, "CreatedAt", "desc");
                }
                return query;
            }

            var parameter = Expression.Parameter(typeof(T), "x");
            var property = GetNestedProperty(parameter, orderBy);
            if (property == null) return query;

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

        private static Expression? GetNestedProperty(Expression parameter, string field)
        {
            Expression property = parameter;
            foreach (var member in field.Split('.'))
            {
                var propInfo = GetCachedProperty(property.Type, member);
                if (propInfo == null) return null;
                property = Expression.Property(property, propInfo);
            }
            return property;
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
