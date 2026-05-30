# mcp_diag.ps1 -- MCP Unity Diagnostics
# Run from the repo root: .\mcp_diag.ps1
# Purpose: Quick check of everything needed for Claude Code <-> Unity MCP to work.

$ROOT   = $PSScriptRoot
$SERVER = "$ROOT\VR_StressTraining\Library\PackageCache\com.gamelovers.mcp-unity@aade29c7dd84\Server~"
$SEP    = "-" * 60

Write-Host ""
Write-Host $SEP
Write-Host "  MCP Unity Diagnostics"
Write-Host $SEP

# 1. Node.js
Write-Host ""
Write-Host "[1] Node.js / npm"
try {
    $nodeVer = node --version 2>&1
    $npmVer  = npm  --version 2>&1
    Write-Host "    node : $nodeVer  (required: 18+)"
    Write-Host "    npm  : $npmVer"
    $major = [int]($nodeVer -replace 'v','').Split('.')[0]
    if ($major -lt 18) {
        Write-Host "    WARN  Node < 18 -- upgrade recommended" -ForegroundColor Yellow
    } else {
        Write-Host "    OK" -ForegroundColor Green
    }
} catch {
    Write-Host "    ERROR  node/npm not found in PATH" -ForegroundColor Red
}

# 2. build/index.js
Write-Host ""
Write-Host "[2] MCP Node server build"
$indexJs = "$SERVER\build\index.js"
if (Test-Path $indexJs) {
    $size = (Get-Item $indexJs).Length
    Write-Host "    OK  build/index.js exists ($size bytes)" -ForegroundColor Green
} else {
    Write-Host "    MISSING  build/index.js not found" -ForegroundColor Red
    Write-Host "    Fix: cd '$SERVER' && npm install && npm run build"
}

# 3. node_modules
Write-Host ""
Write-Host "[3] node_modules"
$nm = "$SERVER\node_modules"
if (Test-Path $nm) {
    Write-Host "    OK  node_modules exists" -ForegroundColor Green
} else {
    Write-Host "    MISSING  node_modules not found" -ForegroundColor Yellow
    Write-Host "    Fix: cd '$SERVER' && npm install"
}

# 4. McpUnitySettings.json
Write-Host ""
Write-Host "[4] McpUnitySettings.json"
$settingsFile = "$ROOT\VR_StressTraining\ProjectSettings\McpUnitySettings.json"
if (Test-Path $settingsFile) {
    $s = Get-Content $settingsFile | ConvertFrom-Json
    Write-Host "    Port                 : $($s.Port)"
    Write-Host "    RequestTimeoutSeconds: $($s.RequestTimeoutSeconds)"
    Write-Host "    AutoStartServer      : $($s.AutoStartServer)"
    Write-Host "    EnableInfoLogs       : $($s.EnableInfoLogs)"
    if ($s.RequestTimeoutSeconds -lt 30) {
        Write-Host "    WARN  Timeout < 30s -- set to 60 in Tools > MCP Unity > Server Window" -ForegroundColor Yellow
    } else {
        Write-Host "    OK  Timeout is $($s.RequestTimeoutSeconds)s" -ForegroundColor Green
    }
} else {
    Write-Host "    NOT FOUND  $settingsFile" -ForegroundColor Red
}

# 5. Root .mcp.json
Write-Host ""
Write-Host "[5] Root .mcp.json"
$mcpJson = "$ROOT\.mcp.json"
if (Test-Path $mcpJson) {
    $m = Get-Content $mcpJson | ConvertFrom-Json
    $serverPath = $m.mcpServers.'mcp-unity'.args[0]
    Write-Host "    Node args path : $serverPath"
    $resolved = Join-Path $ROOT $serverPath
    if (Test-Path $resolved) {
        Write-Host "    OK  Resolved path exists" -ForegroundColor Green
    } else {
        Write-Host "    BROKEN  Resolved path does not exist: $resolved" -ForegroundColor Red
    }
} else {
    Write-Host "    NOT FOUND  $mcpJson" -ForegroundColor Red
}

# 6. Port 8090
Write-Host ""
Write-Host "[6] Port 8090 status"
$netstat = netstat -ano 2>&1 | Select-String ":8090"
if ($netstat) {
    foreach ($line in $netstat) {
        $parts = ($line -replace '\s+', ' ').Trim() -split ' '
        $pid2  = $parts[-1]
        try {
            $proc = Get-Process -Id $pid2 -ErrorAction Stop
            $name = $proc.Name
        } catch {
            $name = "unknown"
        }
        Write-Host "    $($line.ToString().Trim())"
        Write-Host "    -> PID $pid2 = $name" -ForegroundColor Cyan
        if ($name -eq "Unity") {
            Write-Host "    OK  Unity holds port 8090 (normal when MCP Server Online)" -ForegroundColor Green
        } elseif ($name -match "node") {
            Write-Host "    INFO  Node process on 8090 -- stale process? Close Claude Code." -ForegroundColor Yellow
        } else {
            Write-Host "    WARN  Unexpected process on 8090: $name" -ForegroundColor Yellow
        }
    }
} else {
    Write-Host "    Port 8090 free -- Unity MCP server not running" -ForegroundColor Yellow
    Write-Host "    Start Unity and enable Tools -> MCP Unity -> Server Window"
}

# 7. node.exe process check
Write-Host ""
Write-Host "[7] node.exe (MCP bridge process)"
$nodeProcs = Get-Process -Name "node" -ErrorAction SilentlyContinue
if ($nodeProcs) {
    foreach ($p in $nodeProcs) {
        $age = [int](New-TimeSpan -Start $p.StartTime).TotalMinutes
        Write-Host "    PID $($p.Id) | started $($p.StartTime) | age ${age}min | RAM $([int]($p.WorkingSet/1MB))MB"
    }
    Write-Host "    NOTE  If node started before McpUnitySettings.json was changed,"
    Write-Host "    it may cache old timeout. Close and reopen Claude Code to get fresh node." -ForegroundColor Yellow
} else {
    Write-Host "    No node.exe running -- MCP bridge not active" -ForegroundColor Yellow
}

# 8. log.txt (node side logging)
Write-Host ""
Write-Host "[8] MCP node log.txt"
$logFile = "$ROOT\log.txt"
if (Test-Path $logFile) {
    $lines = (Get-Content $logFile).Count
    Write-Host "    Found: $logFile ($lines lines)" -ForegroundColor Green
    Write-Host "    Last 5 lines:"
    Get-Content $logFile -Tail 5 | ForEach-Object { Write-Host "      $_" }
} else {
    Write-Host "    Not found (LOGGING_FILE=true not set in .mcp.json env)" -ForegroundColor Yellow
    Write-Host "    Add to .mcp.json: ""env"": { ""LOGGING_FILE"": ""true"" }"
}

# Summary
Write-Host ""
Write-Host $SEP
Write-Host "  Done. Review WARN/ERROR lines above."
Write-Host "  If all OK but get_scene_info still times out:"
Write-Host "    1. Close Claude Code (kills stale node process)"
Write-Host "    2. Unity: check Tools -> MCP Unity -> Server Window = Online, Timeout = 60"
Write-Host "    3. Reopen Claude Code from: $ROOT"
Write-Host "    4. Test get_scene_info"
Write-Host $SEP
Write-Host ""
