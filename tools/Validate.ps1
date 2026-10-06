param([switch]$Offline)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $projectRoot
try {
    $projects = @('Pulsatilla.Wpf/Pulsatilla.Wpf.csproj',
        'tools/Protection.Checks/Protection.Checks.csproj', 'tools/Localization.Checks/Localization.Checks.csproj',
        'tools/Network.Checks/Network.Checks.csproj', 'tools/Inspection.Checks/Inspection.Checks.csproj',
        'tools/Analysis.Checks/Analysis.Checks.csproj', 'tools/Email.Checks/Email.Checks.csproj',
        'tools/Scanner.Checks/Scanner.Checks.csproj', 'tools/Workflow.Checks/Workflow.Checks.csproj',
        'tools/HostLayout.Checks/HostLayout.Checks.csproj')
    foreach ($project in $projects) {
        $restoreArguments = @('restore', $project, '--ignore-failed-sources')
        if ($Offline) { $restoreArguments += '-p:NuGetAudit=false' }
        & dotnet @restoreArguments
        if ($LASTEXITCODE -ne 0) { throw "Restore failed: $project" }
    }
    & dotnet build Pulsatilla.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    foreach ($project in $projects[1..($projects.Count - 1)]) {
        & dotnet run --project $project -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Checks failed: $project" }
    }
} finally { Pop-Location }
