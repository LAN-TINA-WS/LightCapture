# 参与贡献

欢迎提交可复现的问题、改进建议与代码补丁。请先阅读 [README](README.md) 和 [实现与验证范围](IMPLEMENTATION.md)，确认问题所在模块及当前限制。

## 开发环境

- Windows 10 2004或更新的x64系统，建议使用Windows 11。
- .NET SDK 10，PowerShell，Git。
- 完整录屏测试需要 [FFmpeg](tools/README.md)。

```powershell
dotnet restore .\LightCapture.csproj
dotnet build .\LightCapture.csproj -c Release
```

源码可以在未放置FFmpeg二进制时编译。完整打包脚本会检查编码器是否存在。

## 验证改动

从项目根目录运行下列命令，报告和合成图片会写到忽略目录。离屏验证不会显示真实桌面窗口。

```powershell
$app = '.\bin\Release\net10.0-windows10.0.19041.0\LightCapture.exe'
Start-Process -FilePath $app -ArgumentList '--toolbar-proof --artifacts toolbar-proof' -Wait -PassThru
Start-Process -FilePath $app -ArgumentList '--appearance-proof --artifacts appearance-proof' -Wait -PassThru
```

准备好FFmpeg后，可执行完整自测：

```powershell
Start-Process -FilePath $app -ArgumentList '--self-test --report test-results.json --artifacts test-artifacts' -Wait -PassThru
```

检查进程的 `ExitCode` 与报告中的 `failed`；仅生成报告不代表验证通过。程序自测会向系统剪贴板写入合成图片，并将合成录屏写到本机应用缓存；这些文件不应提交到仓库。

普通自测不访问付费翻译接口。只有显式添加 `--network-test` 才会向默认公共翻译接口发送固定短句。后端协议测试使用内存模拟请求，不能代替真实账户的连接测试。

界面变更请检查100%、125%、150%、200%缩放以及紧凑窗口，说明真实桌面试用与离屏验证各自覆盖的范围。

## 提交内容

- 使用简洁的提交说明，描述用户可见的变化和验证结果。
- 新功能同时更新使用说明、实现范围与必要的变更记录。
- `README.md` 与 `README.en.md` 的功能、限制、版本和下载信息保持同步；顶部使用相对链接切换语言，反馈入口统一指向仓库Issues。
- Markdown围绕本项目的功能、使用、实现与依赖编写，避免产品对比、仿制来源及开发聊天过程。第三方名称只用于必要的依赖、接口和许可说明。
- 保留MIT许可及第三方原始声明，不把外部代码或资源直接标为本项目自有。
- 不提交个人设置、密钥、日志、未经清理的用户截图、录屏、编码器、构建目录或测试产物。文档预览仅使用专门准备的合成内容。

## 问题报告

请提供程序版本、Windows版本、显示器数量与缩放比例、复现步骤、预期结果和实际结果。涉及界面时可提供裁去私人内容的截图；日志同样需要去掉密钥、接口内网地址和个人路径。

提交补丁表示你有权以本项目MIT许可提供该贡献；独立引入的第三方内容需要附上其来源与许可。
