using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Shelf.Core.Data;

public class ShelfDbContext(DbContextOptions<ShelfDbContext> options) : DbContext(options)
{
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Cart>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).ValueGeneratedNever();
            e.HasMany(c => c.Items).WithOne().HasForeignKey(i => i.CartId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CartItem>(e =>
        {
            e.Property(i => i.BookId).HasMaxLength(200).IsRequired();
            e.HasIndex(i => new { i.CartId, i.BookId }).IsUnique();
            e.Property(i => i.RowVersion).IsRowVersion();
        });

        b.Entity<Order>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.Id).ValueGeneratedNever();
            e.Property(o => o.Email).HasMaxLength(Domain.CheckoutValidator.MaxEmailLength).IsRequired();
            e.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(o => o.Total).HasPrecision(10, 2);
            e.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<OrderLine>(e =>
        {
            e.Property(l => l.BookId).HasMaxLength(200).IsRequired();
            e.Property(l => l.Title).HasMaxLength(300).IsRequired();
            e.Property(l => l.UnitPrice).HasPrecision(10, 2);
        });

        b.Entity<ProcessedMessage>(e =>
        {
            e.HasKey(p => p.OrderId);
            e.Property(p => p.OrderId).ValueGeneratedNever();
        });

        // MassTransit's transactional outbox. Checkout publishes OrderPlaced into OutboxMessage/OutboxState rows in
        // the same SaveChanges as the order, and a background service in the API sends them to RabbitMQ afterwards.
        // InboxState is part of the same schema; the fulfilment worker does not use it (it dedupes by OrderId).
        b.AddTransactionalOutboxEntities();
    }
}
