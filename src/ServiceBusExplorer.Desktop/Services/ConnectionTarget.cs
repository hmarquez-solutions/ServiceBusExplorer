using Azure.Messaging.ServiceBus;

namespace ServiceBusExplorer.Desktop.Services;

/// <summary>
/// What to connect to: a SAS connection string, or a namespace signed into with Entra ID.
/// </summary>
public sealed record ConnectionTarget(string FullyQualifiedNamespace, string? ConnectionString, string? TenantId)
{
    const string DefaultSuffix = ".servicebus.windows.net";

    public bool UsesEntra => ConnectionString is null;

    /// <summary>The first label of the host, e.g. "contoso" for contoso.servicebus.windows.net.</summary>
    public string NamespaceName => FullyQualifiedNamespace.Split('.')[0];

    public static bool TryFromConnectionString(string? connectionString, out ConnectionTarget? target, out string error)
    {
        target = null;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            error = "Enter a connection string.";
            return false;
        }

        ServiceBusConnectionStringProperties properties;
        try
        {
            properties = ServiceBusConnectionStringProperties.Parse(connectionString.Trim());
        }
        catch (FormatException ex)
        {
            error = $"That connection string isn't valid: {ex.Message}";
            return false;
        }

        if (string.IsNullOrEmpty(properties.FullyQualifiedNamespace))
        {
            error = "The connection string has no Endpoint.";
            return false;
        }

        // Listing entities needs the namespace, which an entity-scoped connection string can't reach.
        if (!string.IsNullOrEmpty(properties.EntityPath))
        {
            error = "Connection strings scoped to one entity (EntityPath=...) aren't supported yet. Use a namespace-level one.";
            return false;
        }

        target = new ConnectionTarget(properties.FullyQualifiedNamespace, connectionString.Trim(), TenantId: null);
        error = string.Empty;
        return true;
    }

    public static bool TryFromNamespace(string? namespaceName, string? tenantId, out ConnectionTarget? target, out string error)
    {
        target = null;
        var host = (namespaceName ?? string.Empty).Trim();

        foreach (var scheme in new[] { "sb://", "https://", "amqps://" })
        {
            if (host.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                host = host[scheme.Length..];
            }
        }

        host = host.TrimEnd('/');

        if (host.Length == 0 || host.Contains('/') || host.Contains(' '))
        {
            error = "Enter a namespace name, e.g. contoso or contoso.servicebus.windows.net.";
            return false;
        }

        if (!host.Contains('.'))
        {
            host += DefaultSuffix;
        }

        target = new ConnectionTarget(host.ToLowerInvariant(), ConnectionString: null,
            string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim());
        error = string.Empty;
        return true;
    }
}
