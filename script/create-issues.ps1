param(
    [string]$Repo = "JSDevelop/task-app",
    [string]$BasePath = "docs/issues"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $OutputEncoding = [System.Text.Encoding]::UTF8
} catch {
}

function Get-FrontMatterAndBody {
    param(
        [string]$FilePath
    )

    $raw = Get-Content -LiteralPath $FilePath -Raw -Encoding UTF8

    # BOM除去
    $raw = $raw.TrimStart([char]0xFEFF)

    # 改行統一
    $raw = $raw -replace "`r`n", "`n"
    $raw = $raw -replace "`r", "`n"

    $lines = $raw -split "`n"

    # 先頭空行スキップ
    $startIndex = 0
    while ($startIndex -lt $lines.Length -and [string]::IsNullOrWhiteSpace($lines[$startIndex])) {
        $startIndex++
    }

    if ($startIndex -ge $lines.Length -or $lines[$startIndex].Trim() -ne '---') {
        throw "Front Matter が不正です: $FilePath"
    }

    $endIndex = -1
    for ($i = $startIndex + 1; $i -lt $lines.Length; $i++) {
        if ($lines[$i].Trim() -eq '---') {
            $endIndex = $i
            break
        }
    }

    if ($endIndex -eq -1) {
        throw "Front Matter の終了区切り '---' がありません: $FilePath"
    }

    $frontMatterLines = @()
    if ($endIndex -gt $startIndex + 1) {
        $frontMatterLines = $lines[($startIndex + 1)..($endIndex - 1)]
    }

    $bodyLines = @()
    if ($endIndex + 1 -lt $lines.Length) {
        $bodyLines = $lines[($endIndex + 1)..($lines.Length - 1)]
    }

    $title = $null
    $milestone = $null
    $labels = New-Object System.Collections.Generic.List[string]

    $inLabels = $false

    foreach ($line in $frontMatterLines) {
        if ($line -match '^\s*title:\s*"(.*)"\s*$') {
            $title = $matches[1]
            $inLabels = $false
            continue
        }

        if ($line -match '^\s*milestone:\s*(.+?)\s*$') {
            $milestone = $matches[1].Trim()
            $inLabels = $false
            continue
        }

        if ($line -match '^\s*labels:\s*$') {
            $inLabels = $true
            continue
        }

        if ($inLabels -and $line -match '^\s*-\s*(.+?)\s*$') {
            $labels.Add($matches[1].Trim())
            continue
        }

        if ($line.Trim() -ne "") {
            $inLabels = $false
        }
    }

    if ([string]::IsNullOrWhiteSpace($title)) {
        throw "title がありません: $FilePath"
    }

    if ($labels.Count -eq 0) {
        throw "labels がありません: $FilePath"
    }

    if ([string]::IsNullOrWhiteSpace($milestone)) {
        throw "milestone がありません: $FilePath"
    }

    $body = ($bodyLines -join "`n").Trim()

    return @{
        Title = $title
        Milestone = $milestone
        Labels = $labels
        Body = $body
    }
}

function Get-MilestoneMap {
    param(
        [string]$Repo
    )

    $json = gh api "repos/$Repo/milestones?state=all&per_page=100"
    $items = $json | ConvertFrom-Json

    $map = @{}
    foreach ($item in $items) {
        $map[$item.title] = [int]$item.number
    }

    return $map
}

function New-IssueFromMarkdown {
    param(
        [string]$Repo,
        [string]$FilePath,
        [hashtable]$MilestoneMap
    )

    Write-Host "処理中: $FilePath" -ForegroundColor Cyan

    $parsed = Get-FrontMatterAndBody -FilePath $FilePath

    if (-not $MilestoneMap.ContainsKey($parsed.Milestone)) {
        throw "milestone '$($parsed.Milestone)' が GitHub 上に存在しません: $FilePath"
    }

    $tempBodyFile = [System.IO.Path]::GetTempFileName()

    try {
        Set-Content -LiteralPath $tempBodyFile -Value $parsed.Body -Encoding UTF8

        $labelArgs = @()
        foreach ($label in $parsed.Labels) {
            $labelArgs += "--label"
            $labelArgs += $label
        }

        $issueUrl = gh issue create `
            --repo $Repo `
            --title $parsed.Title `
            --body-file $tempBodyFile `
            @labelArgs

        if ([string]::IsNullOrWhiteSpace($issueUrl)) {
            throw "Issue 作成に失敗しました: $FilePath"
        }

        $issueUrl = $issueUrl.Trim()

        if ($issueUrl -notmatch '/issues/(\d+)$') {
            throw "Issue番号を URL から取得できませんでした: $issueUrl"
        }

        $issueNumber = [int]$matches[1]
        $milestoneNumber = [int]$MilestoneMap[$parsed.Milestone]

        gh api "repos/$Repo/issues/$issueNumber" `
            -X PATCH `
            -f milestone=$milestoneNumber | Out-Null

        Write-Host "作成完了: #$issueNumber $($parsed.Title)" -ForegroundColor Green
    }
    finally {
        if (Test-Path $tempBodyFile) {
            Remove-Item $tempBodyFile -Force
        }
    }
}

if (-not (Test-Path -LiteralPath $BasePath)) {
    throw "BasePath が存在しません: $BasePath"
}

Write-Host "Repo: $Repo" -ForegroundColor Yellow
Write-Host "BasePath: $BasePath" -ForegroundColor Yellow

$milestoneMap = Get-MilestoneMap -Repo $Repo

$files = @(Get-ChildItem -LiteralPath $BasePath -Recurse -File -Filter *.md | Sort-Object FullName)

if ($files.Count -eq 0) {
    throw "対象の md ファイルがありません: $BasePath"
}

foreach ($file in $files) {
    New-IssueFromMarkdown -Repo $Repo -FilePath $file.FullName -MilestoneMap $milestoneMap
}