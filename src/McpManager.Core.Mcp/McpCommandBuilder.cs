using McpManager.Core.Data.Models.Mcp;

namespace McpManager.Core.Mcp;

public static class McpCommandBuilder
{
    /// <summary>
    /// Builds command preview from an McpServer entity.
    /// </summary>
    public static string BuildCommandPreview(McpServer server)
    {
        return BuildCommandPreview(server.Command, server.Arguments);
    }

    /// <summary>
    /// Builds a displayable command string from explicit command and arguments.
    /// Arguments containing spaces are quoted.
    /// </summary>
    public static string BuildCommandPreview(string command, List<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(command))
            return "";

        var parts = new List<string> { command };
        foreach (var arg in arguments ?? [])
        {
            if (string.IsNullOrEmpty(arg))
                continue;
            parts.Add(arg.Contains(' ') ? $"\"{arg}\"" : arg);
        }
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Builds a package-runner command (npx/uvx) from a package name and optional
    /// extra arguments (newline-separated).
    /// </summary>
    public static (string Command, List<string> Arguments) BuildPackageRunnerCommand(
        PackageRunner runner,
        string package,
        string extraArguments
    )
    {
        var (command, prefix) = runner switch
        {
            PackageRunner.Uvx => ("uvx", Array.Empty<string>()),
            _ => ("npx", new[] { "-y" }),
        };

        var args = new List<string>(prefix);
        if (!string.IsNullOrWhiteSpace(package))
        {
            args.Add(package);
        }
        if (!string.IsNullOrWhiteSpace(extraArguments))
        {
            args.AddRange(ParseLines(extraArguments));
        }
        return (command, args);
    }

    /// <summary>
    /// Recognizes a stored command/arguments pair as a "simple mode" package-runner
    /// invocation (<c>npx -y &lt;package&gt;</c> or <c>uvx &lt;package&gt;</c>) and splits it back
    /// into the runner, package name, and trailing extra arguments. Returns
    /// <c>null</c> when the command is not a recognized simple-mode runner.
    /// </summary>
    public static SimplePackageCommand ParseSimplePackageCommand(
        string command,
        IReadOnlyList<string> arguments
    )
    {
        arguments ??= [];

        if (
            string.Equals(command, "npx", StringComparison.OrdinalIgnoreCase)
            && arguments.Count >= 2
            && arguments[0] == "-y"
        )
        {
            return new SimplePackageCommand(
                PackageRunner.Npx,
                arguments[1],
                arguments.Skip(2).ToList()
            );
        }

        if (
            string.Equals(command, "uvx", StringComparison.OrdinalIgnoreCase)
            && arguments.Count >= 1
        )
        {
            return new SimplePackageCommand(
                PackageRunner.Uvx,
                arguments[0],
                arguments.Skip(1).ToList()
            );
        }

        return null;
    }

    /// <summary>
    /// Builds a custom command from explicit command and newline-separated arguments text.
    /// </summary>
    public static (string Command, List<string> Arguments) BuildCustomCommand(
        string command,
        string argumentsText
    )
    {
        var args = string.IsNullOrWhiteSpace(argumentsText)
            ? new List<string>()
            : ParseLines(argumentsText);
        return (command, args);
    }

    private static List<string> ParseLines(string text)
    {
        return text.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .ToList();
    }
}

/// <summary>
/// A stored Stdio command recognized as a simple-mode package-runner invocation,
/// decomposed into the runner, package name, and any trailing extra arguments.
/// </summary>
public sealed record SimplePackageCommand(
    PackageRunner Runner,
    string Package,
    List<string> ExtraArguments
);
