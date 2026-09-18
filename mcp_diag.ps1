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

# 5. Root .mcp.json + wrapper
Write-Host ""
Write-Host "[5] Root .mcp.json + Windows wrapper"
$mcpJson = "$ROOT\.mcp.json"
$wrapperBat = "$ROOT\tools\run-mcp-unity.bat"

# 5a. Wrapper bat file
$mcpUnityJs = "$ROOT\VR_StressTraining\Library\PackageCache\com.gamelovers.mcp-unity@aade29c7dd84\Server~\build\unity\mcpUnity.js"
if (Test-Path $wrapperBat) {
    Write-Host "    OK  tools\run-mcp-unity.bat exists" -ForegroundColor Green
    $batContent = Get-Content $wrapperBat -Raw

    # cwd check
    if ($batContent -match "VR_StressTraining") {
        Write-Host "    OK  wrapper cds into VR_StressTraining" -ForegroundColor Green
    } else {
        Write-Host "    WARN  wrapper does not cd into VR_StressTraining" -ForegroundColor Yellow
    }

    # Timeout env var forced in bat
    if ($batContent -match "MCPUNITY_REQUEST_TIMEOUT_SECONDS=60") {
        Write-Host "    OK  wrapper forces MCPUNITY_REQUEST_TIMEOUT_SECONDS=60" -ForegroundColor Green
    } else {
        Write-Host "    WARN  wrapper does not set MCPUNITY_REQUEST_TIMEOUT_SECONDS=60" -ForegroundColor Yellow
        Write-Host "    Add: set MCPUNITY_REQUEST_TIMEOUT_SECONDS=60" -ForegroundColor Yellow
    }

    # node path
    $nodeInBat = "Library\PackageCache\com.gamelovers.mcp-unity@aade29c7dd84\Server~\build\index.js"
    $resolvedNodeInBat = Join-Path "$ROOT\VR_StressTraining" $nodeInBat
    if (Test-Path $resolvedNodeInBat) {
        Write-Host "    OK  node path in wrapper resolves correctly" -ForegroundColor Green
    } else {
        Write-Host "    BROKEN  node path not found: $resolvedNodeInBat" -ForegroundColor Red
    }

    # Settings reachable from wrapper cwd
    $settingsFromWrapper = "$ROOT\VR_StressTraining\ProjectSettings\McpUnitySettings.json"
    if (Test-Path $settingsFromWrapper) {
        $s2 = Get-Content $settingsFromWrapper | ConvertFrom-Json
        Write-Host "    OK  McpUnitySettings.json reachable (timeout=$($s2.RequestTimeoutSeconds)s in file)" -ForegroundColor Green
    } else {
        Write-Host "    BROKEN  McpUnitySettings.json not found from wrapper cwd" -ForegroundColor Red
    }
} else {
    Write-Host "    MISSING  tools\run-mcp-unity.bat not found" -ForegroundColor Red
    Write-Host "    Fix: create tools\run-mcp-unity.bat (see CLAUDE.md)" -ForegroundColor Yellow
}

# 5b-patch. Check mcpUnity.js local patch
Write-Host ""
Write-Host "[5b] mcpUnity.js local patch"
if (Test-Path $mcpUnityJs) {
    $jsContent = Get-Content $mcpUnityJs -Raw
    $hasPatch     = $jsContent -match "MCPUNITY_REQUEST_TIMEOUT_SECONDS"
    $hasDefault60 = $jsContent -match "requestTimeout = 60000"
    $has10k       = $jsContent -match ": 10000"
    $hasBak       = Test-Path ($mcpUnityJs + ".bak")
    if ($hasPatch -and $hasDefault60) {
        Write-Host "    OK  patch applied (env var + 60s fallback)" -ForegroundColor Green
    } else {
        Write-Host "    MISSING  patch not found in mcpUnity.js" -ForegroundColor Red
        Write-Host "    Reapply patch from CLAUDE.md troubleshooting section" -ForegroundColor Yellow
    }
    if ($has10k) {
        Write-Host "    WARN  still contains 10000 ms somewhere -- check patch" -ForegroundColor Yellow
    }
    if ($hasBak) {
        Write-Host "    OK  backup mcpUnity.js.bak exists" -ForegroundColor Green
    } else {
        Write-Host "    INFO  no .bak file (backup before patching is recommended)" -ForegroundColor Yellow
    }
} else {
    Write-Host "    NOT FOUND  $mcpUnityJs" -ForegroundColor Red
}

# 5b. .mcp.json command check
if (Test-Path $mcpJson) {
    $m = Get-Content $mcpJson | ConvertFrom-Json
    $cmd = $m.mcpServers.'mcp-unity'.command
    $arg0 = $m.mcpServers.'mcp-unity'.args[0]
    Write-Host "    command        : $cmd"
    Write-Host "    args[0]        : $arg0"
    if ($cmd -eq "cmd.exe" -and $arg0 -eq "/c") {
        Write-Host "    OK  .mcp.json uses cmd.exe wrapper pattern" -ForegroundColor Green
    } elseif ($m.mcpServers.'mcp-unity'.cwd) {
        Write-Host "    WARN  .mcp.json uses direct cwd -- may not work in Claude Code" -ForegroundColor Yellow
        Write-Host "    Use cmd.exe wrapper instead" -ForegroundColor Yellow
    } else {
        Write-Host "    WARN  unexpected command pattern" -ForegroundColor Yellow
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
