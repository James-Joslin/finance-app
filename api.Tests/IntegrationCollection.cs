using Xunit;

namespace financesApi.Tests;

[CollectionDefinition("Finova database integration", DisableParallelization = true)]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationTestMarker>
{
}

public sealed class IntegrationTestMarker
{
}
