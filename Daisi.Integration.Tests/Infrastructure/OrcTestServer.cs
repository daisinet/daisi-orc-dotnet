using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Grpc.Background;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text;
using System.Text.Json;

namespace Daisi.Integration.Tests.Infrastructure;

/// <summary>
/// Wraps WebApplicationFactory for the ORC server with test-specific overrides:
/// replaces Cosmo with IntegrationTestCosmo, disables background services,
/// and provides helpers for gRPC channel creation.
/// </summary>
public class OrcTestServer : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    public IntegrationTestCosmo TestCosmo { get; }

    public const string TestOrcId = "orc-test-1";
    public const string TestAccountId = "acct-test-1";
    public const string TestHostId = "host-test-1";
    public const string TestHostClientKey = "client-host-test-key";
    public const string TestUserClientKey = "client-user-test-key";
    public const string TestUserId = "user-test-1";

    public OrcTestServer()
    {
        TestCosmo = new IntegrationTestCosmo();

        // Seed the required test data
        TestCosmo.SeedOrc(TestOrcId, TestAccountId);
        TestCosmo.SeedHost(TestHostId, TestAccountId);
        TestCosmo.SeedHostKey(TestHostClientKey, TestHostId, TestAccountId);
        TestCosmo.SeedUserKey(TestUserClientKey, TestUserId, TestAccountId);
        TestCosmo.SeedAccount(TestAccountId, balance: 100000);

        // Seed a default model
        TestCosmo.Models.Add(new Daisi.Orc.Core.Data.Models.DaisiModel
        {
            Name = "Gemma 3 4B Q8 XL",
            FileName = "gemma-3-4b-it-UD-Q8_K_XL.gguf",
            Url = "",
            IsMultiModal = false,
            IsDefault = true,
            Enabled = true,
            LoadAtStartup = false,
            HasReasoning = false,
            Backend = new Daisi.Orc.Core.Data.Models.DaisiModelBackendSettings
            {
                Runtime = 0,
                ContextSize = 8192,
                GpuLayerCount = -1,
                BatchSize = 512,
                AutoFallback = true
            }
        });

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    var json = JsonSerializer.Serialize(new
                    {
                        Daisi = new
                        {
                            OrcId = TestOrcId,
                            AccountId = TestAccountId,
                            MaxHosts = "5"
                        }
                    });
                    var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
                    config.AddJsonStream(stream);
                });

                builder.ConfigureServices(services =>
                {
                    // Remove real Cosmo and background services
                    RemoveService<Cosmo>(services);
                    RemoveHostedService<SessionCleanupService>(services);
                    RemoveHostedService<UptimeCreditService>(services);

                    // Add test Cosmo as singleton
                    services.AddSingleton<Cosmo>(TestCosmo);
                });
            });
    }

    /// <summary>
    /// Creates a GrpcChannel that communicates with the in-process test server.
    /// </summary>
    public GrpcChannel CreateGrpcChannel()
    {
        var handler = new ResponseVersionHandler(_factory.Server.CreateHandler());
        return GrpcChannel.ForAddress(_factory.Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = handler
        });
    }

    private static void RemoveService<T>(IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor != null)
            services.Remove(descriptor);
    }

    private static void RemoveHostedService<T>(IServiceCollection services) where T : class
    {
        var descriptors = services
            .Where(d => d.ServiceType == typeof(IHostedService) &&
                        d.ImplementationType == typeof(T))
            .ToList();
        foreach (var d in descriptors)
            services.Remove(d);
    }

    public void Dispose()
    {
        _factory.Dispose();
    }
}
