using MeisterProPR.Domain.ValueObjects;
using MeisterProPR.Infrastructure.AzureDevOps;

namespace MeisterProPR.Infrastructure.Tests.AzureDevOps;

/// <summary>
///     Tests for the bot-content detection helper and deduplication filtering logic
///     in AdoCommentPoster. These tests exercise pure logic without real ADO calls.
/// </summary>
public class AdoCommentPosterDeduplicationTests
{
    // ── IsBotContent ──────────────────────────────────────────────────────────

    [Fact]
    public void IsBotContent_SummaryPrefix_ReturnsTrue()
    {
        Assert.True(AdoCommentPoster.IsBotContent("**AI Review Summary**\n\nLooks good."));
    }

    [Theory]
    [InlineData("ERROR: Null reference here.")]
    [InlineData("WARNING: Unused variable.")]
    [InlineData("SUGGESTION: Use var instead.")]
    [InlineData("INFO: Consider adding a comment.")]
    public void IsBotContent_SeverityPrefix_ReturnsTrue(string content)
    {
        Assert.True(AdoCommentPoster.IsBotContent(content));
    }

    [Theory]
    [InlineData("Looks good to me.")]
    [InlineData("Fixed in this commit.")]
    [InlineData("LGTM")]
    [InlineData("error: lowercase prefix is not a bot comment")]
    public void IsBotContent_UserComment_ReturnsFalse(string content)
    {
        Assert.False(AdoCommentPoster.IsBotContent(content));
    }

    [Fact]
    public void IsBotContent_EmptyString_ReturnsFalse()
    {
        Assert.False(AdoCommentPoster.IsBotContent(""));
    }

    // ── HasBotSummary ─────────────────────────────────────────────────────────

    [Fact]
    public void HasBotSummary_WithExistingSummaryThread_ReturnsTrue()
    {
        var threads = new List<PrCommentThread>
        {
            new(1, null, null, new List<PrThreadComment>
            {
                new("Bot", "**AI Review Summary**\n\nLooks good."),
            }.AsReadOnly()),
        };

        Assert.True(AdoCommentPoster.HasBotSummary(threads));
    }

    [Fact]
    public void HasBotSummary_WithNoThreads_ReturnsFalse()
    {
        Assert.False(AdoCommentPoster.HasBotSummary([]));
    }

    [Fact]
    public void HasBotSummary_WithOnlyInlineThreads_ReturnsFalse()
    {
        var threads = new List<PrCommentThread>
        {
            new(1, "/src/Foo.cs", 5, new List<PrThreadComment>
            {
                new("Bot", "ERROR: Null ref."),
            }.AsReadOnly()),
        };

        Assert.False(AdoCommentPoster.HasBotSummary(threads));
    }

    // ── HasBotThreadAt ────────────────────────────────────────────────────────

    [Fact]
    public void HasBotThreadAt_BotThreadAtSameFileAndLine_ReturnsTrue()
    {
        var threads = new List<PrCommentThread>
        {
            new(1, "/src/Foo.cs", 42, new List<PrThreadComment>
            {
                new("Bot", "ERROR: Null ref."),
            }.AsReadOnly()),
        };

        Assert.True(AdoCommentPoster.HasBotThreadAt(threads, "/src/Foo.cs", 42));
    }

    [Fact]
    public void HasBotThreadAt_BotThreadAtDifferentLine_ReturnsFalse()
    {
        var threads = new List<PrCommentThread>
        {
            new(1, "/src/Foo.cs", 99, new List<PrThreadComment>
            {
                new("Bot", "ERROR: Different line."),
            }.AsReadOnly()),
        };

        Assert.False(AdoCommentPoster.HasBotThreadAt(threads, "/src/Foo.cs", 42));
    }

    [Fact]
    public void HasBotThreadAt_UserThreadAtSameLocation_ReturnsFalse()
    {
        var threads = new List<PrCommentThread>
        {
            new(1, "/src/Foo.cs", 42, new List<PrThreadComment>
            {
                new("Alice", "Fixed this."),
            }.AsReadOnly()),
        };

        Assert.False(AdoCommentPoster.HasBotThreadAt(threads, "/src/Foo.cs", 42));
    }

    [Fact]
    public void HasBotThreadAt_NoExistingThreads_ReturnsFalse()
    {
        Assert.False(AdoCommentPoster.HasBotThreadAt([], "/src/Foo.cs", 42));
    }

    [Fact]
    public void HasBotThreadAt_NullFilePath_ReturnsFalse()
    {
        var threads = new List<PrCommentThread>
        {
            new(1, null, null, new List<PrThreadComment>
            {
                new("Bot", "**AI Review Summary**\n\nSummary."),
            }.AsReadOnly()),
        };

        // null filePath on inline check means PR-level; inline search uses non-null path
        Assert.False(AdoCommentPoster.HasBotThreadAt(threads, null, null));
    }
}
