namespace Shelf.Contracts;

/// <summary>
/// Published by the API after an order is saved. The fulfilment worker consumes it.
/// Both services reference this one type because MassTransit routes by the message type's full name.
/// </summary>
public record OrderPlaced(Guid OrderId, decimal Total, DateTime PlacedAtUtc);
