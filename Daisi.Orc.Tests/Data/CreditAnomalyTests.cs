using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Tests.Fakes;

namespace Daisi.Orc.Tests.Data
{
    public class CreditAnomalyTests
    {
        private readonly FakeCosmo _cosmo;

        public CreditAnomalyTests()
        {
            _cosmo = new FakeCosmo();
        }

        [Fact]
        public async Task CreateAnomaly_StoresAndReturns()
        {
            var anomaly = new CreditAnomaly
            {
                AccountId = "acct-1",
                HostId = "host-1",
                Type = AnomalyType.InflatedTokens,
                Severity = AnomalySeverity.Medium,
                Description = "Test anomaly"
            };

            var result = await _cosmo.CreateCreditAnomalyAsync(anomaly);

            Assert.NotNull(result.Id);
            Assert.Equal("acct-1", result.AccountId);
            Assert.Single(_cosmo.CreditAnomalies);
        }

        [Fact]
        public async Task GetAnomalies_NoFilters_ReturnsAll()
        {
            await SeedAnomaliesAsync();

            var result = await _cosmo.GetCreditAnomaliesAsync();

            Assert.Equal(3, result.TotalCount);
            Assert.Equal(3, result.Items.Count);
        }

        [Fact]
        public async Task GetAnomalies_FilterByAccount_ReturnsFiltered()
        {
            await SeedAnomaliesAsync();

            var result = await _cosmo.GetCreditAnomaliesAsync(accountId: "acct-1");

            Assert.Equal(2, result.TotalCount);
            Assert.All(result.Items, a => Assert.Equal("acct-1", a.AccountId));
        }

        [Fact]
        public async Task GetAnomalies_FilterByType_ReturnsFiltered()
        {
            await SeedAnomaliesAsync();

            var result = await _cosmo.GetCreditAnomaliesAsync(type: AnomalyType.InflatedTokens);

            Assert.Single(result.Items);
            Assert.Equal(AnomalyType.InflatedTokens, result.Items[0].Type);
        }

        [Fact]
        public async Task GetAnomalies_FilterByStatus_ReturnsFiltered()
        {
            await SeedAnomaliesAsync();

            // All seeded anomalies are Open by default
            var result = await _cosmo.GetCreditAnomaliesAsync(status: AnomalyStatus.Open);

            Assert.Equal(3, result.TotalCount);
        }

        [Fact]
        public async Task GetAnomalies_Paging_ReturnsCorrectPage()
        {
            await SeedAnomaliesAsync();

            var result = await _cosmo.GetCreditAnomaliesAsync(pageSize: 2, pageIndex: 0);

            Assert.Equal(3, result.TotalCount);
            Assert.Equal(2, result.Items.Count);
        }

        [Fact]
        public async Task UpdateAnomalyStatus_UpdatesFields()
        {
            var anomaly = new CreditAnomaly
            {
                AccountId = "acct-1",
                HostId = "host-1",
                Type = AnomalyType.ReceiptVolumeSpike,
                Severity = AnomalySeverity.High,
                Description = "Spike detected"
            };
            await _cosmo.CreateCreditAnomalyAsync(anomaly);

            var updated = await _cosmo.UpdateCreditAnomalyStatusAsync(
                anomaly.Id, "acct-1", AnomalyStatus.Dismissed, "admin-user");

            Assert.Equal(AnomalyStatus.Dismissed, updated.Status);
            Assert.NotNull(updated.DateReviewed);
            Assert.Equal("admin-user", updated.ReviewedBy);
        }

        [Fact]
        public async Task UpdateAnomalyStatus_ActionTaken_SetsCorrectly()
        {
            var anomaly = new CreditAnomaly
            {
                AccountId = "acct-1",
                Type = AnomalyType.CircularCreditFlow,
                Severity = AnomalySeverity.High,
                Description = "Circular flow"
            };
            await _cosmo.CreateCreditAnomalyAsync(anomaly);

            var updated = await _cosmo.UpdateCreditAnomalyStatusAsync(
                anomaly.Id, "acct-1", AnomalyStatus.ActionTaken, "admin-2");

            Assert.Equal(AnomalyStatus.ActionTaken, updated.Status);
            Assert.Equal("admin-2", updated.ReviewedBy);
        }

        [Fact]
        public async Task GetAnomalies_AfterStatusUpdate_ReflectsChange()
        {
            var anomaly = new CreditAnomaly
            {
                AccountId = "acct-1",
                Type = AnomalyType.ZeroWorkUptime,
                Severity = AnomalySeverity.Low,
                Description = "Idle host"
            };
            await _cosmo.CreateCreditAnomalyAsync(anomaly);

            await _cosmo.UpdateCreditAnomalyStatusAsync(
                anomaly.Id, "acct-1", AnomalyStatus.Dismissed, "admin");

            var openResult = await _cosmo.GetCreditAnomaliesAsync(status: AnomalyStatus.Open);
            var dismissedResult = await _cosmo.GetCreditAnomaliesAsync(status: AnomalyStatus.Dismissed);

            Assert.Empty(openResult.Items);
            Assert.Single(dismissedResult.Items);
        }

        private async Task SeedAnomaliesAsync()
        {
            await _cosmo.CreateCreditAnomalyAsync(new CreditAnomaly
            {
                AccountId = "acct-1",
                HostId = "host-1",
                Type = AnomalyType.InflatedTokens,
                Severity = AnomalySeverity.Medium,
                Description = "High token count"
            });
            await _cosmo.CreateCreditAnomalyAsync(new CreditAnomaly
            {
                AccountId = "acct-1",
                HostId = "host-2",
                Type = AnomalyType.ZeroWorkUptime,
                Severity = AnomalySeverity.Low,
                Description = "No work"
            });
            await _cosmo.CreateCreditAnomalyAsync(new CreditAnomaly
            {
                AccountId = "acct-2",
                Type = AnomalyType.CircularCreditFlow,
                Severity = AnomalySeverity.High,
                Description = "Circular flow"
            });
        }
    }
}
