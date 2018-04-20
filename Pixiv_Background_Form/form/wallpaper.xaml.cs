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

            Tracer.GlobalTracer.TraceInfo("Origin rect: (" + rect.Width + "," + rect.Height + ")");
            Tracer.GlobalTracer.TraceInfo("Scaled rect: (" + scaled_width + "," + scaled_height + ")");

            Left = 0;
            Top = 0;
            Width = scaled_width;
            Height = scaled_height;
        }
    }
}
