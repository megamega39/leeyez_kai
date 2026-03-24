using System;
using System.Runtime.InteropServices;

namespace leeyez_kai.Services
{
    /// <summary>Win32 API — ファイル操作関連</summary>
    public static partial class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.U4)]
            public int wFunc;
            public string pFrom;
            public string pTo;
            public short fFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        private const int FO_DELETE = 0x0003;
        private const short FOF_ALLOWUNDO = 0x0040;
        private const short FOF_NOCONFIRMATION = 0x0010;

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        public static void MoveToRecycleBin(string path)
        {
            var fileOp = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + '\0' + '\0',
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION
            };
            SHFileOperation(ref fileOp);
        }
    }
}
