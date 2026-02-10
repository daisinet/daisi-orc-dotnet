using Daisi.Orc.Core.Data;
using Daisi.Orc.Core.Data.Db;
using Daisi.Protos.V1;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using DaisiHost = Daisi.Orc.Core.Data.Models.Host;
using DaisiOrchestrator = Daisi.Orc.Core.Data.Models.Orchestrator;
using AccessKey = Daisi.Orc.Core.Data.Models.AccessKey;
using AccessKeyOwner = Daisi.Orc.Core.Data.Models.AccessKeyOwner;
using KeyTypes = Daisi.Orc.Core.Data.Models.KeyTypes;
using DaisiModel = Daisi.Orc.Core.Data.Models.DaisiModel;
using DaisiModelLLamaSettings = Daisi.Orc.Core.Data.Models.DaisiModelLLamaSettings;
using CreditAccount = Daisi.Orc.Core.Data.Models.CreditAccount;
using CreditTransaction = Daisi.Orc.Core.Data.Models.CreditTransaction;
using UptimePeriod = Daisi.Orc.Core.Data.Models.UptimePeriod;
using HostRelease = Daisi.Orc.Core.Data.Models.HostRelease;

namespace Daisi.Integration.Tests.Infrastructure;

/// <summary>
/// In-memory fake of Cosmo for integration testing. Covers credits (like FakeCosmo)
/// plus auth, hosts, orcs, models, and accounts.
/// </summary>
public class IntegrationTestCosmo : Cosmo
{
    // Access keys
    public ConcurrentDictionary<string, AccessKey> Keys { get; } = new();

    // Hosts
    public ConcurrentDictionary<string, DaisiHost> Hosts { get; } = new();

    // Orchestrators
    public ConcurrentDictionary<string, DaisiOrchestrator> Orcs { get; } = new();

    // Models
    public List<DaisiModel> Models { get; } = new();

    // Credits (same as FakeCosmo)
    public ConcurrentDictionary<string, CreditAccount> CreditAccounts { get; } = new();
    public List<CreditTransaction> Transactions { get; } = new();
    public List<UptimePeriod> UptimePeriods { get; } = new();
    public List<HostRelease> Releases { get; } = new();

    public IntegrationTestCosmo() : base(new ConfigurationBuilder().Build(), "unused")
    {
    }

    // ========== AccessKeys ==========

    public override Task<AccessKey> GetKeyAsync(string key, KeyTypes type)
    {
        var found = Keys.Values.FirstOrDefault(k =>
            k.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && k.Type == type.Name);
        return Task.FromResult(found!);
    }

    public override Task<AccessKey> CreateClientKeyAsync(
        AccessKey secretKey, System.Net.IPAddress? requestorIPAddress,
        AccessKeyOwner owner, List<string>? accessToIds = null)
    {
        var clientKey = new AccessKey
        {
            Type = KeyTypes.Client.Name,
            Key = $"client-test-{Guid.NewGuid():N}",
            Owner = owner,
            ParentKeyId = secretKey.Id,
            DateExpires = DateTime.UtcNow.AddHours(1),
            IpAddress = requestorIPAddress?.ToString() ?? string.Empty,
            AccessToIDs = accessToIds ?? new()
        };
        Keys[clientKey.Key] = clientKey;
        return Task.FromResult(clientKey);
    }

    // ========== Hosts ==========

    public override Task<DaisiHost?> GetHostAsync(string hostId)
    {
        Hosts.TryGetValue(hostId, out var host);
        return Task.FromResult(host);
    }

    public override Task<DaisiHost> PatchHostForConnectionAsync(DaisiHost host)
    {
        Hosts[host.Id] = host;
        return Task.FromResult(host);
    }

    public override Task PatchHostEnvironmentAsync(DaisiHost host)
    {
        if (Hosts.TryGetValue(host.Id, out var existing))
        {
            existing.OperatingSystem = host.OperatingSystem;
            existing.OperatingSystemVersion = host.OperatingSystemVersion;
            existing.AppVersion = host.AppVersion;
        }
        return Task.CompletedTask;
    }

    // ========== Orcs ==========

    public override Task<DaisiOrchestrator> PatchOrcStatusAsync(string orcId, OrcStatus status, string? accountId)
    {
        if (Orcs.TryGetValue(orcId, out var orc))
        {
            orc.Status = status;
            return Task.FromResult(orc);
        }
        var newOrc = new DaisiOrchestrator { Id = orcId, AccountId = accountId ?? "", Status = status };
        Orcs[orcId] = newOrc;
        return Task.FromResult(newOrc);
    }

    public override Task<DaisiOrchestrator> PatchOrcConnectionCountAsync(string orcId, int connectionCount, string? accountId)
    {
        if (Orcs.TryGetValue(orcId, out var orc))
        {
            orc.OpenConnectionCount = connectionCount;
            return Task.FromResult(orc);
        }
        var newOrc = new DaisiOrchestrator { Id = orcId, AccountId = accountId ?? "", OpenConnectionCount = connectionCount };
        Orcs[orcId] = newOrc;
        return Task.FromResult(newOrc);
    }

    public override Task<PagedResult<DaisiOrchestrator>> GetOrcsAsync(PagingInfo? paging, string? orcId, string? accountId)
    {
        var items = Orcs.Values
            .Where(o => (orcId == null || o.Id == orcId) && (accountId == null || o.AccountId == accountId))
            .ToList();

        return Task.FromResult(new PagedResult<DaisiOrchestrator>
        {
            TotalCount = items.Count,
            Items = items
        });
    }

    // ========== Models ==========

    public override Task<List<DaisiModel>> GetAllModelsAsync()
    {
        return Task.FromResult(Models.ToList());
    }

    public override Task<DaisiModel> CreateModelAsync(DaisiModel model)
    {
        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;
        Models.Add(model);
        return Task.FromResult(model);
    }

    // ========== Accounts ==========

    public override Task<bool> UserAllowedToLogin(string userId)
    {
        return Task.FromResult(true);
    }

    // ========== Credits (from FakeCosmo pattern) ==========

    public override Task<CreditAccount> GetOrCreateCreditAccountAsync(string accountId)
    {
        var account = CreditAccounts.GetOrAdd(accountId, _ => new CreditAccount
        {
            Id = GenerateId(CreditAccountIdPrefix),
            AccountId = accountId,
            Balance = 0,
            TotalEarned = 0,
            TotalSpent = 0,
            TotalPurchased = 0
        });
        return Task.FromResult(account);
    }

    public override Task<CreditAccount?> GetCreditAccountAsync(string accountId)
    {
        CreditAccounts.TryGetValue(accountId, out var account);
        return Task.FromResult(account);
    }

    public override Task<CreditAccount> UpdateCreditAccountBalanceAsync(CreditAccount creditAccount)
    {
        creditAccount.DateLastUpdated = DateTime.UtcNow;
        CreditAccounts[creditAccount.AccountId] = creditAccount;
        return Task.FromResult(creditAccount);
    }

    public override Task<CreditAccount> PatchCreditAccountMultipliersAsync(
        string accountId, double? tokenMultiplier, double? uptimeMultiplier)
    {
        var account = CreditAccounts.GetOrAdd(accountId, _ => new CreditAccount
        {
            Id = GenerateId(CreditAccountIdPrefix),
            AccountId = accountId
        });
        if (tokenMultiplier.HasValue) account.TokenEarnMultiplier = tokenMultiplier.Value;
        if (uptimeMultiplier.HasValue) account.UptimeEarnMultiplier = uptimeMultiplier.Value;
        account.DateLastUpdated = DateTime.UtcNow;
        return Task.FromResult(account);
    }

    public override Task<CreditTransaction> CreateCreditTransactionAsync(CreditTransaction transaction)
    {
        if (string.IsNullOrWhiteSpace(transaction.Id))
            transaction.Id = GenerateId(CreditTransactionIdPrefix);
        Transactions.Add(transaction);
        return Task.FromResult(transaction);
    }

    public override Task<PagedResult<CreditTransaction>> GetCreditTransactionsAsync(
        string accountId, int? pageSize = 20, int? pageIndex = 0)
    {
        var filtered = Transactions
            .Where(t => t.AccountId == accountId && t.Amount != 0)
            .OrderByDescending(t => t.DateCreated)
            .ToList();
        var size = pageSize ?? 20;
        var index = pageIndex ?? 0;
        return Task.FromResult(new PagedResult<CreditTransaction>
        {
            TotalCount = filtered.Count,
            Items = filtered.Skip(index * size).Take(size).ToList()
        });
    }

    public override Task<UptimePeriod> CreateUptimePeriodAsync(UptimePeriod uptimePeriod)
    {
        if (string.IsNullOrWhiteSpace(uptimePeriod.Id))
            uptimePeriod.Id = GenerateId(UptimePeriodIdPrefix);
        UptimePeriods.Add(uptimePeriod);
        return Task.FromResult(uptimePeriod);
    }

    public override Task<List<UptimePeriod>> GetUptimePeriodsAsync(
        string accountId, string? hostId = null, DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = UptimePeriods.Where(p => p.AccountId == accountId);
        if (hostId is not null) query = query.Where(p => p.HostId == hostId);
        if (startDate.HasValue) query = query.Where(p => p.DateStarted >= startDate.Value);
        if (endDate.HasValue) query = query.Where(p => p.DateStarted <= endDate.Value);
        return Task.FromResult(query.ToList());
    }

    // ========== Releases ==========

    public override Task<HostRelease?> GetActiveReleaseAsync(string releaseGroup)
    {
        var release = Releases.FirstOrDefault(r => r.ReleaseGroup == releaseGroup && r.IsActive);
        return Task.FromResult(release);
    }

    // ========== Seed helpers ==========

    public CreditAccount SeedAccount(string accountId, long balance = 0,
        double tokenMultiplier = 1.0, double uptimeMultiplier = 1.0)
    {
        var account = new CreditAccount
        {
            Id = GenerateId(CreditAccountIdPrefix),
            AccountId = accountId,
            Balance = balance,
            TotalEarned = 0,
            TotalSpent = 0,
            TotalPurchased = 0,
            TokenEarnMultiplier = tokenMultiplier,
            UptimeEarnMultiplier = uptimeMultiplier
        };
        CreditAccounts[accountId] = account;
        return account;
    }

    public AccessKey SeedHostKey(string clientKey, string hostId, string accountId)
    {
        var secretKey = new AccessKey
        {
            Type = KeyTypes.Secret.Name,
            Key = $"secret-test-{Guid.NewGuid():N}",
            Owner = new AccessKeyOwner
            {
                Id = hostId,
                Name = "Test Host",
                SystemRole = SystemRoles.HostDevice,
                AccountId = accountId
            }
        };
        Keys[secretKey.Key] = secretKey;

        var key = new AccessKey
        {
            Type = KeyTypes.Client.Name,
            Key = clientKey,
            ParentKeyId = secretKey.Id,
            DateExpires = DateTime.UtcNow.AddHours(24),
            Owner = new AccessKeyOwner
            {
                Id = hostId,
                Name = "Test Host",
                SystemRole = SystemRoles.HostDevice,
                AccountId = accountId
            }
        };
        Keys[key.Key] = key;
        return key;
    }

    public AccessKey SeedUserKey(string clientKey, string userId, string accountId)
    {
        var secretKey = new AccessKey
        {
            Type = KeyTypes.Secret.Name,
            Key = $"secret-user-{Guid.NewGuid():N}",
            Owner = new AccessKeyOwner
            {
                Id = userId,
                Name = "Test User",
                SystemRole = SystemRoles.User,
                AccountId = accountId
            }
        };
        Keys[secretKey.Key] = secretKey;

        var key = new AccessKey
        {
            Type = KeyTypes.Client.Name,
            Key = clientKey,
            ParentKeyId = secretKey.Id,
            DateExpires = DateTime.UtcNow.AddDays(30),
            Owner = new AccessKeyOwner
            {
                Id = userId,
                Name = "Test User",
                SystemRole = SystemRoles.User,
                AccountId = accountId
            }
        };
        Keys[key.Key] = key;
        return key;
    }

    public DaisiHost SeedHost(string hostId, string accountId)
    {
        var host = new DaisiHost
        {
            Id = hostId,
            AccountId = accountId,
            Name = "Test Host",
            Status = HostStatus.Offline,
            DateCreated = DateTime.UtcNow,
            IpAddress = "127.0.0.1",
            Port = 0,
            OperatingSystem = "Windows",
            OperatingSystemVersion = "10",
            AppVersion = "1.0.0"
        };
        Hosts[hostId] = host;
        return host;
    }

    public DaisiOrchestrator SeedOrc(string orcId, string accountId)
    {
        var orc = new DaisiOrchestrator
        {
            Id = orcId,
            AccountId = accountId,
            Name = "test-orc",
            Status = OrcStatus.Online,
            Domain = "localhost",
            Port = 5000,
            RequiresSSL = false,
            Networks = []
        };
        Orcs[orcId] = orc;
        return orc;
    }
}
