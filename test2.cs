using System;
using System.Runtime.InteropServices;
class Program {
    [ComImport, Guid(""BCDE0395-E52F-467C-8E3D-C4579291692E"")]
    private class MMDeviceEnumerator { }
    [ComImport, Guid(""A95664D2-9614-4F35-A746-DE8DB63617E6""), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator {
        int EnumAudioEndpoints(int dataFlow, int dwStateMask, [MarshalAs(UnmanagedType.Interface)] out IMMDeviceCollection ppDevices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, [MarshalAs(UnmanagedType.Interface)] out IMMDevice ppDevice);
    }
    [ComImport, Guid(""D666063F-1587-4E43-81F1-B948E807363F""), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice {
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        int OpenPropertyStore(int stgmAccess, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore ppProperties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
    }
    [ComImport, Guid(""0BD7A1BE-7A1A-44DB-8397-CC5392387B5E""), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection {
        int GetCount(out uint pcDevices);
        int Item(uint nDevice, [MarshalAs(UnmanagedType.Interface)] out IMMDevice ppDevice);
    }
    [ComImport, Guid(""1449A1E9-C66A-4FE4-B800-E7378E49A9A4""), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore {
        int GetCount(out uint cProps);
        int GetAt(uint iProp, out PROPERTYKEY pkey);
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY { public Guid fmtid; public uint pid; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT {
        public ushort vt; public ushort wReserved1; public ushort wReserved2; public ushort wReserved3;
        public IntPtr pwszVal;
        public IntPtr dummy1;
    }
    static void Main() {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        enumerator.EnumAudioEndpoints(0, 1, out var collection);
        collection.GetCount(out var count);
        var pkey = new PROPERTYKEY { fmtid = new Guid(""a45c254e-df1c-4efd-8020-67d146a850e0""), pid = 14 };
        for (uint i = 0; i < count; i++) {
            collection.Item(i, out var device);
            device.GetId(out var id);
            try {
                device.OpenPropertyStore(0, out var propStore);
                propStore.GetValue(ref pkey, out var pv);
                string name = pv.vt == 31 && pv.pwszVal != IntPtr.Zero ? Marshal.PtrToStringUni(pv.pwszVal) : ""Vt is "" + pv.vt;
                Console.WriteLine($""ID: {id}\nName: {name}\n"");
            } catch (Exception ex) {
                Console.WriteLine(""Error: "" + ex.Message);
            }
        }
    }
}
