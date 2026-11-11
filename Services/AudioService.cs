using NAudio.CoreAudioApi;
using static DynamicIsland.Native;

namespace DynamicIsland;

/// <summary>Громкость, микрофон, выбор устройства вывода.</summary>
public sealed class AudioService
{
    readonly MMDeviceEnumerator _en = new();
    MMDevice? _out;

    /// <summary>Срабатывает из фонового потока.</summary>
    public event Action? VolumeChanged;

    public AudioService() => RefreshOutput();

    void RefreshOutput()
    {
        try
        {
            if (_out != null) _out.AudioEndpointVolume.OnVolumeNotification -= OnVol;
            _out = _en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _out.AudioEndpointVolume.OnVolumeNotification += OnVol;
        }
        catch { _out = null; }
    }

    void OnVol(AudioVolumeNotificationData d) => VolumeChanged?.Invoke();

    public float Volume
    {
        get { try { return _out?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0; } catch { return 0; } }
        set
        {
            try
            {
                if (_out == null) return;
                _out.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0f, 1f);
                if (value > 0 && _out.AudioEndpointVolume.Mute) _out.AudioEndpointVolume.Mute = false;
            }
            catch { }
        }
    }

    public string OutputName { get { try { return _out?.FriendlyName ?? ""; } catch { return ""; } } }

    public bool MicMuted
    {
        get
        {
            try { return _en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications).AudioEndpointVolume.Mute; }
            catch { return false; }
        }
        set
        {
            foreach (var role in new[] { Role.Communications, Role.Console })
            {
                try { _en.GetDefaultAudioEndpoint(DataFlow.Capture, role).AudioEndpointVolume.Mute = value; } catch { }
            }
        }
    }

    public bool HasMic
    {
        get { try { return _en.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); } catch { return false; } }
    }

    /// <summary>Переключает звук на следующее устройство. Возвращает его имя.</summary>
    public string? NextOutput()
    {
        try
        {
            var list = _en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
            if (list.Count == 0) return null;
            if (list.Count == 1) return list[0].FriendlyName;
            var cur = _en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
            var i = list.FindIndex(d => d.ID == cur);
            var next = list[(i + 1) % list.Count];
            var pc = (IPolicyConfig)new PolicyConfigClient();
            for (int r = 0; r < 3; r++) pc.SetDefaultEndpoint(next.ID, r);
            RefreshOutput();
            VolumeChanged?.Invoke();
            return next.FriendlyName;
        }
        catch { return null; }
    }

    public int OutputCount
    {
        get { try { return _en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Count; } catch { return 0; } }
    }
}
