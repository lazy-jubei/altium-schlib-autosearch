$ErrorActionPreference='Stop'
$package=Split-Path -Parent $PSScriptRoot
$staging=Join-Path ([IO.Path]::GetTempPath()) ('SchLibSearchPaths-build-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
$payload=Join-Path $staging 'payload.zip'
$manifest=Join-Path $staging 'PayloadManifest.cs'
try {
$files=@('SchLibSearchPaths.dll','SchLibSearchPaths.ins','Control.ps1','README.md') | ForEach-Object {Join-Path $package $_}
Compress-Archive -LiteralPath $files -DestinationPath $payload -Force
$hash=(Get-FileHash -LiteralPath $payload).Hash
[IO.File]::WriteAllText($manifest,('namespace LibrarySearchPathsSetup { internal static class PayloadManifest { internal const string Sha256="'+$hash+'"; } }'))
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output=Join-Path $package 'SchLibSearchPaths-Setup-1.1.1.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.Linq.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll ('/out:'+$output) ('/resource:'+$payload+',payload.zip') (Join-Path $PSScriptRoot 'Setup.cs') $manifest
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
Get-Item -LiteralPath $output | Select-Object FullName,Length
} finally {
    foreach($generated in @($payload,$manifest)){if(Test-Path -LiteralPath $generated){Remove-Item -LiteralPath $generated}}
    Remove-Item -LiteralPath $staging
}
