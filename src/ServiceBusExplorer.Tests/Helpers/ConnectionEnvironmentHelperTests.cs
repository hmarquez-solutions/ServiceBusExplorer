using System;
using System.IO;

using FluentAssertions;
using ServiceBusExplorer.Helpers;
using Xunit;

namespace ServiceBusExplorer.Tests.Helpers
{
    public sealed class ConnectionEnvironmentHelperTests : IDisposable
    {
        readonly string userConfigDirectory;
        readonly string userConfigFilePath;

        public ConnectionEnvironmentHelperTests()
        {
            userConfigDirectory = Path.Combine(Path.GetTempPath(), "sbe-env-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(userConfigDirectory);
            userConfigFilePath = Path.Combine(userConfigDirectory, "UserSettings.config");
        }

        public void Dispose()
        {
            if (Directory.Exists(userConfigDirectory))
            {
                Directory.Delete(userConfigDirectory, recursive: true);
            }
        }

        TwoFilesConfiguration OpenConfiguration()
        {
            return TwoFilesConfiguration.Create(userConfigFilePath, ConfigFileUse.UserConfig);
        }

        [Theory]
        [InlineData(ConnectionEnvironment.Untagged, true)]
        [InlineData(ConnectionEnvironment.Prod, true)]
        [InlineData(ConnectionEnvironment.Staging, false)]
        [InlineData(ConnectionEnvironment.Test, false)]
        [InlineData(ConnectionEnvironment.Dev, false)]
        [InlineData((ConnectionEnvironment)42, true)]
        public void RequiresStrictConfirmation_OnlyRelaxesForNonProdTags(ConnectionEnvironment environment, bool expected)
        {
            ConnectionEnvironmentHelper.RequiresStrictConfirmation(environment).Should().Be(expected);
        }

        [Theory]
        [InlineData("contoso-orders-prod", ConnectionEnvironment.Prod)]
        [InlineData("contosoprod", ConnectionEnvironment.Prod)]
        [InlineData("orders-prd-eastus", ConnectionEnvironment.Prod)]
        [InlineData("Production-Bus", ConnectionEnvironment.Prod)]
        [InlineData("orders_prod2", ConnectionEnvironment.Prod)]
        [InlineData("product-catalog", ConnectionEnvironment.Untagged)]
        [InlineData("contoso-orders-dev", ConnectionEnvironment.Untagged)]
        [InlineData("", ConnectionEnvironment.Untagged)]
        [InlineData(null, ConnectionEnvironment.Untagged)]
        public void SuggestEnvironment_OnlySuggestsProdFromTheName(string namespaceName, ConnectionEnvironment expected)
        {
            ConnectionEnvironmentHelper.SuggestEnvironment(namespaceName).Should().Be(expected);
        }

        [Fact]
        public void GetEnvironment_ReturnsUntaggedWhenNothingIsStored()
        {
            ConnectionEnvironmentHelper.GetEnvironment(OpenConfiguration(), "orders").Should().Be(ConnectionEnvironment.Untagged);
            ConnectionEnvironmentHelper.GetEnvironment(OpenConfiguration(), null).Should().Be(ConnectionEnvironment.Untagged);
            ConnectionEnvironmentHelper.GetEnvironment(null, "orders").Should().Be(ConnectionEnvironment.Untagged);
        }

        [Fact]
        public void SetEnvironment_PersistsPerConnectionKey()
        {
            ConnectionEnvironmentHelper.SetEnvironment(OpenConfiguration(), "orders", ConnectionEnvironment.Dev);
            ConnectionEnvironmentHelper.SetEnvironment(OpenConfiguration(), "payments", ConnectionEnvironment.Prod);

            ConnectionEnvironmentHelper.GetEnvironment(OpenConfiguration(), "orders").Should().Be(ConnectionEnvironment.Dev);
            ConnectionEnvironmentHelper.GetEnvironment(OpenConfiguration(), "payments").Should().Be(ConnectionEnvironment.Prod);

            ConnectionEnvironmentHelper.SetEnvironment(OpenConfiguration(), "orders", ConnectionEnvironment.Untagged);
            ConnectionEnvironmentHelper.GetEnvironment(OpenConfiguration(), "orders").Should().Be(ConnectionEnvironment.Untagged);
        }

        [Theory]
        [InlineData("42")]
        [InlineData("prod")]
        [InlineData("not-an-environment")]
        public void GetEnvironment_TreatsUnreadableValuesAsUntagged(string storedValue)
        {
            var configuration = OpenConfiguration();
            configuration.SetValue("connectionEnvironment:orders", storedValue);
            configuration.Save();

            ConnectionEnvironmentHelper.GetEnvironment(OpenConfiguration(), "orders").Should().Be(ConnectionEnvironment.Untagged);
        }
    }
}
