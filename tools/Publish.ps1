param(
    [ValidateSet('win-x64')][string]$Runtime = 'win-x64',
    [switch]$SelfContained,
    [switch]$SkipChecks,
    [switch]$Offline,
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $projectRoot
try {
    if (-not $SkipChecks) { & (Join-Path $PSScriptRoot 'Validate.ps1') -Offline:$Offline }
    [xml]$project = Get-Content -LiteralPath 'Pulsatilla.Wpf/Pulsatilla.Wpf.csproj' -Raw -Encoding UTF8
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid product version.' }
    $packageName = "pulsatilla-$version-$Runtime"
    if ($SelfContained) { $packageName += '-self-contained' }
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $projectRoot "dist/$packageName" }
    $outputPath = [IO.Path]::GetFullPath($OutputDirectory)
    $distRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'dist'))
    if (-not $outputPath.StartsWith($distRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package output must be inside this project dist directory.' }
    if (Test-Path -LiteralPath $outputPath) { throw 'Choose a new output directory; existing releases are not overwritten.' }
    $publishArguments = @('publish', 'Pulsatilla.Wpf/Pulsatilla.Wpf.csproj', '-c', 'Release', '-r', $Runtime, '--self-contained', $SelfContained.ToString().ToLowerInvariant(), '-p:PublishSingleFile=true', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $outputPath)
    if ($Offline) { $publishArguments += @('--ignore-failed-sources', '-p:NuGetAudit=false') }
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    foreach ($document in @('LICENSE','README.md','CHANGELOG.md')) { Copy-Item -LiteralPath $document -Destination $outputPath }
    $docOutput = Join-Path $outputPath 'docs'
    New-Item -ItemType Directory -Path $docOutput | Out-Null
    foreach ($document in @('PRIVACY.md','MONETIZATION.md','LEGAL_AND_WORDING.md','DEVELOPMENT.md','ORIGIN.md','ORIGIN-REVIEW.md')) { Copy-Item -LiteralPath (Join-Path 'docs' $document) -Destination $docOutput }
    $executable = Join-Path $outputPath 'Pulsatilla.exe'
    $manifest = [ordered]@{
        product = 'Pulsatilla'; version = $version; runtime = $Runtime; selfContained = [bool]$SelfContained
        requiredRuntime = $(if ($SelfContained) { 'Included' } else { '.NET 8 Windows Desktop Runtime x64' })
        license = 'MIT'; creator = [string]$project.Project.PropertyGroup.Authors; builtAtUtc = [DateTime]::UtcNow.ToString('o')
        company = [string]$project.Project.PropertyGroup.Company
        executableSha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
        signature = [string](Get-AuthenticodeSignature -LiteralPath $executable).Status
    }
    $utf8 = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText((Join-Path $outputPath 'release.json'), ($manifest | ConvertTo-Json), $utf8)
    $zipPath = $outputPath + '.zip'
    if (Test-Path -LiteralPath $zipPath) { throw 'A ZIP already exists; choose a new output directory.' }
    Compress-Archive -LiteralPath $outputPath -DestinationPath $zipPath
    $hashLines = @(
        "$((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($zipPath))"
        "$($manifest.executableSha256.ToLowerInvariant())  $([IO.Path]::GetFileName($outputPath))/Pulsatilla.exe"
    )
    [IO.File]::WriteAllLines(($outputPath + '-SHA256SUMS.txt'), $hashLines, $utf8)
    Write-Output "Package: $zipPath"
} finally { Pop-Location }
