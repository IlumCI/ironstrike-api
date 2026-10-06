#requires -Version 5
<#
  Build the documentation site into docs\_build\html.

    .\build-docs.ps1

  Needs the .NET SDK and Python 3 with the packages in docs\requirements.txt
  (py -m pip install -r docs\requirements.txt). Warnings fail the build, as they do in CI.
#>
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $here
try {
    dotnet build IronstrikeApi\IronstrikeApi.csproj -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "build failed" }
    $nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
    dotnet run --project docs\tools\RefGen -c Release -- IronstrikeApi\bin\Release\net6.0\IronstrikeApi.dll docs\reference\api refs $nuget
    if ($LASTEXITCODE -ne 0) { throw "reference generation failed (undocumented public members?)" }
    $py = if ($env:PYTHON) { $env:PYTHON } else { 'py' }
    & $py -m sphinx -W --keep-going -b html docs docs\_build\html
    if ($LASTEXITCODE -ne 0) { throw "sphinx failed" }
    Write-Host "==> docs\_build\html\index.html"
} finally { Pop-Location }
