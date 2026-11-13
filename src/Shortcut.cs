using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace DnsDegistirici
{
    // .lnk kısayolu oluşturur/günceller ve System.AppUserModel.ID özelliğini yazar (IShellLink + IPropertyStore).
    static class Shortcut
    {
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        class CShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, int flags);
            void GetIDList(out IntPtr pidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int cmd);
            void SetShowCmd(int cmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int cch, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);
            void Resolve(IntPtr hwnd, int flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct PropertyKey
        {
            public Guid fmtid;
            public int pid;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PropVariant
        {
            public ushort vt;
            public ushort r1, r2, r3;
            public IntPtr p;
            public IntPtr p2;
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        interface IPropertyStore
        {
            void GetCount(out uint count);
            void GetAt(uint index, out PropertyKey key);
            void GetValue(ref PropertyKey key, out PropVariant value);
            void SetValue(ref PropertyKey key, ref PropVariant value);
            void Commit();
        }

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PropVariant pv);

        const ushort VT_LPWSTR = 31;

        static PropertyKey AppIdKey
        {
            get { return new PropertyKey { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 }; }
        }

        // target null ise var olan kısayolun hedefi korunur. Kimlik ve hedef zaten doğruysa dosyaya dokunulmaz.
        public static void Write(string path, string target, string appId)
        {
            var link = (IShellLinkW)new CShellLink();
            var file = (IPersistFile)link;
            bool exists = File.Exists(path);
            if (exists) file.Load(path, 2); // STGM_READWRITE: salt okunur açılırsa özellik yazımı reddedilir

            var store = (IPropertyStore)link;
            var key = AppIdKey;
            string currentId = null;
            PropVariant cur;
            store.GetValue(ref key, out cur);
            if (cur.vt == VT_LPWSTR) currentId = Marshal.PtrToStringUni(cur.p);
            PropVariantClear(ref cur);

            string currentTarget = null;
            if (exists)
            {
                var sb = new StringBuilder(1024);
                link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                currentTarget = sb.ToString();
            }

            bool targetOk = target == null || string.Equals(currentTarget, target, StringComparison.OrdinalIgnoreCase);
            if (exists && targetOk && currentId == appId) return;

            if (target != null)
            {
                link.SetPath(target);
                link.SetWorkingDirectory(Path.GetDirectoryName(target));
                link.SetIconLocation(target, 0);
            }

            var value = new PropVariant { vt = VT_LPWSTR, p = Marshal.StringToCoTaskMemUni(appId) };
            try
            {
                store.SetValue(ref key, ref value);
                store.Commit();
            }
            finally { PropVariantClear(ref value); }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            file.Save(path, true);
        }
    }
}
