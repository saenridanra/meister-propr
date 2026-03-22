using MeisterProPR.Domain.ValueObjects;
using MeisterProPR.Infrastructure.AI;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace MeisterProPR.Infrastructure.Tests.AI;

/// <summary>
///     Unit tests for <see cref="AgentAiCommentResolutionCore" />.
///     The <see cref="IChatClient" /> is substituted to avoid real AI calls.
/// </summary>
public sealed class AgentAiCommentResolutionCoreTests
{
    private static PrCommentThread BuildThread(int threadId, params (string author, string content, Guid? authorId)[] comments)
    {
        var prComments = comments
            .Select(c => new PrThreadComment(c.author, c.content, c.authorId))
            .ToList()
            .AsReadOnly();
        return new PrCommentThread(threadId, "/src/Foo.cs", 10, prComments);
    }

    private static PullRequest BuildPr()
    {
        return new PullRequest(
            "https://dev.azure.com/org",
            "proj",
            "repo",
            1,
            2,
            "Fix null-ref bug",
            null,
            "feature/fix",
            "main",
            new List<ChangedFile>().AsReadOnly());
    }

    private static IChatClient BuildChatClient(string jsonResponse)
    {
        var client = Substitute.For<IChatClient>();
        var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, jsonResponse)]);
        client.GetResponseAsync(
                Arg.Any<IList<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
        return client;
    }

    [Fact]
    public async Task EvaluateCodeChangeAsync_WhenAiReturnsResolved_ReturnsIsResolvedTrue()
    {
        var chatClient = BuildChatClient("""{"resolved": true, "replyText": "Fixed in latest commit."}""");
        var sut = new AgentAiCommentResolutionCore(chatClient);
        var thread = BuildThread(1, ("Bot", "Null reference on line 10.", null));
        var pr = BuildPr();

        var result = await sut.EvaluateCodeChangeAsync(thread, pr);

        Assert.True(result.IsResolved);
        Assert.Equal("Fixed in latest commit.", result.ReplyText);
    }

    [Fact]
    public async Task EvaluateCodeChangeAsync_WhenAiReturnsUnresolved_ReturnsIsResolvedFalse()
    {
        var chatClient = BuildChatClient("""{"resolved": false, "replyText": null}""");
        var sut = new AgentAiCommentResolutionCore(chatClient);
        var thread = BuildThread(1, ("Bot", "Potential race condition.", null));
        var pr = BuildPr();

        var result = await sut.EvaluateCodeChangeAsync(thread, pr);

        Assert.False(result.IsResolved);
        Assert.Null(result.ReplyText);
    }

    [Fact]
    public async Task EvaluateCodeChangeAsync_WhenAiIsUncertain_ReturnsIsResolvedFalse()
    {
        // T022: AI must return unresolved when unsure rather than guessing resolved
        var chatClient = BuildChatClient("""{"resolved": false, "replyText": "I'm not sure if this was fully addressed."}""");
        var sut = new AgentAiCommentResolutionCore(chatClient);
        var thread = BuildThread(1, ("Bot", "Consider edge case.", null));
        var pr = BuildPr();

        var result = await sut.EvaluateCodeChangeAsync(thread, pr);

        Assert.False(result.IsResolved);
    }

    [Fact]
    public async Task EvaluateConversationalReplyAsync_ReturnsReplyText()
    {
        var chatClient = BuildChatClient("""{"resolved": false, "replyText": "Great question! This is intentional because..."}""");
        var sut = new AgentAiCommentResolutionCore(chatClient);
        var thread = BuildThread(
            1,
            ("Bot", "Consider using async here.", null),
            ("Dev", "Why async specifically?", null));

        var result = await sut.EvaluateConversationalReplyAsync(thread);

        Assert.False(result.IsResolved);
        Assert.NotNull(result.ReplyText);
        Assert.Contains("Great question", result.ReplyText);
    }

    [Fact]
    public async Task EvaluateCodeChangeAsync_SendsThreadAndDiffContext_ToChatClient()
    {
        var chatClient = BuildChatClient("""{"resolved": true, "replyText": null}""");
        var sut = new AgentAiCommentResolutionCore(chatClient);
        var thread = BuildThread(1, ("Bot", "Missing null check on line 10.", null));
        var pr = BuildPr();

        await sut.EvaluateCodeChangeAsync(thread, pr);

        await chatClient.Received(1)
            .GetResponseAsync(
                Arg.Is<IList<ChatMessage>>(msgs => msgs.Any(m => m.Text != null && m.Text.Contains("Missing null check"))),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateConversationalReplyAsync_SendsThreadHistory_ToChatClient()
    {
        var chatClient = BuildChatClient("""{"resolved": false, "replyText": "Because of X."}""");
        var sut = new AgentAiCommentResolutionCore(chatClient);
        var thread = BuildThread(
            1,
            ("Bot", "Use StringBuilder here.", null),
            ("Dev", "Why StringBuilder?", null));

        await sut.EvaluateConversationalReplyAsync(thread);

        await chatClient.Received(1)
            .GetResponseAsync(
                Arg.Is<IList<ChatMessage>>(msgs => msgs.Any(m => m.Text != null && m.Text.Contains("Why StringBuilder"))),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>());
    }
}
