using System.Collections.Concurrent;
using System.Threading.Channels;

namespace EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator;

internal sealed class EmailEmulatorEventHub
{
    private readonly ConcurrentDictionary<Guid, Channel<EmailEmulatorEvent>> _subscribers = [];

    internal EmailEmulatorEventSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<EmailEmulatorEvent>(
            new BoundedChannelOptions(32)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
        _subscribers[id] = channel;
        return new EmailEmulatorEventSubscription(
            channel.Reader,
            () =>
            {
                if (_subscribers.TryRemove(id, out var removed))
                {
                    removed.Writer.TryComplete();
                }
            });
    }

    internal void Publish(EmailEmulatorEvent notification)
    {
        foreach (var subscriber in _subscribers.Values)
        {
            subscriber.Writer.TryWrite(notification);
        }
    }
}

internal sealed class EmailEmulatorEventSubscription(
    ChannelReader<EmailEmulatorEvent> reader,
    Action unsubscribe)
    : IDisposable
{
    private readonly Action _unsubscribe = unsubscribe;

    internal ChannelReader<EmailEmulatorEvent> Reader { get; } = reader;

    public void Dispose() => _unsubscribe();
}

internal sealed record EmailEmulatorEvent(
    string Kind,
    Guid? OperationId,
    int? Count)
{
    internal static EmailEmulatorEvent MessageCreated(Guid operationId) =>
        new("message-created", operationId, null);

    internal static EmailEmulatorEvent MessageDeleted(Guid operationId) =>
        new("message-deleted", operationId, null);

    internal static EmailEmulatorEvent AllMessagesDeleted(int count) =>
        new("all-messages-deleted", null, count);
}
