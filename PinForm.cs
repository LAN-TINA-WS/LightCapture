namespace LightCapture;

internal sealed class PinForm : Form
{
    readonly Bitmap image;
    readonly AppSettings settings;
    Point drag;
    bool dragging;
    float zoom;
    public PinForm(Bitmap image, AppSettings settings, Action<Bitmap> edit)
    {
        this.image = image;
        this.settings = settings;
        Text = "轻截 · 贴图";
        ShowInTaskbar = false;
        TopMost = true;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9);
        KeyPreview = true;
        Padding = new Padding(1);
        zoom = Math.Min(1, Math.Min(720f / image.Width, 600f / image.Height));
        var pic = new PictureBox { Dock = DockStyle.Fill, Image = image, SizeMode = PictureBoxSizeMode.Zoom, Cursor = Cursors.SizeAll };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, BackColor = Ui.Surface, WrapContents = false };
        bar.Controls.Add(Ui.Button("编辑", () => edit(new Bitmap(image)), 48));
        bar.Controls.Add(Ui.Button("复制", () => _ = CopyAsync(), 48));
        bar.Controls.Add(Ui.Button("保存", () => Save(), 48));
        bar.Controls.Add(Ui.Button("关闭", Close, 48));
        Controls.Add(pic);
        Controls.Add(bar);
        pic.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { dragging = true; drag = Cursor.Position; pic.Capture = true; } else if (e.Button == MouseButtons.Right) { var menu = new ContextMenuStrip(); menu.Items.Add("编辑", null, (_, _) => edit(new Bitmap(image))); menu.Items.Add("始终置顶", null, (_, _) => TopMost = !TopMost); menu.Items.Add("恢复不透明", null, (_, _) => Opacity = 1); menu.Items.Add("关闭贴图", null, (_, _) => Close()); menu.Closed += (_, _) => Ui.DisposeMenuWhenIdle(menu); menu.Show(Cursor.Position); } };
        pic.MouseMove += (_, _) => { if (dragging) { var p = Cursor.Position; Location = new(Location.X + p.X - drag.X, Location.Y + p.Y - drag.Y); drag = p; } };
        pic.MouseUp += (_, _) => { dragging = false; pic.Capture = false; };
        MouseWheel += (_, e) => { if ((ModifierKeys & Keys.Control) != 0) Opacity = Math.Clamp(Opacity + (e.Delta > 0 ? .08 : -.08), .2, 1); else { zoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.15f : 1 / 1.15f), .1f, 3); ResizeImage(); } };
        Shown += (_, _) => { ResizeImage(); var work = Screen.FromPoint(Cursor.Position).WorkingArea; Location = new(Math.Clamp(Cursor.Position.X, work.Left, Math.Max(work.Left, work.Right - Width)), Math.Clamp(Cursor.Position.Y, work.Top, Math.Max(work.Top, work.Bottom - Height))); };
        FormClosed += (_, _) => image.Dispose();
        Paint += (_, e) => e.Graphics.DrawRectangle(Pens.LightSteelBlue, 0, 0, Width - 1, Height - 1);
    }
    void ResizeImage()
    {
        var work = Screen.FromPoint(Location).WorkingArea;
        Size = new(Math.Clamp((int)(image.Width * zoom) + 2, 235, work.Width), Math.Clamp((int)(image.Height * zoom) + 38, 100, work.Height));
    }
    async Task CopyAsync()
    {
        try
        {
            await ImageOutput.CopyAsync(image);
        }
        catch (Exception ex) { Ui.Error(this, ex); }
    }
    void Save()
    {
        try
        {
            ImageOutput.Save(this, image, settings);
        }
        catch (Exception ex) { Ui.Error(this, ex); }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys key)
    {
        if (key == Keys.Escape)
        {
            Close();
            return true;
        }
        if (key == (Keys.Control | Keys.S))
        {
            Save();
            return true;
        }
        if (key == (Keys.Control | Keys.C))
        {
            _ = CopyAsync();
            return true;
        }
        return base.ProcessCmdKey(ref msg, key);
    }
}
