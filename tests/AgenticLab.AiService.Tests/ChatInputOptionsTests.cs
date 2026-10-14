using AgenticLab.AiService.Application.Conversations;
using Xunit;

namespace AgenticLab.AiService.Tests;

public sealed class ChatInputOptionsTests
{
    [Theory]
    [InlineData(0, 10_000, true)]
    [InlineData(-1, 10_000, true)]
    [InlineData(500, 500, true)]
    [InlineData(500, 501, false)]
    [InlineData(500, 0, true)]
    public void Allows_TextUpToTheLimit(int limit, int length, bool allowed) =>
        Assert.Equal(allowed, new ChatInputOptions { MaxMessageLength = limit }.Allows(new string('x', length)));

    [Fact]
    public void Allows_NullAsEmpty() =>
        Assert.True(new ChatInputOptions { MaxMessageLength = 5 }.Allows(null));

    [Fact]
    public void TooLongMessage_StatesTheLimit() =>
        Assert.Equal("Messages can be at most 500 characters.", new ChatInputOptions { MaxMessageLength = 500 }.TooLongMessage);
}
