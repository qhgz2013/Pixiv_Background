using System;
using System.Collections.Generic;
using System.IO;
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
    /// frmSetting.xaml 的交互逻辑
    /// </summary>
    public partial class frmSetting : Window
    {
        public frmSetting()
        {
            InitializeComponent();
        }

        private bool _form_loaded = false;
        private List<PathSetting> _path;
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            //路径加载
            _path = new List<PathSetting>(Settings.Paths);
            for (int i = 0; i < _path.Count; i++)
            {
                var cbselect = new CheckBox();
                cbselect.Tag = i;
                cbselect.Name = "ctl_0_" + i;
                cbselect.HorizontalAlignment = HorizontalAlignment.Center;
                cbselect.VerticalAlignment = VerticalAlignment.Center;
                cbselect.Click += cbselect_Click;

                var lblpath = new Label();
                lblpath.Tag = i;
                lblpath.Name = "ctl_1_" + i;
                if (_path[i].Directory == null)
                    lblpath.Content = "(null)";
                else if (_path[i].Directory == "")
                    lblpath.Content = "(empty)";
                else
                {
                    lblpath.Content = _path[i].Directory;
                    //路径不存在时红色标出
                    if (!Directory.Exists(_path[i].Directory))
                        lblpath.Foreground = new SolidColorBrush(Colors.Red);
                }
                lblpath.HorizontalAlignment = HorizontalAlignment.Left;
                lblpath.VerticalAlignment = VerticalAlignment.Center;
                lblpath.MouseDoubleClick += lblpath_MouseDoubleClick;

                var cbincludedir = new CheckBox();
                cbincludedir.Tag = i;
                cbincludedir.Name = "ctl_2_" + i;
                cbincludedir.IsChecked = _path[i].IncludingSubDir;
                cbincludedir.HorizontalAlignment = HorizontalAlignment.Center;
                cbincludedir.VerticalAlignment = VerticalAlignment.Center;
                cbincludedir.Click += cbincludedir_Click;

                var row = new RowDefinition();
                row.Height = new GridLength(25);
                PathLayer.RowDefinitions.Add(row);

                PathLayer.Children.Add(cbselect);
                Grid.SetRow(cbselect, i);
                Grid.SetColumn(cbselect, 0);
                PathLayer.Children.Add(lblpath);
                Grid.SetRow(lblpath, i);
                Grid.SetColumn(lblpath, 1);
                PathLayer.Children.Add(cbincludedir);
                Grid.SetRow(cbincludedir, i);
                Grid.SetColumn(cbincludedir, 2);
            }

            //选项加载
            cEnableAnimation.IsChecked = Settings.EnableSlideAnimation;
            cEnableBuffering.IsChecked = Settings.EnableBuffering;
            cEnableDiffWallpaper.IsChecked = Settings.EnableMultiMonitorDifferentWallpaper;
            cEnableQueue.IsChecked = Settings.EnableIllustQueue;
            cEnableWaifu2xUpscaling.IsChecked = Settings.EnableWaifu2xUpscaling;
            tChangeTime.Text = Settings.WallpaperChangeTime.ToString();
            tWaifu2xScaleThreshold.Text = Settings.Waifu2xUpscaleThreshold.ToString();

            if (!string.IsNullOrEmpty(Settings.Waifu2xPath))
            {
                tWaifu2xPath.Text = Settings.Waifu2xPath;
                if (!File.Exists(tWaifu2xPath.Text))
                    tWaifu2xPath.Foreground = new SolidColorBrush(Colors.Red);
                else
                    tWaifu2xPath.Foreground = new SolidColorBrush(Colors.Black);
            }
            else
                tWaifu2xPath.Foreground = new SolidColorBrush(Colors.Black);

            _form_loaded = true;
        }

        private void cbselect_Click(object sender, RoutedEventArgs e)
        {
            if (!_form_loaded) return;
            bApply.IsEnabled = true;
            int tag = (int)((CheckBox)sender).Tag;
            if (tag >= 0 && tag < _path.Count)
            {
                if ((bool)((CheckBox)sender).IsChecked)
                {
                    ((CheckBox)PathLayer.Children[tag * 3]).Background = new SolidColorBrush(Colors.LightGray);
                    ((Label)PathLayer.Children[tag * 3 + 1]).Background = new SolidColorBrush(Colors.LightGray);
                    ((CheckBox)PathLayer.Children[tag * 3 + 2]).Background = new SolidColorBrush(Colors.LightGray);
                }
                else
                {
                    ((CheckBox)PathLayer.Children[tag * 3]).Background = new SolidColorBrush(Colors.White);
                    ((Label)PathLayer.Children[tag * 3 + 1]).Background = new SolidColorBrush(Colors.White);
                    ((CheckBox)PathLayer.Children[tag * 3 + 2]).Background = new SolidColorBrush(Colors.White);
                }
            }
            else
            {
                Tracer.GlobalTracer.TraceWarning("tag id #" + tag + " out of range: maximum " + _path.Count);
            }
        }

        private void lblpath_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            int tag = (int)((Label)sender).Tag;
            if (tag >= 0 && tag < _path.Count)
            {
                var fbd = new System.Windows.Forms.FolderBrowserDialog();
                fbd.RootFolder = Environment.SpecialFolder.MyComputer;
                if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    ((Label)sender).Content = fbd.SelectedPath;
                    ((Label)sender).Foreground = new SolidColorBrush(Colors.Black);
                    _path[tag] = new PathSetting { Directory = fbd.SelectedPath, IncludingSubDir = _path[tag].IncludingSubDir };
                    bApply.IsEnabled = true;
                }
            }
            else
            {
                Tracer.GlobalTracer.TraceWarning("tag id #" + tag + " out of range: maximum " + _path.Count);
            }
        }

        private void cbincludedir_Click(object sender, RoutedEventArgs e)
        {
            if (!_form_loaded) return;
            int tag = (int)((CheckBox)sender).Tag;
            if (tag >= 0 && tag < _path.Count)
            {
                _path[tag] = new PathSetting { Directory = _path[tag].Directory, IncludingSubDir = (bool)((CheckBox)sender).IsChecked };
                bApply.IsEnabled = true;
            }
            else
            {
                Tracer.GlobalTracer.TraceWarning("tag id #" + tag + " out of range: maximum " + _path.Count);
            }
        }
        private void tChangeTime_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_form_loaded) return;
            int result;
            if (!string.IsNullOrEmpty(tChangeTime.Text) && int.TryParse(tChangeTime.Text, out result))
                tChangeTime.Foreground = new SolidColorBrush(Colors.Black);
            else
                tChangeTime.Foreground = new SolidColorBrush(Colors.Red);
        }

        private void tChangeTime_KeyUp(object sender, KeyEventArgs e)
        {
            bApply.IsEnabled = true;
        }

        private void tWaifu2xPath_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_form_loaded) return;
            bApply.IsEnabled = true;
        }

        private void tWaifu2xPath_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_form_loaded) return;
            if (!string.IsNullOrEmpty(tWaifu2xPath.Text) && File.Exists(tWaifu2xPath.Text))
                tWaifu2xPath.Foreground = new SolidColorBrush(Colors.Black);
            else
                tWaifu2xPath.Foreground = new SolidColorBrush(Colors.Red);
        }

        private void cEnableDiffWallpaper_Click(object sender, RoutedEventArgs e)
        {
            if (!_form_loaded) return;
            bApply.IsEnabled = true;
        }

        private void cEnableBuffering_Click(object sender, RoutedEventArgs e)
        {
            if (!_form_loaded) return;
            bApply.IsEnabled = true;
        }

        private void cEnableAnimation_Click(object sender, RoutedEventArgs e)
        {
            if (!_form_loaded) return;
            bApply.IsEnabled = true;
        }

        private void cEnableQueue_Click(object sender, RoutedEventArgs e)
        {
            if (!_form_loaded) return;
            bApply.IsEnabled = true;
        }

        private void cEnableWaifu2xUpscaling_Click(object sender, RoutedEventArgs e)
        {
            if (!_form_loaded) return;
            bApply.IsEnabled = true;
            if (string.IsNullOrEmpty(tWaifu2xPath.Text) || !File.Exists(tWaifu2xPath.Text))
            {
                var ofd = new System.Windows.Forms.OpenFileDialog();
                ofd.Filter = "可执行文件|*.exe|所有文件|*.*";
                if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    tWaifu2xPath.Text = ofd.FileName;
                    tWaifu2xPath.Foreground = new SolidColorBrush(Colors.Black);
                }
                else
                {
                    cEnableWaifu2xUpscaling.IsChecked = false;
                }
            }
        }

        private void bCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void bApply_Click(object sender, RoutedEventArgs e)
        {
            _apply_changes();
            bApply.IsEnabled = false;
        }
        //比较设置的路径和目前已缓存的路径列表
        private bool _paths_diff(List<PathSetting> path)
        {
            if (path.Count != Settings.Paths.Count) return true;
            for (int i = 0; i < path.Count; i++)
            {
                if (path[i].Directory != Settings.Paths[i].Directory || path[i].IncludingSubDir != Settings.Paths[i].IncludingSubDir)
                    return true;
            }
            return false;
        }

        private void _apply_changes()
        {
            var tmp_path = new List<PathSetting>();
            foreach (var item in _path)
            {
                if (!string.IsNullOrEmpty(item.Directory) && Directory.Exists(item.Directory))
                    tmp_path.Add(item);
            }
            if (_paths_diff(tmp_path))
                Settings.Paths = tmp_path;
            if (Settings.EnableIllustQueue != cEnableBuffering.IsChecked)
                Settings.EnableIllustQueue = (bool)cEnableQueue.IsChecked;
            if (Settings.EnableMultiMonitorDifferentWallpaper != cEnableDiffWallpaper.IsChecked)
                Settings.EnableMultiMonitorDifferentWallpaper = (bool)cEnableDiffWallpaper.IsChecked;
            if (Settings.EnableSlideAnimation != cEnableAnimation.IsChecked)
                Settings.EnableSlideAnimation = (bool)cEnableAnimation.IsChecked;
            if (!string.IsNullOrEmpty(tWaifu2xPath.Text) && File.Exists(tWaifu2xPath.Text))
                Settings.Waifu2xPath = tWaifu2xPath.Text;
            if (Settings.EnableWaifu2xUpscaling != cEnableWaifu2xUpscaling.IsChecked)
                Settings.EnableWaifu2xUpscaling = (bool)cEnableWaifu2xUpscaling.IsChecked;
            int result;
            if (!string.IsNullOrEmpty(tChangeTime.Text) && int.TryParse(tChangeTime.Text, out result) && result > 0 && result != Settings.WallpaperChangeTime)
                Settings.WallpaperChangeTime = result;
            double result2;
            if (!string.IsNullOrEmpty(tWaifu2xScaleThreshold.Text) && double.TryParse(tWaifu2xScaleThreshold.Text, out result2) && result2 > 0 && result2 != Settings.Waifu2xUpscaleThreshold)
                Settings.Waifu2xUpscaleThreshold = result2;

            //最后修改这个值，不然会有其他约束
            if (Settings.EnableBuffering != cEnableBuffering.IsChecked)
                Settings.EnableBuffering = (bool)cEnableBuffering.IsChecked;
        }

        private void bConfirm_Click(object sender, RoutedEventArgs e)
        {
            _apply_changes();
            Close();
        }

        private void RemoveSelectedPath_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < PathLayer.Children.Count; i += 3)
            {
                var @checked = (bool)((CheckBox)PathLayer.Children[i]).IsChecked;
                if (@checked)
                {
                    PathLayer.Children.RemoveAt(i);
                    PathLayer.Children.RemoveAt(i);
                    PathLayer.Children.RemoveAt(i);
                    PathLayer.RowDefinitions.RemoveAt(i / 3);
                    _path.RemoveAt(i / 3);
                    i -= 3;
                    continue;
                }
            }
            //updating tag and names
            for (int i = 0; i < PathLayer.Children.Count; i += 3)
            {
                var index = (i / 3);
                ((CheckBox)PathLayer.Children[i]).Tag = index;
                ((CheckBox)PathLayer.Children[i]).Name = "ctl_0_" + index;
                ((Label)PathLayer.Children[i + 1]).Tag = index;
                ((Label)PathLayer.Children[i + 1]).Name = "ctl_1_" + index;
                ((CheckBox)PathLayer.Children[i + 2]).Tag = index;
                ((CheckBox)PathLayer.Children[i + 2]).Name = "ctl_2_" + index;
            }
        }

        private void AddPath_Click(object sender, RoutedEventArgs e)
        {
            var fbd = new System.Windows.Forms.FolderBrowserDialog();
            fbd.RootFolder = Environment.SpecialFolder.MyComputer;
            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var i = _path.Count;

                var cbselect = new CheckBox();
                cbselect.Tag = i;
                cbselect.Name = "ctl_0_" + i;
                cbselect.HorizontalAlignment = HorizontalAlignment.Center;
                cbselect.VerticalAlignment = VerticalAlignment.Center;
                cbselect.Click += cbselect_Click;

                var lblpath = new Label();
                lblpath.Tag = i;
                lblpath.Name = "ctl_1_" + i;
                lblpath.Content = fbd.SelectedPath;
                lblpath.HorizontalAlignment = HorizontalAlignment.Left;
                lblpath.VerticalAlignment = VerticalAlignment.Center;
                lblpath.MouseDoubleClick += lblpath_MouseDoubleClick;

                var cbincludedir = new CheckBox();
                cbincludedir.IsChecked = true;
                cbincludedir.Tag = i;
                cbincludedir.Name = "ctl_2_" + i;
                cbincludedir.HorizontalAlignment = HorizontalAlignment.Center;
                cbincludedir.VerticalAlignment = VerticalAlignment.Center;
                cbincludedir.Click += cbincludedir_Click;

                var row = new RowDefinition();
                row.Height = new GridLength(25);
                PathLayer.RowDefinitions.Add(row);

                PathLayer.Children.Add(cbselect);
                Grid.SetRow(cbselect, i);
                Grid.SetColumn(cbselect, 0);
                PathLayer.Children.Add(lblpath);
                Grid.SetRow(lblpath, i);
                Grid.SetColumn(lblpath, 1);
                PathLayer.Children.Add(cbincludedir);
                Grid.SetRow(cbincludedir, i);
                Grid.SetColumn(cbincludedir, 2);

                _path.Add(new PathSetting { Directory = fbd.SelectedPath, IncludingSubDir = true });
                bApply.IsEnabled = true;
            }
        }

        private void RemoveAllPaths_Click(object sender, RoutedEventArgs e)
        {
            PathLayer.Children.Clear();
            PathLayer.RowDefinitions.Clear();
            _path.Clear();
            bApply.IsEnabled = true;
        }

        private void tWaifu2xScaleThreshold_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_form_loaded) return;
            double result;
            if (!string.IsNullOrEmpty(tWaifu2xScaleThreshold.Text) && double.TryParse(tWaifu2xScaleThreshold.Text, out result))
                tWaifu2xScaleThreshold.Foreground = new SolidColorBrush(Colors.Black);
            else
                tWaifu2xScaleThreshold.Foreground = new SolidColorBrush(Colors.Red);
        }

        private void tWaifu2xScaleThreshold_KeyUp(object sender, KeyEventArgs e)
        {
            bApply.IsEnabled = true;
        }
    }
}
