using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TimedMouseClicker;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new ClickerForm());
    }
}

internal sealed class ClickerForm : Form
{
    private readonly NumericUpDown duration = new() { Minimum = 1, Maximum = 86400, Value = 15, Width = 110 };
    private readonly ComboBox unit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly Button capture = new() { Text = "Lấy vị trí sau 3 giây", AutoSize = true };
    private readonly Button start = new() { Text = "Bắt đầu hẹn giờ", AutoSize = true, Enabled = false };
    private readonly Button cancel = new() { Text = "Hủy (F8)", AutoSize = true, Enabled = false };
    private readonly Label position = new() { Text = "Chưa chọn vị trí click.", AutoSize = true };
    private readonly Label status = new() { Text = "Chọn vị trí, đặt thời gian chờ rồi bấm Bắt đầu.", AutoSize = true };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    private readonly Stopwatch elapsed = new();
    private Point target;
    private bool selecting;
    private double secondsToWait;
    private bool hotkeyRegistered;
    private const int StopHotkey = 1;

    public ClickerForm()
    {
        Text = "Hẹn giờ click chuột";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(580, 270);
        MinimumSize = new Size(580, 310);
        AutoScaleMode = AutoScaleMode.Dpi;
        unit.Items.AddRange(new object[] { "giây", "phút" });
        unit.SelectedIndex = 0;
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(16)
        };
        layout.Controls.Add(new Label { Text = "Tự click chuột trái một lần sau khoảng thời gian đã đặt.", AutoSize = true, Margin = new Padding(0, 0, 0, 14) });
        layout.Controls.Add(capture);
        layout.Controls.Add(position);
        var timing = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 8) };
        timing.Controls.Add(new Label { Text = "Thời gian chờ:", AutoSize = true, Padding = new Padding(0, 5, 5, 0) });
        timing.Controls.Add(duration);
        timing.Controls.Add(unit);
        layout.Controls.Add(timing);
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.Add(start);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons);
        layout.Controls.Add(status);
        Controls.Add(layout);
        capture.Click += (_, _) => Arm(true);
        start.Click += (_, _) => Arm(false);
        cancel.Click += (_, _) => Stop("Đã hủy. Không click chuột.");
        timer.Tick += (_, _) => Tick();
        FormClosed += (_, _) => { ReleaseHotkey(); timer.Dispose(); };
    }

    private void Arm(bool capturePosition)
    {
        // Register before arming so an unavailable emergency hotkey cannot leave a job running.
        if (!RegisterHotKey(Handle, StopHotkey, 0x4000, 0x77)) // MOD_NOREPEAT, F8
        {
            status.Text = "F8 đang được ứng dụng khác sử dụng. Hãy đóng ứng dụng đó rồi thử lại.";
            return;
        }
        hotkeyRegistered = true;
        selecting = capturePosition;
        secondsToWait = capturePosition ? 3 : (double)duration.Value * (unit.SelectedIndex == 1 ? 60 : 1);
        capture.Enabled = start.Enabled = duration.Enabled = unit.Enabled = false;
        cancel.Enabled = true;
        elapsed.Restart();
        timer.Start();
        Tick();
    }

    private void Tick()
    {
        double remaining = secondsToWait - elapsed.Elapsed.TotalSeconds;
        if (remaining > 0)
        {
            var time = TimeSpan.FromSeconds(Math.Ceiling(remaining));
            string countdown = $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
            status.Text = selecting
                ? $"Đưa chuột đến vị trí cần click. Lấy vị trí sau {countdown}."
                : $"Sẽ click tại ({target.X}, {target.Y}) sau {countdown}. F8 để hủy.";
            return;
        }
        if (selecting)
        {
            target = Cursor.Position;
            position.Text = $"Vị trí click: X = {target.X}, Y = {target.Y}";
            Stop("Đã lấy vị trí. Đặt thời gian chờ rồi bấm Bắt đầu.");
            start.Enabled = true;
            return;
        }
        Stop("Đang click…"); // Disarm first: this schedule can fire only once.
        if (!SystemInformation.VirtualScreen.Contains(target))
        {
            status.Text = "Vị trí đã chọn không còn nằm trên màn hình. Hãy lấy lại vị trí.";
            return;
        }
        if (!SetCursorPos(target.X, target.Y))
        {
            status.Text = "Không thể di chuyển chuột tới vị trí đã chọn.";
            return;
        }
        var inputs = new[] { MouseInput(0x0002), MouseInput(0x0004) }; // Left down, left up
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent == 1) SendInput(1, new[] { MouseInput(0x0004) }, Marshal.SizeOf<INPUT>());
        status.Text = sent == 2 ? "Đã click một lần. Hẹn giờ đã kết thúc." : "Không thể gửi click đến ứng dụng đích.";
    }

    private void Stop(string message)
    {
        timer.Stop();
        elapsed.Stop();
        ReleaseHotkey();
        capture.Enabled = duration.Enabled = unit.Enabled = true;
        start.Enabled = position.Text.StartsWith("Vị trí click:");
        cancel.Enabled = false;
        status.Text = message;
    }

    private void ReleaseHotkey()
    {
        if (hotkeyRegistered) UnregisterHotKey(Handle, StopHotkey);
        hotkeyRegistered = false;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312 && message.WParam.ToInt32() == StopHotkey)
            Stop("Đã hủy bằng F8. Không click chuột.");
        base.WndProc(ref message);
    }

    private static INPUT MouseInput(uint flags) => new() { Type = 0, Data = new InputUnion { Mouse = new MOUSEINPUT { Flags = flags } } };
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public UIntPtr ExtraInfo;
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
