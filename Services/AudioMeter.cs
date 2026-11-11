using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace DynamicIsland;

/// <summary>
/// Слушает то, что сейчас звучит из колонок (WASAPI loopback), и раскладывает звук на 4 полосы частот.
/// Ничего не записывает и никуда не отправляет. Работает только пока включён.
/// </summary>
public sealed class AudioMeter
{
    const int N = 1024;            // размер окна БПФ (~21 мс при 48 кГц)
    const int Log2N = 10;
    // Границы полос, Гц: бас, низкая середина, середина, верха
    static readonly (double Lo, double Hi)[] BandHz = { (35, 140), (140, 500), (500, 2200), (2200, 9000) };

    WasapiLoopbackCapture? _cap;
    readonly float[] _buf = new float[N];
    readonly Complex[] _fft = new Complex[N];
    readonly float[] _window = new float[N];
    readonly double[] _peak = { 1e-4, 1e-4, 1e-4, 1e-4 };
    int _pos;

    /// <summary>Уровни 0..1 по полосам. Пишутся из потока захвата, читаются из UI.</summary>
    public readonly float[] Bands = new float[4];
    public bool Running => _cap != null;

    public AudioMeter()
    {
        for (int i = 0; i < N; i++) _window[i] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / (N - 1))));
    }

    public void Start()
    {
        if (_cap != null) return;
        try
        {
            _cap = new WasapiLoopbackCapture();
            _cap.DataAvailable += OnData;
            _cap.RecordingStopped += (_, _) => { };
            _cap.StartRecording();
        }
        catch { _cap = null; }
    }

    public void Stop()
    {
        var c = _cap;
        _cap = null;
        if (c != null)
        {
            try { c.DataAvailable -= OnData; c.StopRecording(); c.Dispose(); } catch { }
        }
        Array.Clear(Bands);
        _pos = 0;
    }

    /// <summary>Перезапуск, если сменилось устройство вывода.</summary>
    public void Restart() { if (_cap == null) return; Stop(); Start(); }

    void OnData(object? sender, WaveInEventArgs e)
    {
        var cap = _cap;
        if (cap == null) return;
        var fmt = cap.WaveFormat;
        if (fmt.Encoding != WaveFormatEncoding.IeeeFloat && fmt.Encoding != WaveFormatEncoding.Extensible) return;
        if (fmt.BitsPerSample != 32) return;
        int ch = fmt.Channels;
        int samples = e.BytesRecorded / 4;
        var span = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, samples * 4));
        for (int i = 0; i + ch <= span.Length; i += ch)
        {
            float v = 0;
            for (int c = 0; c < ch; c++) v += span[i + c];
            _buf[_pos++] = v / ch;
            if (_pos == N) { Analyze(fmt.SampleRate); _pos = 0; }
        }
    }

    void Analyze(int sampleRate)
    {
        for (int i = 0; i < N; i++) { _fft[i].X = _buf[i] * _window[i]; _fft[i].Y = 0; }
        FastFourierTransform.FFT(true, Log2N, _fft);
        double binHz = (double)sampleRate / N;

        for (int b = 0; b < 4; b++)
        {
            int lo = Math.Max(1, (int)(BandHz[b].Lo / binHz));
            int hi = Math.Min(N / 2 - 1, (int)(BandHz[b].Hi / binHz));
            double sum = 0;
            for (int k = lo; k <= hi; k++) sum += _fft[k].X * _fft[k].X + _fft[k].Y * _fft[k].Y;
            double level = Math.Sqrt(sum / Math.Max(1, hi - lo + 1));

            // Автоусиление: сравниваем с недавним пиком полосы, чтобы полоски двигались при любой громкости
            _peak[b] = Math.Max(level, _peak[b] * 0.996);
            if (_peak[b] < 1e-5) _peak[b] = 1e-5;
            double v = level / _peak[b];
            Bands[b] = (float)Math.Clamp(v * v, 0, 1); // квадрат — сильнее подчёркивает удары
        }
    }
}
