using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MageBackend.Web.Middleware;
using MageBackend.Infrastructure.Auth;

namespace MageBackend.Web.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    public class FeatureNameAttribute : Attribute
    {
        public string Name { get; }
        public FeatureNameAttribute(string name) => Name = name;
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class CheckPermissionAttribute : Attribute, IAuthorizationFilter
    {
        private readonly string? _feature;
        private readonly string _action; /* "view", "create", "delete", "activate" */
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public CheckPermissionAttribute(string feature, string action)
        {
            _feature = feature;
            _action = action.ToLower();
        }

        public CheckPermissionAttribute(string action)
        {
            _action = action.ToLower();
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var featureName = ResolveFeatureName(context);

            var user = context.HttpContext.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                throw new AppException("Usuário não autenticado", 401);
            }

            var permissionsClaim = user.FindFirst("permissions")?.Value;
            if (string.IsNullOrEmpty(permissionsClaim))
            {
                throw new AppException($"Sem permissão para {_action} em {featureName}", 403);
            }

            var permissions = JsonSerializer.Deserialize<List<PermissionClaim>>(permissionsClaim, _jsonOptions);
            var perm = permissions?.Find(p => p.Feature.Equals(featureName, StringComparison.OrdinalIgnoreCase));

            if (perm == null)
            {
                throw new AppException($"Sem permissão para {_action} em {featureName}", 403);
            }

            bool hasAccess = _action switch
            {
                "create" => perm.Create,
                "view" => perm.View,
                "delete" => perm.Delete,
                "activate" => perm.Activate,
                _ => false
            };

            if (!hasAccess)
            {
                throw new AppException($"Sem permissão para {_action} em {featureName}", 403);
            }
        }

        private string ResolveFeatureName(AuthorizationFilterContext context)
        {
            if (!string.IsNullOrEmpty(_feature))
            {
                return _feature;
            }

            return ResolveFeatureNameFromMetadata(context);
        }

        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
        private static string ResolveFeatureNameFromMetadata(AuthorizationFilterContext context)
        {
            var featureAttr = context.ActionDescriptor.EndpointMetadata
                .OfType<FeatureNameAttribute>()
                .FirstOrDefault();
            var name = featureAttr?.Name;

            if (string.IsNullOrEmpty(name))
            {
                throw new AppException("Feature name is not configured for this endpoint.", 500);
            }

            return name;
        }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class AuthorizeAdminAttribute : Attribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                throw new AppException("Usuário não autenticado", 401);
            }

            var roleId = user.FindFirst("roleId")?.Value;
            if (roleId != "administrator")
            {
                throw new AppException("Apenas administradores podem acessar este recurso", 403);
            }
        }
    }
}
