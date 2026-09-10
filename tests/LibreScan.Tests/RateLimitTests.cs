using LibreScan.ViewModels;
using Xunit;

namespace LibreScan.Tests;

public class RateLimitTests
{
    [Fact]
    public void MinUpdateInterval_IsAtLeast15Minutes_ToPreventCdnBan()
    {
        Assert.True(MainViewModel.MinUpdateInterval >= TimeSpan.FromMinutes(15),
            "Cooldown must be at least 15 minutes to prevent Cloudflare/Cisco CDN IP bans.");
    }

    [Theory]
    [InlineData(5, true)]   // Checked 5 mins ago -> rate limited
    [InlineData(14, true)]  // Checked 14 mins ago -> rate limited
    [InlineData(16, false)] // Checked 16 mins ago -> eligible for update
    [InlineData(60, false)] // Checked 1 hour ago -> eligible for update
    public void CooldownCalculation_RespectsThreshold(int minutesAgo, bool shouldThrottle)
    {
        var lastAttempt = DateTime.UtcNow.AddMinutes(-minutesAgo);
        var elapsed = DateTime.UtcNow - lastAttempt;
        bool isThrottled = elapsed < MainViewModel.MinUpdateInterval;

        Assert.Equal(shouldThrottle, isThrottled);
    }
}
