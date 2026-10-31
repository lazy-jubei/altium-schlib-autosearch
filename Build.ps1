param([string]$AltiumSystem = 'C:\Program Files\Altium\AD25\System')
$ErrorActionPreference = 'Stop'

# PowerShell 7 ships Roslyn. Give it Altium's .NET 8 assemblies explicitly,
# rather than targeting the newer runtime used by PowerShell itself.
$compilerDlls = @((Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll'), (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll'))
foreach ($compilerDll in $compilerDlls) { [Reflection.Assembly]::LoadFrom($compilerDll) | Out-Null }
$compilerRefs = $compilerDlls + @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName)
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -ReferencedAssemblies $compilerRefs -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
public static class SquareEndsCompiler
{
    public static string Compile(string source, string output, string[] references)
    {
        var trees = Directory.GetFiles(Path.GetDirectoryName(source), "*.cs")
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path)));
        var metadata = references.Select(path => MetadataReference.CreateFromFile(path));
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release, platform: Platform.X64);
        var compilation = CSharpCompilation.Create("SchLibSearchPaths", trees, metadata, options);
        using (var stream = File.Create(output))
        {
            var result = compilation.Emit(stream);
            return String.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString()));
        }
    }
}
'@
$panelRuntimeRoot = Join-Path $AltiumSystem 'DotNet\Runtime\shared'
$panelRuntimeDirs = @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App') | ForEach-Object {
    (Get-ChildItem -LiteralPath (Join-Path $panelRuntimeRoot $_) -Directory |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1).FullName
}
$panelRefs = @()
foreach ($panelRuntimeDir in $panelRuntimeDirs) {
    foreach ($panelRuntimeDll in (Get-ChildItem -LiteralPath $panelRuntimeDir -Filter '*.dll')) {
        try {
            [Reflection.AssemblyName]::GetAssemblyName($panelRuntimeDll.FullName) | Out-Null
            $panelRefs += $panelRuntimeDll.FullName
        } catch { }
    }
}
$panelRefs += @((Join-Path $AltiumSystem 'Altium.SDK.dll'), (Join-Path $AltiumSystem 'Altium.SDK.Interfaces.dll'), (Join-Path $AltiumSystem 'Altium.Edp.Interfaces.dll'), (Join-Path $AltiumSystem 'Altium.Dxp.Interfaces.dll'))
$panelOutput = Join-Path $PSScriptRoot 'SchLibSearchPaths.dll'
$panelRefs += @((Join-Path $AltiumSystem 'Altium.ActiveBOM.Contracts.dll'),(Join-Path $AltiumSystem 'Altium.BOM.Contracts.dll'))
$panelRefs += @((Join-Path $AltiumSystem 'Altium.Edp.Classes.dll'),(Join-Path $AltiumSystem 'Altium.WorkspaceManager.Core.dll'))
$diagnostics = [SquareEndsCompiler]::Compile((Join-Path $PSScriptRoot 'SearchPathLibraries.cs'), $panelOutput, $panelRefs)
if ($diagnostics) { Write-Output $diagnostics }
if ($diagnostics -match 'error CS' -or (Get-Item -LiteralPath $panelOutput).Length -eq 0) {
    throw 'SchLib search path extension compilation failed.'
}
Get-Item -LiteralPath $panelOutput | Select-Object Name, Length
