<#
.SYNOPSIS
  Builds and runs the tool's test suites against the built plugin, and writes the results (with a timestamp) to
  Tests\RESULTS.md.

.DESCRIPTION
  Each *.cs file here with a Main method is a small console program (no test framework needed). It loads the plugin
  DLL by reflection, drives its services against an in-memory fake of Dataverse (FakeApprovals.cs and per-test fakes)
  and a stubbed Teams Approvals API, renders its controls off-screen, and prints one PASS or FAIL line per check. Its
  exit code is the number of failed checks. Nothing here talks to a real environment.

  Build the plugin first (Debug, from Visual Studio or MSBuild). Screenshots the suites take go to Tests\out.

.EXAMPLE
  .\Tests\run-tests.ps1
  .\Tests\run-tests.ps1 -Only SeqReassignTest,ActionsTest
#>
param(
    [string]$Configuration = 'Debug',
    [string[]]$Only
)

$ErrorActionPreference = 'Stop'
$tests = $PSScriptRoot
$repo = Split-Path $tests -Parent
$bin = Join-Path $repo "bin\$Configuration"
$dll = Join-Path $bin 'ApprovalAdministrationTool.dll'
$work = Join-Path $tests 'bin'
$out = Join-Path $tests 'out'
if (-not (Test-Path $dll)) { throw "Build the plugin first: $dll not found." }
New-Item -ItemType Directory -Force $work, $out | Out-Null

# The C# compiler that comes with Visual Studio (C# latest), and the .NET Framework 4.8 reference assemblies.
$csc = Get-ChildItem "${env:ProgramFiles}\Microsoft Visual Studio", "${env:ProgramFiles(x86)}\Microsoft Visual Studio" -Recurse -Filter csc.exe -ErrorAction SilentlyContinue |
    Where-Object FullName -like '*Roslyn*' | Select-Object -First 1 -ExpandProperty FullName
if (-not $csc) { throw 'csc.exe (Roslyn) not found: install Visual Studio with the .NET desktop workload.' }
$fw = "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
$refs = @("/r:$bin\Microsoft.Xrm.Sdk.dll", "/r:$bin\Microsoft.Crm.Sdk.Proxy.dll", "/r:$bin\Microsoft.Identity.Client.dll") +
    ('System.Windows.Forms', 'System.Drawing', 'System.Net.Http', 'System.Runtime.Serialization', 'System.ServiceModel', 'System.Xml', 'System.Xml.Linq',
     'System.Data', 'System.Data.DataSetExtensions', 'System.Core', 'System.Security' | ForEach-Object { "/r:$fw\$_.dll" })

# The suites run from the plugin's folder so its dependencies resolve, with XrmToolBox's binding redirects.
$config = Join-Path $bin 'XrmToolBox.exe.config'

$suites = Get-ChildItem $tests -Filter *.cs | Where-Object { (Get-Content $_.FullName -Raw) -match 'static\s+int\s+Main\s*\(' } | Sort-Object Name
if ($Only) {
    $names = $Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    $suites = @($suites | Where-Object { $names -contains $_.BaseName })
}
if (@($suites).Count -eq 0) { throw "No test suites matched: $($Only -join ', ')" }

$started = Get-Date
$commit = (& git -C $repo rev-parse --short HEAD 2>$null)
$dirty = if ((& git -C $repo status --porcelain 2>$null)) { ' + uncommitted changes' } else { '' }
$results = @()
foreach ($suite in $suites) {
    $name = $suite.BaseName
    $exe = Join-Path $bin "Test.$name.exe"
    $sources = @($suite.FullName)
    if ((Get-Content $suite.FullName -Raw) -match '\bFakeService\b' -and $name -ne 'FakeApprovals') { $sources += (Join-Path $tests 'FakeApprovals.cs') }

    $compile = & $csc /nologo /langversion:latest /target:exe /platform:anycpu "/out:$exe" @refs @sources 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        $results += [pscustomobject]@{ Suite = $name; Passed = 0; Failed = 1; Result = 'did not compile'; Notes = (($compile -split "`n") | Where-Object { $_ -match 'error' } | Select-Object -First 3) -join ' / ' }
        continue
    }

    Copy-Item $config "$exe.config" -Force
    Push-Location $bin
    try { $output = & $exe $dll $out 2>&1 | Out-String; $code = $LASTEXITCODE } finally { Pop-Location }
    Remove-Item $exe, "$exe.config" -Force -ErrorAction SilentlyContinue

    $lines = $output -split "`r?`n"
    $passed = ($lines | Where-Object { $_ -match '^\s*(PASS|ok)\b' }).Count
    $failedLines = $lines | Where-Object { $_ -match '^\s*(FAIL|fail)\b|Unhandled Exception|^\s*at ' }
    $failed = ($lines | Where-Object { $_ -match '^\s*(FAIL|fail)\b' }).Count
    if ($code -ne 0 -and $failed -eq 0) { $failed = [Math]::Max(1, $code) }
    $results += [pscustomobject]@{
        Suite  = $name
        Passed = $passed
        Failed = $failed
        Result = if ($code -eq 0) { 'passed' } else { 'FAILED' }
        Notes  = if ($code -eq 0) { '' } else { ($failedLines | Select-Object -First 3 | ForEach-Object { $_.Trim() }) -join ' / ' }
    }
}

# Results: printed, and written to RESULTS.md with when and on what they ran.
$results | Format-Table Suite, Result, Passed, Failed -AutoSize | Out-String | Write-Host
$totalPassed = ($results | Measure-Object Passed -Sum).Sum
$totalFailed = ($results | Measure-Object Failed -Sum).Sum
$failedSuites = @($results | Where-Object Result -ne 'passed').Count
$md = @()
$md += '# Test results'
$md += ''
$md += "<!-- Written by Tests/run-tests.ps1 on $($started.ToString('yyyy-MM-dd HH:mm:ss zzz')). Run it again to refresh. -->"
$md += ''
$md += "Last run: **$($started.ToString('yyyy-MM-dd HH:mm:ss zzz'))** on commit ``$commit``$dirty, $Configuration build, took $([int]((Get-Date) - $started).TotalSeconds) s."
$md += ''
$md += if ($failedSuites -eq 0) { "All $($results.Count) suites passed ($totalPassed checks)." } else { "$failedSuites of $($results.Count) suites failed ($totalFailed failed checks, $totalPassed passed)." }
$md += ''
$md += '| Suite | Result | Checks passed | Checks failed | Notes |'
$md += '| --- | --- | ---: | ---: | --- |'
foreach ($r in $results) { $md += "| $($r.Suite) | $($r.Result) | $($r.Passed) | $($r.Failed) | $(($r.Notes -replace '\|', '\|')) |" }
$md += ''
$md += 'Each suite is a console program in this folder. It loads the built plugin by reflection and checks it against in-memory fakes of Dataverse and the Teams Approvals API, so no environment is touched. See run-tests.ps1.'
[IO.File]::WriteAllLines((Join-Path $tests 'RESULTS.md'), $md, (New-Object Text.UTF8Encoding($false)))
Write-Host "Results written to $(Join-Path $tests 'RESULTS.md')"
exit $failedSuites
