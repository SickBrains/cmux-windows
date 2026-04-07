using System.Text.Json;
using Cmux.Core.IPC;

namespace Cmux.Cli;

/// <summary>
/// cmux CLI tool — Windows equivalent of the cmux macOS CLI.
/// Communicates with the running cmux app via named pipes.
///
/// Usage:
///   cmux notify --title "Title" --body "Body"
///   cmux workspace list
///   cmux workspace create --name "My Workspace"
///   cmux workspace select --index 0
///   cmux surface create
///   cmux split right
///   cmux split down
///   cmux status
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        var command = args[0].ToLowerInvariant();

        try
        {
            return command switch
            {
                "notify" => await HandleNotify(args[1..]),
                "workspace" => await HandleWorkspace(args[1..]),
                "surface" => await HandleSurface(args[1..]),
                "split" => await HandleSplit(args[1..]),
                "pane" => await HandlePane(args[1..]),
                "run" => await HandleRun(args[1..]),
                "sandbox" => await HandleSandbox(args[1..]),
                "status" => await HandleStatus(),
                "help" or "--help" or "-h" => PrintHelp(),
                "version" or "--version" or "-v" => PrintVersion(),
                _ => Error($"Unknown command: {command}"),
            };
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("Error: Could not connect to cmux. Is it running?");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleNotify(string[] args)
    {
        var parsed = ParseArgs(args);
        var title = parsed.GetValueOrDefault("title", parsed.GetValueOrDefault("_arg0", "Terminal"));
        var body = parsed.GetValueOrDefault("body", parsed.GetValueOrDefault("_arg1", ""));
        var subtitle = parsed.GetValueOrDefault("subtitle");

        var cmdArgs = new Dictionary<string, string>
        {
            ["title"] = title,
            ["body"] = body,
        };
        if (subtitle != null) cmdArgs["subtitle"] = subtitle;

        var response = await NamedPipeClient.SendCommand("NOTIFY", cmdArgs);
        Console.WriteLine(response);
        return 0;
    }

    private static async Task<int> HandleWorkspace(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: cmux workspace <list|create|select>");
            return 1;
        }

        var subcommand = args[0].ToLowerInvariant();
        var parsed = ParseArgs(args[1..]);

        return subcommand switch
        {
            "list" or "ls" => await SendAndPrint("WORKSPACE.LIST"),
            "create" or "new" => await SendAndPrint("WORKSPACE.CREATE", parsed),
            "select" => await SendAndPrint("WORKSPACE.SELECT", parsed),
            "next" => await SendAndPrint("WORKSPACE.NEXT"),
            "previous" or "prev" => await SendAndPrint("WORKSPACE.PREVIOUS"),
            _ => Error($"Unknown workspace command: {subcommand}"),
        };
    }

    private static async Task<int> HandleSurface(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: cmux surface <create>");
            return 1;
        }

        var subcommand = args[0].ToLowerInvariant();

        return subcommand switch
        {
            "create" or "new" => await SendAndPrint("SURFACE.CREATE"),
            "next" => await SendAndPrint("SURFACE.NEXT"),
            "previous" or "prev" => await SendAndPrint("SURFACE.PREVIOUS"),
            _ => Error($"Unknown surface command: {subcommand}"),
        };
    }

    private static async Task<int> HandleSplit(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: cmux split <right|down>");
            return 1;
        }

        var direction = args[0].ToLowerInvariant();

        return direction switch
        {
            "right" or "vertical" or "v" => await SendAndPrint("SPLIT.RIGHT"),
            "down" or "horizontal" or "h" => await SendAndPrint("SPLIT.DOWN"),
            _ => Error($"Unknown split direction: {direction}"),
        };
    }

    private static async Task<int> HandlePane(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: cmux pane <list|focus|write|read>");
            return 1;
        }

        var subcommand = args[0].ToLowerInvariant();
        var parsed = ParseArgs(args[1..]);

        return subcommand switch
        {
            "list" or "ls" => await SendAndPrint("PANE.LIST", parsed),
            "focus" => await SendAndPrint("PANE.FOCUS", parsed),
            "write" => await SendAndPrint("PANE.WRITE", parsed),
            "read" => await SendAndPrint("PANE.READ", parsed),
            _ => Error($"Unknown pane command: {subcommand}"),
        };
    }

    /// <summary>
    /// Splits the current pane down and runs a command in the new pane.
    /// Usage: cmux run "npm run dev"
    /// </summary>
    private static async Task<int> HandleRun(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: cmux run <command>");
            return 1;
        }

        // Split down (Ctrl+Shift+D equivalent)
        await NamedPipeClient.SendCommand("SPLIT.DOWN");

        // Wait for the new pane to initialize its shell session
        await Task.Delay(500);

        // Write the command into the newly focused pane and submit
        var command = string.Join(" ", args);
        var writeArgs = new Dictionary<string, string>
        {
            ["text"] = command,
            ["submit"] = "true",
        };
        return await SendAndPrint("PANE.WRITE", writeArgs);
    }

    private static async Task<int> HandleStatus()
    {
        return await SendAndPrint("STATUS");
    }

    private static async Task<int> HandleSandbox(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: cmux sandbox <status|screen|send|log|launch>");
            return 1;
        }

        var subcommand = args[0].ToLowerInvariant();
        var toolsDir = FindToolsDirectory();
        if (toolsDir == null)
        {
            Console.Error.WriteLine("Error: Cannot find tools/ directory. Run from the repo root.");
            return 1;
        }

        return subcommand switch
        {
            "status" => ShowFile(Path.Combine(toolsDir, "sandbox-result.txt")),
            "screen" => OpenFile(Path.Combine(toolsDir, "sandbox-screen.png")),
            "send" => await SandboxSend(toolsDir, string.Join(" ", args[1..])),
            "log" => ShowFile(Path.Combine(toolsDir, "sandbox-relay-log.txt")),
            "launch" => LaunchSandbox(toolsDir),
            _ => Error($"Unknown sandbox command: {subcommand}"),
        };
    }

    private static int ShowFile(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 1;
        }
        Console.Write(File.ReadAllText(path));
        return 0;
    }

    private static int OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 1;
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        return 0;
    }

    private static async Task<int> SandboxSend(string toolsDir, string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            Console.Error.WriteLine("Usage: cmux sandbox send <command>");
            return 1;
        }

        var cmdFile = Path.Combine(toolsDir, "sandbox-cmd.txt");
        var resultFile = Path.Combine(toolsDir, "sandbox-result.txt");

        if (File.Exists(resultFile)) File.Delete(resultFile);
        await File.WriteAllTextAsync(cmdFile, command);

        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(500);
            if (File.Exists(resultFile))
            {
                Console.Write(await File.ReadAllTextAsync(resultFile));
                return 0;
            }
        }

        Console.Error.WriteLine("Timeout: no response from sandbox relay.");
        return 1;
    }

    private static int LaunchSandbox(string toolsDir)
    {
        var wsbPath = Path.Combine(toolsDir, "sandbox.wsb");
        if (!File.Exists(wsbPath))
        {
            Console.Error.WriteLine($"Sandbox config not found: {wsbPath}");
            return 1;
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(wsbPath) { UseShellExecute = true });
        Console.WriteLine("Windows Sandbox launching...");
        return 0;
    }

    private static string? FindToolsDirectory()
    {
        var dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "tools", "sandbox-relay.ps1");
            if (File.Exists(candidate)) return Path.Combine(dir, "tools");
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return null;
    }

    private static async Task<int> SendAndPrint(string command, Dictionary<string, string>? args = null)
    {
        var response = await NamedPipeClient.SendCommand(command, args);

        // Pretty-print JSON
        try
        {
            using var doc = JsonDocument.Parse(response);
            var pretty = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(pretty);
        }
        catch
        {
            Console.WriteLine(response);
        }

        return 0;
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var result = new Dictionary<string, string>();
        int positional = 0;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.StartsWith("--"))
            {
                var key = arg[2..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    result[key] = args[i + 1];
                    i++;
                }
                else
                {
                    result[key] = "true";
                }
            }
            else if (arg.StartsWith('-') && arg.Length == 2)
            {
                var key = arg[1..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                {
                    result[key] = args[i + 1];
                    i++;
                }
                else
                {
                    result[key] = "true";
                }
            }
            else
            {
                result[$"_arg{positional}"] = arg;
                positional++;
            }
        }

        return result;
    }

    private static int PrintHelp()
    {
        Console.WriteLine("""
            cmux - Terminal multiplexer for AI coding agents (Windows)

            Usage:
              cmux <command> [options]

            Commands:
              notify                Send a notification
                --title <text>      Notification title (default: "Terminal")
                --body <text>       Notification body
                --subtitle <text>   Notification subtitle

              workspace             Manage workspaces
                list                List all workspaces
                create              Create a new workspace
                  --name <text>     Workspace name
                select              Select a workspace
                  --index <n>       Workspace index (0-based)
                  --id <id>         Workspace ID
                next                Switch to next workspace
                previous            Switch to previous workspace

              surface               Manage surfaces (tabs within workspace)
                create              Create a new surface
                next                Switch to next surface
                previous            Switch to previous surface

              split                 Split the focused pane
                right               Split vertically (left/right)
                down                Split horizontally (top/bottom)

              pane                  Manage panes
                list                List panes in current surface
                focus               Focus a pane
                  --paneIndex <n>   Pane index (1-based)
                write               Write text to a pane
                  --text <text>     Text to write
                  --submit          Press Enter after writing
                read                Read pane content
                  --lines <n>       Number of lines (default: 80)

              run <command>         Split down and run a command in the new pane

              sandbox               Windows Sandbox testing environment
                status              Show last sandbox command result
                screen              Open sandbox screenshot in viewer
                send <cmd>          Send a command to sandbox relay
                log                 Show sandbox relay log
                launch              Launch the Windows Sandbox VM

              status                Show cmux status

            Keyboard Shortcuts (in the app):
              Ctrl+N                New workspace
              Ctrl+1-8              Jump to workspace 1-8
              Ctrl+9                Jump to last workspace
              Ctrl+Shift+W          Close workspace
              Ctrl+B                Toggle sidebar
              Ctrl+T                New surface (tab)
              Ctrl+W                Close surface
              Ctrl+D                Split right
              Ctrl+Shift+D          Split down
              Ctrl+Alt+Arrow        Focus pane directionally
              Ctrl+I                Toggle notification panel
              Ctrl+Shift+U          Jump to latest unread
            """);
        return 0;
    }

    private static int PrintVersion()
    {
        Console.WriteLine("cmux 1.1.2 (Windows)");
        return 0;
    }

    private static int Error(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        return 1;
    }
}
