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

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int GetMixFormat(string pszDeviceName, out IntPtr ppFormat);
        int GetDeviceFormat(string pszDeviceName, int bDefault, out IntPtr ppFormat);
        int ResetDeviceFormat(string pszDeviceName);
        int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr pMixFormat);
        int GetProcessingPeriod(string pszDeviceName, int bDefault, out IntPtr pmftDefaultPeriod, out IntPtr pmftMinimumPeriod);
        int SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);
        int GetShareMode(string pszDeviceName, out IntPtr pDeviceFormat);
        int SetShareMode(string pszDeviceName, IntPtr pDeviceFormat);
        int GetPropertyValue(string pszDeviceName, int bStore, IntPtr pKey, out IntPtr pPropVariant);
        int SetPropertyValue(string pszDeviceName, int bStore, IntPtr pKey, IntPtr pPropVariant);
        int SetDefaultEndpoint(string pszDeviceName, int eRole);
        int SetEndpointVisibility(string pszDeviceName, int bVisible);
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient { }

    [ComImport, Guid("00000100-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumDebugPropertyInfo { }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint cProps);
        int GetAt(uint iProp, out PROPERTYKEY pkey);
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr pwszVal;
        public IntPtr offset;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        int GetCount(out uint pcDevices);
        int Item(uint nDevice, [MarshalAs(UnmanagedType.Interface)] out IMMDevice ppDevice);
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int dwStateMask, [MarshalAs(UnmanagedType.Interface)] out IMMDeviceCollection ppDevices);
        int GetDefaultAudioEndpoint(int dataFlow, int role,
            [MarshalAs(UnmanagedType.Interface)] out IMMDevice ppDevice);
        int GetDevice(string pwstrId, [MarshalAs(UnmanagedType.Interface)] out IMMDevice ppDevice);
        int RegisterEndpointNotificationCallback(IntPtr pClient);
        int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        int OpenPropertyStore(int stgmAccess, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore ppProperties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        int GetState(out int pdwState);
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
    private static string? _cachedDeviceId;

    private static IAudioEndpointVolume? GetEndpointVolume()
    {
        if (_cachedVolume != null)
        {
            // キャッシュがある場合、現在のデフォルトデバイスIDが変わっていないかチェック
            string? currentId = null;
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device);
                if (device != null)
                {
                    device.GetId(out currentId);
                    Marshal.ReleaseComObject(device);
                }
                Marshal.ReleaseComObject(enumerator);
            }
            catch { }

            if (currentId == _cachedDeviceId)
                return _cachedVolume;
            
            InvalidateCache(); // デバイスが変わっていれば破棄して再取得
        }

        lock (_lock)
        {
            if (_cachedVolume != null) return _cachedVolume;

            IMMDeviceEnumerator? enumerator = null;
            IMMDevice? device = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out device);
                device.GetId(out _cachedDeviceId);

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

    public class AudioDevice
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsDefault { get; set; }
    }

    /// <summary>利用可能なオーディオ再生デバイス一覧を取得</summary>
    public static List<AudioDevice> GetAudioDevices()
    {
        var devices = new List<AudioDevice>();
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            
            string defaultId = "";
            try
            {
                enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var defDevice);
                defDevice.GetId(out defaultId);
                Marshal.ReleaseComObject(defDevice);
            }
            catch { }

            enumerator.EnumAudioEndpoints(0 /* eRender */, 1 /* DEVICE_STATE_ACTIVE */, out collection);
            collection.GetCount(out var count);

            var PKEY_Device_FriendlyName = new PROPERTYKEY
            {
                fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
                pid = 14
            };

            for (uint i = 0; i < count; i++)
            {
                collection.Item(i, out var device);
                device.GetId(out var id);

                string name = id;
                try
                {
                    device.OpenPropertyStore(0 /* STGM_READ */, out var propStore);
                    propStore.GetValue(ref PKEY_Device_FriendlyName, out var propVar);
                    try
                    {
                        if (propVar.pwszVal != IntPtr.Zero)
                        {
                            name = Marshal.PtrToStringUni(propVar.pwszVal) ?? name;
                        }
                    }
                    finally
                    {
                        PropVariantClear(ref propVar);
                    }
                    Marshal.ReleaseComObject(propStore);
                }
                catch { }

                devices.Add(new AudioDevice
                {
                    Id = id,
                    Name = name,
                    IsDefault = (id == defaultId)
                });

                Marshal.ReleaseComObject(device);
            }
        }
        catch { }
        finally
        {
            if (collection != null) Marshal.ReleaseComObject(collection);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }

        return devices;
    }

    /// <summary>デフォルトのオーディオ再生デバイスを設定</summary>
    public static void SetDefaultAudioDevice(string deviceId)
    {
        try
        {
            var policyConfig = (IPolicyConfig)new PolicyConfigClient();
            policyConfig.SetDefaultEndpoint(deviceId, 0 /* eConsole */);
            policyConfig.SetDefaultEndpoint(deviceId, 1 /* eMultimedia */);
            policyConfig.SetDefaultEndpoint(deviceId, 2 /* eCommunications */);
            Marshal.ReleaseComObject(policyConfig);
            InvalidateCache();
        }
        catch { }
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
