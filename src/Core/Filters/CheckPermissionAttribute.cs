using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MageBackend.Core.Middleware;
using MageBackend.Infrastructure.Auth;

namespace MageBackend.Core.Filters
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class CheckPermissionAttribute : Attribute, IAuthorizationFilter
    {
        private readonly string _feature;
        private readonly string _action; /* "view", "create", "delete", "activate" */

        public CheckPermissionAttribute(string feature, string action)
        {
            _feature = feature;
            _action = action.ToLower();
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                throw new AppException("Usuário não autenticado", 401);
            }

            var permissionsClaim = user.FindFirst("permissions")?.Value;
            if (string.IsNullOrEmpty(permissionsClaim))
            {
                throw new AppException($"Sem permissão para {_action} em {_feature}", 403);
            }

            var permissions = JsonSerializer.Deserialize<List<PermissionClaim>>(permissionsClaim, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var perm = permissions?.FirstOrDefault(p => p.Feature.Equals(_feature, StringComparison.OrdinalIgnoreCase));

            if (perm == null)
            {
                throw new AppException($"Sem permissão para {_action} em {_feature}", 403);
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
                throw new AppException($"Sem permissão para {_action} em {_feature}", 403);
            }
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
