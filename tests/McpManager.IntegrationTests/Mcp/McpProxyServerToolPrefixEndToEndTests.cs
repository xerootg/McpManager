using System.Net.Http.Headers;
using AwesomeAssertions;
using McpManager.Core.Data.Models.ApiKeys;
using McpManager.Core.Data.Models.Mcp;
using McpManager.Core.Mcp;
using McpManager.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace McpManager.IntegrationTests.Mcp;

public class McpProxyServerToolPrefixEndToEndTests : IClassFixture<WebFactoryFixture>
{
    private readonly WebFactoryFixture _factory;

    public McpProxyServerToolPrefixEndToEndTests(WebFactoryFixture factory) => _factory = factory;

    [Fact]
    public async Task GlobalMcp_TwoServersWithDistinctPrefixes_ExposesBothAndRoutesCall()
    {
        var ct = TestContext.Current.CancellationToken;
        var apiKey = await SeedTwoPrefixedEchoServersAsync();

        var httpClient = _factory.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            apiKey
        );

        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(httpClient.BaseAddress!, "/mcp") },
            httpClient,
            ownsHttpClient: false
        );
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: ct);

        // Core use case: two instances of the SAME upstream server (both expose an
        // "echo" tool) coexist on /mcp by giving each a distinct ToolPrefix. The
        // exposed names must be the prefixed names (no bare "echo" collision), and a
        // CallTool by a prefixed name must strip the prefix and route to that server's
        // original "echo" tool.
        var tools = await client.ListToolsAsync(cancellationToken: ct);
        tools.Should().Contain(t => t.Name == "alpha_echo");
        tools.Should().Contain(t => t.Name == "beta_echo");
        tools.Should().NotContain(t => t.Name == "echo");

        var result = await client.CallToolAsync(
            "beta_echo",
            new Dictionary<string, object> { ["message"] = "prefixed-hi" },
            cancellationToken: ct
        );

        result.IsError.Should().NotBe(true);
        result
            .Content.OfType<TextContentBlock>()
            .Should()
            .Contain(c => c.Text != null && c.Text.Contains("prefixed-hi"));
    }

    private async Task<string> SeedTwoPrefixedEchoServersAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var serverManager = sp.GetRequiredService<McpServerManager>();
        var apiKeyManager = sp.GetRequiredService<ApiKeyManager>();

        foreach (var prefix in new[] { "alpha", "beta" })
        {
            var server = await serverManager.Create(
                new McpServer
                {
                    Name = $"pfx-{prefix}{Guid.NewGuid():N}",
                    ToolPrefix = prefix,
                    TransportType = McpTransportType.Stdio,
                    Command = "dotnet",
                    Arguments = [TestStdioServerLocator.DllPath],
                }
            );
            var sync = await serverManager.SyncTools(server);
            sync.Success.Should().BeTrue($"SyncTools precondition ({prefix}): {sync.Error}");
        }

        var apiKey = await apiKeyManager.Create(new ApiKey { Name = $"pfx-{Guid.NewGuid():N}" });
        return apiKey.Key;
    }
}
