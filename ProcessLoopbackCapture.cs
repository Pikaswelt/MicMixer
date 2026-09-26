using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace MicMixer;

// Captures only the audio of one process (and its child processes) via Windows' process-loopback virtual device.
public sealed class ProcessLoopbackCapture : IDisposable
{
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public event Action<byte[], int>? DataAvailable;
    public event Action<Exception>? Failed;

    readonly int _pid;
    Thread? _thread;
    volatile bool _stop;

    public ProcessLoopbackCapture(int pid) => _pid = pid;

    public void Start()
    {
        // COM activation and the capture loop run on an MTA thread, as the audio APIs expect.
        _thread = new Thread(Run) { IsBackground = true, Name = "ProcessLoopback" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void Stop()
    {
        _stop = true;
        if (_thread != null && _thread != Thread.CurrentThread) _thread.Join(1000);
        _thread = null;
    }

    void Run()
    {
        try
        {
            using var client = Activate(_pid);
            using var evt = new AutoResetEvent(false);
            client.Initialize(AudioClientShareMode.Shared,
                AudioClientStreamFlags.Loopback | AudioClientStreamFlags.EventCallback | AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality,
                200_000, 0, WaveFormat, Guid.Empty);
            client.SetEventHandle(evt.SafeWaitHandle.DangerousGetHandle());
            var capture = client.AudioCaptureClient;
            var buffer = new byte[WaveFormat.AverageBytesPerSecond];
            client.Start();
            while (!_stop)
            {
                evt.WaitOne(100);
                while (!_stop && capture.GetNextPacketSize() > 0)
                {
                    var data = capture.GetBuffer(out int frames, out var flags);
                    int bytes = frames * WaveFormat.BlockAlign;
                    if (bytes > buffer.Length) buffer = new byte[bytes];
                    if ((flags & AudioClientBufferFlags.Silent) != 0) Array.Clear(buffer, 0, bytes);
                    else Marshal.Copy(data, buffer, 0, bytes);
                    capture.ReleaseBuffer(frames);
                    DataAvailable?.Invoke(buffer, bytes);
                }
            }
            client.Stop();
        }
        catch (Exception ex)
        {
            if (!_stop) Failed?.Invoke(ex);
        }
    }

    static AudioClient Activate(int pid)
    {
        var p = new ActivationParams { ActivationType = 1, TargetProcessId = (uint)pid, LoopbackMode = 0 };
        var paramsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ActivationParams>());
        var propPtr = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariantBlob>());
        try
        {
            Marshal.StructureToPtr(p, paramsPtr, false);
            Marshal.StructureToPtr(new PropVariantBlob { vt = 65, cbSize = (uint)Marshal.SizeOf<ActivationParams>(), pBlobData = paramsPtr }, propPtr, false);

            var handler = new CompletionHandler();
            int hr = ActivateAudioInterfaceAsync(@"VAD\Process_Loopback", typeof(IAudioClient).GUID, propPtr, handler, out _);
            Marshal.ThrowExceptionForHR(hr);
            if (!handler.Done.Wait(5000)) throw new TimeoutException("Audio-Aktivierung hat nicht geantwortet.");
            Marshal.ThrowExceptionForHR(handler.Result);
            return new AudioClient((IAudioClient)handler.Interface!);
        }
        finally
        {
            Marshal.FreeHGlobal(propPtr);
            Marshal.FreeHGlobal(paramsPtr);
        }
    }

    public void Dispose() => Stop();

    [StructLayout(LayoutKind.Sequential)]
    struct ActivationParams
    {
        public int ActivationType;
        public uint TargetProcessId;
        public int LoopbackMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropVariantBlob
    {
        public ushort vt, r1, r2, r3;
        public uint cbSize;
        public IntPtr pBlobData;
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true)]
    static extern int ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivateAudioInterfaceCompletionHandler
    {
        void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivateAudioInterfaceAsyncOperation
    {
        void GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAgileObject { }

    sealed class CompletionHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public readonly ManualResetEventSlim Done = new();
        public int Result;
        public object? Interface;

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            operation.GetActivateResult(out Result, out Interface);
            Done.Set();
        }
    }
}
