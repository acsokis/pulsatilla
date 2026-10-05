param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $projectRoot
try {
    & node (Join-Path $PSScriptRoot 'Build.Publication.mjs')
    if ($LASTEXITCODE -ne 0) { throw 'Publication page build failed.' }
    [xml]$project = Get-Content -LiteralPath 'Pulsatilla.Wpf/Pulsatilla.Wpf.csproj' -Raw -Encoding UTF8
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid version.' }
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = Join-Path $projectRoot "dist/pulsatilla-publication-kit-$version"
    }
    $outputPath = [IO.Path]::GetFullPath($OutputDirectory)
    $distRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'dist'))
    if (-not $outputPath.StartsWith($distRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Publication output must stay inside the project dist directory.'
    }
    $zipPath = $outputPath + '.zip'
    if ((Test-Path -LiteralPath $outputPath) -or (Test-Path -LiteralPath $zipPath)) { throw 'Existing publication kits are not overwritten.' }
    New-Item -ItemType Directory -Path $outputPath | Out-Null
    Copy-Item -Path 'docs/launch/*' -Destination $outputPath -Recurse
    Copy-Item -LiteralPath 'LICENSE' -Destination $outputPath
    Compress-Archive -LiteralPath $outputPath -DestinationPath $zipPath
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(($zipPath + '.sha256'), ($hash + '  ' + [IO.Path]::GetFileName($zipPath) + "`n"), [Text.UTF8Encoding]::new($false))
    Write-Output "Publication kit: $zipPath"
} finally { Pop-Location }
