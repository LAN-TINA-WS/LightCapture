param([switch]$NetworkTest)
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$projectPath = Join-Path $projectRoot 'LightCapture.csproj'
[xml]$projectDefinition = Get-Content -LiteralPath $projectPath -Raw
$version = [string]$projectDefinition.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid project version.' }
$packageRoot = Join-Path $projectRoot "dist\LightCapture-$version-win-x64"
$artifactRoot = Join-Path $projectRoot 'test-artifacts'
$reportPath = Join-Path $projectRoot 'test-results-release.json'
$proofRoot = Join-Path $projectRoot 'toolbar-proof'
$appearanceRoot = Join-Path $projectRoot 'appearance-proof'
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'tools\ffmpeg.exe'))) {
    throw 'tools\ffmpeg.exe is missing. Supply a local FFmpeg Windows build before publishing.'
}
& dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -o $packageRoot --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$testArgs = "--self-test --report `"$reportPath`" --artifacts `"$artifactRoot`""
if ($NetworkTest) { $testArgs += ' --network-test' }
$testRun = Start-Process -FilePath (Join-Path $packageRoot 'LightCapture.exe') -ArgumentList $testArgs -WindowStyle Hidden -Wait -PassThru
if ($testRun.ExitCode -ne 0) { throw "Tests failed. Review $reportPath" }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$proofRun = Start-Process -FilePath (Join-Path $packageRoot 'LightCapture.exe') -ArgumentList "--toolbar-proof --artifacts `"$proofRoot`"" -WindowStyle Hidden -Wait -PassThru
if ($proofRun.ExitCode -ne 0) { throw "Toolbar layout checks failed. Review $proofRoot" }
$proof = Get-Content -LiteralPath (Join-Path $proofRoot 'results.json') -Raw | ConvertFrom-Json
$appearanceRun = Start-Process -FilePath (Join-Path $packageRoot 'LightCapture.exe') -ArgumentList "--appearance-proof --artifacts `"$appearanceRoot`"" -WindowStyle Hidden -Wait -PassThru
if ($appearanceRun.ExitCode -ne 0) { throw "Settings and menu checks failed. Review $appearanceRoot" }
$appearance = Get-Content -LiteralPath (Join-Path $appearanceRoot 'results.json') -Raw | ConvertFrom-Json
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $packageRoot '使用说明.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'IMPLEMENTATION.md') -Destination (Join-Path $packageRoot '实现与测试范围.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Destination $packageRoot
Copy-Item -LiteralPath $reportPath -Destination (Join-Path $packageRoot '验证结果.json')
Copy-Item -LiteralPath (Join-Path $proofRoot 'results.json') -Destination (Join-Path $packageRoot '工具栏布局验证.json')
Copy-Item -LiteralPath (Join-Path $proofRoot 'toolbar-150.png') -Destination (Join-Path $packageRoot '工具栏预览.png')
Copy-Item -LiteralPath (Join-Path $proofRoot 'toolbar-brush-150.png') -Destination (Join-Path $packageRoot '普通画笔预览.png')
Copy-Item -LiteralPath (Join-Path $proofRoot 'toolbar-mosaic-150.png') -Destination (Join-Path $packageRoot '马赛克画笔预览.png')
Copy-Item -LiteralPath (Join-Path $appearanceRoot 'results.json') -Destination (Join-Path $packageRoot '设置与菜单验证.json')
Copy-Item -LiteralPath (Join-Path $appearanceRoot 'settings-100.png') -Destination (Join-Path $packageRoot '设置预览.png')
Copy-Item -LiteralPath (Join-Path $appearanceRoot 'size-settings.png') -Destination (Join-Path $packageRoot '界面大小设置.png')
Copy-Item -LiteralPath (Join-Path $appearanceRoot 'tray-menu-100.png') -Destination (Join-Path $packageRoot '托盘菜单预览.png')
Copy-Item -LiteralPath (Join-Path $appearanceRoot 'translation-100-wide.png') -Destination (Join-Path $packageRoot '识图翻译预览.png')
Copy-Item -LiteralPath (Join-Path $appearanceRoot 'translation-100-medium.png') -Destination (Join-Path $packageRoot '识图翻译紧凑预览.png')
Copy-Item -LiteralPath (Join-Path $appearanceRoot 'translation-150-busy.png') -Destination (Join-Path $packageRoot '翻译请求中预览.png')
Copy-Item -LiteralPath (Join-Path $appearanceRoot 'buttons-150-white.png') -Destination (Join-Path $packageRoot '圆角按钮状态预览.png')
Copy-Item -LiteralPath (Join-Path $appearanceRoot 'translation-backend-OpenAiCompatible-150-compact.png') -Destination (Join-Path $packageRoot '翻译后端设置预览.png')
Copy-Item -LiteralPath (Join-Path $appearanceRoot 'translation-backend-DeepL-150-compact.png') -Destination (Join-Path $packageRoot 'DeepL设置预览.png')
Copy-Item -LiteralPath (Join-Path $appearanceRoot 'magnifier-150.png') -Destination (Join-Path $packageRoot '放大镜取色预览.png')
Copy-Item -LiteralPath (Join-Path $appearanceRoot 'window-preselection.png') -Destination (Join-Path $packageRoot '窗口预选效果示例.png')

$manifest = Get-ChildItem -LiteralPath $packageRoot -File -Recurse | Where-Object Name -ne 'manifest.json' | ForEach-Object {
    [pscustomobject]@{ path = [System.IO.Path]::GetRelativePath($packageRoot, $_.FullName).Replace('\','/'); size = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
}
[pscustomobject]@{ version=$version; builtUtc=[DateTimeOffset]::UtcNow.ToString('o'); programTestsPassed=$report.passed; toolbarLayoutChecksPassed=$proof.passed; interfaceChecksPassed=$appearance.passed; guiTests='pending-user-trial'; files=$manifest } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $packageRoot 'manifest.json') -Encoding utf8
$zipPath = Join-Path $projectRoot "dist\LightCapture-$version-win-x64.zip"
Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal -Force
[pscustomobject]@{ executable=(Join-Path $packageRoot 'LightCapture.exe'); zip=$zipPath; bytes=(Get-Item -LiteralPath $zipPath).Length; testsPassed=$report.passed; testsFailed=$report.failed } | ConvertTo-Json
