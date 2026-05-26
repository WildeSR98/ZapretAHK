
# =====================================================================
#  GitHub Folder Downloader
#  Usage: run the script and paste a GitHub folder link when prompted
#  Or:    .\download-github-folder.ps1 -Url "https://github.com/..." -Destination "C:\path"
# =====================================================================

param(
    [string]$Url = "",
    [string]$Destination = ""
)

function Write-Ok   { param($msg) Write-Host "  [OK]   $msg" -ForegroundColor Green }
function Write-Fail { param($msg) Write-Host "  [FAIL] $msg" -ForegroundColor Red }
function Write-Info { param($msg) Write-Host "  $msg" -ForegroundColor Cyan }
function Write-Head { param($msg) Write-Host "" ; Write-Host "$msg" -ForegroundColor Yellow }

Write-Host ""
Write-Host "=============================================" -ForegroundColor DarkCyan
Write-Host "   GitHub Folder Downloader (PowerShell)    " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor DarkCyan
Write-Host ""

# ---- Input URL -------------------------------------------------------
if (-not $Url) {
    Write-Host "Paste GitHub folder URL:" -ForegroundColor White
    Write-Host "Example: https://github.com/user/repo/tree/main/some-folder" -ForegroundColor DarkGray
    Write-Host ""
    $Url = Read-Host "  URL"
}

if (-not $Url) {
    Write-Fail "No URL provided. Exiting."
    exit 1
}

# ---- Parse URL -------------------------------------------------------
# Expected: https://github.com/{owner}/{repo}/tree/{branch}/{path}
$pattern = '^https?://github\.com/([^/]+)/([^/]+)/tree/([^/]+)(/(.+))?$'
if ($Url -notmatch $pattern) {
    Write-Fail "Invalid URL format."
    Write-Fail "Expected: https://github.com/owner/repo/tree/branch/folder"
    exit 1
}

$owner  = $Matches[1]
$repo   = $Matches[2]
$branch = [uri]::UnescapeDataString($Matches[3])   # decode in case URL was copy-pasted already encoded
$path   = if ($Matches[5]) { [uri]::UnescapeDataString($Matches[5]) } else { "" }

Write-Info "Repository : $owner/$repo"
Write-Info "Branch     : $branch"
Write-Info "Path       : $(if ($path) { $path } else { '(root)' })"

# ---- Destination folder ----------------------------------------------
if (-not $Destination) {
    $folderName = if ($path) { Split-Path $path -Leaf } else { $repo }
    Write-Host ""
    Write-Host "Where to save? (press Enter for current dir '$folderName')" -ForegroundColor White
    $userInput = Read-Host "  Destination path"
    if ($userInput) {
        $Destination = $userInput
    } else {
        $Destination = Join-Path (Get-Location) $folderName
    }
}

Write-Info "Saving to  : $Destination"

# ---- Recursively enumerate files via GitHub API ----------------------
Write-Head "Fetching file list from GitHub API..."

function Get-GitHubFiles {
    param([string]$ApiPath)

    $encodedBranch = [uri]::EscapeDataString($branch)
    $apiUrl = "https://api.github.com/repos/$owner/$repo/contents/$ApiPath" + "?ref=$encodedBranch"

    try {
        $headers = @{
            "User-Agent" = "PowerShell-GH-Downloader"
            "Accept"     = "application/vnd.github.v3+json"
        }
        $response = Invoke-RestMethod -Uri $apiUrl -UseBasicParsing -Headers $headers
    } catch {
        Write-Fail "API error for path '$ApiPath': $_"
        return @()
    }

    $result = @()
    foreach ($item in $response) {
        if ($item.type -eq 'file') {
            $result += $item
        } elseif ($item.type -eq 'dir') {
            $result += Get-GitHubFiles -ApiPath $item.path
        }
    }
    return $result
}

$allFiles = Get-GitHubFiles -ApiPath $path
$total = $allFiles.Count

if ($total -eq 0) {
    Write-Fail "No files found. Check the URL and try again."
    exit 1
}

Write-Info "Found $total file(s)"

# ---- Download files --------------------------------------------------
Write-Head "Downloading..."

$ok   = 0
$fail = 0

foreach ($file in $allFiles) {
    # Build relative path (strip the requested folder prefix)
    $relativePath = if ($path) {
        $file.path.Substring($path.Length).TrimStart('/')
    } else {
        $file.path
    }

    $localPath = Join-Path $Destination ($relativePath -replace '/', '\')
    $localDir  = Split-Path $localPath -Parent

    if (-not (Test-Path $localDir)) {
        New-Item -ItemType Directory -Force -Path $localDir | Out-Null
    }

    try {
        Invoke-WebRequest -Uri $file.download_url -OutFile $localPath -UseBasicParsing
        Write-Ok $relativePath
        $ok++
    } catch {
        Write-Fail "$relativePath  ($_)"
        $fail++
    }
}

# ---- Summary ---------------------------------------------------------
Write-Host ""
Write-Host "=============================================" -ForegroundColor DarkCyan
if ($fail -eq 0) {
    Write-Host "  Done!  Success: $ok  |  Failed: $fail" -ForegroundColor Green
} else {
    Write-Host "  Done!  Success: $ok  |  Failed: $fail" -ForegroundColor Yellow
}
Write-Host "  Folder: $Destination" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor DarkCyan
Write-Host ""
