using Grpc.Core;
using System.Threading.Channels;

namespace Daisi.Orc.Tests.Fakes
{
    /// <summary>
    /// Fake IServerStreamWriter that captures all written messages via a Channel.
    /// Use WrittenChannel.Reader to consume written messages in tests.
    /// </summary>
    public class FakeServerStreamWriter<T> : IServerStreamWriter<T>
    {
        public Channel<T> WrittenChannel { get; } = Channel.CreateUnbounded<T>();
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message)
        {
            WrittenChannel.Writer.TryWrite(message);
            return Task.CompletedTask;
        }
    }
}
