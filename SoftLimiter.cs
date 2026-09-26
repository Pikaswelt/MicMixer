using NAudio.Wave;

namespace MicMixer;

// Passes audio untouched below the knee and bends peaks smoothly toward 1.0 instead of hard clipping.
public sealed class SoftLimiter(ISampleProvider source, float knee = 0.8f) : ISampleProvider
{
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        float range = 1f - knee;
        for (int i = offset; i < offset + read; i++)
        {
            float x = buffer[i];
            float a = MathF.Abs(x);
            if (a > knee)
                buffer[i] = MathF.CopySign(knee + range * MathF.Tanh((a - knee) / range), x);
        }
        return read;
    }
}
