using Daisi.Protos.V1;
using Daisi.SDK.Models;
using Grpc.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Channels;

namespace Daisi.Orc.Grpc.CommandServices.Interfaces
{
    public interface IOrcCommandHandler
    {
        ServerCallContext CallContext { get; set; }
        Task HandleAsync(string hostId, Command command, ChannelWriter<Command> responseQueue, CancellationToken cancellationToken = default);
    }
}
