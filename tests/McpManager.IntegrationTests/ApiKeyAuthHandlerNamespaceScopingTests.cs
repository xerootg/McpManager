using System.Net.Http.Headers;
using AwesomeAssertions;
using McpManager.Core.Data.Models.ApiKeys;
using McpManager.Core.Data.Models.Mcp;
using McpManager.Core.Mcp;
using McpManager.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace McpManager.IntegrationTests;

public class ApiKeyAuthHandlerNamespaceScopingTests : IClassFixture<WebFactoryFixture>
{
    private readonly WebFactoryFixture _factory;

    public ApiKeyAuthHandlerNamespaceScopingTests(WebFactoryFixture factory) => _factory = factory;

    [Fact]
    public async Task ScopedKey_OnlyAuthenticatesOnItsOwnNamespaceEndpoint()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );

        string scopedKey;
        string allowedSlug;
        string otherSlug;
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var nsManager = sp.GetRequiredService<McpNamespaceManager>();
            var allowedNs = await nsManager.Create(
                new McpNamespace
                {
                    Name = $"Scope A {Guid.NewGuid():N}",
                    Slug = $"scope-a-{Guid.NewGuid():N}",
                    Description = "scoping test",
                }
            );
            var otherNs = await nsManager.Create(
                new McpNamespace
                {
                    Name = $"Scope B {Guid.NewGuid():N}",
                    Slug = $"scope-b-{Guid.NewGuid():N}",
                    Description = "scoping test",
                }
            );
            allowedSlug = allowedNs.Slug;
            otherSlug = otherNs.Slug;

            var created = await sp.GetRequiredService<ApiKeyManager>()
                .Create(
                    new ApiKey
                    {
                        Name = $"scoped-{Guid.NewGuid():N}",
                        AllowedNamespaces = [allowedNs],
                    }
                );
            scopedKey = created.Key;
        }

        // Security contract: a key scoped to namespace A must be rejected on the
        // global /mcp endpoint (which exposes every server) and on any other
        // namespace's endpoint, while still authenticating on its own endpoint.
        var onGlobal = await PostMcpAsync(client, "/mcp", scopedKey, ct);
        ((int)onGlobal.StatusCode)
            .Should()
            .BeInRange(401, 403, "a scoped key must not authenticate on the global /mcp endpoint");

        var onOtherNamespace = await PostMcpAsync(client, $"/mcp/ns/{otherSlug}", scopedKey, ct);
        ((int)onOtherNamespace.StatusCode)
            .Should()
            .BeInRange(401, 403, "a scoped key must not authenticate on another namespace");

        var onAllowedNamespace = await PostMcpAsync(
            client,
            $"/mcp/ns/{allowedSlug}",
            scopedKey,
            ct
        );
        ((int)onAllowedNamespace.StatusCode)
            .Should()
            .NotBeInRange(401, 403, "a scoped key must authenticate on its allowed namespace");
    }

    [Fact]
    public async Task UnscopedKey_AuthenticatesOnGlobalAndNamespaceEndpoints()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );

        string unscopedKey;
        string slug;
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var ns = await sp.GetRequiredService<McpNamespaceManager>()
                .Create(
                    new McpNamespace
                    {
                        Name = $"Open {Guid.NewGuid():N}",
                        Slug = $"open-{Guid.NewGuid():N}",
                        Description = "scoping test",
                    }
                );
            slug = ns.Slug;

            var created = await sp.GetRequiredService<ApiKeyManager>()
                .Create(new ApiKey { Name = $"unscoped-{Guid.NewGuid():N}" });
            unscopedKey = created.Key;
        }

        var onGlobal = await PostMcpAsync(client, "/mcp", unscopedKey, ct);
        ((int)onGlobal.StatusCode)
            .Should()
            .NotBeInRange(401, 403, "an unscoped key keeps full access to /mcp");

        var onNamespace = await PostMcpAsync(client, $"/mcp/ns/{slug}", unscopedKey, ct);
        ((int)onNamespace.StatusCode)
            .Should()
            .NotBeInRange(401, 403, "an unscoped key can use any namespace endpoint");
    }

    private static async Task<HttpResponseMessage> PostMcpAsync(
        HttpClient client,
        string path,
        string key,
        CancellationToken ct
    )
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}",
                System.Text.Encoding.UTF8,
                "application/json"
            ),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return await client.SendAsync(request, ct);
    }
}
