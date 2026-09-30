namespace Shelf.Core.Data;

public class Cart
{
    public Guid Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<CartItem> Items { get; set; } = new();
}

public class CartItem
{
    public int Id { get; set; }
    public Guid CartId { get; set; }
    public string BookId { get; set; } = "";
    public int Quantity { get; set; }

    /// <summary>
    /// SQL Server rowversion, used as an optimistic concurrency token. An update or delete made from a stale read
    /// fails with DbUpdateConcurrencyException instead of silently overwriting a newer change.
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public enum OrderStatus
{
    Placed,
    Fulfilled,
}

public class Order
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public OrderStatus Status { get; set; }
    public decimal Total { get; set; }
    public DateTime PlacedAtUtc { get; set; }
    public DateTime? FulfilledAtUtc { get; set; }
    public List<OrderLine> Lines { get; set; } = new();
}

/// <summary>Title and price are copied from the catalogue at checkout, so a later price change never rewrites a past order.</summary>
public class OrderLine
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public string BookId { get; set; } = "";
    public string Title { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

/// <summary>
/// One row per OrderPlaced message the worker has handled. OrderId is the primary key, which is what makes a
/// redelivered message a no-op even when two copies race.
/// </summary>
public class ProcessedMessage
{
    public Guid OrderId { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}
