namespace Cmux.Core.Services;

/// <summary>
/// Resolves a shell command string into a short display name for pane auto-naming.
/// Returns null for trivial commands that shouldn't rename the pane (cd, ls, clear, etc.).
/// </summary>
public static class CommandNameResolver
{
    private static readonly HashSet<string> IgnoredCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "cd", "ls", "dir", "cls", "clear", "pwd", "echo", "cat", "type", "exit",
        "history", "alias", "export", "set", "env", "whoami", "hostname",
        "mkdir", "rmdir", "rm", "cp", "mv", "touch", "chmod", "chown",
        "head", "tail", "less", "more", "grep", "find", "wc", "sort",
    };

    private static readonly Dictionary<string, string> KnownPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["npm run dev"] = "npm dev",
        ["npm run build"] = "npm build",
        ["npm run start"] = "npm start",
        ["npm run test"] = "npm test",
        ["npm start"] = "npm start",
        ["npm test"] = "npm test",
        ["npm install"] = "npm install",
        ["yarn dev"] = "yarn dev",
        ["yarn start"] = "yarn start",
        ["pnpm dev"] = "pnpm dev",
        ["dotnet run"] = "dotnet run",
        ["dotnet watch"] = "dotnet watch",
        ["dotnet test"] = "dotnet test",
        ["docker compose up"] = "docker compose",
        ["docker-compose up"] = "docker compose",
        ["docker compose down"] = "docker down",
        ["cargo run"] = "cargo run",
        ["cargo build"] = "cargo build",
        ["cargo test"] = "cargo test",
        ["go run"] = "go run",
        ["go build"] = "go build",
        ["go test"] = "go test",
        ["make"] = "make",
        ["gradle run"] = "gradle",
        ["mvn spring-boot:run"] = "spring-boot",
    };

    public static string? Resolve(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var trimmed = command.Trim();

        // Check known multi-word patterns first (longest match)
        foreach (var (pattern, name) in KnownPatterns)
        {
            if (trimmed.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                return name;
        }

        // Parse first word
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        var firstWord = Path.GetFileNameWithoutExtension(parts[0]);

        // Ignore trivial commands
        if (IgnoredCommands.Contains(firstWord))
            return null;

        // Special patterns by first word
        return firstWord.ToLowerInvariant() switch
        {
            "python" or "python3" or "py" => ResolvePython(parts),
            "node" => ResolveWithArg(parts, "node"),
            "ssh" => ResolveSsh(parts),
            "git" => parts.Length > 1 ? $"git {parts[1]}" : "git",
            "docker" => parts.Length > 1 ? $"docker {parts[1]}" : "docker",
            "kubectl" or "k" => parts.Length > 1 ? $"k8s {parts[1]}" : "k8s",
            "terraform" or "tf" => parts.Length > 1 ? $"tf {parts[1]}" : "terraform",
            "uvicorn" => "uvicorn",
            "gunicorn" => "gunicorn",
            "flask" => "flask",
            "django" => "django",
            "rails" => "rails",
            "java" => ResolveJava(parts),
            "javac" => "javac",
            "tribot" => "tribot",
            "recaf" => "recaf",
            "claude" => "claude",
            "vim" or "nvim" or "vi" => parts.Length > 1 ? $"vim {Path.GetFileName(parts[^1])}" : "vim",
            "nano" => parts.Length > 1 ? $"nano {Path.GetFileName(parts[^1])}" : "nano",
            "htop" or "top" or "btop" => "monitor",
            _ => firstWord,
        };
    }

    private static string ResolvePython(string[] parts)
    {
        if (parts.Length < 2) return "python";
        var arg = parts[1];
        if (arg.StartsWith("-")) return "python";
        var name = Path.GetFileNameWithoutExtension(arg);
        return name switch
        {
            "manage" => "django",
            "app" or "main" or "server" => "python",
            _ => $"py:{name}",
        };
    }

    private static string ResolveWithArg(string[] parts, string prefix)
    {
        if (parts.Length < 2) return prefix;
        var arg = parts[1];
        if (arg.StartsWith("-")) return prefix;
        return $"{prefix} {Path.GetFileName(arg)}";
    }

    private static string ResolveSsh(string[] parts)
    {
        foreach (var part in parts.Skip(1))
        {
            if (part.StartsWith("-")) continue;
            // user@host or just host
            var host = part.Contains('@') ? part[(part.IndexOf('@') + 1)..] : part;
            return $"ssh {host}";
        }
        return "ssh";
    }

    private static string ResolveJava(string[] parts)
    {
        foreach (var part in parts.Skip(1))
        {
            if (part.StartsWith("-")) continue;
            if (part.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                return $"java {Path.GetFileName(part)}";
            return $"java {part}";
        }
        return "java";
    }
}
