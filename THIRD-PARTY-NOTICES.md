# 第三方组件与服务

根目录的 [MIT许可](LICENSE) 适用于轻截自身代码与项目自有资源。下面的第三方内容遵循各自的许可，不因本项目采用MIT而改变。

## NAudio 2.2.1

Copyright 2020 Mark Heath。使用MIT许可，原文见 [NAudio-MIT.txt](licenses/NAudio-MIT.txt)。通过NuGet还原，不在源码仓库中存放其编译结果。

- 项目：[NAudio](https://github.com/naudio/NAudio)
- 用途：Windows音频采集、混音与格式处理。

## FFmpeg

FFmpeg是独立的可选编码器，由程序通过命令行进程调用。源码仓库不包含 `ffmpeg.exe`，准备方法见 [工具说明](tools/README.md)。所选构建启用的组件会影响其适用许可，不能将其标为本项目的MIT代码。

本机参考构建为Gyan essentials FFmpeg 6.1.1静态构建，启用GPLv3。保留的GPLv3原文见 [FFmpeg-GPLv3.txt](licenses/FFmpeg-GPLv3.txt)，其二进制SHA-256为：

```text
273F9665EF6F547E00A5C6DEC464BD9EEB3F3E7E2F05F2485AECD763E908F611
```

- 项目：[FFmpeg](https://ffmpeg.org/)
- 构建提供方：[Gyan](https://www.gyan.dev/ffmpeg/builds/)
- 上游6.1.1源码：[版本归档](https://ffmpeg.org/releases/ffmpeg-6.1.1.tar.xz)
- 许可与分发要求：[FFmpeg说明](https://ffmpeg.org/legal.html)

上游版本归档不代替具体二进制构建所需的全部对应源码、依赖、补丁与构建信息。若另行公开包含编码器的便携包，需要核对并满足该构建的分发要求；源码仓库的MIT许可声明不代表已经完成第三方二进制分发准备。

## .NET与Windows组件

项目使用.NET 10、WinForms及Microsoft Windows SDK .NET投影，通过SDK和NuGet还原。自包含发布包会包含相应运行时及其许可声明，依赖清单见生成的 `LightCapture.deps.json`。

Windows系统接口和本机OCR语言包由操作系统提供。它们不随本项目源码重新授权。

## 翻译服务

翻译服务的名称用于标识可配置的接口，其服务条款、密钥权限及用量由提供方管理。本项目不附带服务密钥。

- [MyMemory接口](https://mymemory.translated.net/doc/spec.php)：使用Get接口，不调用提交语料的Set／Import接口。
- [OpenAI Chat Completions格式](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create)：兼容模式的协议参考，实际服务由用户填写的地址决定。
- [DeepL文字翻译](https://developers.deepl.com/api-reference/translate/request-translation)：Free／Pro账户使用各自的官方端点。

本机OCR不发送截图；翻译仅向用户选择的服务发送文字。贡献与测试时请使用合成内容，勿在仓库中放入个人密钥或真实业务数据。
