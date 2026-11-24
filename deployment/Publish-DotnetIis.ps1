# Stage a matching, self-contained Windows x64 API and Blazor IIS site. Does not modify IIS or services.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $ReleasePath
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$release = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ReleasePath)
if (Test-Path -LiteralPath $release) {
    throw "Release path already exists: $release. Choose a new release directory."
}
$api = Join-Path $release 'api'
$site = Join-Path $release 'site'
$config = Join-Path $PSScriptRoot 'iis-web.config'
# Catch XML mistakes before publishing. Only the Blazor site gets this config.
[xml] $iisConfig = Get-Content -LiteralPath $config -Raw

function Publish-Project($Project, $Destination) {
    dotnet publish (Join-Path $repo $Project) -c Release -r win-x64 --self-contained true -o $Destination
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $Project" }
}

New-Item -ItemType Directory -Path $release | Out-Null
Publish-Project 'src/CFC.ChatAI.Api/CFC.ChatAI.Api.csproj' $api
Publish-Project 'src/CFC.ChatAI.Web/CFC.ChatAI.Web.csproj' $site
foreach ($file in @((Join-Path $api 'CFC.ChatAI.Api.exe'), (Join-Path $site 'CFC.ChatAI.Web.exe'))) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Publish did not produce $file" }
}
$framework = Join-Path $site 'wwwroot/_framework'
if (-not (Get-ChildItem -LiteralPath $framework -Filter 'blazor.web*.js' -File)) {
    throw 'Publish did not produce the Blazor browser script.'
}
Copy-Item -LiteralPath $config -Destination (Join-Path $site 'web.config') -Force
Write-Host "Stage complete. API executable: $api\CFC.ChatAI.Api.exe; IIS physical path: $site"
