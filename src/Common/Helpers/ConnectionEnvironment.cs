using System;
using System.Text.RegularExpressions;

namespace ServiceBusExplorer.Helpers
{
    /// <summary>
    /// The environment a connection points at. Untagged and Prod both get the strict
    /// confirmations, so a missing or unreadable tag can never weaken a safety check.
    /// </summary>
    public enum ConnectionEnvironment
    {
        Untagged,
        Dev,
        Test,
        Staging,
        Prod
    }

    public static class ConnectionEnvironmentHelper
    {
        // Stored as appSettings so it works in every ConfigFileUse mode without a new config section.
        const string SettingKeyPrefix = "connectionEnvironment:";

        // A "prod", "prd" or "production" token, or a name ending in "prod" (e.g. contosoprod).
        // "product-catalog" does not match.
        static readonly Regex ProdNamePattern = new Regex(
            @"(?<![a-z])(prod|prd|production)(?![a-z])|prod$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool RequiresStrictConfirmation(ConnectionEnvironment environment)
        {
            return environment != ConnectionEnvironment.Dev &&
                   environment != ConnectionEnvironment.Test &&
                   environment != ConnectionEnvironment.Staging;
        }

        public static ConnectionEnvironment GetEnvironment(TwoFilesConfiguration configuration, string connectionKey)
        {
            if (configuration == null || string.IsNullOrWhiteSpace(connectionKey))
            {
                return ConnectionEnvironment.Untagged;
            }

            // Parse the effective value here rather than with GetEnumValue: in BothConfig mode that falls back
            // to the application config when the user value is unreadable, which could weaken the tag.
            // IsDefined rejects numbers such as "42", which Enum.TryParse accepts.
            var value = configuration.GetStringValue(SettingKeyPrefix + connectionKey);

            return Enum.TryParse(value, out ConnectionEnvironment environment) &&
                   Enum.IsDefined(typeof(ConnectionEnvironment), environment)
                ? environment
                : ConnectionEnvironment.Untagged;
        }

        public static void SetEnvironment(TwoFilesConfiguration configuration, string connectionKey,
            ConnectionEnvironment environment)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (string.IsNullOrWhiteSpace(connectionKey))
            {
                throw new ArgumentException("The connection key cannot be empty.", nameof(connectionKey));
            }

            configuration.SetValue(SettingKeyPrefix + connectionKey, environment.ToString());
            configuration.Save();
        }

        /// <summary>
        /// Suggests a tag for a newly saved connection from its namespace name. Only ever
        /// suggests Prod; anything else stays Untagged, which is also strict.
        /// </summary>
        public static ConnectionEnvironment SuggestEnvironment(string namespaceName)
        {
            return !string.IsNullOrWhiteSpace(namespaceName) && ProdNamePattern.IsMatch(namespaceName)
                ? ConnectionEnvironment.Prod
                : ConnectionEnvironment.Untagged;
        }
    }
}
