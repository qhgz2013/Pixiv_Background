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
            //pink LightBlue PaleVioletRed
            _cpu_history = new history_wrapper(cpu_history, Brushes.PaleVioletRed, false) { min = 0, max = 1 };
            _ram_history = new history_wrapper(ram_history, Brushes.DeepSkyBlue, false) { min = 0, max = 1 };
            _cpu_temp_history = new history_wrapper(cpu_temp_history, Brushes.Orange, true);
            _net_history = new history_wrapper(net_history, Brushes.OrangeRed, true) { brush2 = Brushes.Green };
            _disk_history = new history_wrapper(disk_history, Brushes.MediumPurple, true) { brush2 = Brushes.Peru };
            _gpu_history = new history_wrapper(gpu_history, Brushes.Crimson, false) { min = 0, max = 1 };
            _gpu_temp_history = new history_wrapper(gpu_temp_history, Brushes.Cyan, true);
        }

        private history_wrapper _cpu_history, _cpu_temp_history, _ram_history, _net_history, _disk_history, _gpu_history, _gpu_temp_history;
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
                cpu_temp.Content = float.IsNaN(ResourceMonitor.CPU_Temp) ? "--℃" : string.Format("{0:0}℃", ResourceMonitor.CPU_Temp);
                cpu_value.Content = float.IsNaN(ResourceMonitor.CPU_Usage) ? "--%" : string.Format("{0:0}%", ResourceMonitor.CPU_Usage * 100);
                gpu_temp.Content = float.IsNaN(ResourceMonitor.GPU_Temp) ? "--℃" : string.Format("{0:0}℃", ResourceMonitor.GPU_Temp);
                gpu_value.Content = float.IsNaN(ResourceMonitor.GPU_Usage) ? "--%" : string.Format("{0:0}%", ResourceMonitor.GPU_Usage * 100);
                ram_value.Content = float.IsNaN(ResourceMonitor.RAM_Usage) ? "--%" : string.Format("{0:0}%", ResourceMonitor.RAM_Usage * 100);
                net_sent_value.Content = float.IsNaN(ResourceMonitor.NET_Sent) ? "↑ --" : string.Format("↑ {0}", util.FormatBytes((ulong)ResourceMonitor.NET_Sent, 1).TrimEnd('B'));
                net_recv_value.Content = float.IsNaN(ResourceMonitor.NET_Recv) ? "↓ --" : string.Format("↓ {0}", util.FormatBytes((ulong)ResourceMonitor.NET_Recv, 1).TrimEnd('B'));
                disk_read_value.Content = float.IsNaN(ResourceMonitor.DISK_Read) ? "↑ --" : string.Format("↑ {0}", util.FormatBytes((ulong)ResourceMonitor.DISK_Read, 1).TrimEnd('B'));
                disk_write_value.Content = float.IsNaN(ResourceMonitor.DISK_Write) ? "↓ --" : string.Format("↓ {0}", util.FormatBytes((ulong)ResourceMonitor.DISK_Write, 1).TrimEnd('B'));

                _cpu_history.AddValue(ResourceMonitor.CPU_Usage);
                _ram_history.AddValue(ResourceMonitor.RAM_Usage);
                _gpu_history.AddValue(ResourceMonitor.GPU_Usage);
                _net_history.AddValue(ResourceMonitor.NET_Sent, ResourceMonitor.NET_Recv);
                _disk_history.AddValue(ResourceMonitor.DISK_Read, ResourceMonitor.DISK_Write);
                _cpu_temp_history.AddValue(ResourceMonitor.CPU_Temp);
                _gpu_temp_history.AddValue(ResourceMonitor.GPU_Temp);
            }));
        }

        private class history_wrapper
        {
            private Canvas _parent;
            private int _timescale { get { return (int)Math.Floor(_parent.ActualWidth); } }
            private List<Tuple<float, float>> _values;
            private float _min, _max;

            public Brush brush1, brush2;
            public bool auto_scale;
            public float min, max;
            public history_wrapper(Canvas parent, Brush brush1 = null, bool autoscale = true)
            {
                _parent = parent;
                _values = new List<Tuple<float, float>>();
                _min = float.NaN;
                _max = float.NaN;
                min = float.NaN;
                max = float.NaN;
                if (brush1 == null)
                    brush1 = Brushes.Black;
                this.brush1 = brush1;
                brush2 = Brushes.Transparent;
                auto_scale = autoscale;
            }
            public void AddValue(float value1, float value2 = float.NaN)
            {
                if (float.IsNaN(_min))
                    _min = value1;
                if (float.IsNaN(_max))
                    _max = value1;
                _min = Math.Min(_min, value1);
                if (!float.IsNaN(value2))
                    _min = Math.Min(_min, value2);
                _max = Math.Max(_max, value1);
                if (!float.IsNaN(value2))
                    _max = Math.Max(_max, value2);
                _values.Add(new Tuple<float, float>(value1, value2));
                if (_values.Count > _timescale)
                    _remove_first();
                _update_canvas();
            }
            private void _remove_first()
            {
                var first = _values[0];
                _values.RemoveAt(0);
                var first_args = _find_min_max(first);

                if (first_args.Item1 == _min)
                {
                    _min = _find_min_max(_values[0]).Item1;
                    for (int i = 0; i < _values.Count; i++)
                        _min = Math.Min(_min, _find_min_max(_values[i]).Item1);
                }
                if (first_args.Item2 == _max)
                {
                    _max = _find_min_max(_values[0]).Item2;
                    for (int i = 0; i < _values.Count; i++)
                        _max = Math.Max(_max, _find_min_max(_values[i]).Item2);
                }
            }
            private Tuple<float, float> _find_min_max(Tuple<float, float> input)
            {
                if (float.IsNaN(input.Item2))
                    return new Tuple<float, float>(input.Item1, input.Item1);
                else if (float.IsNaN(input.Item1))
                    return new Tuple<float, float>(input.Item2, input.Item2);
                else
                    return new Tuple<float, float>(Math.Min(input.Item1, input.Item2), Math.Max(input.Item1, input.Item2));
            }
            private void _update_canvas()
            {
                var poly1 = new Polygon();
                poly1.Fill = brush1;
                poly1.Stroke = brush1;
                poly1.StrokeThickness = 1;
                poly1.HorizontalAlignment = HorizontalAlignment.Left;
                poly1.VerticalAlignment = VerticalAlignment.Top;
                poly1.Opacity = 0.5;
                var poly2 = new Polygon();
                poly2.Fill = brush2;
                poly2.Stroke = brush2;
                poly2.StrokeThickness = 1;
                poly2.HorizontalAlignment = HorizontalAlignment.Left;
                poly2.VerticalAlignment = VerticalAlignment.Top;
                poly2.Opacity = 0.5;

                var points1 = new PointCollection();
                var points2 = new PointCollection();
                var control_width = _values.Count;
                var control_height = (int)Math.Floor(_parent.ActualHeight);
                points1.Add(new Point(control_width - 1, control_height - 1));
                points1.Add(new Point(0, control_height - 1));
                points2.Add(new Point(control_width - 1, control_height - 1));
                points2.Add(new Point(0, control_height - 1));

                var t_min = auto_scale ? _min : min;
                var t_max = auto_scale ? _max : max;
                if (t_min != t_max && !float.IsNaN(t_min) && !float.IsNaN(t_max))
                {
                    for (int i = 0; i < _values.Count; i++)
                    {
                        if (float.IsNaN(_values[i].Item1))
                            points1.Add(new Point(i, control_height - 1));
                        else
                        {
                            var normalized = (_values[i].Item1 - t_min) / (t_max - t_min);
                            var rev_norm = 1 - normalized;
                            var rev_scaled = rev_norm * control_height;
                            points1.Add(new Point(i, rev_scaled));
                        }
                        if (float.IsNaN(_values[i].Item2))
                            points2.Add(new Point(i, control_height - 1));
                        else
                        {
                            var normalized = (_values[i].Item2 - t_min) / (t_max - t_min);
                            var rev_norm = 1 - normalized;
                            var rev_scaled = rev_norm * control_height;
                            points2.Add(new Point(i, rev_scaled));
                        }
                    }
                }
                poly1.Points = points1;
                poly2.Points = points2;
                _parent.Children.Clear();
                _parent.Children.Add(poly2);
                _parent.Children.Add(poly1);
            }
        }
    }
}
