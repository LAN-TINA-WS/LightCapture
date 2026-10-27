using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace LightCapture;

internal enum TranslationBackend { MyMemory, OpenAiCompatible, DeepL }

internal sealed class TranslationPreferences
{
    public string Provider { get; set; } = "mymemory";
    public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string OpenAiModel { get; set; } = "";
    public string OpenAiKeyProtected { get; set; } = "";
    public string DeepLKeyProtected { get; set; } = "";
    public bool DeepLFreeApi { get; set; } = true;
    [JsonIgnore] public string OpenAiApiKey { get => ReadSecret(OpenAiKeyProtected); set => OpenAiKeyProtected = Protect(value); }
    [JsonIgnore] public string DeepLApiKey { get => ReadSecret(DeepLKeyProtected); set => DeepLKeyProtected = Protect(value); }
    [JsonIgnore] public TranslationBackend Backend => Provider?.ToLowerInvariant() switch
    {
        "openai" => TranslationBackend.OpenAiCompatible, "deepl" => TranslationBackend.DeepL, _ => TranslationBackend.MyMemory
    };
    [JsonIgnore] public string DisplayName => Backend switch
    {
        TranslationBackend.OpenAiCompatible => "OpenAI 兼容", TranslationBackend.DeepL => "DeepL", _ => "MyMemory"
    };
    internal TranslationPreferences Clone() => new()
    {
        Provider = Provider, OpenAiBaseUrl = OpenAiBaseUrl, OpenAiModel = OpenAiModel,
        OpenAiKeyProtected = OpenAiKeyProtected, DeepLKeyProtected = DeepLKeyProtected, DeepLFreeApi = DeepLFreeApi
    };
    internal void Validate()
    {
        if (Backend == TranslationBackend.OpenAiCompatible)
        {
            _ = ChatEndpoint();
            if (string.IsNullOrWhiteSpace(OpenAiModel)) throw new InvalidOperationException("请填写 OpenAI 兼容接口使用的模型名称。");
            if (OpenAiModel.Length > 200 || OpenAiModel.Any(char.IsControl)) throw new InvalidOperationException("模型名称过长或包含无效字符。");
            ValidateKey(OpenAiApiKey);
            if (ChatEndpoint().Host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(OpenAiApiKey))
                throw new InvalidOperationException("OpenAI 官方接口需要 API 密钥；请填写密钥，或改用你的兼容服务地址。");
        }
        else if (Backend == TranslationBackend.DeepL)
        {
            if (string.IsNullOrWhiteSpace(DeepLApiKey)) throw new InvalidOperationException("请填写 DeepL API 密钥。");
            ValidateKey(DeepLApiKey);
        }
    }
    static void ValidateKey(string key)
    {
        if (key.Any(char.IsControl)) throw new InvalidOperationException("API 密钥包含无效字符，请重新粘贴。");
    }
    internal Uri ChatEndpoint()
    {
        if (!Uri.TryCreate(OpenAiBaseUrl?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("接口地址需要是完整的 HTTP 或 HTTPS 地址，不含账号、查询参数和片段。");
        string path = uri.AbsolutePath.TrimEnd('/');
        return new UriBuilder(uri) { Path = path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ? path : path + "/chat/completions" }.Uri;
    }
    static string Protect(string? value) => string.IsNullOrWhiteSpace(value) ? ""
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
    static string ReadSecret(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) when (ex is FormatException or CryptographicException) { return ""; }
    }
}
