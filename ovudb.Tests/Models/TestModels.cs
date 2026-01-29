namespace ovudb.Tests.Models;

public class TestEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public bool IsActive { get; set; }
}

public class TestEntityWithoutId
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class TestEntityWithLongId
{
    public long EntityId { get; set; }
    public string Description { get; set; } = string.Empty;
}
