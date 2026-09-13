#Requires -Version 5.1
<#
.SYNOPSIS
    Generates C# classes from UANodeSet.xsd using the .NET xsd.exe tool.
.DESCRIPTION
    Runs xsd.exe to produce UANodeSet.cs in the Opc.Ua.Export namespace,
    then prepends a #pragma to suppress missing-XML-comment warnings.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$xsd = Get-Command xsd -ErrorAction SilentlyContinue
if (-not $xsd) {
    Write-Error "xsd.exe is not in the PATH. Install the Windows SDK or Visual Studio."
    exit 1
}

Push-Location $PSScriptRoot
try {
    Write-Host "Processing NodeSet Schema"
    & xsd /classes /n:Opc.Ua.Export UANodeSet.xsd
    if ($LASTEXITCODE -ne 0) { throw "xsd.exe failed with exit code $LASTEXITCODE" }

    $content = Get-Content -Raw -Encoding UTF8 'UANodeSet.cs'
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    $final = "#pragma warning disable 1591`r`n" + $content
    [System.IO.File]::WriteAllText(
        (Join-Path $PSScriptRoot 'UANodeSet.cs'),
        $final,
        $utf8NoBom
    )

    Write-Host "UANodeSet.cs generated successfully."
}
finally {
    Pop-Location
}
