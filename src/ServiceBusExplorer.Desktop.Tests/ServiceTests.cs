using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using ServiceBusExplorer.Desktop.Models;
using ServiceBusExplorer.Desktop.Services;
using Xunit;

namespace ServiceBusExplorer.Desktop.Tests;

public class ConnectionTargetTests
{
    [Fact]
    public void ConnectionString_IsAcceptedWithExtraTokensTheWinFormsAppWrites()
    {
        const string connectionString =
            "Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=abc=;TransportType=Amqp";

        ConnectionTarget.TryFromConnectionString(connectionString, out var target, out var error).Should().BeTrue(error);

        target!.FullyQualifiedNamespace.Should().Be("contoso.servicebus.windows.net");
        target.NamespaceName.Should().Be("contoso");
        target.UsesEntra.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a connection string")]
    [InlineData("Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=abc=;EntityPath=orders")]
    public void ConnectionString_RejectsMissingMalformedOrEntityScoped(string connectionString)
    {
        ConnectionTarget.TryFromConnectionString(connectionString, out var target, out var error).Should().BeFalse();
        target.Should().BeNull();
        error.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("contoso", "contoso.servicebus.windows.net")]
    [InlineData("  Contoso.servicebus.windows.net  ", "contoso.servicebus.windows.net")]
    [InlineData("sb://contoso.servicebus.windows.net/", "contoso.servicebus.windows.net")]
    [InlineData("https://contoso.servicebus.usgovcloudapi.net", "contoso.servicebus.usgovcloudapi.net")]
    public void Namespace_IsNormalizedToAHost(string input, string expected)
    {
        ConnectionTarget.TryFromNamespace(input, " tenant-1 ", out var target, out _).Should().BeTrue();

        target!.FullyQualifiedNamespace.Should().Be(expected);
        target.TenantId.Should().Be("tenant-1");
        target.UsesEntra.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("contoso.servicebus.windows.net/orders")]
    [InlineData("two words")]
    public void Namespace_RejectsNonHosts(string input)
    {
        ConnectionTarget.TryFromNamespace(input, null, out var target, out _).Should().BeFalse();
        target.Should().BeNull();
    }
}

public class EntityTreeFilterTests
{
    static readonly IReadOnlyList<EntityInfo> Entities = new[]
    {
        TestData.Queue("orders-intake", deadLetter: 3),
        TestData.Queue("email-outbound"),
        TestData.Topic("order-events",
            TestData.Subscription("order-events", "billing", deadLetter: 4),
            TestData.Subscription("order-events", "shipping"))
    };

    [Fact]
    public void EmptyFilter_KeepsEverything()
    {
        EntityTreeFilter.Apply(Entities, "", onlyWithDeadLetters: false).Should().BeEquivalentTo(Entities);
    }

    [Fact]
    public void TopicNameMatch_KeepsAllItsSubscriptions()
    {
        var result = EntityTreeFilter.Apply(Entities, "EVENTS", onlyWithDeadLetters: false);

        result.Should().ContainSingle().Which.Subscriptions.Select(s => s.Name).Should().Equal("billing", "shipping");
    }

    [Fact]
    public void SubscriptionMatch_KeepsTheTopicButOnlyMatchingSubscriptions()
    {
        var result = EntityTreeFilter.Apply(Entities, "ship", onlyWithDeadLetters: false);

        result.Should().ContainSingle().Which.Subscriptions.Select(s => s.Name).Should().Equal("shipping");
    }

    [Fact]
    public void OnlyWithDeadLetters_KeepsEntitiesThatHaveThem()
    {
        var result = EntityTreeFilter.Apply(Entities, "", onlyWithDeadLetters: true);

        result.Select(e => e.Name).Should().Equal("orders-intake", "order-events");
        result[1].Subscriptions.Select(s => s.Name).Should().Equal("billing");
    }
}

public class MessageBodyFormatterTests
{
    [Fact]
    public void Json_IsPrettyPrinted()
    {
        var formatted = MessageBodyFormatter.Format(Encoding.UTF8.GetBytes("{\"orderId\":42,\"lines\":[1,2]}"));

        formatted.Kind.Should().Be(BodyKind.Json);
        formatted.Text.Should().Contain("\"orderId\": 42").And.Contain(System.Environment.NewLine);
    }

    [Fact]
    public void BracedTextThatIsNotJson_StaysText()
    {
        MessageBodyFormatter.Format(Encoding.UTF8.GetBytes("{not json")).Should().Be(new FormattedBody(BodyKind.Text, "{not json"));
    }

    [Fact]
    public void Empty_IsEmpty()
    {
        MessageBodyFormatter.Format(Array.Empty<byte>()).Kind.Should().Be(BodyKind.Empty);
    }

    [Fact]
    public void InvalidUtf8_IsShownAsHex()
    {
        var formatted = MessageBodyFormatter.Format(new byte[] { 0xFF, 0xFE, 0x00, 0x81 });

        formatted.Kind.Should().Be(BodyKind.Binary);
        formatted.Text.Should().StartWith("00000000  fffe0081");
    }

    [Fact]
    public void ControlBytesThatAreValidUtf8_AreShownAsHex()
    {
        MessageBodyFormatter.Format(new byte[] { 0x00, 0x01, 0x02 }).Kind.Should().Be(BodyKind.Binary);
        MessageBodyFormatter.Format(Encoding.UTF8.GetBytes("line one\r\n\tline two")).Kind.Should().Be(BodyKind.Text);
    }

    [Fact]
    public void LegacySdkString_IsUnwrapped()
    {
        // This is how WindowsAzure.ServiceBus serializes new BrokeredMessage("...") bodies.
        using var stream = new MemoryStream();
        using (var writer = XmlDictionaryWriter.CreateBinaryWriter(stream))
        {
            new DataContractSerializer(typeof(string)).WriteObject(writer, "{\"legacy\":true}");
        }

        var formatted = MessageBodyFormatter.Format(stream.ToArray());

        formatted.Kind.Should().Be(BodyKind.Json);
        formatted.Text.Should().Contain("\"legacy\": true");
    }
}

public class ServiceBusNamespaceClientTests
{
    [Fact]
    public void ToSnapshot_CopiesTheFieldsTheInspectorShows()
    {
        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("hello"),
            messageId: "m-1",
            subject: "OrderCreated",
            contentType: "text/plain",
            correlationId: "c-1",
            sessionId: "s-1",
            properties: new Dictionary<string, object>
            {
                ["tenant"] = "east",
                ["DeadLetterReason"] = "MaxDeliveryCountExceeded",
                ["DeadLetterErrorDescription"] = "Gave up"
            },
            deliveryCount: 10,
            sequenceNumber: 48213,
            deadLetterSource: "orders",
            serviceBusMessageState: ServiceBusMessageState.Scheduled);

        var snapshot = ServiceBusNamespaceClient.ToSnapshot(received);

        snapshot.SequenceNumber.Should().Be(48213);
        snapshot.MessageId.Should().Be("m-1");
        snapshot.Subject.Should().Be("OrderCreated");
        snapshot.SessionId.Should().Be("s-1");
        snapshot.DeliveryCount.Should().Be(10);
        snapshot.State.Should().Be("Scheduled");
        snapshot.DeadLetterReason.Should().Be("MaxDeliveryCountExceeded");
        snapshot.DeadLetterErrorDescription.Should().Be("Gave up");
        snapshot.DeadLetterSource.Should().Be("orders");
        snapshot.ApplicationProperties.Should().ContainKey("tenant");
        Encoding.UTF8.GetString(snapshot.Body).Should().Be("hello");
    }
}
