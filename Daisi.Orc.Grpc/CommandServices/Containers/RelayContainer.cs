using Daisi.Protos.V1;
using Grpc.Core;

namespace Daisi.Orc.Grpc.CommandServices.Relays
{
    public class RelayContainer
    {
        /// <summary>
        /// Key is Session ID. Relay information for connected Consumer.
        /// </summary>
        public static Dictionary<string, Relay> Relays { get; set; } = new Dictionary<string, Relay>();


    }

    public class Relay
    {
        public string HostId { get; set; }
        public string SessionId { get; set; }

        public IServerStreamWriter<SendInferenceResponse> AppResponseStream { get; set; }

    }
}
