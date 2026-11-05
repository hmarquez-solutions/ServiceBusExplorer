using FluentAssertions;
using Microsoft.ServiceBus.Messaging;
using ServiceBusExplorer.Forms;
using ServiceBusExplorer.Helpers;
using Xunit;

using static ServiceBusExplorer.Tests.Forms.ConnectFormEntraTests;

namespace ServiceBusExplorer.Tests.Forms
{
    // None of these tests change a saved connection's tag, so nothing is written to the config file.
    public class ConnectFormEnvironmentTests
    {
        const string ControlNameServiceBusNamespace = "cboServiceBusNamespace";
        const string ControlNameEnvironment = "cboEnvironment";

        [Fact]
        public void NoSelection_EnvironmentIsUntaggedAndDisabled()
        {
            int itemCount = 0;
            object selectedItem = null;
            bool enabled = true;
            ConnectionEnvironment selectedEnvironment = ConnectionEnvironment.Prod;

            RunOnStaThread(() =>
            {
                ResetManualConnectionState();

                using (var form = new ConnectForm(new ServiceBusHelper((message, asynchronous) => { }),
                           ConfigFileUse.ApplicationConfig, null))
                {
                    var environmentPicker = GetComboBox(form, ControlNameEnvironment);
                    itemCount = environmentPicker.Items.Count;
                    selectedItem = environmentPicker.SelectedItem;
                    enabled = environmentPicker.Enabled;
                    selectedEnvironment = form.SelectedConnectionEnvironment;
                }
            });

            itemCount.Should().Be(5);
            selectedItem.Should().Be(ConnectionEnvironment.Untagged);
            enabled.Should().BeFalse();
            selectedEnvironment.Should().Be(ConnectionEnvironment.Untagged);
        }

        [Fact]
        public void UnsavedConnection_ReturnsTheTagPicked()
        {
            ConnectionEnvironment selectedEnvironment = ConnectionEnvironment.Untagged;
            bool enabled = false;

            RunOnStaThread(() =>
            {
                ResetManualConnectionState();

                using (var form = new ConnectForm(new ServiceBusHelper((message, asynchronous) => { }),
                           ConfigFileUse.ApplicationConfig, null))
                {
                    GetComboBox(form, ControlNameServiceBusNamespace).SelectedIndex = 1; // Enter connection string
                    var environmentPicker = GetComboBox(form, ControlNameEnvironment);
                    enabled = environmentPicker.Enabled;
                    environmentPicker.SelectedItem = ConnectionEnvironment.Dev;
                    selectedEnvironment = form.SelectedConnectionEnvironment;
                }
            });

            enabled.Should().BeTrue();
            selectedEnvironment.Should().Be(ConnectionEnvironment.Dev);
        }

        [Fact]
        public void SavedConnectionWithoutTag_ShowsUntagged()
        {
            const string savedEntryName = "Saved without environment tag";
            ConnectionEnvironment selectedEnvironment = ConnectionEnvironment.Prod;

            RunOnStaThread(() =>
            {
                ResetManualConnectionState();

                var helper = new ServiceBusHelper((message, asynchronous) => { });
                helper.ServiceBusNamespaces[savedEntryName] = new ServiceBusNamespace(
                    "sb://untagged.servicebus.windows.net/",
                    "untagged",
                    "tenant",
                    TransportType.NetMessaging,
                    null,
                    true);

                using (var form = new ConnectForm(helper, ConfigFileUse.ApplicationConfig, null))
                {
                    GetComboBox(form, ControlNameServiceBusNamespace).SelectedItem = savedEntryName;
                    selectedEnvironment = form.SelectedConnectionEnvironment;
                }
            });

            selectedEnvironment.Should().Be(ConnectionEnvironment.Untagged);
        }
    }
}
