using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Forms;

namespace Pixiv_Background_Form
{
    public class ScreenWatcher
    {
        private enum PROCESS_DPI_AWARENESS
        {
            PROCESS_DPI_UNAWARE,
            PROCESS_SYSTEM_DPI_AWARE,
            PROCESS_PER_MONITOR_DPI_AWARE
        }
        [DllImport("Shcore.dll")]
        private static extern int GetProcessDpiAwareness(IntPtr hprocess, out PROCESS_DPI_AWARENESS value);
        [DllImport("Shcore.dll")]
        private static extern int SetProcessDpiAwareness(PROCESS_DPI_AWARENESS value);
        /// <summary>
        /// 获取每个显示器的原始分辨率和位置
        /// </summary>
        /// <returns></returns>
        public static Rectangle[] GetScreenBoundary()
        {
            var frm = new testForm();
            var data = frm.data;

            var ret = new Rectangle[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                ret[i] = data[i].Bounds;
            }
            return ret;
        }
        /// <summary>
        /// 获取主屏幕的原始分辨率和位置
        /// </summary>
        /// <returns></returns>
        public static Rectangle GetPrimaryScreenBoundary()
        {
            var frm = new testForm();
            var data = frm.data;
            foreach (var item in data)
            {
                if (item.Primary)
                    return item.Bounds;
            }
            return new Rectangle();
        }

        /// <summary>
        /// 获取所有屏幕在同一图片中展开的大小
        /// </summary>
        /// <returns></returns>
        public static Size GetTotalSize()
        {
            var data = GetScreenBoundary();
            if (data.Length == 0) return new Size();
            var min_x = data[0].Left;
            var max_x = data[0].Right;
            var min_y = data[0].Top;
            var max_y = data[0].Bottom;

            for (int i = 1; i < data.Length; i++)
            {
                if (data[i].Left < min_x) min_x = data[i].Left;
                if (data[i].Right > max_x) max_x = data[i].Right;
                if (data[i].Top < min_y) min_y = data[i].Top;
                if (data[i].Bottom > max_y) max_y = data[i].Bottom;
            }
            return new Size(max_x - min_x, max_y - min_y);
        }

        /// <summary>
        /// 返回一个经过坐标变换（即保证所有屏幕的x和y都大于0）的屏幕位置和大小
        /// </summary>
        /// <returns></returns>
        public static Rectangle[] GetTransformedScreenBoundary()
        {
            var data = GetScreenBoundary();
            if (data.Length == 0) return null;
            var min_x = data[0].Left;
            var min_y = data[0].Top;

            for (int i = 1; i < data.Length; i++)
            {
                if (data[i].Left < min_x) min_x = data[i].Left;
                if (data[i].Top < min_y) min_y = data[i].Top;
            }

            var ret = new Rectangle[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                ret[i] = new Rectangle(data[i].X - min_x, data[i].Y - min_y, data[i].Width, data[i].Height);
            }
            return ret;
        }
        //一个测试用的窗体，用于获取原始分辨率，todo：支持多屏不同dpi
        private class testForm : Form
        {
            public testForm()
            {
                SetProcessDpiAwareness(PROCESS_DPI_AWARENESS.PROCESS_PER_MONITOR_DPI_AWARE);
                data = Screen.AllScreens;
            }
            public Screen[] data;
        }

    }
}
