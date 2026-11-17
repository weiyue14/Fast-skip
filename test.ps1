$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'dist') | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$output = Join-Path $PSScriptRoot 'dist\FastSkip.Tests.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
& $compiler /nologo /target:exe /main:TestRunner /codepage:65001 "/resource:$PSScriptRoot\src\UI.xaml,FastSkip.UI.xaml" "/out:$output" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll /reference:System.Xaml.dll "/reference:$wpf\WindowsBase.dll" "/reference:$wpf\PresentationCore.dll" "/reference:$wpf\PresentationFramework.dll" "/reference:$wpf\WindowsFormsIntegration.dll" $sources (Join-Path $PSScriptRoot 'tests\TestRunner.cs')
if ($LASTEXITCODE -ne 0) { throw '测试编译失败' }
& $output
if ($LASTEXITCODE -ne 0) { throw '测试失败' }
