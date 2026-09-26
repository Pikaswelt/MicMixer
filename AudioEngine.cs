using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicMixer;

public enum MixMode { MicOnly, MicAndApp, AppOnly }

public sealed class AudioEngine : IDisposable
{
    static readonly WaveFormat MixFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    WasapiCapture? _mic;
    ProcessLoopbackCapture? _app;
    WasapiOut? _out;
    MixingSampleProvider? _mixer;
    VolumeSampleProvider? _micVol;
    VolumeSampleProvider? _appVol;
    ISampleProvider? _appInput;
    int _appPid;
    float _micLevel = 1f, _appLevel = 0.5f;
    MixMode _mode = MixMode.MicOnly;

    public bool IsRunning => _out != null;
    public event Action<string>? Error;

    public void Start(MMDevice micDevice, MMDevice outputDevice)
    {
        Stop();
        _mixer = new MixingSampleProvider(MixFormat) { ReadFully = true };

        _mic = new WasapiCapture(micDevice, true, 10);
        _micVol = new VolumeSampleProvider(Buffer(_mic));
        _mixer.AddMixerInput(_micVol);
        _mic.StartRecording();

        _out = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, 40);
        _out.Init(new SoftLimiter(_mixer));
        _out.Play();

        ApplyVolumes();
        RestartAppCapture();
    }

    public void Stop()
    {
        StopAppCapture();
        _mic?.StopRecording();
        _mic?.Dispose();
        _mic = null;
        _out?.Stop();
        _out?.Dispose();
        _out = null;
        _mixer = null;
    }

    public void SetMode(MixMode mode)
    {
        _mode = mode;
        ApplyVolumes();
        RestartAppCapture();
    }

    public void SetTargetProcess(int pid)
    {
        if (pid == _appPid) return;
        _appPid = pid;
        RestartAppCapture();
    }

    public void SetMicLevel(float v) { _micLevel = v; ApplyVolumes(); }
    public void SetAppLevel(float v) { _appLevel = v; ApplyVolumes(); }

    void ApplyVolumes()
    {
        if (_micVol != null) _micVol.Volume = _mode == MixMode.AppOnly ? 0f : _micLevel;
        if (_appVol != null) _appVol.Volume = _mode == MixMode.MicOnly ? 0f : _appLevel;
    }

    void RestartAppCapture()
    {
        StopAppCapture();
        if (!IsRunning || _mode == MixMode.MicOnly || _appPid == 0) return;
        var capture = new ProcessLoopbackCapture(_appPid);
        var buffer = NewBuffer(capture.WaveFormat);
        capture.DataAvailable += (data, count) => buffer.AddSamples(data, 0, count);
        capture.Failed += ex => Error?.Invoke("Fenster-Audio konnte nicht aufgenommen werden: " + ex.Message);
        _app = capture;
        _appVol = new VolumeSampleProvider(Convert(buffer));
        _appInput = _appVol;
        ApplyVolumes();
        _mixer!.AddMixerInput(_appInput);
        capture.Start();
    }

    void StopAppCapture()
    {
        if (_appInput != null) _mixer?.RemoveMixerInput(_appInput);
        _appInput = null;
        _appVol = null;
        _app?.Dispose();
        _app = null;
    }

    static JitterBuffer NewBuffer(WaveFormat format) => new(format);

    static ISampleProvider Buffer(WasapiCapture capture)
    {
        var buffer = NewBuffer(capture.WaveFormat);
        capture.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        return Convert(buffer);
    }

    // Brings any input format to the 48 kHz stereo float mix format.
    static ISampleProvider Convert(IWaveProvider buffer)
    {
        ISampleProvider sp = buffer.ToSampleProvider();
        if (sp.WaveFormat.Channels == 1)
            sp = new MonoToStereoSampleProvider(sp);
        else if (sp.WaveFormat.Channels > 2)
            sp = new MultiplexingSampleProvider(new[] { sp }, 2);
        if (sp.WaveFormat.SampleRate != MixFormat.SampleRate)
            sp = new WdlResamplingSampleProvider(sp, MixFormat.SampleRate);
        return sp;
    }

    public void Dispose() => Stop();
}

