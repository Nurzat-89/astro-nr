using AstroNucleaus.Domain.Common;

namespace AstroNucleaus.Domain.Tests;

public class EntityTests
{
    private sealed class TestEntity : Entity;

    [Fact]
    public void NewEntity_HasNonEmptyId() => Assert.NotEqual(Guid.Empty, new TestEntity().Id);
}
