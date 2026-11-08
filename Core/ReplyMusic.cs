namespace TacticalRequests.Core;

internal static class ReplyMusic
{
    public const int SampleRate = 22050;

    // Each phrase leaves room for the notes to decay before playback ends.
    public static byte[] Create(Reply reply)
    {
        double[] samples = new double[(int)(SampleRate * 2.4)];
        switch (reply)
        {
            case Reply.Take:
                AddNote(samples, 0, 60, 0.7, 0.16);
                AddNote(samples, 0, 72, 0.4, 0.32);
                AddNote(samples, 0.23, 76, 0.4, 0.32);
                AddNote(samples, 0.46, 79, 0.4, 0.32);
                AddNote(samples, 0.92, 76, 0.4, 0.3);
                AddNote(samples, 1.15, 84, 0.95, 0.3);
                AddNote(samples, 1.15, 67, 1.0, 0.16);
                break;
            case Reply.Cannot:
                AddNote(samples, 0, 57, 0.9, 0.16);
                AddNote(samples, 0, 72, 0.7, 0.3);
                AddNote(samples, 0.71, 69, 0.7, 0.3);
                AddNote(samples, 1.42, 64, 0.9, 0.3);
                AddNote(samples, 1.42, 57, 0.9, 0.16);
                break;
            case Reply.Wait:
                AddNote(samples, 0, 67, 0.38, 0.28);
                AddNote(samples, 0.3, 74, 0.38, 0.28);
                AddNote(samples, 1.2, 67, 0.38, 0.28);
                AddNote(samples, 1.5, 74, 0.7, 0.28);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(reply));
        }

        return Encode(samples);
    }

    public static byte[] CreateChat()
    {
        var samples = new double[(int)(SampleRate * 0.65)];
        AddNote(samples, 0, 76, 0.25, 0.3);
        AddNote(samples, 0.16, 81, 0.4, 0.3);
        return Encode(samples);
    }

    private static byte[] Encode(double[] samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short value = (short)(Math.Clamp(samples[i], -1, 1) * short.MaxValue);
            pcm[i * 2] = (byte)(value & 255);
            pcm[i * 2 + 1] = (byte)((value >> 8) & 255);
        }
        return pcm;
    }

    private static void AddNote(double[] samples, double start, int midi, double duration, double gain)
    {
        double frequency = 440 * Math.Pow(2, (midi - 69) / 12.0);
        int first = (int)(start * SampleRate);
        int length = Math.Min((int)(duration * SampleRate), samples.Length - first);
        for (int i = 0; i < length; i++)
        {
            double time = (double)i / SampleRate;
            double attack = Math.Min(1, time / 0.012);
            double release = Math.Min(1, (double)(length - 1 - i) / (SampleRate * 0.09));
            double envelope = attack * release * Math.Exp(-3 * time / duration);
            double phase = 2 * Math.PI * frequency * time;
            double tone = (Math.Sin(phase) + 0.25 * Math.Sin(2 * phase) + 0.1 * Math.Sin(3 * phase)) / 1.35;
            samples[first + i] += tone * envelope * gain;
        }
    }
}
