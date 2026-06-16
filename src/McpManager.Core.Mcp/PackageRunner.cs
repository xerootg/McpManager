using System.ComponentModel.DataAnnotations;

namespace McpManager.Core.Mcp;

/// <summary>
/// Package runner used to launch a Stdio MCP server from a published package
/// without a manual install step.
/// </summary>
public enum PackageRunner
{
    /// <summary>Run a Node/npm package via <c>npx -y &lt;package&gt;</c>.</summary>
    [Display(Name = "npx (Node)")]
    Npx,

    /// <summary>Run a Python/PyPI package via <c>uvx &lt;package&gt;</c>.</summary>
    [Display(Name = "uvx (Python)")]
    Uvx,
}
