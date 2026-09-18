@echo off
rem run-mcp-unity.bat
rem Wrapper that sets cwd to VR_StressTraining and forces 120s timeout
rem before launching the MCP Unity node server.
rem
rem WHY cwd: com.gamelovers.mcp-unity reads McpUnitySettings.json via
rem   path.resolve(process.cwd(), './ProjectSettings/McpUnitySettings.json')
rem Without correct cwd the file is not found and timeout falls back to default.
rem
rem WHY MCPUNITY_REQUEST_TIMEOUT_SECONDS: a local patch in build/unity/mcpUnity.js
rem reads this env var first (before the config file), so timeout is always 120s
rem even if the config file is not found or Unity resets it.
rem
rem Usage: invoked by Claude Code via root .mcp.json (cmd.exe /c tools\run-mcp-unity.bat)

cd /d "%~dp0..\VR_StressTraining"

set MCPUNITY_REQUEST_TIMEOUT_SECONDS=120

echo MCP wrapper cwd: %CD%
echo MCP timeout forced: %MCPUNITY_REQUEST_TIMEOUT_SECONDS%s

node "Library\PackageCache\com.gamelovers.mcp-unity@aade29c7dd84\Server~\build\index.js"
