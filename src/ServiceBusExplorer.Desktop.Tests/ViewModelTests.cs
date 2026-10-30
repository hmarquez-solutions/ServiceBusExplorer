using FluentAssertions;
using ServiceBusExplorer.Desktop.Models;
using ServiceBusExplorer.Desktop.ViewModels;
using Xunit;

namespace ServiceBusExplorer.Desktop.Tests;

public class MainWindowViewModelTests
{
    readonly FakeNamespaceClient fake = new()
    {
        Entities = new[]
        {
            TestData.Queue("orders-intake", active: 1284, deadLetter: 37),
            TestData.Topic("order-events", TestData.Subscription("order-events", "billing", deadLetter: 4))
        },
        Messages = (_, _) => new[] { TestData.Message(1), TestData.Message(2) }
    };

    MainWindowViewModel CreateConnectedReadyViewModel(ConnectionEnvironment environment = ConnectionEnvironment.Prod)
    {
        var viewModel = new MainWindowViewModel(_ => fake);
        viewModel.Connect.ConnectionString =
            "Endpoint=sb://contoso-orders.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=abc=";
        viewModel.Connect.Environment = environment;
        return viewModel;
    }

    static EntityNodeViewModel FindNode(MainWindowViewModel viewModel, string title) =>
        viewModel.Nodes.SelectMany(group => group.Children.Concat(group.Children.SelectMany(child => child.Children)))
            .Single(node => node.Title == title);

    [Fact]
    public async Task Connect_BuildsTheNavigatorAndShowsTheEnvironment()
    {
        var viewModel = CreateConnectedReadyViewModel();

        await viewModel.ConnectCommand.ExecuteAsync(null);

        viewModel.IsConnected.Should().BeTrue();
        viewModel.WindowTitle.Should().Be("[PROD] contoso-orders - Service Bus Explorer");
        viewModel.EnvironmentColor.Should().Be("#C42B1C");
        viewModel.Nodes.Select(group => group.Title).Should().Equal("Queues", "Topics");
        viewModel.Nodes[0].Children.Single().DeadLetterText.Should().Be("37");
        viewModel.Nodes[1].Children.Single().Children.Single().Title.Should().Be("billing");
    }

    [Fact]
    public async Task Connect_WithBadInput_DoesNotCreateAClient()
    {
        var created = false;
        var viewModel = new MainWindowViewModel(_ => { created = true; return fake; });

        await viewModel.ConnectCommand.ExecuteAsync(null);

        created.Should().BeFalse();
        viewModel.IsConnected.Should().BeFalse();
        viewModel.Connect.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Connect_Failure_ExplainsAccessDeniedAndDisposesTheClient()
    {
        fake.GetEntitiesFailure = new UnauthorizedAccessException("401");
        var viewModel = CreateConnectedReadyViewModel();

        await viewModel.ConnectCommand.ExecuteAsync(null);

        viewModel.IsConnected.Should().BeFalse();
        viewModel.Connect.Error.Should().Contain("Manage rights");
        fake.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task SelectingAnEntity_PeeksItsActiveMessages()
    {
        var viewModel = CreateConnectedReadyViewModel();
        await viewModel.ConnectCommand.ExecuteAsync(null);

        viewModel.SelectedNode = FindNode(viewModel, "orders-intake");

        viewModel.CurrentEntity!.Entity.Name.Should().Be("orders-intake");
        fake.Peeks.Should().ContainSingle().Which.Should().Be(("orders-intake", MessageSource.Active, 100));
        viewModel.CurrentEntity.Messages.Should().HaveCount(2);
        viewModel.CurrentEntity.SelectedMessage!.SequenceNumber.Should().Be(1);
    }

    [Fact]
    public async Task SelectingAGroupHeader_KeepsTheCurrentEntity()
    {
        var viewModel = CreateConnectedReadyViewModel();
        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.SelectedNode = FindNode(viewModel, "orders-intake");
        var current = viewModel.CurrentEntity;

        viewModel.SelectedNode = viewModel.Nodes[1];
        viewModel.FilterText = "bill";

        viewModel.CurrentEntity.Should().BeSameAs(current);
        FindNode(viewModel, "billing").Should().NotBeNull();
    }

    [Fact]
    public async Task Refresh_UpdatesCountsAndKeepsLoadedMessages()
    {
        var viewModel = CreateConnectedReadyViewModel();
        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.SelectedNode = FindNode(viewModel, "orders-intake");

        fake.Entities = new[] { TestData.Queue("orders-intake", active: 1300, deadLetter: 40) };
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.CurrentEntity!.DeadLetterText.Should().Be("40");
        viewModel.CurrentEntity.Messages.Should().HaveCount(2);
        fake.Peeks.Should().HaveCount(1);
    }

    [Fact]
    public async Task Connect_UsesTheEnvironmentPickedWhenConnectWasPressed()
    {
        var viewModel = CreateConnectedReadyViewModel(ConnectionEnvironment.Prod);
        fake.EntitiesGate = new TaskCompletionSource();

        var connecting = viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.Connect.Environment = ConnectionEnvironment.Dev;
        fake.EntitiesGate.SetResult();
        await connecting;

        viewModel.EnvironmentLabel.Should().Be("PROD");
    }

    [Fact]
    public async Task RefreshThatFinishesAfterDisconnect_IsDropped()
    {
        var viewModel = CreateConnectedReadyViewModel();
        await viewModel.ConnectCommand.ExecuteAsync(null);
        fake.EntitiesGate = new TaskCompletionSource();

        var refreshing = viewModel.RefreshCommand.ExecuteAsync(null);
        await viewModel.DisconnectCommand.ExecuteAsync(null);
        fake.EntitiesGate.SetResult();
        await refreshing;

        viewModel.IsConnected.Should().BeFalse();
        viewModel.Nodes.Should().BeEmpty();
        viewModel.StatusText.Should().Be("Not connected");
    }

    [Fact]
    public async Task Disconnect_DisposesTheClientAndClearsTheExplorer()
    {
        var viewModel = CreateConnectedReadyViewModel();
        await viewModel.ConnectCommand.ExecuteAsync(null);

        await viewModel.DisconnectCommand.ExecuteAsync(null);

        fake.IsDisposed.Should().BeTrue();
        viewModel.IsConnected.Should().BeFalse();
        viewModel.Nodes.Should().BeEmpty();
        viewModel.CurrentEntity.Should().BeNull();
        viewModel.WindowTitle.Should().Be("Service Bus Explorer");
    }
}

public class EntityViewModelTests
{
    readonly FakeNamespaceClient fake = new()
    {
        Messages = (_, source) => source == MessageSource.DeadLetter
            ? new[] { TestData.Message(7, "MaxDeliveryCountExceeded") }
            : new[] { TestData.Message(1) }
    };

    [Fact]
    public async Task Topic_CannotBeBrowsed()
    {
        var viewModel = new EntityViewModel(TestData.Topic("order-events"), fake);

        await viewModel.LoadAsync();

        viewModel.CanBrowse.Should().BeFalse();
        viewModel.HasMessages.Should().BeFalse();
        viewModel.Notice.Should().Contain("subscriptions");
        fake.Peeks.Should().BeEmpty();
    }

    [Fact]
    public async Task SessionEntity_OnlyBrowsesItsDeadLetterQueue()
    {
        var viewModel = new EntityViewModel(TestData.Queue("invoices", requiresSession: true), fake);

        await viewModel.LoadAsync();
        viewModel.CanBrowse.Should().BeFalse();
        fake.Peeks.Should().BeEmpty();

        viewModel.ShowSourceCommand.Execute(MessageSource.DeadLetter);

        viewModel.CanBrowse.Should().BeTrue();
        fake.Peeks.Should().ContainSingle().Which.Source.Should().Be(MessageSource.DeadLetter);
    }

    [Fact]
    public void SwitchingToDeadLetter_PeeksTheDeadLetterQueue()
    {
        var viewModel = new EntityViewModel(TestData.Queue("orders"), fake);

        viewModel.ShowSourceCommand.Execute(MessageSource.DeadLetter);

        viewModel.IsDeadLetterSource.Should().BeTrue();
        viewModel.Messages.Single().DeadLetterReason.Should().Be("MaxDeliveryCountExceeded");
        viewModel.Notice.Should().Be("1 dead-lettered messages peeked · nothing was removed");
    }

    [Fact]
    public void Scheduled_SaysWhatWasSearched()
    {
        var viewModel = new EntityViewModel(TestData.Queue("orders"), fake);

        viewModel.ShowSourceCommand.Execute(MessageSource.Scheduled);

        viewModel.TimeColumnHeader.Should().Be("Scheduled for");
        viewModel.Notice.Should().StartWith("1 scheduled among the first 100 messages peeked");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(250, 250)]
    [InlineData(50000, EntityViewModel.MaxPeekCount)]
    public void PeekCount_IsClamped(int requested, int expected)
    {
        var viewModel = new EntityViewModel(TestData.Queue("orders"), fake) { PeekCount = requested };

        viewModel.PeekCount.Should().Be(expected);
    }

    [Fact]
    public async Task PeekFailure_IsShown()
    {
        var failing = new FakeNamespaceClient { Messages = (_, _) => throw new InvalidOperationException("boom") };
        var viewModel = new EntityViewModel(TestData.Queue("orders"), failing);

        await viewModel.LoadAsync();

        viewModel.Error.Should().Be("boom");
        viewModel.IsLoading.Should().BeFalse();
    }
}

public class MessageViewModelTests
{
    [Fact]
    public void SystemProperties_IncludeDeadLetterDetailsOnlyWhenDeadLettered()
    {
        var active = new MessageViewModel(TestData.Message(1));
        var deadLettered = new MessageViewModel(TestData.Message(2, "TTLExpiredException"));

        active.SystemProperties.Select(row => row.Name).Should().NotContain("DeadLetterReason");
        deadLettered.SystemProperties.Should().Contain(new PropertyRow("DeadLetterReason", "TTLExpiredException"));
        active.SystemProperties.Should().Contain(new PropertyRow("TimeToLive", "Never expires"));
    }

    [Fact]
    public void Body_IsFormattedAndSummarized()
    {
        var message = new MessageViewModel(TestData.Message(1, body: "{\"id\":1}"));

        message.BodySummary.Should().Be("JSON · 8 B · application/json");
        message.BodyText.Should().Contain("\"id\": 1");
        message.ApplicationProperties.Should().Equal(new PropertyRow("tenant", "east"));
    }
}
