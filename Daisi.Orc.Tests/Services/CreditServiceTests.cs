using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Core.Services;
using Daisi.Orc.Tests.Fakes;
using Daisi.Protos.V1;

namespace Daisi.Orc.Tests.Services
{
    public class CreditServiceTests
    {
        private readonly FakeCosmo _cosmo;
        private readonly CreditService _service;

        public CreditServiceTests()
        {
            _cosmo = new FakeCosmo();
            _service = new CreditService(_cosmo);
        }

        #region EarnTokenCreditsAsync

        [Fact]
        public async Task EarnTokenCredits_NewAccount_CreatesAccountAndAwardsCredits()
        {
            var tx = await _service.EarnTokenCreditsAsync("acct-1", 100);

            Assert.Equal("acct-1", tx.AccountId);
            Assert.Equal(CreditTransactionType.TokenEarning, tx.Type);
            Assert.Equal(100, tx.Amount);
            Assert.Equal(100, tx.Balance);
            Assert.Equal(1.0, tx.Multiplier);

            var account = _cosmo.CreditAccounts["acct-1"];
            Assert.Equal(100, account.Balance);
            Assert.Equal(100, account.TotalEarned);
        }

        [Fact]
        public async Task EarnTokenCredits_ExistingAccount_AccumulatesBalance()
        {
            _cosmo.SeedAccount("acct-1", balance: 500);

            var tx = await _service.EarnTokenCreditsAsync("acct-1", 200);

            Assert.Equal(700, tx.Balance);
            Assert.Equal(200, tx.Amount);

            var account = _cosmo.CreditAccounts["acct-1"];
            Assert.Equal(700, account.Balance);
            Assert.Equal(200, account.TotalEarned);
        }

        [Fact]
        public async Task EarnTokenCredits_WithTokenMultiplier_AppliesMultiplier()
        {
            _cosmo.SeedAccount("acct-1", balance: 0, tokenMultiplier: 2.0);

            var tx = await _service.EarnTokenCreditsAsync("acct-1", 100);

            // 100 tokens * 2.0 multiplier * 1.0 uptime bonus = 200 credits
            Assert.Equal(200, tx.Amount);
            Assert.Equal(200, tx.Balance);
            Assert.Equal(2.0, tx.Multiplier);
        }

        [Fact]
        public async Task EarnTokenCredits_SetsRelatedEntityId()
        {
            var tx = await _service.EarnTokenCreditsAsync("acct-1", 50, "inf-123");

            Assert.Equal("inf-123", tx.RelatedEntityId);
        }

        [Fact]
        public async Task EarnTokenCredits_WithHighUptime_AppliesUptimeBonus()
        {
            _cosmo.SeedAccount("acct-1", balance: 0);
            // Seed 99%+ uptime (Gold tier = 1.5x)
            int nearFullMonthMinutes = (int)(30 * 24 * 60 * 0.995); // 99.5%
            _cosmo.SeedUptimePeriods("acct-1", "host-1", nearFullMonthMinutes);

            var tx = await _service.EarnTokenCreditsAsync("acct-1", 100);

            // 100 tokens * 1.0 token multiplier * 1.5 gold uptime = 150
            Assert.Equal(150, tx.Amount);
        }

        #endregion

        #region SpendCreditsAsync

        [Fact]
        public async Task SpendCredits_SufficientBalance_DebitsAndReturnsTrue()
        {
            _cosmo.SeedAccount("acct-1", balance: 500);

            var result = await _service.SpendCreditsAsync("acct-1", 200);

            Assert.True(result);

            var account = _cosmo.CreditAccounts["acct-1"];
            Assert.Equal(300, account.Balance);
            Assert.Equal(200, account.TotalSpent);
        }

        [Fact]
        public async Task SpendCredits_InsufficientBalance_ReturnsFalse()
        {
            _cosmo.SeedAccount("acct-1", balance: 50);

            var result = await _service.SpendCreditsAsync("acct-1", 200);

            Assert.False(result);

            // Balance should not have changed
            var account = _cosmo.CreditAccounts["acct-1"];
            Assert.Equal(50, account.Balance);
            Assert.Equal(0, account.TotalSpent);
        }

        [Fact]
        public async Task SpendCredits_ZeroBalance_ReturnsFalse()
        {
            _cosmo.SeedAccount("acct-1", balance: 0);

            var result = await _service.SpendCreditsAsync("acct-1", 1);

            Assert.False(result);
        }

        [Fact]
        public async Task SpendCredits_ExactBalance_Succeeds()
        {
            _cosmo.SeedAccount("acct-1", balance: 100);

            var result = await _service.SpendCreditsAsync("acct-1", 100);

            Assert.True(result);
            Assert.Equal(0, _cosmo.CreditAccounts["acct-1"].Balance);
        }

        [Fact]
        public async Task SpendCredits_CreatesNegativeTransaction()
        {
            _cosmo.SeedAccount("acct-1", balance: 500);

            await _service.SpendCreditsAsync("acct-1", 200, "inf-456");

            var tx = _cosmo.Transactions.Last();
            Assert.Equal(-200, tx.Amount);
            Assert.Equal(CreditTransactionType.InferenceSpend, tx.Type);
            Assert.Equal("inf-456", tx.RelatedEntityId);
            Assert.Equal(300, tx.Balance);
        }

        #endregion

        #region HasSufficientCreditsAsync

        [Fact]
        public async Task HasSufficientCredits_WithEnoughBalance_ReturnsTrue()
        {
            _cosmo.SeedAccount("acct-1", balance: 500);

            var result = await _service.HasSufficientCreditsAsync("acct-1", 100);

            Assert.True(result);
        }

        [Fact]
        public async Task HasSufficientCredits_InsufficientBalance_ReturnsFalse()
        {
            _cosmo.SeedAccount("acct-1", balance: 50);

            var result = await _service.HasSufficientCreditsAsync("acct-1", 100);

            Assert.False(result);
        }

        [Fact]
        public async Task HasSufficientCredits_NoAccount_ReturnsFalse()
        {
            var result = await _service.HasSufficientCreditsAsync("nonexistent", 1);

            Assert.False(result);
        }

        [Fact]
        public async Task HasSufficientCredits_ExactBalance_ReturnsTrue()
        {
            _cosmo.SeedAccount("acct-1", balance: 100);

            var result = await _service.HasSufficientCreditsAsync("acct-1", 100);

            Assert.True(result);
        }

        #endregion

        #region PurchaseCreditsAsync

        [Fact]
        public async Task PurchaseCredits_IncreasesBalanceAndTotalPurchased()
        {
            _cosmo.SeedAccount("acct-1", balance: 100);

            var tx = await _service.PurchaseCreditsAsync("acct-1", 500, "Monthly plan");

            Assert.Equal(CreditTransactionType.Purchase, tx.Type);
            Assert.Equal(500, tx.Amount);
            Assert.Equal(600, tx.Balance);
            Assert.Equal("Monthly plan", tx.Description);

            var account = _cosmo.CreditAccounts["acct-1"];
            Assert.Equal(600, account.Balance);
            Assert.Equal(500, account.TotalPurchased);
        }

        [Fact]
        public async Task PurchaseCredits_NewAccount_CreatesAndPurchases()
        {
            var tx = await _service.PurchaseCreditsAsync("new-acct", 1000);

            Assert.Equal(1000, tx.Amount);
            Assert.Equal(1000, tx.Balance);
            Assert.Contains("Purchased 1000 credits", tx.Description);
        }

        #endregion

        #region AwardUptimeCreditsAsync

        [Fact]
        public async Task AwardUptimeCredits_OneHour_AwardsDefaultRate()
        {
            _cosmo.SeedAccount("acct-1", balance: 0);

            var tx = await _service.AwardUptimeCreditsAsync("acct-1", "host-1", 60);

            Assert.Equal(CreditTransactionType.UptimeEarning, tx.Type);
            Assert.Equal(CreditService.DefaultUptimeCreditsPerHour, tx.Amount);
            Assert.Equal("host-1", tx.RelatedEntityId);
        }

        [Fact]
        public async Task AwardUptimeCredits_PartialHour_ProRates()
        {
            _cosmo.SeedAccount("acct-1", balance: 0);

            var tx = await _service.AwardUptimeCreditsAsync("acct-1", "host-1", 30);

            // 30 min = 0.5 hours, 10 credits/hour * 0.5 = 5
            Assert.Equal(5, tx.Amount);
        }

        [Fact]
        public async Task AwardUptimeCredits_SmallPeriod_MinimumOneCredit()
        {
            _cosmo.SeedAccount("acct-1", balance: 0);

            var tx = await _service.AwardUptimeCreditsAsync("acct-1", "host-1", 1);

            // 1 min = 0.0167 hours, 10 * 0.0167 = 0.167 → rounds to 0, but min is 1
            Assert.Equal(1, tx.Amount);
        }

        [Fact]
        public async Task AwardUptimeCredits_WithMultiplier_AppliesMultiplier()
        {
            _cosmo.SeedAccount("acct-1", balance: 0, uptimeMultiplier: 3.0);

            var tx = await _service.AwardUptimeCreditsAsync("acct-1", "host-1", 60);

            // 10 credits/hour * 1 hour * 3.0x = 30
            Assert.Equal(30, tx.Amount);
        }

        [Fact]
        public async Task AwardUptimeCredits_CreatesUptimePeriod()
        {
            _cosmo.SeedAccount("acct-1", balance: 0);

            await _service.AwardUptimeCreditsAsync("acct-1", "host-1", 60);

            Assert.Single(_cosmo.UptimePeriods);
            var period = _cosmo.UptimePeriods.First();
            Assert.Equal("acct-1", period.AccountId);
            Assert.Equal("host-1", period.HostId);
            Assert.Equal(60, period.TotalMinutes);
        }

        #endregion

        #region AdjustCreditsAsync

        [Fact]
        public async Task AdjustCredits_PositiveAmount_IncreasesBalance()
        {
            _cosmo.SeedAccount("acct-1", balance: 100);

            var tx = await _service.AdjustCreditsAsync("acct-1", 500, "Promo bonus");

            Assert.Equal(CreditTransactionType.AdminAdjustment, tx.Type);
            Assert.Equal(500, tx.Amount);
            Assert.Equal(600, tx.Balance);
            Assert.Equal("Promo bonus", tx.Description);

            var account = _cosmo.CreditAccounts["acct-1"];
            Assert.Equal(600, account.Balance);
            Assert.Equal(500, account.TotalEarned);
        }

        [Fact]
        public async Task AdjustCredits_NegativeAmount_DecreasesBalance()
        {
            _cosmo.SeedAccount("acct-1", balance: 500);

            var tx = await _service.AdjustCreditsAsync("acct-1", -200, "Penalty");

            Assert.Equal(-200, tx.Amount);
            Assert.Equal(300, tx.Balance);

            var account = _cosmo.CreditAccounts["acct-1"];
            Assert.Equal(300, account.Balance);
            Assert.Equal(200, account.TotalSpent);
        }

        [Fact]
        public async Task AdjustCredits_DefaultDescription()
        {
            _cosmo.SeedAccount("acct-1", balance: 0);

            var tx = await _service.AdjustCreditsAsync("acct-1", 100);

            Assert.Equal("Admin adjustment of 100 credits", tx.Description);
        }

        #endregion

        #region SetMultipliersAsync

        [Fact]
        public async Task SetMultipliers_TokenOnly_UpdatesTokenMultiplier()
        {
            _cosmo.SeedAccount("acct-1");

            var account = await _service.SetMultipliersAsync("acct-1", tokenMultiplier: 2.5);

            Assert.Equal(2.5, account.TokenEarnMultiplier);
            Assert.Equal(1.0, account.UptimeEarnMultiplier); // unchanged
        }

        [Fact]
        public async Task SetMultipliers_UptimeOnly_UpdatesUptimeMultiplier()
        {
            _cosmo.SeedAccount("acct-1");

            var account = await _service.SetMultipliersAsync("acct-1", uptimeMultiplier: 3.0);

            Assert.Equal(1.0, account.TokenEarnMultiplier); // unchanged
            Assert.Equal(3.0, account.UptimeEarnMultiplier);
        }

        [Fact]
        public async Task SetMultipliers_Both_UpdatesBoth()
        {
            _cosmo.SeedAccount("acct-1");

            var account = await _service.SetMultipliersAsync("acct-1", 1.5, 2.0);

            Assert.Equal(1.5, account.TokenEarnMultiplier);
            Assert.Equal(2.0, account.UptimeEarnMultiplier);
        }

        #endregion

        #region GetBalanceAsync

        [Fact]
        public async Task GetBalance_ExistingAccount_ReturnsAccount()
        {
            _cosmo.SeedAccount("acct-1", balance: 999);

            var account = await _service.GetBalanceAsync("acct-1");

            Assert.Equal("acct-1", account.AccountId);
            Assert.Equal(999, account.Balance);
        }

        [Fact]
        public async Task GetBalance_NewAccount_CreatesWithZeroBalance()
        {
            var account = await _service.GetBalanceAsync("new-acct");

            Assert.Equal("new-acct", account.AccountId);
            Assert.Equal(0, account.Balance);
        }

        #endregion

        #region CalculateUptimeBonusTierAsync

        [Fact]
        public async Task CalculateUptimeBonusTier_NoUptime_ReturnsNone()
        {
            var tier = await _service.CalculateUptimeBonusTierAsync("acct-1");

            Assert.Equal(UptimeBonusTier.None, tier);
        }

        [Fact]
        public async Task CalculateUptimeBonusTier_90PercentUptime_ReturnsBronze()
        {
            int totalMinutesInMonth = 30 * 24 * 60;
            int bronzeMinutes = (int)(totalMinutesInMonth * 0.91);
            _cosmo.SeedUptimePeriods("acct-1", "host-1", bronzeMinutes);

            var tier = await _service.CalculateUptimeBonusTierAsync("acct-1");

            Assert.Equal(UptimeBonusTier.Bronze, tier);
        }

        [Fact]
        public async Task CalculateUptimeBonusTier_95PercentUptime_ReturnsSilver()
        {
            int totalMinutesInMonth = 30 * 24 * 60;
            int silverMinutes = (int)(totalMinutesInMonth * 0.96);
            _cosmo.SeedUptimePeriods("acct-1", "host-1", silverMinutes);

            var tier = await _service.CalculateUptimeBonusTierAsync("acct-1");

            Assert.Equal(UptimeBonusTier.Silver, tier);
        }

        [Fact]
        public async Task CalculateUptimeBonusTier_99PercentUptime_ReturnsGold()
        {
            int totalMinutesInMonth = 30 * 24 * 60;
            int goldMinutes = (int)(totalMinutesInMonth * 0.995);
            _cosmo.SeedUptimePeriods("acct-1", "host-1", goldMinutes);

            var tier = await _service.CalculateUptimeBonusTierAsync("acct-1");

            Assert.Equal(UptimeBonusTier.Gold, tier);
        }

        [Fact]
        public async Task CalculateUptimeBonusTier_LowUptime_ReturnsNone()
        {
            int totalMinutesInMonth = 30 * 24 * 60;
            int lowMinutes = (int)(totalMinutesInMonth * 0.50); // 50%
            _cosmo.SeedUptimePeriods("acct-1", "host-1", lowMinutes);

            var tier = await _service.CalculateUptimeBonusTierAsync("acct-1");

            Assert.Equal(UptimeBonusTier.None, tier);
        }

        #endregion

        #region ProcessInferenceReceiptAsync

        [Fact]
        public async Task ProcessInferenceReceipt_DifferentAccounts_AwardsHostDebitsConsumer()
        {
            _cosmo.SeedAccount("host-acct", balance: 0);
            _cosmo.SeedAccount("consumer-acct", balance: 1000);

            var receipt = new InferenceReceipt
            {
                InferenceId = "inf-1",
                SessionId = "sess-1",
                ConsumerAccountId = "consumer-acct",
                TokenCount = 100,
                ComputeTimeMs = 500,
                ModelName = "test-model"
            };

            await _service.ProcessInferenceReceiptAsync(receipt, "host-acct");

            var hostAccount = _cosmo.CreditAccounts["host-acct"];
            var consumerAccount = _cosmo.CreditAccounts["consumer-acct"];

            Assert.Equal(100, hostAccount.Balance);
            Assert.Equal(100, hostAccount.TotalEarned);
            Assert.Equal(900, consumerAccount.Balance);
            Assert.Equal(100, consumerAccount.TotalSpent);
        }

        [Fact]
        public async Task ProcessInferenceReceipt_SameAccount_NoChanges()
        {
            _cosmo.SeedAccount("same-acct", balance: 500);

            var receipt = new InferenceReceipt
            {
                InferenceId = "inf-1",
                SessionId = "sess-1",
                ConsumerAccountId = "same-acct",
                TokenCount = 100
            };

            await _service.ProcessInferenceReceiptAsync(receipt, "same-acct");

            var account = _cosmo.CreditAccounts["same-acct"];
            Assert.Equal(500, account.Balance); // unchanged
            Assert.Empty(_cosmo.Transactions);
        }

        [Fact]
        public async Task ProcessInferenceReceipt_ConsumerInsufficientBalance_HostStillEarns()
        {
            _cosmo.SeedAccount("host-acct", balance: 0);
            _cosmo.SeedAccount("consumer-acct", balance: 0); // no balance

            var receipt = new InferenceReceipt
            {
                InferenceId = "inf-1",
                SessionId = "sess-1",
                ConsumerAccountId = "consumer-acct",
                TokenCount = 100
            };

            await _service.ProcessInferenceReceiptAsync(receipt, "host-acct");

            // Host still earns
            var hostAccount = _cosmo.CreditAccounts["host-acct"];
            Assert.Equal(100, hostAccount.Balance);

            // Consumer balance didn't change (spend returned false)
            var consumerAccount = _cosmo.CreditAccounts["consumer-acct"];
            Assert.Equal(0, consumerAccount.Balance);
        }

        #endregion

        #region Transaction Ledger Integrity

        [Fact]
        public async Task TransactionLedger_MultipleOperations_MaintainsCorrectBalances()
        {
            // Purchase 1000 credits
            await _service.PurchaseCreditsAsync("acct-1", 1000);

            // Earn 200 from tokens
            await _service.EarnTokenCreditsAsync("acct-1", 200);

            // Spend 300
            await _service.SpendCreditsAsync("acct-1", 300);

            // Earn 50 from uptime
            await _service.AwardUptimeCreditsAsync("acct-1", "host-1", 300); // 5 hours = 50 credits

            var account = _cosmo.CreditAccounts["acct-1"];
            Assert.Equal(1000 + 200 - 300 + 50, account.Balance);

            // Verify transaction balances are running totals
            var txs = _cosmo.Transactions.Where(t => t.AccountId == "acct-1").ToList();
            Assert.True(txs.Count >= 4);

            // Last transaction balance should match current balance
            Assert.Equal(account.Balance, txs.Last().Balance);
        }

        [Fact]
        public async Task TransactionLedger_EachTransactionHasCorrectType()
        {
            _cosmo.SeedAccount("acct-1", balance: 1000);

            await _service.PurchaseCreditsAsync("acct-1", 100);
            await _service.EarnTokenCreditsAsync("acct-1", 50);
            await _service.SpendCreditsAsync("acct-1", 25);
            await _service.AwardUptimeCreditsAsync("acct-1", "host-1", 60);
            await _service.AdjustCreditsAsync("acct-1", 10);

            var types = _cosmo.Transactions.Select(t => t.Type).ToList();
            Assert.Contains(CreditTransactionType.Purchase, types);
            Assert.Contains(CreditTransactionType.TokenEarning, types);
            Assert.Contains(CreditTransactionType.InferenceSpend, types);
            Assert.Contains(CreditTransactionType.UptimeEarning, types);
            Assert.Contains(CreditTransactionType.AdminAdjustment, types);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public async Task EarnTokenCredits_ZeroTokens_AwardsZeroCredits()
        {
            _cosmo.SeedAccount("acct-1", balance: 100);

            var tx = await _service.EarnTokenCreditsAsync("acct-1", 0);

            Assert.Equal(0, tx.Amount);
            Assert.Equal(100, _cosmo.CreditAccounts["acct-1"].Balance);
        }

        [Fact]
        public async Task SpendCredits_ZeroAmount_Succeeds()
        {
            _cosmo.SeedAccount("acct-1", balance: 100);

            var result = await _service.SpendCreditsAsync("acct-1", 0);

            Assert.True(result);
            Assert.Equal(100, _cosmo.CreditAccounts["acct-1"].Balance);
        }

        [Fact]
        public async Task PurchaseCredits_LargeAmount_Succeeds()
        {
            var tx = await _service.PurchaseCreditsAsync("acct-1", 1_000_000);

            Assert.Equal(1_000_000, tx.Amount);
            Assert.Equal(1_000_000, _cosmo.CreditAccounts["acct-1"].Balance);
        }

        #endregion
    }
}
