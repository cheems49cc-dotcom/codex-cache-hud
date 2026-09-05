param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src-dotnet/CodexMonitorHud.App/CodexMonitorHud.App.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw -Encoding UTF8)).Project.PropertyGroup.Version
$output = Join-Path $root "artifacts/CodexCacheHUD-$version-win-x64"
if (Test-Path -LiteralPath $output) { throw 'Output already exists. Review/remove the previous build before publishing again.' }
& $Dotnet publish $project -c Release -r win-x64 --self-contained true -o $output -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
foreach ($name in @('README.md','README.zh-CN.md','LICENSE','PRIVACY.md','COLOR_ATTRIBUTION.md')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $output
}
$forbidden = Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object {
    $_.Extension -in '.pdb','.jsonl','.sqlite','.db','.key','.pem','.pfx' -or $_.Name -in 'auth.json','settings.json','.env'
}
if ($forbidden) { throw 'Unexpected private/debug files in build output; nothing packaged.' }
$zip = "$output.zip"
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$zip.sha256", "$hash  $([IO.Path]::GetFileName($zip))`n", [Text.UTF8Encoding]::new($false))
Write-Output "Built: $zip"
Write-Output "SHA256: $hash"
