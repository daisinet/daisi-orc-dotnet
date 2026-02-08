using Daisi.Protos.V1;
using Grpc.Core;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    public class AppCommandsRPC : AppCommandsProto.AppCommandsProtoBase
    {
        public override Task Open(IAsyncStreamReader<Command> requestStream, IServerStreamWriter<Command> responseStream, ServerCallContext context)
        {
            return base.Open(requestStream, responseStream, context);
        }
    }
}
