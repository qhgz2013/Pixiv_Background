using GlobalUtil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
            var rect = ScreenWatcher.GetTotalSize();
            var scaled_width = rect.Width / ScreenWatcher.Scale;
            var scaled_height = rect.Height / ScreenWatcher.Scale;

            Left = 0;
            Top = 0;
            Width = scaled_width;
            Height = scaled_height;

            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var primary_screen = ScreenWatcher.GetPrimaryScreenBoundary();

            //设置overlay样式为对齐主显示器右上角
            var margin = overlay.Margin;
            var rect = ScreenWatcher.GetTotalSize();
            margin.Right = 10 + (rect.Width - primary_screen.Right) / ScreenWatcher.Scale;
            margin.Top = 10 + primary_screen.Top / ScreenWatcher.Scale;
            overlay.Margin = margin;

            //获取handle，设为背景窗体
            var source = PresentationSource.FromVisual(this);
            var helper = new System.Windows.Interop.WindowInteropHelper((Window)sender);
            Desktop.SetWallpaperUsingFormHandle(helper.Handle);

            //在alt+tab上隐藏
            int exStyle = (int)WinAPI.GetWindowLong(helper.Handle, (int)WinAPI.GetWindowLongFields.GWL_EXSTYLE);
            exStyle |= (int)WinAPI.ExtendedWindowStyles.WS_EX_TOOLWINDOW;
            WinAPI.SetWindowLong(helper.Handle, (int)WinAPI.GetWindowLongFields.GWL_EXSTYLE, exStyle);

            ResourceMonitor.ResourceUpdated += _on_resource_callback;
        }

        public void SetBackgroundImage(string path)
        {
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            var buffer = new byte[fs.Length];
            fs.Read(buffer, 0, buffer.Length);
            fs.Close();
            var ms = new MemoryStream(buffer);
            ms.Position = 0;
            Dispatcher.Invoke(new ThreadStart(delegate
            {
                var src = new BitmapImage();
                src.BeginInit();
                src.StreamSource = ms;
                src.EndInit();

                var img = new Image();
                img.Source = src;
                img.Name = background_img.Name;

                var parent = ((Grid)background_img.Parent);
                parent.Children.Add(img);

                var w_binding = new Binding();
                w_binding.RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Window), 1);
                w_binding.Path = new PropertyPath(ActualWidthProperty);
                var h_binding = new Binding();
                h_binding.RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Window), 1);
                h_binding.Path = new PropertyPath(ActualHeightProperty);

                img.SetBinding(WidthProperty, w_binding);
                img.SetBinding(HeightProperty, h_binding);
                var sb = new Storyboard();
                var ani_fade_out = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromSeconds(2)));
                var ani_fade_in = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromSeconds(2)));
                ani_fade_out.Completed += delegate
                {
                    //replacing the component
                    parent.Children.Remove(background_img);
                    background_img = img;
                };

                Storyboard.SetTarget(ani_fade_out, background_img);
                Storyboard.SetTargetProperty(ani_fade_out, new PropertyPath(OpacityProperty));
                Storyboard.SetTarget(ani_fade_in, img);
                Storyboard.SetTargetProperty(ani_fade_in, new PropertyPath(OpacityProperty));

                sb.Children.Add(ani_fade_in);
                sb.Children.Add(ani_fade_out);

                sb.Begin();
            }));
        }

        private void _on_resource_callback(object sender, EventArgs e)
        {
            Dispatcher.Invoke(new ThreadStart(delegate
            {
                cpu_temp.Content = float.IsNaN(ResourceMonitor.CPU_Temp) ? "--" : string.Format("{0:0}℃", ResourceMonitor.CPU_Temp);
                cpu_value.Content = float.IsNaN(ResourceMonitor.CPU_Usage) ? "--" : string.Format("{0:0}%", ResourceMonitor.CPU_Usage * 100);
                gpu_temp.Content = float.IsNaN(ResourceMonitor.GPU_Temp) ? "--" : string.Format("{0:0}℃", ResourceMonitor.GPU_Temp);
                gpu_value.Content = float.IsNaN(ResourceMonitor.GPU_Usage) ? "--" : string.Format("{0:0}%", ResourceMonitor.GPU_Usage * 100);
                ram_value.Content = float.IsNaN(ResourceMonitor.RAM_Usage) ? "--" : string.Format("{0:0}%", ResourceMonitor.RAM_Usage * 100);
                net_sent_value.Content = float.IsNaN(ResourceMonitor.NET_Sent) ? "--" : string.Format("↑ {0}", util.FormatBytes((ulong)ResourceMonitor.NET_Sent, 1).TrimEnd('B'));
                net_recv_value.Content = float.IsNaN(ResourceMonitor.NET_Recv) ? "--" : string.Format("↓ {0}", util.FormatBytes((ulong)ResourceMonitor.NET_Recv, 1).TrimEnd('B'));
                disk_read_value.Content = float.IsNaN(ResourceMonitor.DISK_Read) ? "--" : string.Format("↑ {0}", util.FormatBytes((ulong)ResourceMonitor.DISK_Read, 1).TrimEnd('B'));
                disk_write_value.Content = float.IsNaN(ResourceMonitor.DISK_Write) ? "--" : string.Format("↓ {0}", util.FormatBytes((ulong)ResourceMonitor.DISK_Write, 1).TrimEnd('B'));
            }));
        }
    }
}
