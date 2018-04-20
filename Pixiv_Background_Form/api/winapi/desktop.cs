using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Pixiv_Background_Form
{
    /// <summary>
    /// 修改桌面背景的几种方法
    /// </summary>
    public class Desktop
    {
        /// <summary>
        /// 激活桌面
        /// </summary>
        public static void EnableActiveDesktop()
        {
            IntPtr result = IntPtr.Zero;
            WinAPI.SendMessageTimeout(WinAPI.FindWindow("Progman", null), 0x52c, IntPtr.Zero, IntPtr.Zero, 0, 500, out result);
        }
        /// <summary>
        /// 修改壁纸为指定路径下的壁纸图片，且带有渐变效果
        /// </summary>
        /// <param name="path">壁纸图片的路径</param>
        public static void SetWallpaperUsingActiveDesktop(string path)
        {
            EnableActiveDesktop();

            ThreadStart threadStarter = () =>
            {
                WinAPI.IActiveDesktop _activeDesktop = WinAPI.ActiveDesktopWrapper.GetActiveDesktop();
                _activeDesktop.SetWallpaper(path, 0);
                _activeDesktop.ApplyChanges(WinAPI.AD_Apply.ALL | WinAPI.AD_Apply.FORCE);

                Marshal.ReleaseComObject(_activeDesktop);
            };
            Thread thread = new Thread(threadStarter);
            thread.SetApartmentState(ApartmentState.STA); //Set the thread to STA (REQUIRED!!!!)
            thread.Start();
            thread.Join(2000);

        }
        /// <summary>
        /// 修改桌面背景为指定窗体的句柄
        /// </summary>
        /// <param name="handle">要显示在桌面上的窗体句柄</param>
        public static void SetWallpaperUsingFormHandle(IntPtr handle)
        {
            EnableActiveDesktop();

            IntPtr workerw = IntPtr.Zero;
            WinAPI.EnumWindows(new WinAPI.EnumWindowsProc((tophandle, topparamhandle) =>
            {
                IntPtr p = WinAPI.FindWindowEx(tophandle,
                                            IntPtr.Zero,
                                            "SHELLDLL_DefView",
                                            null);

                if (p != IntPtr.Zero)
                {
                    // Gets the WorkerW Window after the current one.
                    workerw = WinAPI.FindWindowEx(IntPtr.Zero,
                                               tophandle,
                                               "WorkerW",
                                               null);
                }

                return true;
            }), IntPtr.Zero);
            WinAPI.SetParent(handle, workerw);
        }
        /// <summary>
        /// 修改壁纸为指定路径下的壁纸图片，无任何切换效果
        /// </summary>
        /// <param name="path">壁纸图片的路径</param>
        public static void SetWallpaperUsingSystemParameterInfo(string path)
        {
            WinAPI.SystemParametersInfo(WinAPI.SPI_SETDESKWALLPAPER, 0, path, WinAPI.SPIF_UPDATEINIFILE | WinAPI.SPIF_SENDWININICHANGE);
        }
    }
}
