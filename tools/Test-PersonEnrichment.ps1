<#
.SYNOPSIS
    Validates repeated-person enrichment after a full Tuvima ingestion run.

.DESCRIPTION
    Reads the expected_person_enrichment block written by GenerateTestEpubs and
    verifies that those people exist in the Engine API, are linked to multiple
    library titles, and have the expected Wikidata/Wikipedia enrichment fields.
#>
[CmdletBinding()]
param(
    [string]$EngineUrl = "http://localhost:61495",

    [string]$ManifestPath = "C:\temp\tuvima-watch\MANIFEST.json",

    [switch]$SkipHeadshotDownloadCheck
)

$ErrorActionPreference = "Stop"

function Invoke-EngineJson {
    param([Parameter(Mandatory)][string]$Path)

    Invoke-RestMethod -Uri "$EngineUrl$Path" -Method GET -TimeoutSec 30
}

function Get-Items {
    param($Response)

    if ($null -eq $Response) { return @() }
    if ($null -ne $Response.items) { return @($Response.items) }
    if ($null -ne $Response.Items) { return @($Response.Items) }
    if ($null -ne $Response.value) { return @($Response.value) }
    if ($null -ne $Response.Value) { return @($Response.Value) }
    return @($Response)
}

function Get-PropertyValue {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string[]]$Names
    )

    foreach ($name in $Names) {
        $property = $Object.PSObject.Properties |
            Where-Object { $_.Name.Equals($name, [System.StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1
        if ($null -ne $property -and $null -ne $property.Value) {
            return $property.Value
        }
    }

    return $null
}

function Normalize-Text {
    param([string]$Value)
    if ($null -eq $Value) { return "" }
    return $Value.Trim().ToLowerInvariant()
}

if (-not (Test-Path -LiteralPath $ManifestPath)) {
    throw "Manifest not found: $ManifestPath. Run tools/GenerateTestEpubs first."
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$expectedPeople = @($manifest.expected_person_enrichment)
if ($expectedPeople.Count -eq 0) {
    throw "Manifest does not contain expected_person_enrichment entries: $ManifestPath"
}

Write-Host "Validating person enrichment against $EngineUrl"
Write-Host "Manifest: $ManifestPath"
Write-Host ""

$peopleResponse = Invoke-EngineJson "/persons?limit=500"
$people = Get-Items $peopleResponse

$failures = New-Object System.Collections.Generic.List[string]

foreach ($expected in $expectedPeople) {
    $name = [string]$expected.name
    $expectedQid = [string]$expected.expected_wikidata_qid

    $person = $null
    if (-not [string]::IsNullOrWhiteSpace($expectedQid)) {
        $person = $people | Where-Object {
            $_.wikidata_qid -and $_.wikidata_qid.Equals($expectedQid, [System.StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1
    }
    if ($null -eq $person) {
        $person = $people | Where-Object {
            (Normalize-Text $_.name) -eq (Normalize-Text $name)
        } | Select-Object -First 1
    }

    if ($null -eq $person) {
        $failures.Add("Missing person '$name'.")
        Write-Host "[FAIL] $name - person not found" -ForegroundColor Red
        continue
    }

    $detail = Invoke-EngineJson "/persons/$($person.id)"
    $credits = Get-Items (Invoke-EngineJson "/persons/$($person.id)/library-credits")
    $creditTitles = @($credits | ForEach-Object {
        [string](Get-PropertyValue $_ @("title", "display_title", "work_title", "media_title", "name"))
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $creditMediaTypes = @($credits | ForEach-Object {
        [string](Get-PropertyValue $_ @("media_type", "mediaType", "kind", "media_kind"))
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)

    $personFailures = New-Object System.Collections.Generic.List[string]

    $hasExpectedQid = -not [string]::IsNullOrWhiteSpace($expectedQid)
    $qidMatches = [string]::Equals([string]$detail.wikidata_qid, $expectedQid, [System.StringComparison]::OrdinalIgnoreCase)
    if ($hasExpectedQid -and -not $qidMatches) {
        $personFailures.Add("expected QID $expectedQid, got '$($detail.wikidata_qid)'")
    }

    if ($credits.Count -lt [int]$expected.minimum_owned_credits) {
        $personFailures.Add("expected at least $($expected.minimum_owned_credits) credits, got $($credits.Count)")
    }

    if ($null -ne $expected.minimum_media_items -and $credits.Count -lt [int]$expected.minimum_media_items) {
        $personFailures.Add("expected at least $($expected.minimum_media_items) linked media items, got $($credits.Count)")
    }

    $expectedMediaTypes = @($expected.expected_media_types)
    foreach ($expectedMediaType in $expectedMediaTypes) {
        if ([string]::IsNullOrWhiteSpace([string]$expectedMediaType)) { continue }
        $hasMediaType = $creditMediaTypes | Where-Object {
            $_.Equals([string]$expectedMediaType, [System.StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1
        if ($null -eq $hasMediaType) {
            $personFailures.Add("missing expected media type '$expectedMediaType' in credits; got [$($creditMediaTypes -join ', ')]")
        }
    }

    foreach ($expectedTitle in @($expected.expected_titles)) {
        if ([string]::IsNullOrWhiteSpace([string]$expectedTitle)) { continue }
        $hasTitle = $creditTitles | Where-Object {
            $_.Equals([string]$expectedTitle, [System.StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1
        if ($null -eq $hasTitle) {
            $personFailures.Add("missing expected title '$expectedTitle' in credits")
        }
    }

    if ($expected.require_biography -and [string]::IsNullOrWhiteSpace([string]$detail.biography)) {
        $personFailures.Add("missing Wikipedia biography/description")
    }

    if ($expected.require_headshot) {
        if ([string]::IsNullOrWhiteSpace([string]$detail.headshot_url)) {
            $personFailures.Add("missing headshot URL")
        }
        elseif (-not $SkipHeadshotDownloadCheck) {
            try {
                $headshotPath = [string]$detail.headshot_url
                $headshotUri = if ($headshotPath.StartsWith("http", [System.StringComparison]::OrdinalIgnoreCase)) {
                    $headshotPath
                } else {
                    "$EngineUrl$headshotPath"
                }
                $headers = @(curl.exe -s -D - -o NUL $headshotUri)
                if ($LASTEXITCODE -ne 0 -or $headers.Count -eq 0) {
                    $personFailures.Add("headshot endpoint failed")
                    continue
                }
                $statusLine = [string]($headers | Select-Object -First 1)
                $contentTypeLine = [string]($headers | Where-Object { $_ -match '^Content-Type:' } | Select-Object -First 1)
                $contentType = $contentTypeLine -replace '^Content-Type:\s*', ''
                if (-not ($statusLine -match '^HTTP/\S+\s+2\d\d')) {
                    $personFailures.Add("headshot endpoint returned $statusLine")
                }
                elseif ([string]::IsNullOrWhiteSpace($contentType)) {
                    $personFailures.Add("headshot endpoint did not return a content type")
                }
                elseif (-not $contentType.StartsWith("image/", [System.StringComparison]::OrdinalIgnoreCase)) {
                    $personFailures.Add("headshot endpoint did not return an image content type")
                }
            }
            catch {
                $personFailures.Add("headshot endpoint failed: $($_.Exception.Message)")
            }
        }
    }

    if ($personFailures.Count -gt 0) {
        foreach ($failure in $personFailures) {
            $failures.Add("${name}: $failure")
        }
        Write-Host "[FAIL] $name - $($personFailures -join '; ')" -ForegroundColor Red
    }
    else {
        $shownTitles = ($creditTitles | Select-Object -First 4) -join "; "
        $shownMediaTypes = ($creditMediaTypes | Select-Object -First 4) -join ", "
        Write-Host "[PASS] $name - $($credits.Count) credits; QID=$($detail.wikidata_qid); media=$shownMediaTypes; titles=$shownTitles" -ForegroundColor Green
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "Person enrichment validation failed:" -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host " - $failure" -ForegroundColor Red
    }
    exit 1
}

Write-Host "Person enrichment validation passed." -ForegroundColor Green
