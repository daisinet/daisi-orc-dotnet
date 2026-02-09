using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Tests.Fakes;

namespace Daisi.Orc.Tests.Data
{
    public class CosmoCreditsTests
    {
        private readonly FakeCosmo _cosmo;

        public CosmoCreditsTests()
        {
            _cosmo = new FakeCosmo();
        }

        #region GetOrCreateCreditAccountAsync

        [Fact]
        public async Task GetOrCreateCreditAccount_NewAccount_CreatesWithDefaults()
        {
            var account = await _cosmo.GetOrCreateCreditAccountAsync("acct-1");

            Assert.NotNull(account);
            Assert.Equal("acct-1", account.AccountId);
            Assert.Equal(0, account.Balance);
            Assert.Equal(0, account.TotalEarned);
            Assert.Equal(0, account.TotalSpent);
            Assert.Equal(0, account.TotalPurchased);
            Assert.Equal(1.0, account.TokenEarnMultiplier);
            Assert.Equal(1.0, account.UptimeEarnMultiplier);
        }

        [Fact]
        public async Task GetOrCreateCreditAccount_ExistingAccount_ReturnsSame()
        {
            var seeded = _cosmo.SeedAccount("acct-1", balance: 500);

            var account = await _cosmo.GetOrCreateCreditAccountAsync("acct-1");

            Assert.Equal(500, account.Balance);
            Assert.Equal(seeded.Id, account.Id);
        }

        [Fact]
        public async Task GetOrCreateCreditAccount_CalledTwice_ReturnsSameAccount()
        {
            var first = await _cosmo.GetOrCreateCreditAccountAsync("acct-1");
            var second = await _cosmo.GetOrCreateCreditAccountAsync("acct-1");

            Assert.Equal(first.Id, second.Id);
            Assert.Equal(first.AccountId, second.AccountId);
        }

        #endregion

        #region GetCreditAccountAsync

        [Fact]
        public async Task GetCreditAccount_NonExistent_ReturnsNull()
        {
            var account = await _cosmo.GetCreditAccountAsync("nonexistent");

            Assert.Null(account);
        }

        [Fact]
        public async Task GetCreditAccount_Existing_ReturnsAccount()
        {
            _cosmo.SeedAccount("acct-1", balance: 999);

            var account = await _cosmo.GetCreditAccountAsync("acct-1");

            Assert.NotNull(account);
            Assert.Equal(999, account.Balance);
        }

        #endregion

        #region UpdateCreditAccountBalanceAsync

        [Fact]
        public async Task UpdateCreditAccountBalance_UpdatesAllFields()
        {
            var account = _cosmo.SeedAccount("acct-1", balance: 100);
            account.Balance = 200;
            account.TotalEarned = 150;
            account.TotalSpent = 50;

            var result = await _cosmo.UpdateCreditAccountBalanceAsync(account);

            Assert.Equal(200, result.Balance);
            Assert.Equal(150, result.TotalEarned);
            Assert.Equal(50, result.TotalSpent);
            Assert.True(result.DateLastUpdated <= DateTime.UtcNow);
        }

        #endregion

        #region PatchCreditAccountMultipliersAsync

        [Fact]
        public async Task PatchMultipliers_TokenOnly_UpdatesToken()
        {
            _cosmo.SeedAccount("acct-1");

            var account = await _cosmo.PatchCreditAccountMultipliersAsync("acct-1", 2.5, null);

            Assert.Equal(2.5, account.TokenEarnMultiplier);
            Assert.Equal(1.0, account.UptimeEarnMultiplier);
        }

        [Fact]
        public async Task PatchMultipliers_UptimeOnly_UpdatesUptime()
        {
            _cosmo.SeedAccount("acct-1");

            var account = await _cosmo.PatchCreditAccountMultipliersAsync("acct-1", null, 3.0);

            Assert.Equal(1.0, account.TokenEarnMultiplier);
            Assert.Equal(3.0, account.UptimeEarnMultiplier);
        }

        [Fact]
        public async Task PatchMultipliers_Both_UpdatesBoth()
        {
            _cosmo.SeedAccount("acct-1");

            var account = await _cosmo.PatchCreditAccountMultipliersAsync("acct-1", 1.5, 2.0);

            Assert.Equal(1.5, account.TokenEarnMultiplier);
            Assert.Equal(2.0, account.UptimeEarnMultiplier);
        }

        [Fact]
        public async Task PatchMultipliers_NewAccount_CreatesAndPatches()
        {
            var account = await _cosmo.PatchCreditAccountMultipliersAsync("new-acct", 2.0, 3.0);

            Assert.Equal("new-acct", account.AccountId);
            Assert.Equal(2.0, account.TokenEarnMultiplier);
            Assert.Equal(3.0, account.UptimeEarnMultiplier);
        }

        #endregion

        #region CreateCreditTransactionAsync

        [Fact]
        public async Task CreateTransaction_AddsToStore()
        {
            var tx = new CreditTransaction
            {
                AccountId = "acct-1",
                Type = CreditTransactionType.Purchase,
                Amount = 100,
                Balance = 100,
                Description = "Test purchase"
            };

            var result = await _cosmo.CreateCreditTransactionAsync(tx);

            Assert.Single(_cosmo.Transactions);
            Assert.Equal("acct-1", result.AccountId);
            Assert.Equal(100, result.Amount);
        }

        [Fact]
        public async Task CreateTransaction_PreservesAllFields()
        {
            var tx = new CreditTransaction
            {
                AccountId = "acct-1",
                Type = CreditTransactionType.TokenEarning,
                Amount = 50,
                Balance = 550,
                Description = "Token earning",
                RelatedEntityId = "inf-123",
                Multiplier = 1.5
            };

            var result = await _cosmo.CreateCreditTransactionAsync(tx);

            Assert.Equal(CreditTransactionType.TokenEarning, result.Type);
            Assert.Equal(50, result.Amount);
            Assert.Equal(550, result.Balance);
            Assert.Equal("Token earning", result.Description);
            Assert.Equal("inf-123", result.RelatedEntityId);
            Assert.Equal(1.5, result.Multiplier);
        }

        #endregion

        #region GetCreditTransactionsAsync

        [Fact]
        public async Task GetTransactions_Empty_ReturnsEmptyResult()
        {
            var result = await _cosmo.GetCreditTransactionsAsync("acct-1");

            Assert.Equal(0, result.TotalCount);
            Assert.Empty(result.Items);
        }

        [Fact]
        public async Task GetTransactions_FiltersByAccountId()
        {
            await _cosmo.CreateCreditTransactionAsync(new CreditTransaction
            {
                AccountId = "acct-1", Amount = 100, Balance = 100
            });
            await _cosmo.CreateCreditTransactionAsync(new CreditTransaction
            {
                AccountId = "acct-2", Amount = 200, Balance = 200
            });

            var result = await _cosmo.GetCreditTransactionsAsync("acct-1");

            Assert.Equal(1, result.TotalCount);
            Assert.All(result.Items, t => Assert.Equal("acct-1", t.AccountId));
        }

        [Fact]
        public async Task GetTransactions_ExcludesZeroAmount()
        {
            await _cosmo.CreateCreditTransactionAsync(new CreditTransaction
            {
                AccountId = "acct-1", Amount = 0, Balance = 0
            });
            await _cosmo.CreateCreditTransactionAsync(new CreditTransaction
            {
                AccountId = "acct-1", Amount = 100, Balance = 100
            });

            var result = await _cosmo.GetCreditTransactionsAsync("acct-1");

            Assert.Equal(1, result.TotalCount);
            Assert.Equal(100, result.Items.First().Amount);
        }

        [Fact]
        public async Task GetTransactions_Pagination_Works()
        {
            for (int i = 1; i <= 5; i++)
            {
                await _cosmo.CreateCreditTransactionAsync(new CreditTransaction
                {
                    AccountId = "acct-1", Amount = i * 10, Balance = i * 10
                });
            }

            var page1 = await _cosmo.GetCreditTransactionsAsync("acct-1", pageSize: 2, pageIndex: 0);
            var page2 = await _cosmo.GetCreditTransactionsAsync("acct-1", pageSize: 2, pageIndex: 1);

            Assert.Equal(5, page1.TotalCount);
            Assert.Equal(2, page1.Items.Count);
            Assert.Equal(2, page2.Items.Count);
        }

        #endregion

        #region UptimePeriod Operations

        [Fact]
        public async Task CreateUptimePeriod_AddsToStore()
        {
            var period = new UptimePeriod
            {
                AccountId = "acct-1",
                HostId = "host-1",
                DateStarted = DateTime.UtcNow.AddHours(-1),
                DateEnded = DateTime.UtcNow,
                TotalMinutes = 60,
                CreditsPaid = 10
            };

            var result = await _cosmo.CreateUptimePeriodAsync(period);

            Assert.Single(_cosmo.UptimePeriods);
            Assert.Equal("acct-1", result.AccountId);
            Assert.Equal("host-1", result.HostId);
        }

        [Fact]
        public async Task GetUptimePeriods_FiltersByAccountId()
        {
            _cosmo.SeedUptimePeriods("acct-1", "host-1", 60);
            _cosmo.SeedUptimePeriods("acct-2", "host-2", 60);

            var result = await _cosmo.GetUptimePeriodsAsync("acct-1");

            Assert.Single(result);
            Assert.All(result, p => Assert.Equal("acct-1", p.AccountId));
        }

        [Fact]
        public async Task GetUptimePeriods_FiltersByHostId()
        {
            _cosmo.SeedUptimePeriods("acct-1", "host-1", 60);
            _cosmo.SeedUptimePeriods("acct-1", "host-2", 60);

            var result = await _cosmo.GetUptimePeriodsAsync("acct-1", "host-1");

            Assert.Single(result);
            Assert.Equal("host-1", result.First().HostId);
        }

        [Fact]
        public async Task GetUptimePeriods_FiltersByDateRange()
        {
            _cosmo.UptimePeriods.Add(new UptimePeriod
            {
                AccountId = "acct-1",
                HostId = "host-1",
                DateStarted = DateTime.UtcNow.AddDays(-10),
                TotalMinutes = 60
            });
            _cosmo.UptimePeriods.Add(new UptimePeriod
            {
                AccountId = "acct-1",
                HostId = "host-1",
                DateStarted = DateTime.UtcNow.AddDays(-60),
                TotalMinutes = 60
            });

            var result = await _cosmo.GetUptimePeriodsAsync(
                "acct-1",
                startDate: DateTime.UtcNow.AddDays(-30),
                endDate: DateTime.UtcNow);

            Assert.Single(result);
        }

        #endregion
    }
}
