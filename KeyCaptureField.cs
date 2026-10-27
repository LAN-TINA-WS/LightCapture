using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace LightCapture;

internal static class HotkeyKey
{
    internal static bool IsAssignable(Keys key)
    {
        int code = (int)key;
        return code >= 8 && code <= 254 && key is not (Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LShiftKey or Keys.RShiftKey
            or Keys.LControlKey or Keys.RControlKey or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin or Keys.Escape or Keys.ProcessKey or Keys.Packet);
    }
    internal static bool AllowsStandalone(Keys key) => key is >= Keys.F1 and <= Keys.F24 or Keys.PrintScreen;
    internal static string Name(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => ((int)key - (int)Keys.D0).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "Num " + ((int)key - (int)Keys.NumPad0),
        Keys.PrintScreen => "Print Screen", Keys.Prior => "Page Up", Keys.Next => "Page Down",
        Keys.Back => "Backspace", Keys.Return => "Enter", Keys.Capital => "Caps Lock", Keys.NumLock => "Num Lock", Keys.Scroll => "Scroll Lock",
        Keys.Oemplus => "=", Keys.OemMinus => "-", Keys.Oemcomma => ",", Keys.OemPeriod => ".", Keys.OemSemicolon => ";",
        Keys.OemQuestion => "/", Keys.Oemtilde => "`", Keys.OemOpenBrackets => "[", Keys.OemCloseBrackets => "]", Keys.OemQuotes => "'", Keys.OemPipe or Keys.OemBackslash => "\\",
        Keys.Multiply => "Num *", Keys.Add => "Num +", Keys.Subtract => "Num -", Keys.Divide => "Num /", Keys.Decimal => "Num .",
        Keys.MediaNextTrack => "下一曲", Keys.MediaPreviousTrack => "上一曲", Keys.MediaPlayPause => "播放 / 暂停", Keys.MediaStop => "停止播放",
        Keys.VolumeMute => "静音", Keys.VolumeDown => "音量 -", Keys.VolumeUp => "音量 +",
        _ => key.ToString()
    };
}

internal sealed class KeyCaptureField : Control
{
    Keys selectedKey = Keys.A;
    Keys releaseKey;
    bool awaitingRelease, suspended;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal float UiScale { get; set; } = 1;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal Keys SelectedKey
    {
        get => selectedKey;
        set { selectedKey = HotkeyKey.IsAssignable(value) ? value : Keys.A; UpdateName(); Invalidate(); }
    }
    internal bool Recording { get; private set; }
    internal event Action<bool>? CaptureStateChanged;
    internal KeyCaptureField()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true; Cursor = Cursors.Hand; BackColor = Color.White; AccessibleRole = AccessibleRole.PushButton; UpdateName();
    }
    internal void BeginCapture()
    {
        if (Recording) return;
        awaitingRelease = false; Recording = true; UpdateName(); Invalidate(); SetSuspended(true);
    }
    internal void CancelCapture()
    {
        if (!Recording && !awaitingRelease) return;
        awaitingRelease = false; Recording = false; UpdateName(); Invalidate(); SetSuspended(false);
    }
    void SetSuspended(bool value)
    {
        if (suspended == value) return;
        suspended = value; CaptureStateChanged?.Invoke(value);
    }
    internal void ReleaseKey(Keys key)
    {
        if (!awaitingRelease || key != releaseKey) return;
        awaitingRelease = false; SetSuspended(false);
    }
    void UpdateName() => AccessibleName = Recording ? "截图主键，正在录入，按 Esc 取消" : "截图主键 " + HotkeyKey.Name(selectedKey) + "，点击后按键录入";
    protected override AccessibleObject CreateAccessibilityInstance() => new KeyCaptureAccessibility(this);
    sealed class KeyCaptureAccessibility(KeyCaptureField owner) : ControlAccessibleObject(owner)
    {
        public override string DefaultAction => "录入按键";
        public override void DoDefaultAction() { owner.Focus(); owner.BeginCapture(); }
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { Focus(); BeginCapture(); }
        base.OnMouseDown(e);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (awaitingRelease) return true;
        if (Recording)
        {
            if (key == Keys.Escape) CancelCapture();
            else if (HotkeyKey.IsAssignable(key))
            {
                SelectedKey = key; Recording = false; awaitingRelease = true; releaseKey = key;
                UpdateName(); Invalidate();
            }
            // Eat modifiers and dialog keys so they cannot save, close or move focus.
            return true;
        }
        if (keyData is Keys.Enter or Keys.Space) { BeginCapture(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void OnKeyUp(KeyEventArgs e) { ReleaseKey(e.KeyCode); base.OnKeyUp(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { CancelCapture(); base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Parent?.BackColor ?? Color.White);
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiShape.Round(new(.5f, .5f, Width - 1, Height - 1), 10 * UiScale);
        using var fill = new SolidBrush(Recording ? UiShape.Tint : Color.White); g.FillPath(fill, path);
        using var border = new Pen(Recording || Focused ? Ui.Blue : UiShape.Border, Math.Max(1, UiScale)); g.DrawPath(border, path);
        TextRenderer.DrawText(g, Recording ? "请按键…" : HotkeyKey.Name(selectedKey), Font, Rectangle.Inflate(ClientRectangle, -(int)(8 * UiScale), 0),
            Recording ? Ui.Blue : Ui.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues && !Recording) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -5, -5));
    }
    protected override void Dispose(bool disposing) { if (disposing) CancelCapture(); base.Dispose(disposing); }
}
