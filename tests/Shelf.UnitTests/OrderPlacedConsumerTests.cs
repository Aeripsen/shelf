using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shelf.Contracts;
using Shelf.Fulfilment;

namespace Shelf.UnitTests;

/// <summary>
/// Runs the real consumer and its real retry policy (OrderPlacedConsumerDefinition) on MassTransit's in-memory
/// test harness, with the database-facing fulfilment replaced by a fake.
/// </summary>
public class OrderPlacedConsumerTests
{
    private sealed class FlakyFulfilment(int failuresBeforeSuccess) : IOrderFulfilment
    {
        private int _calls;
        public int Calls => _calls;
        public List<Guid> SeenOrderIds { get; } = new();
        public TaskCompletionSource Succeeded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<FulfilmentResult> HandleAsync(Guid orderId, CancellationToken ct = default)
        {
            lock (SeenOrderIds)
                SeenOrderIds.Add(orderId);
            if (Interlocked.Increment(ref _calls) <= failuresBeforeSuccess)
                throw new TimeoutException("simulated transient database timeout");
            Succeeded.TrySetResult();
            return Task.FromResult(FulfilmentResult.Fulfilled);
        }
    }

    private static ServiceProvider BuildProvider(IOrderFulfilment fulfilment) =>
        new ServiceCollection()
            .AddLogging()
            .AddSingleton(fulfilment)
            .AddMassTransitTestHarness(x =>
            {
                x.SetTestTimeouts(testInactivityTimeout: TimeSpan.FromSeconds(3));
                x.AddConsumer<OrderPlacedConsumer, OrderPlacedConsumerDefinition>();
            })
            .BuildServiceProvider(true);

    [Test]
    public async Task Consumer_passes_the_message_order_id_to_fulfilment()
    {
        var fake = new FlakyFulfilment(failuresBeforeSuccess: 0);
        await using var provider = BuildProvider(fake);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var orderId = Guid.NewGuid();
        await harness.Bus.Publish(new OrderPlaced(orderId, 11.50m, DateTime.UtcNow));

        Assert.That(await harness.GetConsumerHarness<OrderPlacedConsumer>().Consumed.Any<OrderPlaced>(m => m.Context.Message.OrderId == orderId), Is.True);
        Assert.That(fake.SeenOrderIds, Is.EqualTo(new[] { orderId }));
    }

    [Test]
    public async Task Transient_failure_is_retried_and_the_message_is_not_faulted()
    {
        var fake = new FlakyFulfilment(failuresBeforeSuccess: 1);
        await using var provider = BuildProvider(fake);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var orderId = Guid.NewGuid();
        await harness.Bus.Publish(new OrderPlaced(orderId, 11.50m, DateTime.UtcNow));

        // First retry interval is 1 second, so success should arrive well inside 10.
        var finished = await Task.WhenAny(fake.Succeeded.Task, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Multiple(async () =>
        {
            Assert.That(finished, Is.SameAs(fake.Succeeded.Task), "the retry never succeeded");
            Assert.That(fake.Calls, Is.EqualTo(2), "one failure, then one successful retry");
            Assert.That(await harness.Published.Any<Fault<OrderPlaced>>(), Is.False, "a retried success must not publish a Fault");
        });
    }

    [Test]
    public async Task Configured_policy_waits_1_then_5_seconds_between_attempts()
    {
        // Two failures, so success needs the first two intervals of the real policy: 1 s, then 5 s.
        // A policy with shorter or fewer intervals finishes too early or never succeeds.
        var fake = new FlakyFulfilment(failuresBeforeSuccess: 2);
        await using var provider = BuildProvider(fake);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        await harness.Bus.Publish(new OrderPlaced(Guid.NewGuid(), 11.50m, DateTime.UtcNow));
        var finished = await Task.WhenAny(fake.Succeeded.Task, Task.Delay(TimeSpan.FromSeconds(20)));
        clock.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(finished, Is.SameAs(fake.Succeeded.Task), "the second retry never succeeded");
            Assert.That(fake.Calls, Is.EqualTo(3));
            Assert.That(clock.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(5.5)), "1 s + 5 s of retry delay expected");
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(15)), "success should not wait for the 15 s interval");
        });
    }
}
