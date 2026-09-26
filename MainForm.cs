using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace MicMixer;

public sealed class MainForm : Form
{
    readonly AudioEngine _engine = new();
    readonly MMDeviceEnumerator _devices = new();

    readonly ComboBox _micBox = Combo();
    readonly ComboBox _outBox = Combo();
    readonly ComboBox _windowBox = Combo();
    readonly RadioButton _modeMic = new() { Text = "Normal – nur Mikrofon", AutoSize = true, Checked = true };
    readonly RadioButton _modeBoth = new() { Text = "Mikrofon + Fenster-Audio", AutoSize = true };
    readonly RadioButton _modeApp = new() { Text = "Nur Fenster-Audio", AutoSize = true };
    readonly TrackBar _micVol = Slider(100);
    readonly TrackBar _appVol = Slider(50);
    readonly Button _startBtn = new() { Text = "Start", AutoSize = true, Padding = new Padding(16, 4, 16, 4) };
    readonly Label _status = new() { AutoSize = true, ForeColor = Color.DimGray };

    public MainForm()
    {
        Text = "MicMixer";
        Font = new Font("Segoe UI", 10f);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var refreshBtn = new Button { Text = "↻", Width = 36 };
        refreshBtn.Click += (_, _) => LoadWindows();
        var windowRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
        windowRow.Controls.AddRange(new Control[] { _windowBox, refreshBtn });

        var modes = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = Padding.Empty };
        modes.Controls.AddRange(new Control[] { _modeMic, _modeBoth, _modeApp });

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(14) };
        void Row(string label, Control c)
        {
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 12, 3) });
            layout.Controls.Add(c);
        }
        Row("Modus", modes);
        Row("Fenster", windowRow);
        Row("Mikrofon", _micBox);
        Row("Ausgabe", _outBox);
        Row("Mikro-Lautstärke", _micVol);
        Row("Fenster-Lautstärke", _appVol);
        layout.Controls.Add(_startBtn);
        layout.Controls.Add(_status);
        Controls.Add(layout);

        LoadDevices();
        LoadWindows();

        _modeMic.CheckedChanged += (_, _) => ModeChanged();
        _modeBoth.CheckedChanged += (_, _) => ModeChanged();
        _modeApp.CheckedChanged += (_, _) => ModeChanged();
        _windowBox.SelectedIndexChanged += (_, _) => _engine.SetTargetProcess(SelectedPid());
        _micVol.ValueChanged += (_, _) => _engine.SetMicLevel(_micVol.Value / 100f);
        _appVol.ValueChanged += (_, _) => _engine.SetAppLevel(_appVol.Value / 100f);
        _startBtn.Click += (_, _) => ToggleRunning();
        _engine.Error += msg => BeginInvoke(() => _status.Text = msg);

        _engine.SetMicLevel(_micVol.Value / 100f);
        _engine.SetAppLevel(_appVol.Value / 100f);
        ModeChanged();
    }

    static ComboBox Combo() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
    static TrackBar Slider(int value) => new() { Minimum = 0, Maximum = 150, Value = value, TickFrequency = 25, Width = 340 };

    void LoadDevices()
    {
        var mics = _devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Where(d => !IsCable(d)).ToList();
        _micBox.DataSource = mics;
        _micBox.DisplayMember = nameof(MMDevice.FriendlyName);
        if (_devices.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications))
        {
            var def = _devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications).ID;
            _micBox.SelectedItem = mics.FirstOrDefault(d => d.ID == def) ?? mics.FirstOrDefault();
        }

        var outs = _devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
        _outBox.DataSource = outs;
        _outBox.DisplayMember = nameof(MMDevice.FriendlyName);
        var cable = outs.Where(IsCable).OrderBy(d => d.FriendlyName.Contains("16 Ch")).FirstOrDefault();
        if (cable != null)
        {
            _outBox.SelectedItem = cable;
            _status.Text = "In Discord/Spiel als Mikrofon „CABLE Output“ wählen.";
        }
        else
        {
            _status.Text = "VB-Cable fehlt – ohne es hört dich niemand. Siehe README.";
            _status.ForeColor = Color.Firebrick;
        }
    }

    static bool IsCable(MMDevice d) => d.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase);

    void LoadWindows()
    {
        var previous = SelectedPid();
        var items = Process.GetProcesses()
            .Where(p => p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(p.MainWindowTitle) && p.Id != Environment.ProcessId)
            .Select(p => new WindowItem(p.Id, $"{p.ProcessName} – {p.MainWindowTitle}"))
            .OrderByDescending(w => w.Label.Contains("spotify", StringComparison.OrdinalIgnoreCase) || w.Label.Contains("youtube", StringComparison.OrdinalIgnoreCase))
            .ThenBy(w => w.Label)
            .ToList();
        _windowBox.DataSource = items;
        _windowBox.SelectedItem = items.FirstOrDefault(w => w.Pid == previous) ?? items.FirstOrDefault();
    }

    int SelectedPid() => (_windowBox.SelectedItem as WindowItem)?.Pid ?? 0;

    void ModeChanged()
    {
        var mode = _modeBoth.Checked ? MixMode.MicAndApp : _modeApp.Checked ? MixMode.AppOnly : MixMode.MicOnly;
        _windowBox.Enabled = _appVol.Enabled = mode != MixMode.MicOnly;
        _engine.SetTargetProcess(SelectedPid());
        _engine.SetMode(mode);
    }

    void ToggleRunning()
    {
        if (_engine.IsRunning)
        {
            _engine.Stop();
            _startBtn.Text = "Start";
            _micBox.Enabled = _outBox.Enabled = true;
            return;
        }
        if (_micBox.SelectedItem is not MMDevice mic || _outBox.SelectedItem is not MMDevice output) return;
        try
        {
            _engine.Start(mic, output);
            _startBtn.Text = "Stopp";
            _micBox.Enabled = _outBox.Enabled = false;
        }
        catch (Exception ex)
        {
            _engine.Stop();
            _status.Text = "Start fehlgeschlagen: " + ex.Message;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _engine.Dispose();
        base.OnFormClosed(e);
    }

    sealed record WindowItem(int Pid, string Label)
    {
        public override string ToString() => Label.Length > 60 ? Label[..60] + "…" : Label;
    }
}
