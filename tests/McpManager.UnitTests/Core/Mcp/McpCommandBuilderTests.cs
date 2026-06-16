using AwesomeAssertions;
using McpManager.Core.Mcp;
using Xunit;

namespace McpManager.UnitTests.Core.Mcp;

public class McpCommandBuilderTests
{
    [Fact]
    public void BuildCommandPreview_BlankCommand_ReturnsEmpty()
    {
        var result = McpCommandBuilder.BuildCommandPreview("   ", ["a", "b"]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildCommandPreview_ArgumentWithSpace_IsQuoted()
    {
        var result = McpCommandBuilder.BuildCommandPreview("npx", ["-y", "@scope/pkg name"]);

        result.Should().Be("npx -y \"@scope/pkg name\"");
    }

    [Fact]
    public void BuildCommandPreview_NullArguments_ReturnsCommandOnly()
    {
        var result = McpCommandBuilder.BuildCommandPreview("node", null);

        result.Should().Be("node");
    }

    [Fact]
    public void BuildCommandPreview_EmptyArgumentEntries_AreSkipped()
    {
        var result = McpCommandBuilder.BuildCommandPreview("node", ["", "server.js", ""]);

        result.Should().Be("node server.js");
    }

    [Fact]
    public void BuildPackageRunnerCommand_Npx_NoExtraArguments_ReturnsYFlagAndPackage()
    {
        var (command, arguments) = McpCommandBuilder.BuildPackageRunnerCommand(
            PackageRunner.Npx,
            "@scope/pkg",
            null
        );

        command.Should().Be("npx");
        arguments.Should().Equal("-y", "@scope/pkg");
    }

    [Fact]
    public void BuildPackageRunnerCommand_Npx_WithExtraArguments_SplitsByNewlineAndTrims()
    {
        var (command, arguments) = McpCommandBuilder.BuildPackageRunnerCommand(
            PackageRunner.Npx,
            "@scope/pkg",
            "  --flag\n--other  \n\n--last"
        );

        command.Should().Be("npx");
        arguments.Should().Equal("-y", "@scope/pkg", "--flag", "--other", "--last");
    }

    [Fact]
    public void BuildPackageRunnerCommand_Uvx_OmitsYFlagAndPrefixesPackage()
    {
        var (command, arguments) = McpCommandBuilder.BuildPackageRunnerCommand(
            PackageRunner.Uvx,
            "mcp-server-git",
            null
        );

        command.Should().Be("uvx");
        arguments.Should().Equal("mcp-server-git");
    }

    [Fact]
    public void BuildPackageRunnerCommand_Uvx_WithExtraArguments_AppendsAfterPackage()
    {
        var (command, arguments) = McpCommandBuilder.BuildPackageRunnerCommand(
            PackageRunner.Uvx,
            "mcp-server-git",
            "--repository\n/tmp/repo"
        );

        command.Should().Be("uvx");
        arguments.Should().Equal("mcp-server-git", "--repository", "/tmp/repo");
    }

    [Fact]
    public void BuildPackageRunnerCommand_BlankPackage_IsOmitted()
    {
        var (command, arguments) = McpCommandBuilder.BuildPackageRunnerCommand(
            PackageRunner.Uvx,
            "  ",
            null
        );

        command.Should().Be("uvx");
        arguments.Should().BeEmpty();
    }

    [Fact]
    public void ParseSimplePackageCommand_NpxWithYFlag_ReturnsNpxRunnerPackageAndExtras()
    {
        var result = McpCommandBuilder.ParseSimplePackageCommand(
            "npx",
            ["-y", "@scope/pkg", "/tmp", "--verbose"]
        );

        result.Should().NotBeNull();
        result!.Runner.Should().Be(PackageRunner.Npx);
        result.Package.Should().Be("@scope/pkg");
        result.ExtraArguments.Should().Equal("/tmp", "--verbose");
    }

    [Fact]
    public void ParseSimplePackageCommand_Uvx_ReturnsUvxRunnerPackageAndExtras()
    {
        var result = McpCommandBuilder.ParseSimplePackageCommand(
            "uvx",
            ["mcp-server-git", "--repository", "/tmp/repo"]
        );

        result.Should().NotBeNull();
        result!.Runner.Should().Be(PackageRunner.Uvx);
        result.Package.Should().Be("mcp-server-git");
        result.ExtraArguments.Should().Equal("--repository", "/tmp/repo");
    }

    [Fact]
    public void ParseSimplePackageCommand_NpxWithoutYFlag_ReturnsNull()
    {
        // Without the leading "-y" this is a custom command, not simple mode.
        var result = McpCommandBuilder.ParseSimplePackageCommand("npx", ["@scope/pkg"]);

        result.Should().BeNull();
    }

    [Fact]
    public void ParseSimplePackageCommand_CustomCommand_ReturnsNull()
    {
        var result = McpCommandBuilder.ParseSimplePackageCommand("python", ["-m", "server"]);

        result.Should().BeNull();
    }

    [Fact]
    public void ParseSimplePackageCommand_RoundTripsBuildPackageRunnerCommand()
    {
        var (command, arguments) = McpCommandBuilder.BuildPackageRunnerCommand(
            PackageRunner.Uvx,
            "mcp-server-git",
            "--repository\n/tmp/repo"
        );

        var result = McpCommandBuilder.ParseSimplePackageCommand(command, arguments);

        result.Should().NotBeNull();
        result!.Runner.Should().Be(PackageRunner.Uvx);
        result.Package.Should().Be("mcp-server-git");
        result.ExtraArguments.Should().Equal("--repository", "/tmp/repo");
    }

    [Fact]
    public void BuildCustomCommand_BlankArgumentsText_ReturnsEmptyArguments()
    {
        var (command, arguments) = McpCommandBuilder.BuildCustomCommand("python", "");

        command.Should().Be("python");
        arguments.Should().BeEmpty();
    }

    [Fact]
    public void BuildCustomCommand_NewlineSeparatedArguments_AreParsed()
    {
        var (command, arguments) = McpCommandBuilder.BuildCustomCommand(
            "python",
            "-m\nmymodule\n--port=8080"
        );

        command.Should().Be("python");
        arguments.Should().Equal("-m", "mymodule", "--port=8080");
    }
}
