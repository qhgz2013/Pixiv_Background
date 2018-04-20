using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Pixiv_Background_Form
{
    public class Desktop
    {
        public static void EnableActiveDesktop()
        {
            IntPtr result = IntPtr.Zero;
            WinAPI.SendMessageTimeout(WinAPI.FindWindow("Progman", null), 0x52c, IntPtr.Zero, IntPtr.Zero, 0, 500, out result);
        }

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

        public static void SetWallpaperWithRetry(string path, int retryCount, Action<string> sw)
        {
            //set the wallpaper to the new image
            sw(path);

            //check if really set and retry up to 3 times
            int tryCount = 0;
            do
            {
                //if matching, break
                if (GetWallpaperUsingSystemParameterInfo().ToLower() == path.ToLower()) break;

                sw(path);

                tryCount++;
            } while (tryCount < 3);
        }

        public static void SetWallpaperUsingSystemParameterInfo(string path)
        {
            WinAPI.SystemParametersInfo(WinAPI.SPI_SETDESKWALLPAPER, 0, path, WinAPI.SPIF_UPDATEINIFILE | WinAPI.SPIF_SENDWININICHANGE);
        }

        public static String GetWallpaperUsingSystemParameterInfo()
        {
            var wallpaper = new String('\0', WinAPI.MAX_PATH);
            WinAPI.SystemParametersInfo(WinAPI.SPI_GETDESKWALLPAPER, (UInt32)wallpaper.Length, wallpaper, 0);
            wallpaper = wallpaper.Substring(0, wallpaper.IndexOf('\0'));
            return wallpaper;
        }


        public static void SetDwmColor(System.Drawing.Color newColor)
        {
            if (WinAPI.DwmIsCompositionEnabled())
            {
                WinAPI.DWM_COLORIZATION_PARAMS color;
                //get the current color
                WinAPI.DwmGetColorizationParameters(out color);
                //set new color to transition too
                color.ColorizationColor = (uint)System.Drawing.Color.FromArgb(255, newColor.R, newColor.G, newColor.B).ToArgb();
                //transition
                WinAPI.DwmSetColorizationParameters(ref color, 0);
            }
        }

        public static Color GetCurrentAeroColor()
        {
            if (WinAPI.DwmIsCompositionEnabled())
            {
                WinAPI.DWM_COLORIZATION_PARAMS color;
                //get the current color
                WinAPI.DwmGetColorizationParameters(out color);

                Color c = Color.FromArgb((int)color.ColorizationColor);

                return c;
            }

            return Color.Empty;
        }
    }
}
