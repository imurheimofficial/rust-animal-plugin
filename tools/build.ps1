param(
    [switch]$Deploy,                 # copy dist/Beastmaster.cs to the running dev server (hot reload)
    [ValidateSet('oxide', 'carbon')][string]$Framework = 'oxide'
)
# Build = bundle src/*.cs -> dist/Beastmaster.cs, then compile it with the harness against the real server DLLs.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repo
python -I tools/bundle.py
if ($LASTEXITCODE -ne 0) { throw "bundle failed" }
dotnet build build/Harness.csproj -nologo -v:quiet -p:PluginFile=../dist/Beastmaster.cs
if ($LASTEXITCODE -ne 0) { throw "harness compile failed" }
if ($Deploy) {
    $plugins = if ($Framework -eq 'oxide') { 'C:\RustDev\server-oxide\oxide\plugins' } else { 'C:\RustDev\server-carbon\carbon\plugins' }
    Copy-Item -LiteralPath (Join-Path $repo 'dist\Beastmaster.cs') -Destination $plugins -Force
    "deployed to $plugins"
}
"BUILD OK"
