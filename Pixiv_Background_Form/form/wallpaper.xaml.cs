using GlobalUtil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Pixiv_Background_Form
{
    /// <summary>
    /// wallpaper.xaml 的交互逻辑
    /// </summary>
    public partial class Wallpaper : Window
    {
        public Wallpaper()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var rect = ScreenWatcher.GetTotalSize();
            var scaled_width = rect.Width / ScreenWatcher.Scale;
            var scaled_height = rect.Height / ScreenWatcher.Scale;

            Left = 0;
            Top = 0;
            Width = scaled_width;
            Height = scaled_height;

            var primary_screen = ScreenWatcher.GetPrimaryScreenBoundary();

            //设置overlay样式为对齐主显示器右上角
            var margin = overlay.Margin;
            margin.Right = 10 + (rect.Width - primary_screen.Right) / ScreenWatcher.Scale;
            margin.Top = 10 + primary_screen.Top / ScreenWatcher.Scale;
            overlay.Margin = margin;

            //获取handle，设为背景窗体
            var source = PresentationSource.FromVisual(this);
            var helper = new System.Windows.Interop.WindowInteropHelper((Window)sender);
            Desktop.SetWallpaperUsingFormHandle(helper.Handle);


        }
    }
}
