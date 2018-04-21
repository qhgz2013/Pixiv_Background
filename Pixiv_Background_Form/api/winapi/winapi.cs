using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Pixiv_Background_Form
{
    public class WinAPI
    {
        #region Wallpaper Setting
        public const uint SPIF_UPDATEINIFILE = 0x1;
        public const uint SPIF_SENDWININICHANGE = 0x02;
        public const uint SPI_SETDESKWALLPAPER = 20;
        public const uint SPI_GETDESKWALLPAPER = 0x73;
        public const int MAX_PATH = 260;
        public const int COLOR_DESKTOP = 1;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);

        [DllImport("user32.dll")]
        public static extern bool SetSysColors(int cElements, int[] lpaElements, COLORREF lpaRgbValues);

        //struct came from http://www.pinvoke.net/default.aspx/Structures/COLORREF.html
        [StructLayout(LayoutKind.Sequential)]
        public struct COLORREF
        {
            public uint ColorDWORD;

            public COLORREF(uint color)
            {
                ColorDWORD = color;
            }

            public COLORREF(System.Drawing.Color color)
            {
                ColorDWORD = color.R + (((uint)color.G) << 8) + (((uint)color.B) << 16);
            }

            public System.Drawing.Color GetColor()
            {
                return System.Drawing.Color.FromArgb((int)(0x000000FFU & ColorDWORD),
               (int)(0x0000FF00U & ColorDWORD) >> 8, (int)(0x00FF0000U & ColorDWORD) >> 16);
            }

            public void SetColor(System.Drawing.Color color)
            {
                ColorDWORD = color.R + (((uint)color.G) << 8) + (((uint)color.B) << 16);
            }
        }

        //below code came from forum posting: http://www.neowin.net/forum/topic/1035559-fade-effect-when-changing-wallpaper/
        [StructLayout(LayoutKind.Sequential)]
        public struct WALLPAPEROPT
        {
            public static readonly int SizeOf = Marshal.SizeOf(typeof(WALLPAPEROPT));
            public WallPaperStyle dwStyle;
        }

        public enum WallPaperStyle : int
        {
            WPSTYLE_CENTER = 0,
            WPSTYLE_TILE = 1,
            WPSTYLE_STRETCH = 2,
            /// <summary>Introduced in Windows 7</summary>
            WPSTYLE_KEEPASPECT = 3,
            /// <summary>Introduced in Windows 7</summary>
            WPSTYLE_CROPTOFIT = 4,
            /// <summary>Introduced in Windows 8</summary>
            WPSTYLE_SPAN = 5,
            WPSTYLE_MAX = 5
        }

        [Flags]
        public enum AD_Apply : int
        {
            SAVE = 0x00000001,
            HTMLGEN = 0x00000002,
            REFRESH = 0x00000004,
            ALL = SAVE | HTMLGEN | REFRESH,
            FORCE = 0x00000008,
            BUFFERED_REFRESH = 0x00000010,
            DYNAMICREFRESH = 0x00000020
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct COMPONENTSOPT
        {
            public static readonly int SizeOf = Marshal.SizeOf(typeof(COMPONENTSOPT));
            public int dwSize;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fEnableComponents;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fActiveDesktop;
        }

        [Flags]
        public enum CompItemState : int
        {
            NORMAL = 0x00000001,
            FULLSCREEN = 00000002,
            SPLIT = 0x00000004,
            VALIDSIZESTATEBITS = NORMAL | SPLIT | FULLSCREEN,
            VALIDSTATEBITS = NORMAL | SPLIT | FULLSCREEN | unchecked((int)0x80000000) | 0x40000000
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct COMPSTATEINFO
        {
            public static readonly int SizeOf = Marshal.SizeOf(typeof(COMPSTATEINFO));
            public int dwSize;
            public int iLeft;
            public int iTop;
            public int dwWidth;
            public int dwHeight;
            public CompItemState dwItemState;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct COMPPOS
        {
            public const int COMPONENT_TOP = 0x3FFFFFFF;
            public const int COMPONENT_DEFAULT_LEFT = 0xFFFF;
            public const int COMPONENT_DEFAULT_TOP = 0xFFFF;
            public static readonly int SizeOf = Marshal.SizeOf(typeof(COMPPOS));

            public int dwSize;
            public int iLeft;
            public int iTop;
            public int dwWidth;
            public int dwHeight;
            public int izIndex;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fCanResize;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fCanResizeX;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fCanResizeY;
            public int iPreferredLeftPercent;
            public int iPreferredTopPercent;
        }

        public enum CompType : int
        {
            HTMLDOC = 0,
            PICTURE = 1,
            WEBSITE = 2,
            CONTROL = 3,
            CFHTML = 4
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 8)]
        public struct COMPONENT
        {
            private const int INTERNET_MAX_URL_LENGTH = 2084; // = INTERNET_MAX_SCHEME_LENGTH (32) + "://\0".Length +   INTERNET_MAX_PATH_LENGTH (2048)
            public static readonly int SizeOf = Marshal.SizeOf(typeof(COMPONENT));

            public int dwSize;
            public int dwID;
            public CompType iComponentType;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fChecked;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fDirty;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fNoScroll;
            public COMPPOS cpPos;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string wszFriendlyName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = INTERNET_MAX_URL_LENGTH)]
            public string wszSource;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = INTERNET_MAX_URL_LENGTH)]
            public string wszSubscribedURL;

            public int dwCurItemState;
            public COMPSTATEINFO csiOriginal;
            public COMPSTATEINFO csiRestored;
        }

        public enum DtiAddUI : int
        {
            DEFAULT = 0x00000000,
            DISPSUBWIZARD = 0x00000001,
            POSITIONITEM = 0x00000002,
        }

        [Flags]
        public enum ComponentModify : int
        {
            TYPE = 0x00000001,
            CHECKED = 0x00000002,
            DIRTY = 0x00000004,
            NOSCROLL = 0x00000008,
            POS_LEFT = 0x00000010,
            POS_TOP = 0x00000020,
            SIZE_WIDTH = 0x00000040,
            SIZE_HEIGHT = 0x00000080,
            POS_ZINDEX = 0x00000100,
            SOURCE = 0x00000200,
            FRIENDLYNAME = 0x00000400,
            SUBSCRIBEDURL = 0x00000800,
            ORIGINAL_CSI = 0x00001000,
            RESTORED_CSI = 0x00002000,
            CURITEMSTATE = 0x00004000,
            ALL = TYPE | CHECKED | DIRTY | NOSCROLL | POS_LEFT | SIZE_WIDTH |
                SIZE_HEIGHT | POS_ZINDEX | SOURCE |
                FRIENDLYNAME | POS_TOP | SUBSCRIBEDURL | ORIGINAL_CSI |
                RESTORED_CSI | CURITEMSTATE
        }

        [Flags]
        public enum AddURL : int
        {
            SILENT = 0x0001
        }

        [ComImport]
        [Guid("F490EB00-1240-11D1-9888-006097DEACF9")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IActiveDesktop
        {
            [PreserveSig]
            int ApplyChanges(AD_Apply dwFlags);
            [PreserveSig]
            int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)]  System.Text.StringBuilder pwszWallpaper,
                      int cchWallpaper,
                      int dwReserved);
            [PreserveSig]
            int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string pwszWallpaper, int dwReserved);
            [PreserveSig]
            int GetWallpaperOptions(ref WALLPAPEROPT pwpo, int dwReserved);
            [PreserveSig]
            int SetWallpaperOptions(ref WALLPAPEROPT pwpo, int dwReserved);
            [PreserveSig]
            int GetPattern([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszPattern, int cchPattern, int dwReserved);
            [PreserveSig]
            int SetPattern([MarshalAs(UnmanagedType.LPWStr)] string pwszPattern, int dwReserved);
            [PreserveSig]
            int GetDesktopItemOptions(ref COMPONENTSOPT pco, int dwReserved);
            [PreserveSig]
            int SetDesktopItemOptions(ref COMPONENTSOPT pco, int dwReserved);
            [PreserveSig]
            int AddDesktopItem(ref COMPONENT pcomp, int dwReserved);
            [PreserveSig]
            int AddDesktopItemWithUI(IntPtr hwnd, ref COMPONENT pcomp, DtiAddUI dwFlags);
            [PreserveSig]
            int ModifyDesktopItem(ref COMPONENT pcomp, ComponentModify dwFlags);
            [PreserveSig]
            int RemoveDesktopItem(ref COMPONENT pcomp, int dwReserved);
            [PreserveSig]
            int GetDesktopItemCount(out int lpiCount, int dwReserved);
            [PreserveSig]
            int GetDesktopItem(int nComponent, ref COMPONENT pcomp, int dwReserved);
            [PreserveSig]
            int GetDesktopItemByID(IntPtr dwID, ref COMPONENT pcomp, int dwReserved);
            [PreserveSig]
            int GenerateDesktopItemHtml([MarshalAs(UnmanagedType.LPWStr)] string pwszFileName, ref COMPONENT pcomp, int dwReserved);
            [PreserveSig]
            int AddUrl(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string pszSource, ref COMPONENT pcomp, AddURL dwFlags);
            [PreserveSig]
            int GetDesktopItemBySource([MarshalAs(UnmanagedType.LPWStr)] string pwszSource, ref COMPONENT pcomp, int dwReserved);
        }

        public class ActiveDesktopWrapper
        {
            static readonly Guid CLSID_ActiveDesktop = new Guid("{75048700-EF1F-11D0-9888-006097DEACF9}");

            public static IActiveDesktop GetActiveDesktop()
            {
                Type typeActiveDesktop = Type.GetTypeFromCLSID(CLSID_ActiveDesktop);
                return (IActiveDesktop)Activator.CreateInstance(typeActiveDesktop);
            }
        }

        #endregion

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool Wow64DisableWow64FsRedirection(ref IntPtr ptr);

        #region WindowStuff
        public static IntPtr GetHandleForSHELLDLL_DefView()
        {
            IntPtr _ProgMan = GetShellWindow();
            IntPtr _SHELLDLL_DefViewParent = _ProgMan;
            IntPtr _SHELLDLL_DefView = FindWindowEx(_ProgMan, IntPtr.Zero, "SHELLDLL_DefView", null);

            if (_SHELLDLL_DefView == IntPtr.Zero)
            {

                EnumWindows((hwnd, lParam) =>
                {
                    StringBuilder sb = new StringBuilder(255);
                    GetClassName(hwnd, sb, sb.Capacity);
                    if (sb.ToString() == "WorkerW")
                    {
                        IntPtr child = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                        if (child != IntPtr.Zero)
                        {
                            _SHELLDLL_DefViewParent = hwnd;
                            _SHELLDLL_DefView = child;
                            return false;
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }

            return _SHELLDLL_DefView;
        }


        public static IntPtr GetHandleForDesktop()
        {
            IntPtr _SHELLDLL_DefView = GetHandleForSHELLDLL_DefView();
            IntPtr _SysListView32 = FindWindowEx(_SHELLDLL_DefView, IntPtr.Zero, "SysListView32", "FolderView");

            return _SysListView32;
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
        public static extern IntPtr SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int x, int Y, int cx, int cy, SetWindowPosFlags wFlags);

        /// <summary>
        ///     Special window handles
        /// </summary>
        public enum SpecialWindowHandles
        {
            HWND_TOP = 0,
            HWND_BOTTOM = 1,
            HWND_TOPMOST = -1,
            HWND_NOTOPMOST = -2
        }

        [Flags]
        public enum SetWindowPosFlags : uint
        {
            // ReSharper disable InconsistentNaming

            /// <summary>
            ///     If the calling thread and the thread that owns the window are attached to different input queues, the system posts the request to the thread that owns the window. This prevents the calling thread from blocking its execution while other threads process the request.
            /// </summary>
            SWP_ASYNCWINDOWPOS = 0x4000,

            /// <summary>
            ///     Prevents generation of the WM_SYNCPAINT message.
            /// </summary>
            SWP_DEFERERASE = 0x2000,

            /// <summary>
            ///     Draws a frame (defined in the window's class description) around the window.
            /// </summary>
            SWP_DRAWFRAME = 0x0020,

            /// <summary>
            ///     Applies new frame styles set using the SetWindowLong function. Sends a WM_NCCALCSIZE message to the window, even if the window's size is not being changed. If this flag is not specified, WM_NCCALCSIZE is sent only when the window's size is being changed.
            /// </summary>
            SWP_FRAMECHANGED = 0x0020,

            /// <summary>
            ///     Hides the window.
            /// </summary>
            SWP_HIDEWINDOW = 0x0080,

            /// <summary>
            ///     Does not activate the window. If this flag is not set, the window is activated and moved to the top of either the topmost or non-topmost group (depending on the setting of the hWndInsertAfter parameter).
            /// </summary>
            SWP_NOACTIVATE = 0x0010,

            /// <summary>
            ///     Discards the entire contents of the client area. If this flag is not specified, the valid contents of the client area are saved and copied back into the client area after the window is sized or repositioned.
            /// </summary>
            SWP_NOCOPYBITS = 0x0100,

            /// <summary>
            ///     Retains the current position (ignores X and Y parameters).
            /// </summary>
            SWP_NOMOVE = 0x0002,

            /// <summary>
            ///     Does not change the owner window's position in the Z order.
            /// </summary>
            SWP_NOOWNERZORDER = 0x0200,

            /// <summary>
            ///     Does not redraw changes. If this flag is set, no repainting of any kind occurs. This applies to the client area, the nonclient area (including the title bar and scroll bars), and any part of the parent window uncovered as a result of the window being moved. When this flag is set, the application must explicitly invalidate or redraw any parts of the window and parent window that need redrawing.
            /// </summary>
            SWP_NOREDRAW = 0x0008,

            /// <summary>
            ///     Same as the SWP_NOOWNERZORDER flag.
            /// </summary>
            SWP_NOREPOSITION = 0x0200,

            /// <summary>
            ///     Prevents the window from receiving the WM_WINDOWPOSCHANGING message.
            /// </summary>
            SWP_NOSENDCHANGING = 0x0400,

            /// <summary>
            ///     Retains the current size (ignores the cx and cy parameters).
            /// </summary>
            SWP_NOSIZE = 0x0001,

            /// <summary>
            ///     Retains the current Z order (ignores the hWndInsertAfter parameter).
            /// </summary>
            SWP_NOZORDER = 0x0004,

            /// <summary>
            ///     Displays the window.
            /// </summary>
            SWP_SHOWWINDOW = 0x0040,
        }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("User32.dll")]
        public static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("User32.dll")]
        public static extern void ReleaseDC(IntPtr dc);

        [DllImport("user32.dll", SetLastError = false)]
        public static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOACTIVATE = 0x0010;

        //static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        public static void SetBottom(IntPtr hWnd)
        {
            SetWindowPos(hWnd, 1, 0, 0, 0, 0, SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOACTIVATE);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr result);
        #endregion

        #region Window Styles
        [Flags]
        public enum ExtendedWindowStyles
        {
            WS_EX_TOOLWINDOW = 0x00000080
        }
        [Flags]
        public enum GetWindowLongFields
        {
            GWL_EXSTYLE = (-20)
        }
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        #endregion


        #region appbar
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uCallbackMessage;
            public int uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        public enum ABMsg : int
        {
            ABM_NEW,
            ABM_REMOVE,
            ABM_QUERYPOS,
            ABM_SETPOS,
            ABM_GETSTATE,
            ABM_GETTASKBARPOS,
            ABM_ACTIVATE,
            ABM_GETAUTOHIDEBAR,
            ABM_SETAUTOHIDEBAR,
            ABM_WINDOWPOSCHANGED,
            ABM_SETSTATE
        }
        public enum ABNotify : int
        {
            ABN_STATECHANGE,
            ABN_POSCHANGED,
            ABN_FULLSCREENAPP,
            ABN_WINDOWARRANGE
        }
        public enum ABEdge : int
        {
            ABE_LEFT,
            ABE_TOP,
            ABE_RIGHT,
            ABE_BOTTOM
        }
        [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]

        private static extern uint SHAppBarMessage(int dwMessage, ref APPBARDATA pData);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int RegisterWindowMessage(string msg);
        public static uint RegisterAppBar(IntPtr handle, out int registeredWindowMessage)
        {
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(abd);
            abd.hWnd = handle;
            registeredWindowMessage = RegisterWindowMessage("APPBARMSG_PIXIV_HELPER");
            abd.uCallbackMessage = registeredWindowMessage;
            var ret = SHAppBarMessage((int)ABMsg.ABM_NEW, ref abd);
            return ret;
        }
        public static uint UnregisterAppBar(IntPtr handle)
        {
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(abd);
            abd.hWnd = handle;
            var ret = SHAppBarMessage((int)ABMsg.ABM_REMOVE, ref abd);
            return ret;
        }
        #endregion


        #region memory

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public class MemoryStatusEx
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MemoryStatusEx()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
            }
        }
        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);
        #endregion
    }
}
