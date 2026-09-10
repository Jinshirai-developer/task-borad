param([ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repository 'desktop/TaskBoard.Windows/TaskBoard.Windows.csproj'
$tests = Join-Path $repository 'desktop/TaskBoard.Windows.Core.Tests/TaskBoard.Windows.Core.Tests.csproj'
$destination = Join-Path $repository "artifacts/windows/$Runtime"
# Each publish uses a fresh directory, so removed application files cannot remain in the package.
$packageDirectory = Join-Path $destination ('app-' + [guid]::NewGuid().ToString('N'))
Push-Location $repository
try {
    dotnet test $tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Windowsクライアントのテストに失敗しました。' }
    dotnet publish $project -c Release -r $Runtime --self-contained true -o $packageDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Windowsクライアントのビルドに失敗しました。' }
    Copy-Item (Join-Path $repository 'desktop/README.txt') (Join-Path $packageDirectory 'README.txt')
    $archive = Join-Path $destination "taskboard-windows-0.1.0-$Runtime.zip"
    Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $archive -Force
    (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content "$archive.sha256" -Encoding ascii
    Write-Output "ZIP: $archive"
    Write-Output "EXE: $(Join-Path $packageDirectory 'TaskBoard.Windows.exe')"
} finally { Pop-Location }
