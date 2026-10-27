using System.Drawing.Imaging;
using System.Text.Json;

namespace LightCapture;

internal static class TranslationSettingsProof
{
    internal static IEnumerable<object> Generate(string directory)
    {
        foreach (float scale in new[] { 1, 1.25f, 1.5f, 2 })
        foreach (bool compact in new[] { false, true })
        foreach (var backend in Enum.GetValues<TranslationBackend>())
        {
            var settings = new AppSettings
            {
                Translation = new()
                {
                    OpenAiBaseUrl = "https://example.test/v1", OpenAiModel = "your-model-name",
                    OpenAiApiKey = "synthetic-openai-ui-key", DeepLApiKey = "synthetic-deepl-ui-key"
                }
            };
            string before = JsonSerializer.Serialize(settings);
            using var pane = new SettingsPane(settings, () => { }, () => { });
            pane.Size = new((int)((compact ? 560 : 780) * scale), (int)((compact ? 430 : 810) * scale));
            pane.Configure(scale); pane.CreateControl();
            var card = pane.TranslationCard;
            card.ProviderField.Input.SelectedIndex = (int)backend;
            string baseUrl = card.AddressField.Input.Text, model = card.ModelField.Input.Text;
            card.ProviderField.Input.SelectedIndex = ((int)backend + 1) % 3;
            card.ProviderField.Input.SelectedIndex = (int)backend;
            Check(card.AddressField.Input.Text == baseUrl && card.ModelField.Input.Text == model, "Switching backend cleared its parameters.");
            Check(card.ReadPreferences().Backend == backend, "Backend selection was not captured.");
            card.ReadPreferences().Validate();
            Check(card.AddressField.Visible == (backend == TranslationBackend.OpenAiCompatible), "Address field shown for wrong backend.");
            Check(card.DeepLPlanField.Visible == (backend == TranslationBackend.DeepL), "DeepL plan field shown for wrong backend.");
            var secret = backend == TranslationBackend.DeepL ? card.DeepLKeyField : card.OpenAiKeyField;
            Check(secret.Input.UseSystemPasswordChar, "Secrets are not masked by default.");
            if (secret.Visible)
            {
                secret.RevealButton!.PerformClick(); Check(!secret.Input.UseSystemPasswordChar, "Reveal button did not reveal the key.");
                secret.RevealButton.PerformClick(); Check(secret.Input.UseSystemPasswordChar, "Reveal button did not hide the key again.");
            }
            Check(JsonSerializer.Serialize(settings) == before, "Unsaved backend editing modified persisted settings.");
            if (backend == TranslationBackend.OpenAiCompatible)
            {
                card.ModelField.Input.Clear(); card.TestButton.PerformClick();
                Check(card.TestButton.Text == "测试连接" && card.Controls.OfType<Label>().Any(label => label.Text.Contains("请填写") && label.Text.Contains("模型")), "Invalid connection test did not show a local validation message.");
                CheckChildren(card); card.ModelField.Input.Text = model;
            }
            CheckChildren(card);
            var footer = pane.SaveButton.Parent!.Bounds;
            pane.ScrollBody.AutoScrollPosition = new(0, pane.ScrollBody.AutoScrollMinSize.Height);
            Check(card.Bottom + card.Parent!.Top <= pane.ScrollBody.ClientSize.Height, "Backend settings cannot be reached by scrolling.");
            Check(pane.SaveButton.Parent.Bounds == footer && pane.SaveButton.Parent.ClientRectangle.Contains(pane.SaveButton.Bounds), "Expanded backend settings moved or clipped Save.");
            string name = $"translation-backend-{backend}-{(int)(scale * 100)}{(compact ? "-compact" : "")}.png";
            using var bitmap = new Bitmap(card.Width, card.Height);
            card.DrawToBitmap(bitmap, card.ClientRectangle); bitmap.Save(Path.Combine(directory, name), ImageFormat.Png);
            yield return new { name, scale, compact, backend = backend.ToString(), width = card.Width, height = card.Height, status = "passed" };
        }
    }
    static void CheckChildren(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (!child.Visible) continue;
            Check(parent.ClientRectangle.Contains(child.Bounds), "Backend settings control is clipped: " + child.AccessibleName + child.Text);
            if (child is Label label) Check(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height, "Backend settings label is clipped.");
            if (child is not ComboBox) CheckChildren(child);
        }
    }
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
