namespace Daisi.Integration.Tests.Infrastructure;

/// <summary>
/// Defines a test collection that shares a single IntegrationTestFixture
/// across all test classes. This ensures only one ORC server and host client
/// are created for the entire test run.
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
}
