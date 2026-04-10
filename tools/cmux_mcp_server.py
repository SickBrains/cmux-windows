#!/usr/bin/env python3
"""
cmux MCP server — exposes the cmux CLI as MCP tools so any MCP-capable AI
(Claude Desktop, Cursor, Zed, etc.) can control cmux directly.

Install dependencies:
    pip install mcp

Run manually (for testing):
    python tools/cmux_mcp_server.py

Claude Desktop config:
    {
      "mcpServers": {
        "cmux": {
          "command": "python",
          "args": ["C:\\\\Users\\\\MAG\\\\Documents\\\\GitHub\\\\cmux-windows\\\\tools\\\\cmux_mcp_server.py"]
        }
      }
    }
"""

from __future__ import annotations

import asyncio
import json
import os
import shutil
import subprocess
from pathlib import Path
from typing import Any

from mcp.server import Server
from mcp.server.stdio import stdio_server
from mcp.types import TextContent, Tool


def find_cmux_exe() -> str:
    """Locate cmux.exe — dev build first, then published binary, then PATH."""
    # 1. Environment override
    env_path = os.environ.get("CMUX_EXE")
    if env_path and Path(env_path).exists():
        return env_path

    # 2. Relative to this script — walk up to find dev build first
    script_dir = Path(__file__).resolve().parent
    for ancestor in [script_dir, *script_dir.parents]:
        candidate = ancestor / "src" / "Cmux.Cli" / "bin" / "Debug" / "net10.0-windows" / "cmux.exe"
        if candidate.exists():
            return str(candidate)

    # 3. Published location
    for ancestor in [script_dir, *script_dir.parents]:
        candidate = ancestor / "publish" / "app" / "cmux.exe"
        if candidate.exists():
            return str(candidate)

    # 4. PATH lookup (last resort — might be stale published version)
    which = shutil.which("cmux")
    if which:
        return which

    raise RuntimeError(
        "Could not locate cmux.exe. Set CMUX_EXE environment variable or add cmux to PATH."
    )


CMUX = find_cmux_exe()
server = Server("cmux")


async def run_cmux(*args: str, timeout: float = 10.0) -> str:
    """Run the cmux CLI with given args and return stdout."""
    proc = await asyncio.create_subprocess_exec(
        CMUX,
        *args,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
    )
    try:
        stdout, stderr = await asyncio.wait_for(proc.communicate(), timeout=timeout)
    except asyncio.TimeoutError:
        proc.kill()
        return json.dumps({"error": f"cmux command timed out after {timeout}s"})

    out = stdout.decode("utf-8", errors="replace").strip()
    err = stderr.decode("utf-8", errors="replace").strip()

    if proc.returncode != 0:
        return json.dumps({"error": err or f"exit code {proc.returncode}", "stdout": out})

    return out or err


def text(content: str) -> list[TextContent]:
    return [TextContent(type="text", text=content)]


@server.list_tools()
async def list_tools() -> list[Tool]:
    return [
        Tool(
            name="cmux_status",
            description="Get cmux status — version, workspace count, selected workspace, unread notifications.",
            inputSchema={"type": "object", "properties": {}},
        ),
        Tool(
            name="cmux_workspace_list",
            description="List all cmux workspaces with their IDs, names, and surface counts.",
            inputSchema={"type": "object", "properties": {}},
        ),
        Tool(
            name="cmux_workspace_select",
            description="Select a workspace by index, ID, or name.",
            inputSchema={
                "type": "object",
                "properties": {
                    "index": {"type": "integer", "description": "Workspace index (0-based)"},
                    "id": {"type": "string", "description": "Workspace ID"},
                    "name": {"type": "string", "description": "Workspace name (fuzzy match)"},
                },
            },
        ),
        Tool(
            name="cmux_workspace_create",
            description="Create a new workspace.",
            inputSchema={
                "type": "object",
                "properties": {
                    "name": {"type": "string", "description": "Workspace name"},
                },
            },
        ),
        Tool(
            name="cmux_pane_list",
            description="List all panes in the current focused surface with IDs, names, cwd, and focus state.",
            inputSchema={"type": "object", "properties": {}},
        ),
        Tool(
            name="cmux_pane_focus",
            description="Focus a specific pane by index (1-based).",
            inputSchema={
                "type": "object",
                "properties": {
                    "paneIndex": {"type": "integer", "description": "Pane index (1-based)"},
                    "paneId": {"type": "string", "description": "Pane ID (alternative to index)"},
                },
            },
        ),
        Tool(
            name="cmux_pane_write",
            description="Type text into the focused pane. Optionally submit (press Enter).",
            inputSchema={
                "type": "object",
                "properties": {
                    "text": {"type": "string", "description": "Text to type"},
                    "submit": {"type": "boolean", "description": "Press Enter after typing", "default": False},
                    "paneIndex": {"type": "integer", "description": "Target pane index (optional, defaults to focused)"},
                },
                "required": ["text"],
            },
        ),
        Tool(
            name="cmux_pane_read",
            description="Read the last N lines from a pane's terminal buffer.",
            inputSchema={
                "type": "object",
                "properties": {
                    "lines": {"type": "integer", "description": "Number of lines to read", "default": 80},
                    "paneIndex": {"type": "integer", "description": "Target pane index (optional, defaults to focused)"},
                },
            },
        ),
        Tool(
            name="cmux_split_right",
            description="Split the focused pane vertically (new pane on the right).",
            inputSchema={"type": "object", "properties": {}},
        ),
        Tool(
            name="cmux_split_down",
            description="Split the focused pane horizontally (new pane below).",
            inputSchema={"type": "object", "properties": {}},
        ),
        Tool(
            name="cmux_run",
            description="Split the focused pane down and run a command in the new pane. Perfect for starting servers and long-running processes.",
            inputSchema={
                "type": "object",
                "properties": {
                    "command": {"type": "string", "description": "Shell command to run"},
                },
                "required": ["command"],
            },
        ),
        Tool(
            name="cmux_surface_create",
            description="Create a new surface (tab) in the current workspace.",
            inputSchema={"type": "object", "properties": {}},
        ),
        Tool(
            name="cmux_notify",
            description="Send a notification to cmux (shows in notification panel and Windows toast if window is not focused).",
            inputSchema={
                "type": "object",
                "properties": {
                    "title": {"type": "string", "description": "Notification title"},
                    "body": {"type": "string", "description": "Notification body"},
                    "subtitle": {"type": "string", "description": "Optional subtitle"},
                },
                "required": ["title", "body"],
            },
        ),
        Tool(
            name="cmux_sandbox_send",
            description="Send a command to the Windows Sandbox VM (isolated cmux instance for testing). The sandbox must be running (cmux_sandbox_launch).",
            inputSchema={
                "type": "object",
                "properties": {
                    "command": {
                        "type": "string",
                        "description": "Command to run. Can be a cmux CLI command ('cmux status'), 'screenshot', 'rebuild', or any PowerShell command.",
                    },
                },
                "required": ["command"],
            },
        ),
        Tool(
            name="cmux_sandbox_screen",
            description="Take a screenshot of the sandbox cmux window. Returns the path to the saved PNG.",
            inputSchema={"type": "object", "properties": {}},
        ),
        Tool(
            name="cmux_sandbox_launch",
            description="Launch the Windows Sandbox VM with cmux. Takes ~60s to boot, install .NET, build, and start.",
            inputSchema={"type": "object", "properties": {}},
        ),
        Tool(
            name="cmux_sandbox_status",
            description="Get the last sandbox command result (from sandbox-result.txt).",
            inputSchema={"type": "object", "properties": {}},
        ),
    ]


@server.call_tool()
async def call_tool(name: str, arguments: dict[str, Any] | None) -> list[TextContent]:
    args = arguments or {}

    if name == "cmux_status":
        return text(await run_cmux("status"))

    if name == "cmux_workspace_list":
        return text(await run_cmux("workspace", "list"))

    if name == "cmux_workspace_select":
        cli_args = ["workspace", "select"]
        if "index" in args:
            cli_args += ["--index", str(args["index"])]
        elif "id" in args:
            cli_args += ["--id", args["id"]]
        elif "name" in args:
            cli_args += ["--name", args["name"]]
        return text(await run_cmux(*cli_args))

    if name == "cmux_workspace_create":
        cli_args = ["workspace", "create"]
        if "name" in args:
            cli_args += ["--name", args["name"]]
        return text(await run_cmux(*cli_args))

    if name == "cmux_pane_list":
        return text(await run_cmux("pane", "list"))

    if name == "cmux_pane_focus":
        cli_args = ["pane", "focus"]
        if "paneIndex" in args:
            cli_args += ["--paneIndex", str(args["paneIndex"])]
        elif "paneId" in args:
            cli_args += ["--paneId", args["paneId"]]
        return text(await run_cmux(*cli_args))

    if name == "cmux_pane_write":
        cli_args = ["pane", "write", "--text", args["text"]]
        if args.get("submit"):
            cli_args.append("--submit")
        if "paneIndex" in args:
            cli_args += ["--paneIndex", str(args["paneIndex"])]
        return text(await run_cmux(*cli_args))

    if name == "cmux_pane_read":
        cli_args = ["pane", "read", "--lines", str(args.get("lines", 80))]
        if "paneIndex" in args:
            cli_args += ["--paneIndex", str(args["paneIndex"])]
        return text(await run_cmux(*cli_args))

    if name == "cmux_split_right":
        return text(await run_cmux("split", "right"))

    if name == "cmux_split_down":
        return text(await run_cmux("split", "down"))

    if name == "cmux_run":
        return text(await run_cmux("run", args["command"], timeout=15.0))

    if name == "cmux_surface_create":
        return text(await run_cmux("surface", "create"))

    if name == "cmux_notify":
        cli_args = ["notify", "--title", args["title"], "--body", args["body"]]
        if "subtitle" in args:
            cli_args += ["--subtitle", args["subtitle"]]
        return text(await run_cmux(*cli_args))

    if name == "cmux_sandbox_send":
        return text(await run_cmux("sandbox", "send", args["command"], timeout=120.0))

    if name == "cmux_sandbox_screen":
        # Trigger a fresh screenshot via relay then return the path
        await run_cmux("sandbox", "send", "screenshot", timeout=15.0)
        screen_path = Path(CMUX).parent
        # Walk up to find tools/sandbox-screen.png
        for ancestor in [screen_path, *screen_path.parents]:
            candidate = ancestor / "tools" / "sandbox-screen.png"
            if candidate.exists():
                return text(f"Screenshot saved: {candidate}")
        return text("Screenshot requested but file not found")

    if name == "cmux_sandbox_launch":
        return text(await run_cmux("sandbox", "launch"))

    if name == "cmux_sandbox_status":
        return text(await run_cmux("sandbox", "status"))

    return text(json.dumps({"error": f"Unknown tool: {name}"}))


async def main():
    async with stdio_server() as (read_stream, write_stream):
        await server.run(
            read_stream,
            write_stream,
            server.create_initialization_options(),
        )


if __name__ == "__main__":
    asyncio.run(main())
