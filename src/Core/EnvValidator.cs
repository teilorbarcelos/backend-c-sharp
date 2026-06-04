using System.Diagnostics.CodeAnalysis;

namespace MageBackend.Core
{
    /*
     * Wrapper defensivo para leitura obrigatória de variáveis de ambiente.
     * Marcado [ExcludeFromCodeCoverage] porque os throws aqui só disparam em
     * ambientes mal configurados (que o pipeline de teste nunca produz) e
     * representam guard rails de boot, não lógica de negócio.
     */
    [ExcludeFromCodeCoverage]
    internal static class EnvValidator
    {
        public static string Required(string name)
        {
            var value = System.Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new System.InvalidOperationException($"{name} environment variable is not set.");
            }
            return value;
        }

        public static string RequiredAny(params string[] names)
        {
            foreach (var name in names)
            {
                var value = System.Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            throw new System.InvalidOperationException($"One of [{string.Join(", ", names)}] environment variables must be set.");
        }
    }
}
