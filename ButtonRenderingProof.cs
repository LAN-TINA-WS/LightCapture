using System.Drawing.Imaging;
using System.Reflection;

namespace LightCapture;

// Exercise the WM_PAINT path on private, never-shown controls. DrawToBitmap alone
// initializes the background differently and can hide missing corner painting.
internal static class ButtonRenderingProof
{
    static readonly MethodInfo WindowProcedure = typeof(Control).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic)!;
    static readonly MethodInfo MouseEnter = typeof(Control).GetMethod("OnMouseEnter", BindingFlags.Instance | BindingFlags.NonPublic)!;
    static readonly MethodInfo MouseDown = typeof(Control).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!;
    static readonly MethodInfo MouseUp = typeof(Control).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)!;
    static readonly MethodInfo MouseLeave = typeof(Control).GetMethod("OnMouseLeave", BindingFlags.Instance | BindingFlags.NonPublic)!;

    internal static IEnumerable<object> Generate(string directory)
    {
        foreach (float scale in new[] { 1, 1.25f, 1.5f, 2 })
        foreach (var (background, suffix) in new[] { (Color.White, "white"), (Ui.Surface, "surface"), (Color.FromArgb(231, 239, 251), "tint") })
        {
            using var host = new Form { BackColor = background, AutoScaleMode = AutoScaleMode.None };
            using var fonts = new UiFonts();
            using var montage = new Bitmap((int)(324 * scale), (int)(416 * scale));
            using var canvas = Graphics.FromImage(montage);
            canvas.Clear(background);
            int row = 0;
            foreach (bool primary in new[] { false, true })
            {
                using var button = new ModernButton(primary ? "保存设置" : "导出 Word", () => { })
                {
                    Primary = primary, UiScale = scale, Font = fonts.Get(16 * scale),
                    Size = new((int)(140 * scale), (int)(44 * scale)), Location = new(8, 8)
                };
                host.Controls.Add(button);
                _ = button.Handle;
                if (primary) host.AcceptButton = button;
                var states = new[] { "normal", "hover", "pressed", "default", "disabled", "reenabled" };
                for (int index = 0; index < states.Length; index++)
                {
                    string state = states[index];
                    if (state == "hover") MouseEnter.Invoke(button, [EventArgs.Empty]);
                    if (state == "pressed") MouseDown.Invoke(button, [new MouseEventArgs(MouseButtons.Left, 1, button.Width / 2, button.Height / 2, 0)]);
                    if (state == "default")
                    {
                        MouseUp.Invoke(button, [new MouseEventArgs(MouseButtons.Left, 1, button.Width / 2, button.Height / 2, 0)]);
                        MouseLeave.Invoke(button, [EventArgs.Empty]); button.NotifyDefault(true);
                    }
                    if (state == "disabled") button.Enabled = false;
                    if (state == "reenabled") button.Enabled = true;
                    using var rendered = PaintClient(button);
                    canvas.DrawImageUnscaled(rendered, (int)((12 + index % 2 * 156) * scale), (int)((12 + row * 64 + index / 2 * 64) * scale));
                    // Save before asserting so a failing build keeps the visual evidence.
                    string name = $"buttons-{(int)(scale * 100)}-{suffix}.png";
                    montage.Save(Path.Combine(directory, name), ImageFormat.Png);
                    CheckCorners(rendered, background, $"{name}/{primary}/{state}", scale);
                    Color expectedFill = !button.Enabled ? Color.FromArgb(238, 241, 246)
                        : primary ? state == "pressed" ? Color.FromArgb(25, 75, 180) : state == "hover" ? Color.FromArgb(34, 91, 206) : Ui.Blue
                        : state == "pressed" ? Color.FromArgb(226, 234, 248) : state == "hover" ? Color.FromArgb(242, 246, 253) : Color.White;
                    Check(rendered.GetPixel(rendered.Width / 2, (int)(5 * scale)).ToArgb() == expectedFill.ToArgb(), "Button state fill is wrong: " + state);
                }
                // Parent repaint and resize must replace the old backdrop as well.
                host.BackColor = Color.FromArgb(248, 235, 227);
                button.Size = new((int)(152 * scale), (int)(48 * scale));
                using (var changed = PaintClient(button)) CheckCorners(changed, host.BackColor, "parent-change-and-resize", scale);
                host.BackColor = background;
                host.AcceptButton = null;
                row += 3;
            }
            yield return new { name = $"buttons-{(int)(scale * 100)}-{suffix}.png", scale, background = suffix, states = 12, parentChangeAndResize = true, status = "passed" };
        }
    }

    static Bitmap PaintClient(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
        var hdc = graphics.GetHdc();
        try
        {
            var message = Message.Create(control.Handle, 0x000F /* WM_PAINT */, hdc, IntPtr.Zero);
            WindowProcedure.Invoke(control, [message]);
        }
        finally { graphics.ReleaseHdc(hdc); }
        return bitmap;
    }

    static void CheckCorners(Bitmap bitmap, Color expected, string name, float scale)
    {
        int inset = Math.Max(1, (int)scale);
        foreach (int x in new[] { 0, inset, bitmap.Width - 1 - inset, bitmap.Width - 1 })
        foreach (int y in new[] { 0, inset, bitmap.Height - 1 - inset, bitmap.Height - 1 })
            Check(bitmap.GetPixel(x, y).ToArgb() == expected.ToArgb(), $"Button corner retained stale pixels at {x},{y}: {name}; expected {expected}, got {bitmap.GetPixel(x, y)}.");
    }

    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
