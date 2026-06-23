using System.Net.Http.Headers;
using AwesomeAssertions;
using McpManager.Core.Data.Models.ApiKeys;
using McpManager.Core.Data.Models.Mcp;
using McpManager.Core.Mcp;
using McpManager.Core.Repositories.Mcp;
using McpManager.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace McpManager.IntegrationTests.Mcp;

public class McpNamespaceProxyServerToolPrefixEndToEndTests : IClassFixture<WebFactoryFixture>
{
    private readonly WebFactoryFixture _factory;

    public McpNamespaceProxyServerToolPrefixEndToEndTests(WebFactoryFixture factory) =>
        _factory = factory;

    [Fact]
    public async Task NamespaceMcp_ServerWithPrefix_PrependsToOverrideNameAndRoutesCall()
    {
        var ct = TestContext.Current.CancellationToken;
        var (apiKey, slug) = await SeedNamespaceWithPrefixedServerAsync();

        var httpClient = _factory.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            apiKey
        );

        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(httpClient.BaseAddress!, $"/mcp/ns/{slug}"),
            },
            httpClient,
            ownsHttpClient: false
        );
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: ct);

        // The server's ToolPrefix applies on top of the per-tool name override: the
        // tool is renamed "echo_renamed" and the server prefix is "grp_", so the
        // exposed name is "grp_echo_renamed". A CallTool by that name must strip the
        // prefix/override and route to the original upstream "echo" tool.
        var tools = await client.ListToolsAsync(cancellationToken: ct);
        tools.Should().Contain(t => t.Name == "grp_echo_renamed");
        tools.Should().NotContain(t => t.Name == "echo");
        tools.Should().NotContain(t => t.Name == "echo_renamed");

        var result = await client.CallToolAsync(
            "grp_echo_renamed",
            new Dictionary<string, object> { ["message"] = "ns-prefix-hi" },
            cancellationToken: ct
        );

        result.IsError.Should().NotBe(true);
        result
            .Content.OfType<TextContentBlock>()
            .Should()
            .Contain(c => c.Text != null && c.Text.Contains("ns-prefix-hi"));
    }

    private async Task<(string apiKey, string slug)> SeedNamespaceWithPrefixedServerAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var serverManager = sp.GetRequiredService<McpServerManager>();
        var namespaceManager = sp.GetRequiredService<McpNamespaceManager>();
        var nsToolRepo = sp.GetRequiredService<McpNamespaceToolRepository>();
        var apiKeyManager = sp.GetRequiredService<ApiKeyManager>();

        var server = await serverManager.Create(
            new McpServer
            {
                Name = $"nspfx-srv-{Guid.NewGuid():N}",
                ToolPrefix = "grp",
                TransportType = McpTransportType.Stdio,
                Command = "dotnet",
                Arguments = [TestStdioServerLocator.DllPath],
            }
        );
        var sync = await serverManager.SyncTools(server);
        sync.Success.Should().BeTrue($"server-level SyncTools precondition: {sync.Error}");

        var slug = $"e2epfx{Guid.NewGuid():N}"[..16];
        var ns = await namespaceManager.Create(new McpNamespace { Name = "Prefix NS", Slug = slug });
        var nsServer = await namespaceManager.AddServer(ns, server);

        var nsTool = await nsToolRepo
            .GetByNamespaceServer(nsServer)
            .Include(t => t.McpTool)
            .SingleAsync();
        await namespaceManager.UpdateToolOverride(nsTool, "echo_renamed", null);

        var apiKey = await apiKeyManager.Create(new ApiKey { Name = $"nspfx-{Guid.NewGuid():N}" });
        return (apiKey.Key, slug);
    }
}
