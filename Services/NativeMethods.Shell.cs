using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace leeyez_kai.Services
{
    /// <summary>Win32 API — シェルアイコン関連</summary>
    public static partial class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        public const uint SHGFI_ICON = 0x000000100;
        public const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        public const uint SHGFI_OPENICON = 0x000000002;
        public const uint SHGFI_SMALLICON = 0x000000001;
        public const uint SHGFI_PIDL = 0x000000008;
        public const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        public const int CSIDL_DRIVES = 0x0011;
        public const int CSIDL_DESKTOP = 0x0000;
        public const int CSIDL_NETWORK = 0x0012;

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll")]
        public static extern int SHGetSpecialFolderLocation(IntPtr hwnd, int csidl, out IntPtr ppidl);

        [DllImport("shell32.dll")]
        public static extern IntPtr SHGetFileInfo(IntPtr ppidl, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        public static extern int StrCmpLogicalW(string psz1, string psz2);

        public static Icon? GetFileIcon(string path, bool smallIcon = true)
        {
            var shfi = new SHFILEINFO();
            uint flags = SHGFI_ICON | (smallIcon ? SHGFI_SMALLICON : 0);
            var result = SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
            if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero) return null;
            try { return (Icon)Icon.FromHandle(shfi.hIcon).Clone(); }
            finally { DestroyIcon(shfi.hIcon); }
        }

        public static Icon? GetExtensionIcon(string extension, bool smallIcon = true)
        {
            var shfi = new SHFILEINFO();
            uint flags = SHGFI_ICON | SHGFI_USEFILEATTRIBUTES | (smallIcon ? SHGFI_SMALLICON : 0);
            var result = SHGetFileInfo("file" + extension, FILE_ATTRIBUTE_NORMAL, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
            if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero) return null;
            try { return (Icon)Icon.FromHandle(shfi.hIcon).Clone(); }
            finally { DestroyIcon(shfi.hIcon); }
        }

        public static Icon? GetFolderIcon(bool open = false, bool smallIcon = true)
        {
            var shfi = new SHFILEINFO();
            uint flags = SHGFI_ICON | SHGFI_USEFILEATTRIBUTES | (smallIcon ? SHGFI_SMALLICON : 0) | (open ? SHGFI_OPENICON : 0);
            var result = SHGetFileInfo("folder", FILE_ATTRIBUTE_DIRECTORY, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
            if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero) return null;
            try { return (Icon)Icon.FromHandle(shfi.hIcon).Clone(); }
            finally { DestroyIcon(shfi.hIcon); }
        }

        public static Icon? GetSpecialFolderIcon(int csidl, bool smallIcon = true)
        {
            if (SHGetSpecialFolderLocation(IntPtr.Zero, csidl, out IntPtr pidl) != 0) return null;
            try
            {
                var shfi = new SHFILEINFO();
                uint flags = SHGFI_ICON | SHGFI_PIDL | (smallIcon ? SHGFI_SMALLICON : 0);
                var result = SHGetFileInfo(pidl, 0, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
                if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero) return null;
                try { return (Icon)Icon.FromHandle(shfi.hIcon).Clone(); }
                finally { DestroyIcon(shfi.hIcon); }
            }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }
    }
}
