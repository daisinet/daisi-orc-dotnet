using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Services;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Orc.Grpc.Background;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Orc.Grpc.CommandServices.Handlers;
using Daisi.Orc.Grpc.CommandServices.HostCommandHandlers;
using Daisi.Orc.Grpc.RPCServices.V1;
using Daisi.SDK.Extensions;
using Daisi.SDK.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using System.Security.Claims;

public partial class Program
{
    public static WebApplication App { get; private set; }
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddRoutingCore();

        builder.Services
                .AddAuthentication(DaisiAuthenticationOptions.DefaultScheme)
                .AddScheme<DaisiAuthenticationOptions, DaisiAuthenticationHandler>(DaisiAuthenticationOptions.DefaultScheme, options => { });
        builder.Services.AddAuthorization();

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddOrcClientKeyProvider();
        builder.Services.AddDaisiClients();
        builder.Services.AddSingleton<Cosmo>();
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<OrcService>();

        builder.Services.AddTransient<HeartbeatRequestCommandHandler>();
        builder.Services.AddTransient<SessionIncomingQueueHandler>();
        builder.Services.AddTransient<EnvironmentRequestCommandHandler>();
        builder.Services.AddTransient<InferenceCommandHandler>();

        // Add services to the container.
        builder.Services.AddGrpc(options =>
        {
#if DEBUG
            options.EnableDetailedErrors = true;
#endif
        });

        builder.Services.AddHostedService<SessionCleanupService>();

        var app = App = builder.Build();

        // Configure the HTTP request pipeline.
        app.MapGrpcService<AuthRPC>();
        app.MapGrpcService<AccountsRPC>();
        app.MapGrpcService<DappsRPC>();
        app.MapGrpcService<HostCommandsRPC>();
        app.MapGrpcService<HostsRPC>();
        app.MapGrpcService<ModelsRPC>();
        app.MapGrpcService<NetworksRPC>();
        app.MapGrpcService<OrcsRPC>();
        app.MapGrpcService<RelayInferenceRPC>();
        app.MapGrpcService<RelaySettingsRPC>();
        app.MapGrpcService<SessionsRPC>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        app.MapGet("/", () => "Communication with DAISI endpoints must be made through the DAISI SDK. To download a DAISI host application and/or the SDK, go to https://daisi.ai");

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.Lifetime.ApplicationStarted.Register(async () =>
        {
            var scoped = App.Services.CreateScope();

            Cosmo cosmo = scoped.ServiceProvider.GetService<Cosmo>()!;

            var orcId = App.Configuration.GetValue<string>("Daisi:OrcId")!;
            var accountId = App.Configuration.GetValue<string>("Daisi:AccountId")!;
            await cosmo.PatchOrcStatusAsync(orcId, Daisi.Protos.V1.OrcStatus.Online, accountId);
        });

        app.Lifetime.ApplicationStopping.Register(async () =>
        {
            var scoped = App.Services.CreateScope();

            Cosmo cosmo = scoped.ServiceProvider.GetService<Cosmo>()!;
            await HostContainer.UnregisterAllAsync(cosmo, App.Configuration);

            var orcId = App.Configuration.GetValue<string>("Daisi:OrcId")!;
            var accountId = App.Configuration.GetValue<string>("Daisi:AccountId")!;
            await cosmo.PatchOrcStatusAsync(orcId, Daisi.Protos.V1.OrcStatus.Offline, accountId);
        });

        app.Run();
    }
}

#if DEBUG

#endif
