# Publishes the site and copies it to the NAS (Z: = \\LUNARBASE\docker).
# Afterwards restart the "wowcharacters" project in DSM Container Manager.
# The character list and stories in Z:\wowcharacters\data and the .env file are never touched.

$ErrorActionPreference = 'Stop'
$target = 'Z:\wowcharacters'

if (-not (Test-Path 'Z:\')) { throw 'Drive Z: (\\LUNARBASE\docker) is not mapped.' }

$publish = "$PSScriptRoot\publish"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish "$PSScriptRoot\WowCharacters.csproj" -c Release -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
foreach ($name in 'appsettings.Development.json', 'web.config', 'WowCharacters.exe') {
    Remove-Item (Join-Path $publish $name) -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force "$target\app", "$target\data" | Out-Null
robocopy $publish "$target\app" /MIR /NFL /NDL /NJH /NP
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE." }

Copy-Item "$PSScriptRoot\deploy\docker-compose.yml" "$target\docker-compose.yml" -Force
if (-not (Test-Path "$target\.env")) {
    Write-Warning "No .env in $target yet. Copy deploy\.env.example there as .env and fill in the Blizzard API client."
}
Write-Host "Copied to $target. Restart the 'wowcharacters' project on the NAS."
exit 0
