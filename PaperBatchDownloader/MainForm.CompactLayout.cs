namespace PaperBatchDownloader;

public sealed partial class MainForm
{
    private readonly Button btnOptions = new() { Text = "Tùy chọn ▸", AutoSize = true };
    private readonly FlowLayoutPanel advancedOptions = new()
    {
        AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, Visible = false, Margin = Padding.Empty
    };

    private void InitializeCompactLayout(TableLayoutPanel top, FlowLayoutPanel buttons)
    {
        var interval = top.GetControlFromPosition(0, 3)!;
        var checkbox = top.GetControlFromPosition(0, 4)!;
        top.Controls.Remove(interval);
        top.Controls.Remove(checkbox);
        top.Controls.Remove(lblPublisherSummary);
        var settings = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        foreach (Control control in buttons.Controls.Cast<Control>().ToArray())
        {
            bool primary = control == btnStart || control == btnPause || control == btnContinue
                || control == btnSkip || control == btnRetryIncomplete || control == cmbAccessMode
                || control.Text is "Đổi hồ sơ" or "LIBRARY ACCESS";
            if (!primary) settings.Controls.Add(control);
        }
        foreach (Control control in new[] { interval, checkbox })
        {
            control.Dock = DockStyle.None;
            control.Margin = Padding.Empty;
        }
        advancedOptions.Controls.AddRange(new Control[] { settings, interval, checkbox, lblPublisherSummary });
        lblPublisherSummary.AutoSize = false;
        lblPublisherSummary.AutoEllipsis = true;
        lblPublisherSummary.Height = Font.Height + 8;
        buttons.Controls.Add(btnOptions);
        buttons.Padding = Padding.Empty;
        buttons.Margin = Padding.Empty;
        top.Padding = new Padding(4, 2, 4, 2);
        top.RowCount = 4;
        top.RowStyles.Clear();
        for (int i = 0; i < 4; i++) top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.Controls.Add(advancedOptions, 0, 3);
        top.SetColumnSpan(advancedOptions, 4);
        void FitOptions()
        {
            int width = Math.Max(100, top.ClientSize.Width - top.Padding.Horizontal - 12);
            foreach (Control row in advancedOptions.Controls)
            {
                row.MaximumSize = new Size(width, 0);
                row.Width = width;
            }
        }
        top.SizeChanged += (_, _) => FitOptions();
        btnOptions.Click += (_, _) =>
        {
            advancedOptions.Visible = !advancedOptions.Visible;
            btnOptions.Text = advancedOptions.Visible ? "Tùy chọn ▾" : "Tùy chọn ▸";
            FitOptions();
        };
    }
}
