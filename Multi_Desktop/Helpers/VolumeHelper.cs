using System.Runtime.InteropServices;

namespace Multi_Desktop.Helpers;

/// <summary>
/// Windows Core Audio API を使用してシステム音量を取得・設定するヘルパー
/// COM オブジェクトをキャッシュして毎回の再生成・リークを防止
/// </summary>
internal static class VolumeHelper
{
    // ─── COM インターフェース定義 ─────────────────────
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
        int GetDefaultAudioEndpoint(int dataFlow, int role,
            [MarshalAs(UnmanagedType.Interface)] out IMMDevice ppDevice);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr pNotify);
        int UnregisterControlChangeNotify(IntPtr pNotify);
        int GetChannelCount(out uint pnChannelCount);
        int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        int GetMasterVolumeLevel(out float pfLevelDB);
        int GetMasterVolumeLevelScalar(out float pfLevel);
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }

    private static readonly Guid IID_IAudioEndpointVolume =
        new("5CDF2C82-841E-4546-9722-0CF74078229A");

    // ─── キャッシュされた COM オブジェクト ─────────────
    private static readonly object _lock = new();
    private static IAudioEndpointVolume? _cachedVolume;

    private static IAudioEndpointVolume? GetEndpointVolume()
    {
        if (_cachedVolume != null) return _cachedVolume;

        lock (_lock)
        {
            if (_cachedVolume != null) return _cachedVolume;

            IMMDeviceEnumerator? enumerator = null;
            IMMDevice? device = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out device);
                var iid = IID_IAudioEndpointVolume;
                device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var obj);
                _cachedVolume = obj as IAudioEndpointVolume;
                return _cachedVolume;
            }
            catch { return null; }
            finally
            {
                // 中間 COM オブジェクトを解放（IAudioEndpointVolume はキャッシュ）
                if (device != null) Marshal.ReleaseComObject(device);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
        }
    }

    /// <summary>現在の音量を 0.0〜1.0 で取得</summary>
    public static float GetVolume()
    {
        try
        {
            var vol = GetEndpointVolume();
            if (vol == null) return 0.5f;
            vol.GetMasterVolumeLevelScalar(out var level);
            return level;
        }
        catch
        {
            InvalidateCache();
            return 0.5f;
        }
    }

    /// <summary>音量を 0.0〜1.0 で設定</summary>
    public static void SetVolume(float level)
    {
        try
        {
            var vol = GetEndpointVolume();
            if (vol == null) return;
            var guid = Guid.Empty;
            vol.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref guid);
        }
        catch { InvalidateCache(); }
    }

    /// <summary>ミュート状態を取得</summary>
    public static bool GetMute()
    {
        try
        {
            var vol = GetEndpointVolume();
            if (vol == null) return false;
            vol.GetMute(out var muted);
            return muted;
        }
        catch
        {
            InvalidateCache();
            return false;
        }
    }

    /// <summary>ミュート状態を設定</summary>
    public static void SetMute(bool mute)
    {
        try
        {
            var vol = GetEndpointVolume();
            if (vol == null) return;
            var guid = Guid.Empty;
            vol.SetMute(mute, ref guid);
        }
        catch { InvalidateCache(); }
    }

    /// <summary>ミュート状態をトグル</summary>
    public static void ToggleMute()
    {
        SetMute(!GetMute());
    }

    /// <summary>キャッシュを無効化（デバイス変更時などにリトライ可能にする）</summary>
    private static void InvalidateCache()
    {
        lock (_lock)
        {
            if (_cachedVolume != null)
            {
                try { Marshal.ReleaseComObject(_cachedVolume); } catch { }
                _cachedVolume = null;
            }
        }
    }

    /// <summary>アプリ終了時にキャッシュされた COM オブジェクトを解放</summary>
    public static void Cleanup()
    {
        InvalidateCache();
    }
}
