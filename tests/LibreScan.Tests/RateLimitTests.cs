using LibreScan.ViewModels;
using Xunit;

namespace LibreScan.Tests;

public class RateLimitTests
{
    [Fact]
    public void MinUpdateInterval_IsAtLeast2Hours_ToPreventCdnBan()
    {
        Assert.True(MainViewModel.MinUpdateInterval >= TimeSpan.FromHours(2),
            "Cooldown must be at least 2 hours to prevent Cloudflare/Cisco CDN IP bans.");
    }

    [Theory]
    [InlineData(5, true)]   // Checked 5 mins ago -> rate limited
    [InlineData(119, true)] // Checked 119 mins ago -> rate limited
    [InlineData(121, false)] // Checked 121 mins ago -> eligible for update
    [InlineData(180, false)] // Checked 3 hours ago -> eligible for update
    public void CooldownCalculation_RespectsThreshold(int minutesAgo, bool shouldThrottle)
    {
        var lastAttempt = DateTime.UtcNow.AddMinutes(-minutesAgo);
        var elapsed = DateTime.UtcNow - lastAttempt;
        bool isThrottled = elapsed < MainViewModel.MinUpdateInterval;

        Assert.Equal(shouldThrottle, isThrottled);
    }
}
