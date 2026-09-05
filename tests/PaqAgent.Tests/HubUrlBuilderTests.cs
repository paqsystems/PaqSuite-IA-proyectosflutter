using PaqAgent;

namespace PaqAgent.Tests;

public class HubUrlBuilderTests
{
    [Fact]
    public void BuildHubUrl_appendsAgentHubAndQuery()
    {
        var url = HubUrlBuilder.BuildHubUrl(
            "http://127.0.0.1:5100",
            "lab-agent-01",
            "lab",
            "secret-token");

        Assert.Equal(
            "http://127.0.0.1:5100/agent-hub?agentId=lab-agent-01&clientId=lab&agentToken=secret-token",
            url);
    }

    [Fact]
    public void BuildHubUrl_doesNotDuplicateAgentHub()
    {
        var url = HubUrlBuilder.BuildHubUrl(
            "http://127.0.0.1:5100/agent-hub",
            "a",
            "c",
            "t");

        Assert.DoesNotContain("agent-hub/agent-hub", url);
        Assert.Contains("/agent-hub?", url);
    }

    [Fact]
    public void BuildSafeHubUrlForLogs_redactsToken()
    {
        var safe = HubUrlBuilder.BuildSafeHubUrlForLogs(
            "http://127.0.0.1:5100/agent-hub",
            "lab-agent-01",
            "lab");

        Assert.Contains("agentToken=***", safe);
        Assert.DoesNotContain("lab-token", safe);
    }
}
