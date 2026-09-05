using PaqAgent.Options;

namespace PaqAgent.Tests;

public class AgentOptionsTests
{
    [Fact]
    public void HasRequiredIdentity_false_whenTokenMissing()
    {
        var options = new AgentOptions
        {
            AgentId = "a",
            ClientId = "c",
            AgentToken = "",
            GatewayUrl = "http://127.0.0.1:5100/agent-hub"
        };

        Assert.False(options.HasRequiredIdentity);
    }

    [Fact]
    public void HasRequiredIdentity_true_whenComplete()
    {
        var options = new AgentOptions
        {
            AgentId = "a",
            ClientId = "c",
            AgentToken = "token",
            GatewayUrl = "http://127.0.0.1:5100/agent-hub"
        };

        Assert.True(options.HasRequiredIdentity);
    }
}
