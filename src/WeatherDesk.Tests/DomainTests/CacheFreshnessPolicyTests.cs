using WeatherDesk.Domain.Rules;

namespace WeatherDesk.Tests.DomainTests;

public sealed class CacheFreshnessPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    [Fact]
    public void Snapshot_retrieved_five_minutes_ago_is_fresh()
    {
        var snapshot = DomainTestData.CreateSnapshot(Now.AddMinutes(-5));

        Assert.True(CacheFreshnessPolicy.IsFresh(snapshot, Now, Ttl));
        Assert.Equal(TimeSpan.FromMinutes(5), CacheFreshnessPolicy.AgeOf(snapshot, Now));
    }

    [Fact]
    public void Snapshot_older_than_ttl_is_stale()
    {
        var snapshot = DomainTestData.CreateSnapshot(Now.AddHours(-2));

        Assert.False(CacheFreshnessPolicy.IsFresh(snapshot, Now, Ttl));
        Assert.Equal(TimeSpan.FromHours(2), CacheFreshnessPolicy.AgeOf(snapshot, Now));
    }

    [Fact]
    public void Snapshot_at_ttl_boundary_is_fresh_and_negative_ttl_is_never_fresh()
    {
        var snapshot = DomainTestData.CreateSnapshot(Now.Add(Ttl));

        Assert.True(CacheFreshnessPolicy.IsFresh(snapshot, Now.Add(Ttl), Ttl));
        Assert.False(CacheFreshnessPolicy.IsFresh(snapshot, Now.Add(Ttl), TimeSpan.FromTicks(-1)));
    }

    [Fact]
    public void Future_retrieval_timestamp_has_zero_age()
    {
        var snapshot = DomainTestData.CreateSnapshot(Now.AddMinutes(5));

        Assert.Equal(TimeSpan.Zero, CacheFreshnessPolicy.AgeOf(snapshot, Now));
        Assert.True(CacheFreshnessPolicy.IsFresh(snapshot, Now, Ttl));
    }
}
