@echo off
rem run-mcp-unity.bat
rem Wrapper that sets cwd to VR_StressTraining before launching the MCP Unity node server.
rem
rem WHY: com.gamelovers.mcp-unity reads McpUnitySettings.json via
rem   path.resolve(process.cwd(), './ProjectSettings/McpUnitySettings.json')
rem When Claude Code opens from repo root, process.cwd() is wrong and
rem the file is not found, causing the timeout to fall back to 10s hardcoded.
rem This wrapper cd's into VR_StressTraining first, so node finds the file.
rem
rem Usage: invoked by Claude Code via root .mcp.json (cmd.exe /c tools\run-mcp-unity.bat)

cd /d "%~dp0..\VR_StressTraining"
node "Library\PackageCache\com.gamelovers.mcp-unity@aade29c7dd84\Server~\build\index.js"
