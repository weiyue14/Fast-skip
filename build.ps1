param([ValidatePattern('^[A-Za-z0-9._-]+\.exe$')][string]$OutputName = 'FastSkip.exe')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '需要 Windows 自带的 .NET Framework 4.x 编译器。' }
$out = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $out | Out-Null
& (Join-Path $root 'make-icon.ps1')
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName)
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 "/win32manifest:$root\app.manifest" "/win32icon:$root\assets\FastSkip.ico" "/resource:$root\assets\FastSkip.ico,FastSkip.Icon.ico" "/resource:$root\src\UI.xaml,FastSkip.UI.xaml" "/out:$out\$OutputName" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll /reference:System.Xaml.dll "/reference:$wpf\WindowsBase.dll" "/reference:$wpf\PresentationCore.dll" "/reference:$wpf\PresentationFramework.dll" "/reference:$wpf\WindowsFormsIntegration.dll" $sources
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Write-Host "已生成 $out\$OutputName"
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $out 'Fast Skip.lnk'))
$shortcut.TargetPath = Join-Path $out $OutputName
$shortcut.WorkingDirectory = $out
$shortcut.IconLocation = (Join-Path $out $OutputName) + ',0'
$shortcut.Description = 'Fast Skip'
$shortcut.Save()
Write-Host "已生成 $out\Fast Skip.lnk"
