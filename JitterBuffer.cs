using NAudio.Wave;

namespace MicMixer;

// Holds playback until a small cushion is buffered, and re-primes after an underrun instead of stuttering.
public sealed class JitterBuffer : IWaveProvider
{
    readonly BufferedWaveProvider _buffer;
    readonly int _primeBytes;
    readonly int _maxBytes;
    bool _priming = true;

    public JitterBuffer(WaveFormat format, int primeMs = 40, int maxMs = 200)
    {
        _buffer = new BufferedWaveProvider(format)
        {
            ReadFully = false,
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(1),
        };
        _primeBytes = format.ConvertLatencyToByteSize(primeMs);
        _maxBytes = format.ConvertLatencyToByteSize(maxMs);
    }

    public WaveFormat WaveFormat => _buffer.WaveFormat;

    public void AddSamples(byte[] data, int offset, int count) => _buffer.AddSamples(data, offset, count);

    public int Read(byte[] dest, int offset, int count)
    {
        int buffered = _buffer.BufferedBytes;
        if (_priming)
        {
            if (buffered < _primeBytes)
            {
                Array.Clear(dest, offset, count);
                return count;
            }
            _priming = false;
        }

        // Clock drift between devices slowly grows the backlog; trim it back so latency stays low.
        if (buffered > _maxBytes)
        {
            int skip = buffered - _primeBytes;
            skip -= skip % WaveFormat.BlockAlign;
            var trash = new byte[skip];
            _buffer.Read(trash, 0, skip);
        }

        int read = _buffer.Read(dest, offset, count);
        if (read < count)
        {
            Array.Clear(dest, offset + read, count - read);
            _priming = true;
        }
        return count;
    }
}
