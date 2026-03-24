using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace leeyez_kai.Services
{
    /// <summary>Win32 API — 共通定義</summary>
    public static partial class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }
}
