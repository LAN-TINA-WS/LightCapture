# FFmpeg工具配置

源码仓库不存放 `ffmpeg.exe`。截图、图片编辑和本机文字识别不需要编码器；录屏、GIF导出与完整编码测试需要它。

## 使用方式

从 [FFmpeg项目](https://ffmpeg.org/download.html) 选择适合Windows x64的构建，确认包含 `gdigrab`、`lavfi`、`libx264`、AAC、GIF编码及相关滤镜。

- 本地开发或完整打包：把可执行文件放在本目录，路径为 `tools/ffmpeg.exe`。
- 已运行的应用：也可在“偏好设置 → 录屏编码器”选择本机可执行文件。

程序还会检查运行目录下的 `tools/ffmpeg.exe` 和 `ffmpeg.exe`，不会自动从PATH查找编码器。

## 构建与测试

`dotnet build` 允许编码器缺失；`.\build.ps1` 要求本目录存在编码器，因为它执行录制自测并制作本地完整便携包。

公开Release通过 `.\package-release.ps1 -EncoderPath <本机编码器路径>` 构建，编码器仅用于合成自测，不放入最终公开ZIP。下载该便携包后，录屏用户需在设置中配置自己的编码器。

本机开发曾验证的参考文件为FFmpeg 6.1.1 essentials静态构建，SHA-256如下。此哈希仅描述该参考文件，不代表其他构建都必须匹配：

```text
273F9665EF6F547E00A5C6DEC464BD9EEB3F3E7E2F05F2485AECD763E908F611
```

## 第三方分发

FFmpeg构建的许可取决于启用的组件。上述本机参考构建启用GPLv3，许可原文见 [FFmpeg-GPLv3.txt](../licenses/FFmpeg-GPLv3.txt)。如重新分发编码器，需要核对构建配置、对应源码和许可要求，见 [第三方声明](../THIRD-PARTY-NOTICES.md) 与 [FFmpeg许可说明](https://ffmpeg.org/legal.html)。本仓库不自动下载第三方编码器。
