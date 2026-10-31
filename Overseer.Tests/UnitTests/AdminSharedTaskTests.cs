using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class AdminSharedTaskTests
{
    [Theory]
    [InlineData("snapshot-refusals:42", "snapshot-refusals", 42L)]
    [InlineData("snapshot-autoblock:7", "snapshot-autoblock", 7L)]
    [InlineData("ai-spend:20261031:3", "ai-spend:20261031", 3L)]
    public void ASharedTaskId_ParsesIntoItsKeyAndSequence(string alertId, string key, long sequence)
    {
        Assert.True(AdminSharedTask.TryParse(alertId, out var task));
        Assert.Equal(key, task.Key);
        Assert.Equal(sequence, task.Sequence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("account-deletion-review:5")]
    [InlineData("sentry-dsn-missing")]
    [InlineData("snapshot-guard-observe")]
    [InlineData("snapshot-refusals")]
    [InlineData("snapshot-refusals:abc")]
    [InlineData("snapshot-refusals:0")]
    [InlineData("snapshot-refusals:-1")]
    [InlineData("snapshot-refusals:+1")]
    [InlineData("snapshot-refusals:1:2")]
    [InlineData("snapshot-refusals:99999999999999999999")]
    [InlineData("ai-spend:2026:3")]
    [InlineData("ai-spend:2026103x:3")]
    [InlineData("ai-spend:20261031")]
    [InlineData("ai-spend:20261031:0")]
    public void AnyOtherId_IsNotASharedTask(string? alertId)
    {
        Assert.False(AdminSharedTask.TryParse(alertId, out _));
    }

    [Fact]
    public void AnOversizedId_IsNotASharedTask()
    {
        Assert.False(AdminSharedTask.TryParse("snapshot-refusals:" + new string('1', 200), out _));
    }
}
