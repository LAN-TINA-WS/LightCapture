using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace LightCapture;

internal static class TranslationTests
{
    const string SyntheticKey = "lightcapture-synthetic-test-key";
    internal static void Register(Action<string, Action> test)
    {
        test("翻译配置兼容旧文件及空配置", () =>
        {
            Check(AppSettings.Deserialize("{\"Hotkey\":66}").Translation.Backend == TranslationBackend.MyMemory, "Old settings lost their default backend.");
            Check(AppSettings.Deserialize("{\"Hotkey\":66,\"Translation\":null}").Hotkey == Keys.B, "Null translation configuration broke other settings.");
            Check(new TranslationPreferences { Provider = "unknown" }.Backend == TranslationBackend.MyMemory, "Unknown backend must use the documented default.");
        });
        test("翻译密钥加密保存及配置恢复", () =>
        {
            var config = ChatConfig(); config.DeepLApiKey = SyntheticKey + "-deepl";
            string json = JsonSerializer.Serialize(new AppSettings { Translation = config });
            Check(!json.Contains(SyntheticKey) && !json.Contains("ApiKey"), "Plaintext secret was serialized.");
            var restored = AppSettings.Deserialize(json).Translation;
            Check(restored.OpenAiApiKey == SyntheticKey && restored.DeepLApiKey == SyntheticKey + "-deepl", "Protected secrets did not round-trip.");
            Check(restored.OpenAiModel == "synthetic-model" && restored.Backend == TranslationBackend.OpenAiCompatible, "Backend/model configuration was lost.");
            var clone = config.Clone(); clone.OpenAiModel = "another-model"; clone.OpenAiApiKey = "another-synthetic-key";
            Check(config.OpenAiModel == "synthetic-model" && config.OpenAiApiKey == SyntheticKey, "Cloned preferences modified the original.");
        });
        test("翻译接口地址规范化及配置校验", () =>
        {
            foreach (string endpoint in new[] { "https://example.test/prefix/v1", "https://example.test/prefix/v1/", "https://example.test/prefix/v1/chat/completions" })
                Check(new TranslationPreferences { OpenAiBaseUrl = endpoint }.ChatEndpoint().AbsoluteUri == "https://example.test/prefix/v1/chat/completions", "Endpoint prefix or suffix was lost/duplicated.");
            foreach (string invalid in new[] { "not a url", "file:///tmp/test", "https://user:pass@example.test/v1", "https://example.test/v1?key=test", "https://example.test/v1#fragment" })
                Throws(() => new TranslationPreferences { OpenAiBaseUrl = invalid }.ChatEndpoint());
            new TranslationPreferences { OpenAiBaseUrl = "invalid" }.Validate(); // inactive fields do not prevent saving MyMemory.
            Throws(() => new TranslationPreferences { Provider = "openai", OpenAiModel = "" }.Validate());
            Throws(() => new TranslationPreferences { Provider = "deepl" }.Validate());
            new TranslationPreferences { Provider = "openai", OpenAiBaseUrl = "http://127.0.0.1:11434/v1", OpenAiModel = "local-model" }.Validate();
            Throws(() => new TranslationPreferences { Provider = "openai", OpenAiModel = "synthetic-model" }.Validate());
        });
        test("MyMemory分段编码及换行保留", () =>
        {
            string text = string.Concat(Enumerable.Repeat("中文 🌟 & 引号\" + 测试 ", 50)) + "\r\n\r\nSecond line.\n";
            int calls = 0;
            using var handler = new Handler((request, _) =>
            {
                calls++; Check(request.Method == HttpMethod.Get && request.RequestUri!.Host == "api.mymemory.translated.net", "Wrong MyMemory route.");
                Check(request.Headers.Authorization == null, "MyMemory received another provider's key.");
                var query = Query(request.RequestUri!);
                Check(query["langpair"] == "en|zh-CN" && Encoding.UTF8.GetByteCount(query["q"]) <= 450, "Language mapping or UTF-8 chunk size was incorrect.");
                return Task.FromResult(Reply(new { responseStatus = 200, responseData = new { translatedText = WebUtility.HtmlEncode(query["q"]) } }));
            });
            using var client = new HttpClient(handler);
            Check(Translate(text, new(), client) == text && calls > 1, "Chunks/HTML decoding did not preserve the original layout.");
        });
        test("OpenAI兼容接口请求及译文解析", () =>
        {
            string original = "Hello \"world\".\nKeep paragraphs.";
            using var handler = new Handler(async (request, token) =>
            {
                Check(request.Method == HttpMethod.Post && request.RequestUri!.AbsoluteUri == "https://example.test/v1/chat/completions", "Wrong chat endpoint.");
                Check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == SyntheticKey, "Missing chat authorization.");
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token)); var root = body.RootElement;
                Check(root.GetProperty("model").GetString() == "synthetic-model" && !root.GetProperty("stream").GetBoolean(), "Wrong chat model or streaming option.");
                var messages = root.GetProperty("messages");
                Check(messages.GetArrayLength() == 2 && messages[1].GetProperty("content").GetString() == original, "Source text was changed or mixed into instructions.");
                Check(messages[0].GetProperty("content").GetString()!.Contains("Simplified Chinese"), "Target language missing from translation instructions.");
                return Reply(new { choices = new[] { new { message = new { content = "你好，世界。\n保留段落。" }, finish_reason = "stop" } } });
            });
            using var client = new HttpClient(handler);
            Check(Translate(original, ChatConfig(), client) == "你好，世界。\n保留段落。", "Chat translation response was not extracted.");
        });
        test("DeepL账户端点密钥及语言映射", () =>
        {
            foreach (bool free in new[] { true, false })
            {
                using var handler = new Handler(async (request, token) =>
                {
                    Check(request.RequestUri!.Host == (free ? "api-free.deepl.com" : "api.deepl.com") && request.RequestUri.AbsolutePath == "/v2/translate", "Wrong DeepL account endpoint.");
                    Check(request.Headers.Authorization?.Scheme == "DeepL-Auth-Key" && request.Headers.Authorization.Parameter == SyntheticKey, "Wrong DeepL authorization.");
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                    Check(body.RootElement.GetProperty("source_lang").GetString() == "EN" && body.RootElement.GetProperty("target_lang").GetString() == "ZH-HANS", "Wrong DeepL language codes.");
                    Check(body.RootElement.GetProperty("text")[0].GetString() == "Hello world.", "Wrong DeepL text.");
                    return Reply(new { translations = new[] { new { text = "你好，世界。" } } });
                });
                using var client = new HttpClient(handler);
                Check(Translate("Hello world.", new() { Provider = "deepl", DeepLFreeApi = free, DeepLApiKey = SyntheticKey }, client) == "你好，世界。", "DeepL translation response was not extracted.");
            }
        });
        test("翻译错误不泄露密钥且不静默切换后端", () =>
        {
            foreach (int status in new[] { 401, 403, 404, 429, 456, 500, 302 })
            {
                int calls = 0;
                using var handler = new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(SyntheticKey + " private-original-text") }); });
                using var client = new HttpClient(handler);
                var error = Throws(() => Translate("private-original-text", ChatConfig(), client));
                Check(!error.ToString().Contains(SyntheticKey) && !error.ToString().Contains("private-original-text") && calls == 1, "Error leaked sensitive data or fell back to another provider.");
            }
        });
        test("翻译空响应错误格式及截断提示", () =>
        {
            foreach (string reply in new[] { "<html>error</html>", "[]", "{}", "{\"choices\":[]}", "{\"choices\":[null]}", "{\"choices\":[{\"message\":null}]}", "{\"choices\":[{\"message\":{\"content\":null}}]}", "{\"choices\":[{\"message\":{\"content\":\"partial\"},\"finish_reason\":\"length\"}]}" })
            {
                using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply) }));
                using var client = new HttpClient(handler); Throws(() => Translate("Hello world.", ChatConfig(), client));
            }
        });
        test("翻译取消不会被转换成网络错误", () =>
        {
            using var cancellation = new CancellationTokenSource();
            using var handler = new Handler(async (_, token) => { cancellation.Cancel(); await Task.Delay(10000, token); return Reply(new { }); });
            using var client = new HttpClient(handler);
            try { TranslationService.TranslateAsync("Hello.", "en", "zh-CN", ChatConfig(), cancellation.Token, client).GetAwaiter().GetResult(); throw new InvalidOperationException("Cancellation was ignored."); }
            catch (OperationCanceledException) { }
        });
        test("翻译超时返回可读提示", () =>
        {
            using var handler = new Handler(async (_, token) => { await Task.Delay(10000, token); return Reply(new { }); });
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(50) };
            Check(Throws(() => Translate("Hello.", ChatConfig(), client)).Message.Contains("超时"), "Timeout was not distinguished from user cancellation.");
        });
        test("翻译后端切换不沿用另一服务密钥", () =>
        {
            var config = ChatConfig(); config.DeepLApiKey = "deepl-synthetic-key";
            using var handler = new Handler((request, _) =>
            {
                if (request.RequestUri!.Host == "example.test")
                {
                    Check(request.Headers.Authorization?.Parameter == SyntheticKey, "Chat key changed.");
                    return Task.FromResult(Reply(new { choices = new[] { new { message = new { content = "你好。" }, finish_reason = "stop" } } }));
                }
                Check(request.RequestUri.Host == "api-free.deepl.com" && request.Headers.Authorization?.Parameter == "deepl-synthetic-key", "DeepL inherited another backend's configuration.");
                return Task.FromResult(Reply(new { translations = new[] { new { text = "你好。" } } }));
            });
            using var client = new HttpClient(handler);
            Translate("Hello.", config, client); config.Provider = "deepl"; Translate("Hello.", config, client);
        });
    }
    static TranslationPreferences ChatConfig() => new() { Provider = "openai", OpenAiBaseUrl = "https://example.test/v1", OpenAiModel = "synthetic-model", OpenAiApiKey = SyntheticKey };
    static string Translate(string text, TranslationPreferences config, HttpClient client) => TranslationService.TranslateAsync(text, "en", "zh-CN", config, CancellationToken.None, client).GetAwaiter().GetResult();
    static HttpResponseMessage Reply<T>(T value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    static Dictionary<string, string> Query(Uri uri) => uri.Query.TrimStart('?').Split('&').Select(value => value.Split('=', 2)).ToDictionary(value => value[0], value => Uri.UnescapeDataString(value[1]));
    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    static InvalidOperationException Throws(Action action)
    {
        try { action(); } catch (InvalidOperationException error) { return error; }
        throw new Exception("Expected a readable configuration/service error.");
    }
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
