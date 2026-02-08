using Daisi.SDK.Interfaces.Authentication;

namespace Daisi.Orc.Grpc.Authentication
{
    public static class OrcClientKeyProviderExtensions
    {
        public static IServiceCollection AddOrcClientKeyProvider(this IServiceCollection serviceProvider)
        {
            serviceProvider.AddSingleton<IClientKeyProvider, OrcClientKeyProvider>();
            return serviceProvider;
        }
    }
    public class OrcClientKeyProvider : IClientKeyProvider
    {
        public string GetClientKey()
        {
#if DEBUG
            return "client-debug";
#endif

            return string.Empty;


        }
    }
}
