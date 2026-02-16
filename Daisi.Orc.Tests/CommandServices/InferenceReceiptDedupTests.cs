using Daisi.Orc.Grpc.CommandServices.HostCommandHandlers;

namespace Daisi.Orc.Tests.CommandServices
{
    public class InferenceReceiptDedupTests : IDisposable
    {
        public InferenceReceiptDedupTests()
        {
            // Clear the static dictionary before each test
            InferenceReceiptCommandHandler.ProcessedReceipts.Clear();
        }

        public void Dispose()
        {
            InferenceReceiptCommandHandler.ProcessedReceipts.Clear();
        }

        [Fact]
        public void TryAdd_FirstReceipt_Succeeds()
        {
            var key = "host-1:inf-001";
            var result = InferenceReceiptCommandHandler.ProcessedReceipts.TryAdd(key, DateTime.UtcNow);

            Assert.True(result);
            Assert.True(InferenceReceiptCommandHandler.ProcessedReceipts.ContainsKey(key));
        }

        [Fact]
        public void TryAdd_DuplicateReceipt_Fails()
        {
            var key = "host-1:inf-001";
            InferenceReceiptCommandHandler.ProcessedReceipts.TryAdd(key, DateTime.UtcNow);

            var result = InferenceReceiptCommandHandler.ProcessedReceipts.TryAdd(key, DateTime.UtcNow);

            Assert.False(result);
            Assert.Single(InferenceReceiptCommandHandler.ProcessedReceipts);
        }

        [Fact]
        public void TryAdd_DifferentInferenceIds_BothSucceed()
        {
            var key1 = "host-1:inf-001";
            var key2 = "host-1:inf-002";

            var result1 = InferenceReceiptCommandHandler.ProcessedReceipts.TryAdd(key1, DateTime.UtcNow);
            var result2 = InferenceReceiptCommandHandler.ProcessedReceipts.TryAdd(key2, DateTime.UtcNow);

            Assert.True(result1);
            Assert.True(result2);
            Assert.Equal(2, InferenceReceiptCommandHandler.ProcessedReceipts.Count);
        }

        [Fact]
        public void TryAdd_SameInferenceDifferentHosts_BothSucceed()
        {
            var key1 = "host-1:inf-001";
            var key2 = "host-2:inf-001";

            var result1 = InferenceReceiptCommandHandler.ProcessedReceipts.TryAdd(key1, DateTime.UtcNow);
            var result2 = InferenceReceiptCommandHandler.ProcessedReceipts.TryAdd(key2, DateTime.UtcNow);

            Assert.True(result1);
            Assert.True(result2);
        }

        [Fact]
        public void ExpiredEntries_CanBeDetectedByTimestamp()
        {
            var expiredTime = DateTime.UtcNow.AddHours(-25);
            var recentTime = DateTime.UtcNow;

            InferenceReceiptCommandHandler.ProcessedReceipts.TryAdd("host-1:old", expiredTime);
            InferenceReceiptCommandHandler.ProcessedReceipts.TryAdd("host-1:new", recentTime);

            // Verify we can identify expired entries by checking timestamps
            var cutoff = DateTime.UtcNow.AddHours(-24);
            var expiredKeys = InferenceReceiptCommandHandler.ProcessedReceipts
                .Where(kvp => kvp.Value < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            Assert.Single(expiredKeys);
            Assert.Equal("host-1:old", expiredKeys[0]);

            // Clean them up
            foreach (var key in expiredKeys)
                InferenceReceiptCommandHandler.ProcessedReceipts.TryRemove(key, out _);

            Assert.Single(InferenceReceiptCommandHandler.ProcessedReceipts);
            Assert.True(InferenceReceiptCommandHandler.ProcessedReceipts.ContainsKey("host-1:new"));
        }
    }
}
