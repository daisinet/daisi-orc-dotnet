using Daisi.Orc.Grpc.RPCServices.V1;
using Daisi.Protos.V1;
using System.Collections.Concurrent;

namespace Daisi.Orc.Grpc.CommandServices.Containers
{
    public class SessionContainer
    {
        private static ConcurrentDictionary<string, DaisiSession> SessionsById { get; set; } = new ConcurrentDictionary<string, DaisiSession>();

        public static bool TryGet(string sessionId, out DaisiSession session)
        {
            if (SessionsById.TryGetValue(sessionId, out DaisiSession storedSession))
            {
                if (storedSession is not null)
                {
                    storedSession.ResetInteraction();
                }
                session = storedSession!;
                return true;
            }

            session = default!;
            return false;
        }

        public static void Add(DaisiSession session)
        {
            SessionsById.TryAdd(session.Id, session);
        }

        public static List<DaisiSession> GetExpired()
        {
            return SessionsById.Values.Where(s => s.IsExpired).ToList();
        }

        internal static List<DaisiSession> CleanUpExpired()
        {
            var expired = GetExpired();
            for (int i = 0; i < expired.Count; i++)
            {
                Close(expired[i].Id);
                i--;
            }
            return expired;
        }

        internal static void Close(string sessionId)
        {
            if (SessionsById.TryRemove(sessionId, out var removedSession))
                HostContainer.SendSessionCloseRequest(removedSession.CreateResponse.Host.Id, sessionId);
        }

        internal static bool TryGetExistingSession(string? clientKey, string id, out DaisiSession existingSession)
        {
            var session = SessionsById.Values.FirstOrDefault(s => s.CreateClientKey == clientKey && s.CreateResponse.Host.Id == id);
            existingSession = session;
            return session != null;
        }
    }

    public class DaisiSession
    {
        public string Id { get; set; }
        public DateTime DateCreated { get; } = DateTime.UtcNow;
        public DateTime DateLastInteraction { get; private set; } = DateTime.UtcNow;
        public bool IsExpired => (DateTime.UtcNow - DateLastInteraction).TotalSeconds > 600;
        public string CreateClientKey { get; set; }
        public string ClaimClientKey { get; set; }

        public CreateSessionRequest CreateRequest { get; set; }
        public CreateSessionResponse CreateResponse { get; set; }

        public ClaimSessionRequest ClaimRequest { get; set; }
        public ClaimSessionResponse ClaimResponse { get; set; }

        public CloseSessionRequest CloseRequest { get; set; }
        public CloseSessionResponse CloseResponse { get; set; }

        public void ResetInteraction()
        {
            DateLastInteraction = DateTime.UtcNow;
        }

    }
}
