using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;

namespace Daisi.Orc.Tests.Models
{
    public class CreditModelsTests
    {
        #region CreditAccount

        [Fact]
        public void CreditAccount_DefaultValues_AreCorrect()
        {
            var account = new CreditAccount();

            // Id is now set explicitly via GetDeterministicId, not auto-generated
            Assert.Null(account.Id);
            Assert.Equal(0, account.Balance);
            Assert.Equal(0, account.TotalEarned);
            Assert.Equal(0, account.TotalSpent);
            Assert.Equal(0, account.TotalPurchased);
            Assert.Equal(1.0, account.TokenEarnMultiplier);
            Assert.Equal(1.0, account.UptimeEarnMultiplier);
            Assert.Equal(UptimeBonusTier.None, account.CachedBonusTier);
            Assert.Null(account.BonusTierCalculatedAt);
            Assert.True(account.DateCreated <= DateTime.UtcNow);
            Assert.True(account.DateLastUpdated <= DateTime.UtcNow);
        }

        [Fact]
        public void CreditAccount_DeterministicId_IsDeterministic()
        {
            var id1 = CreditAccount.GetDeterministicId("acct-123");
            var id2 = CreditAccount.GetDeterministicId("acct-123");

            Assert.Equal(id1, id2);
        }

        [Fact]
        public void CreditAccount_DeterministicId_HasCorrectPrefix()
        {
            var id = CreditAccount.GetDeterministicId("acct-123");
            Assert.StartsWith(CreditAccount.IdPrefix, id);
            Assert.Equal("creditaccount-acct-123", id);
        }

        #endregion

        #region CreditTransaction

        [Fact]
        public void CreditTransaction_DefaultValues_AreCorrect()
        {
            var tx = new CreditTransaction();

            Assert.True(tx.Id.StartsWith(Cosmo.CreditTransactionIdPrefix));
            Assert.Equal(1.0, tx.Multiplier);
            Assert.True(tx.DateCreated <= DateTime.UtcNow);
            Assert.Null(tx.RelatedEntityId);
        }

        [Fact]
        public void CreditTransaction_IdGeneration_HasCorrectPrefix()
        {
            var tx = new CreditTransaction();
            Assert.StartsWith("ctx-", tx.Id);
        }

        [Fact]
        public void CreditTransactionType_HasAllExpectedValues()
        {
            var values = Enum.GetValues<CreditTransactionType>();

            Assert.Contains(CreditTransactionType.TokenEarning, values);
            Assert.Contains(CreditTransactionType.UptimeEarning, values);
            Assert.Contains(CreditTransactionType.InferenceSpend, values);
            Assert.Contains(CreditTransactionType.Purchase, values);
            Assert.Contains(CreditTransactionType.AdminAdjustment, values);
            Assert.Contains(CreditTransactionType.MarketplacePurchase, values);
            Assert.Contains(CreditTransactionType.ProviderEarning, values);
            Assert.Contains(CreditTransactionType.SubscriptionRenewal, values);
            Assert.Equal(8, values.Length);
        }

        #endregion

        #region UptimePeriod

        [Fact]
        public void UptimePeriod_DefaultValues_AreCorrect()
        {
            var period = new UptimePeriod();

            Assert.True(period.Id.StartsWith(Cosmo.UptimePeriodIdPrefix));
            Assert.Equal(UptimeBonusTier.None, period.BonusTier);
            Assert.Null(period.DateEnded);
        }

        [Fact]
        public void UptimePeriod_IdGeneration_HasCorrectPrefix()
        {
            var period = new UptimePeriod();
            Assert.StartsWith("upt-", period.Id);
        }

        [Fact]
        public void UptimeBonusTier_HasAllExpectedValues()
        {
            var values = Enum.GetValues<UptimeBonusTier>();

            Assert.Contains(UptimeBonusTier.None, values);
            Assert.Contains(UptimeBonusTier.Bronze, values);
            Assert.Contains(UptimeBonusTier.Silver, values);
            Assert.Contains(UptimeBonusTier.Gold, values);
            Assert.Equal(4, values.Length);
        }

        #endregion

        #region CreditAccount Properties

        [Fact]
        public void CreditAccount_CanSetAllProperties()
        {
            var now = DateTime.UtcNow;
            var account = new CreditAccount
            {
                Id = "cred-test",
                AccountId = "acct-123",
                Balance = 1000,
                TotalEarned = 500,
                TotalSpent = 200,
                TotalPurchased = 700,
                TokenEarnMultiplier = 1.5,
                UptimeEarnMultiplier = 2.0,
                DateCreated = now,
                DateLastUpdated = now
            };

            Assert.Equal("cred-test", account.Id);
            Assert.Equal("acct-123", account.AccountId);
            Assert.Equal(1000, account.Balance);
            Assert.Equal(500, account.TotalEarned);
            Assert.Equal(200, account.TotalSpent);
            Assert.Equal(700, account.TotalPurchased);
            Assert.Equal(1.5, account.TokenEarnMultiplier);
            Assert.Equal(2.0, account.UptimeEarnMultiplier);
            Assert.Equal(now, account.DateCreated);
            Assert.Equal(now, account.DateLastUpdated);
        }

        #endregion

        #region CreditTransaction Properties

        [Fact]
        public void CreditTransaction_CanSetAllProperties()
        {
            var tx = new CreditTransaction
            {
                AccountId = "acct-1",
                Type = CreditTransactionType.TokenEarning,
                Amount = 100,
                Balance = 500,
                Description = "Test earning",
                RelatedEntityId = "inf-1",
                Multiplier = 1.5
            };

            Assert.Equal("acct-1", tx.AccountId);
            Assert.Equal(CreditTransactionType.TokenEarning, tx.Type);
            Assert.Equal(100, tx.Amount);
            Assert.Equal(500, tx.Balance);
            Assert.Equal("Test earning", tx.Description);
            Assert.Equal("inf-1", tx.RelatedEntityId);
            Assert.Equal(1.5, tx.Multiplier);
        }

        #endregion

        #region UptimePeriod Properties

        [Fact]
        public void UptimePeriod_CanSetAllProperties()
        {
            var start = DateTime.UtcNow.AddHours(-1);
            var end = DateTime.UtcNow;

            var period = new UptimePeriod
            {
                AccountId = "acct-1",
                HostId = "host-1",
                DateStarted = start,
                DateEnded = end,
                TotalMinutes = 60,
                CreditsPaid = 10,
                BonusTier = UptimeBonusTier.Gold
            };

            Assert.Equal("acct-1", period.AccountId);
            Assert.Equal("host-1", period.HostId);
            Assert.Equal(start, period.DateStarted);
            Assert.Equal(end, period.DateEnded);
            Assert.Equal(60, period.TotalMinutes);
            Assert.Equal(10, period.CreditsPaid);
            Assert.Equal(UptimeBonusTier.Gold, period.BonusTier);
        }

        #endregion
    }
}
