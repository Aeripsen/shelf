namespace Shelf.UnitTests;

/// <summary>A clock the test controls.</summary>
public class FakeTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}
