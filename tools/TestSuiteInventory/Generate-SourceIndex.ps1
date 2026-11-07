# Extract the source declaration behind each xUnit Fact/Theory for the migration inventory.
# Runtime discovery remains authoritative for theory rows, skips, and selection.
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\..\docs\testing-inventory\Phase1.SourceMethods.csv')
)

$projects = @(
    'VanillaGraphicsExpanded.Tests',
    'OpenGLInterceptionGenerator.Tests',
    'ShaderBuildTool.Tests',
    'ShaderContractGenerator.Tests'
)
$rows = [System.Collections.Generic.List[object]]::new()
$supportRows = [System.Collections.Generic.List[object]]::new()
$methodPattern = '^\s*(?:public|internal|private|protected)\s+(?:(?:static|async|virtual|override|sealed|new)\s+)*(?:[\w<>\[\]\?,\.]+\s+)+(?<name>[A-Za-z_]\w*)\s*\('

foreach ($project in $projects) {
    $projectPath = Join-Path $RepositoryRoot $project
    $files = Get-ChildItem -LiteralPath $projectPath -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\(?:obj|bin|artifacts|TestResults)\\' }

    foreach ($file in $files) {
        $lines = [System.IO.File]::ReadAllLines($file.FullName)
        $rowCountBeforeFile = $rows.Count
        $namespace = ''
        $class = ''
        $pending = $false
        $attributeLine = 0
        $attributeKind = ''

        for ($index = 0; $index -lt $lines.Length; $index++) {
            $line = $lines[$index]
            if (-not $namespace -and $line -match '^\s*namespace\s+(?<value>[\w\.]+)\s*[;{]') {
                $namespace = $Matches.value
            }
            if ($line -match '^\s*(?:(?:public|internal|private|protected|sealed|abstract|static|partial|unsafe)\s+)*class\s+(?<value>\w+)') {
                # Collection definitions can precede the test class in the same file.
                $candidateClass = $Matches.value
                if ($candidateClass -match 'Tests$') {
                    $class = $candidateClass
                }
            }
            if ($line -notmatch '^\s*///' -and $line -match '\[(?<kind>Fact|Theory)(?:Attribute)?\b') {
                $pending = $true
                $attributeLine = $index + 1
                $attributeKind = $Matches.kind
            }
            if (-not $pending -or $line -notmatch $methodPattern) {
                continue
            }

            $method = $Matches.name
            $summary = ''
            for ($back = $attributeLine - 2; $back -ge [Math]::Max(0, $attributeLine - 8); $back--) {
                if ($lines[$back] -match '<summary>(?<value>[^<]+)') {
                    $summary = $Matches.value.Trim()
                    break
                }
                if ($lines[$back].Trim() -and $lines[$back] -notmatch '^\s*///') {
                    break
                }
            }
            $relative = [System.IO.Path]::GetRelativePath($RepositoryRoot, $file.FullName).Replace('\', '/')
            $rows.Add([pscustomobject]@{
                Project = $project
                SourcePath = $relative
                SourceLine = $index + 1
                Namespace = $namespace
                Class = $class
                Method = $method
                SourceKey = "$project::$namespace.$class.$method"
                Attribute = $attributeKind
                Summary = $summary
            })
            $pending = $false
            $attributeKind = ''
        }

        if ($rows.Count -eq $rowCountBeforeFile) {
            $relative = [System.IO.Path]::GetRelativePath($RepositoryRoot, $file.FullName).Replace('\', '/')
            $declaredTypes = @($lines | ForEach-Object {
                if ($_ -match '^\s*(?:(?:public|internal|private|protected|sealed|abstract|static|partial|unsafe|readonly)\s+)*(?:class|record|struct|interface|enum)\s+(?<type>\w+)') {
                    $Matches.type
                }
            } | Select-Object -Unique)
            $sourceText = [string]::Join("`n", $lines)
            $supportRows.Add([pscustomobject]@{
                Project = $project
                SourcePath = $relative
                DeclaredTypes = $declaredTypes -join ';'
                IDisposableCue = $sourceText -match '\bIDisposable\b'
                StaticStateCue = $sourceText -match '\bstatic\s+(?:readonly\s+)?(?:\w+[<>,\[\]?]*\s+)+\w+\s*(?:=|;)'
                RoleHint = if ($relative -match '/(Fixtures|Helpers|Assertions|TestSupport)/') { $Matches[1] } else { 'Local support or collection' }
                Unresolved = 'Review consumer ownership, effects, and fixture lifetime'
            })
        }
    }
}

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$rows | Sort-Object Project, SourcePath, SourceLine | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding utf8
$supportPath = Join-Path $outputDirectory 'Phase1.SupportSources.csv'
$supportRows | Sort-Object Project, SourcePath | Export-Csv -LiteralPath $supportPath -NoTypeInformation -Encoding utf8
Write-Output "Source test methods: $($rows.Count)"
Write-Output "Support-only source files: $($supportRows.Count)"
Write-Output "Output: $OutputPath"
