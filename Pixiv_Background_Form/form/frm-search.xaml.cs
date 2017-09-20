using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Pixiv_Background_Form
{
    /// <summary>
    /// frmSearch.xaml 的交互逻辑
    /// </summary>
    public partial class frmSearch : Window
    {
        #region static module (single instantiation)
        private static frmSearch _single_inst = null;
        /// <summary>
        /// 实例化搜索窗口（仅允许单实例化）
        /// </summary>
        /// <param name="pathData"></param>
        /// <param name="sqlData"></param>
        public static void Instantiate(Dictionary<IllustKey, string> pathData, DataStorage sqlData)
        {
            lock (_instantiate_lock)
            {
                if (_single_inst != null) return;
                //STA thread
                var tmp_thd = new Thread(new ThreadStart(delegate
                {
                    Tracer.GlobalTracer.TraceInfo("instantiating frmSearching");
                    _single_inst = new frmSearch(pathData, sqlData);
                    _single_inst.Closed += (_sender, _e) => { _single_inst.Dispatcher.InvokeShutdown(); };
                    _single_inst.Closing += (_sender, _e) => 
                    {
                        _e.Cancel = true;
                        _single_inst.Hide();
                        _single_inst._history.Clear();
                        _single_inst._current_history_index = 0;
                        _single_inst._update_ui();
                    }; //closing override
                    System.Windows.Threading.Dispatcher.Run();
                }));
                tmp_thd.SetApartmentState(ApartmentState.STA);
                tmp_thd.IsBackground = true;
                tmp_thd.Start();
            }
        }
        private static object _instantiate_lock = new object();
        public static frmSearch SingleInstantiation { get { return _single_inst; } }
        #endregion

        private DataStorage _sqldata;
        private Dictionary<IllustKey, string> _pathdata;
        private object _extern_lock = new object();
        protected frmSearch(Dictionary<IllustKey, string> pathData, DataStorage sqlData)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            InitializeComponent();
            _pathdata = pathData;
            _sqldata = sqlData;
            cSearchType.SelectedIndex = 0;
            tSearchString.Focus();
            _history = new List<KeyValuePair<int, string>>();
        }

        private IllustKey[] _tokey(Illust[] data)
        {
            var ret = new IllustKey[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                ret[i] = new IllustKey { id = data[i].ID, page = 0 };
            }
            return ret;
        }

        //当前要显示的IllustKey数组
        private IllustKey[] _cached_illustKeys;
        private Illust[] _cached_illusts;
        //当前要显示的User数组
        private User[] _cached_users;
        //开始读取的偏移量
        private int _data_offset;
        //每次读取的数据量
        private const int DEFAULT_FETCH_COUNT = 100;

        #region slice search
        private bool _queue_history = true;
        //输入回调
        private void tSearchString_KeyUp(object sender, KeyEventArgs e)
        {
            if (string.IsNullOrEmpty(tSearchString.Text)) return;
            if (e.Key == Key.Enter)
            {
                e.Handled = true;

                var checked_data = cSearchType.SelectedIndex;
                _cached_illusts = null;
                _cached_users = null;
                _data_offset = 0;
                scrollViewer.ScrollToTop();

                lDataPanel.Children.Clear();

                switch (checked_data)
                {
                    case 0:
                        //投稿ID
                        _cached_illusts = _sqldata.GetIllustByFuzzyID(tSearchString.Text);
                        break;
                    case 1:
                        //投稿标题
                        _cached_illusts = _sqldata.GetIllustByTitle(tSearchString.Text);
                        break;
                    case 2:
                        //投稿Tag
                        _cached_illusts = _sqldata.GetIllustByTag(tSearchString.Text);
                        break;
                    case 3:
                        //投稿作者名称
                        _cached_illusts = _sqldata.GetIllustByAuthorName(tSearchString.Text);
                        break;
                    case 4:
                        //用户ID
                        _cached_users = _sqldata.GetUserByFuzzyID(tSearchString.Text);
                        break;
                    case 5:
                        //用户名称
                        _cached_users = _sqldata.GetUserByName(tSearchString.Text);
                        break;
                    default:
                        break;
                }

                if (_cached_illusts != null && _cached_illusts.Length > 0)
                {
                    var existed_image = new List<IllustKey>();
                    var data_key = _tokey(_cached_illusts);
                    var temp_lock = new object();
                    Parallel.ForEach(_pathdata, item =>
                    {
                        var find = _cached_illusts.FirstOrDefault(o => o.ID == item.Key.id);
                        if (find.ID != 0)
                        {
                            lock (temp_lock)
                                existed_image.Add(item.Key);
                        }
                    });
                    _cached_illustKeys = existed_image.ToArray();

                    _show_data_illust();
                }
                else if (_cached_users != null && _cached_users.Length > 0)
                {
                    _show_data_user();
                }
                else
                {
                    _show_not_found();
                }
                if (_queue_history) _append_history(cSearchType.SelectedIndex, tSearchString.Text);
            }
        }

        //输出没有找到结果
        private void _show_not_found()
        {
            lDataPanel.Visibility = Visibility.Hidden;
            lNotFound.Visibility = Visibility.Visible;
        }

        private bool _background_working = false;
        //输出投稿数据
        private void _show_data_illust()
        {
            if (_background_working) return;
            _background_working = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Dispatcher.Invoke(new ThreadStart(delegate
                {
                    lDataPanel.Visibility = Visibility.Visible;
                    lNotFound.Visibility = Visibility.Hidden;
                }));
                var from = _data_offset;
                var to = Math.Min(_data_offset + DEFAULT_FETCH_COUNT, _cached_illustKeys.Length);

                if (from == to) { _background_working = false; return; }

                var thumbnail = new System.Drawing.Image[to - from];

                var illusts = new Illust[to - from];
                var users = new Dictionary<uint, User>();

                var user_lock = new object();
                //parallel execution
                var future = Parallel.For(from, to, i =>
                //for (int i = from; i < to; i++)
                {
                    var item = _cached_illustKeys[i];

                    var path = _pathdata[item];
                    Stream img = File.OpenRead(path);
                    var bytes = util.ReadBytes(img, (int)img.Length);
                    img.Close();
                    img = new MemoryStream(bytes);
                    img.Seek(0, SeekOrigin.Begin);
                    var imgdata = System.Drawing.Image.FromStream(img);

                    //rendering thumbnail
                    var ratio = 200.0 / imgdata.Height * ScreenWatcher.Scale;
                    var width = (int)(imgdata.Width * ratio);
                    var thumb = new System.Drawing.Bitmap(width, (int)(200 * ScreenWatcher.Scale));
                    var gr = System.Drawing.Graphics.FromImage(thumb);
                    gr.DrawImage(imgdata, new System.Drawing.Rectangle(new System.Drawing.Point(), thumb.Size), new System.Drawing.Rectangle(new System.Drawing.Point(), imgdata.Size), System.Drawing.GraphicsUnit.Pixel);

                    gr.Dispose();
                    imgdata.Dispose();

                    thumbnail[i - from] = thumb;
                    illusts[i - from] = _cached_illusts.First(o => o.ID == _cached_illustKeys[i].id);

                    lock (user_lock)
                    {
                        if (!users.ContainsKey(illusts[i - from].Author_ID))
                            users.Add(illusts[i - from].Author_ID, _sqldata.GetUserInfo(illusts[i - from].Author_ID, DataUpdateMode.No_Update));
                    }
                });

                for (int i = 0; i < thumbnail.Length; i++)
                {
                    if ((i % 10) == 0) Thread.Sleep(100);
                    Dispatcher.Invoke(new ThreadStart(delegate
                    {
                        var ui = new PanelItem(thumbnail[i], System.Net.WebUtility.HtmlDecode(illusts[i].Title), System.Net.WebUtility.HtmlDecode(users[illusts[i].Author_ID].Name), true, true);
                        var tag = new _temp_struct { illust = illusts[i], user = users[illusts[i].Author_ID], path = _pathdata[_cached_illustKeys[i + from]] };
                        ui.Tag = tag;
                        ui.SourceImageClick += _on_illust_image_clicked;
                        ui.TitleClick += _on_illust_title_clicked;
                        ui.DescriptionClick += _on_illust_user_clicked;
                        lDataPanel.Children.Add(ui);
                    }));

                }

                _data_offset = to;
                if (to == _cached_illustKeys.Length)
                    Dispatcher.Invoke(new ThreadStart(delegate
                    {
                        _add_bottom_control();
                    }));

                _background_working = false;
            });
        }

        private struct _temp_struct
        {
            public Illust illust;
            public User user;
            public string path;
        }
        //输出用户数据
        private void _show_data_user()
        {
            if (_background_working) return;
            _background_working = true;

            ThreadPool.QueueUserWorkItem(delegate
            {
                Dispatcher.Invoke(new ThreadStart(delegate
                {
                    lDataPanel.Visibility = Visibility.Visible;
                    lNotFound.Visibility = Visibility.Hidden;
                }));
                var from = _data_offset;
                var to = Math.Min(_cached_users.Length, _data_offset + DEFAULT_FETCH_COUNT);
                if (from == to) { _background_working = false; return; }
                for (int i = from; i < to; i++)
                {
                    var item = _cached_users[i];
                    var face = item.User_Face;
                    if (face == null) face = new System.Drawing.Bitmap(1, 1);

                    if ((i % 10) == 0) Thread.Sleep(100);
                    Dispatcher.Invoke(new ThreadStart(delegate
                    {
                        var ui = new PanelItem(face, System.Net.WebUtility.HtmlDecode(item.Name), null, true, false, 100);
                        ui.Tag = item;
                        ui.SourceImageClick += _on_user_image_clicked;
                        ui.DescriptionClick += _on_user_label_clicked;
                        ui.TitleClick += _on_user_label_clicked;
                        lDataPanel.Children.Add(ui);
                    }));
                }
                _data_offset = to;
                if (to == _cached_users.Length)
                    Dispatcher.Invoke(new ThreadStart(delegate
                    {
                        _add_bottom_control();
                    }));

                _background_working = false;
            });
        }

        private void _add_bottom_control()
        {
            var grid = new Grid();
            grid.Width = lDataPanel.ActualWidth;
            grid.Background = new LinearGradientBrush(Colors.White, Colors.LightGray, 90);
            var lbl = new Label();
            lbl.Content = "(我还是有底线的)";
            lbl.HorizontalAlignment = HorizontalAlignment.Center;
            grid.Children.Add(lbl);
            lDataPanel.Children.Add(grid);

            var rel_src = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(WrapPanel), 1);
            var binding = new Binding();
            binding.RelativeSource = rel_src;
            binding.Path = new PropertyPath("ActualWidth");
            grid.SetBinding(WidthProperty, binding);
        }
        #endregion

        #region UI interact
        //点击用户信息
        private void _on_user_image_clicked(object sender, MouseEventArgs e)
        {
            var taginfo = (User)((PanelItem)((Grid)((Image)sender).Parent).Parent).Tag;
            cSearchType.SelectedIndex = 3;
            tSearchString.Text = taginfo.Name.ToString();

            KeyEventArgs ke = new KeyEventArgs(Keyboard.PrimaryDevice, Keyboard.PrimaryDevice.ActiveSource, 0, Key.Enter) { RoutedEvent = KeyUpEvent };
            tSearchString_KeyUp(tSearchString, ke);
        }
        private void _on_user_label_clicked(object sender, MouseEventArgs e)
        {
            var taginfo = (User)((PanelItem)((Grid)((Label)sender).Parent).Parent).Tag;
            cSearchType.SelectedIndex = 3;
            tSearchString.Text = taginfo.Name.ToString();

            KeyEventArgs ke = new KeyEventArgs(Keyboard.PrimaryDevice, Keyboard.PrimaryDevice.ActiveSource, 0, Key.Enter) { RoutedEvent = KeyUpEvent };
            tSearchString_KeyUp(tSearchString, ke);
        }
        private void _on_illust_image_clicked(object sender, EventArgs e)
        {
            var taginfo = (_temp_struct)((PanelItem)((Grid)((Image)sender).Parent).Parent).Tag;
            var detail_ui = new frmDetailed(taginfo.illust, taginfo.user, taginfo.path);
            detail_ui.Show();
        }
        //在投稿里点击标题
        private void _on_illust_title_clicked(object sender, EventArgs e)
        {
            var taginfo = (_temp_struct)((PanelItem)((Grid)((Label)sender).Parent).Parent).Tag;
            var detail_ui = new frmDetailed(taginfo.illust, taginfo.user, taginfo.path);
            detail_ui.Show();
        }
        //在投稿里点击用户名称
        private void _on_illust_user_clicked(object sender, EventArgs e)
        {
            var taginfo = (_temp_struct)((PanelItem)((Grid)((Label)sender).Parent).Parent).Tag;
            cSearchType.SelectedIndex = 3;
            tSearchString.Text = taginfo.user.Name.ToString();

            KeyEventArgs ke = new KeyEventArgs(Keyboard.PrimaryDevice, Keyboard.PrimaryDevice.ActiveSource, 0, Key.Enter) { RoutedEvent = KeyUpEvent };
            tSearchString_KeyUp(tSearchString, ke);
        }
        //在详细信息里点击到Tag
        private void _on_info_tag_clicked(object sender, RoutedEventArgs e)
        {
            cSearchType.SelectedIndex = 2;
            tSearchString.Text = ((Run)((Hyperlink)sender).Inlines.FirstInline).Text;
            Focus();
            KeyEventArgs ke = new KeyEventArgs(Keyboard.PrimaryDevice, Keyboard.PrimaryDevice.ActiveSource, 0, Key.Enter) { RoutedEvent = KeyUpEvent };
            tSearchString_KeyUp(tSearchString, ke);
        }
        //移动到底部触发更新事件
        private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            double trigger_offset = scrollViewer.ActualHeight * 3;
            if (Math.Abs(scrollViewer.VerticalOffset - scrollViewer.ScrollableHeight) < trigger_offset)
            {
                if (_cached_illusts != null && _cached_illusts.Length > 0)
                    _show_data_illust();
                else if (_cached_users != null && _cached_users.Length > 0)
                    _show_data_user();
            }
        }

        private void Window_Unloaded(object sender, RoutedEventArgs e)
        {
        }
        #endregion

        #region history
        private int _current_history_index;
        private List<KeyValuePair<int, string>> _history;
        private void _move_last_history()
        {
            if (_current_history_index <= 0 || _background_working) return;
            var data = _history[--_current_history_index];
            cSearchType.SelectedIndex = data.Key;
            tSearchString.Text = data.Value;
            KeyEventArgs ke = new KeyEventArgs(Keyboard.PrimaryDevice, new HwndSource(0, 0, 0, 0, 0, "", IntPtr.Zero), 0, Key.Enter) { RoutedEvent = KeyUpEvent };
            _queue_history = false;
            tSearchString_KeyUp(tSearchString, ke);
            _queue_history = true;
            _update_ui();
        }
        private void _move_next_history()
        {
            if (_current_history_index >= _history.Count - 1 || _background_working) return;
            var data = _history[++_current_history_index];
            cSearchType.SelectedIndex = data.Key;
            tSearchString.Text = data.Value;
            KeyEventArgs ke = new KeyEventArgs(Keyboard.PrimaryDevice, new HwndSource(0, 0, 0, 0, 0, "", IntPtr.Zero), 0, Key.Enter) { RoutedEvent = KeyUpEvent };
            _queue_history = false;
            tSearchString_KeyUp(tSearchString, ke);
            _queue_history = true;
            _update_ui();
        }
        private void _update_ui()
        {
            bPrev.IsEnabled = _can_move_last();
            if (_can_move_last()) bPrev.ToolTip = "[" + ((ComboBoxItem)cSearchType.Items[_history[_current_history_index - 1].Key]).Content.ToString() + "]: " + _history[_current_history_index - 1].Value;
            else bPrev.ToolTip = "";
            bNext.IsEnabled = _can_move_next();
            if (_can_move_next()) bNext.ToolTip = "[" + ((ComboBoxItem)cSearchType.Items[_history[_current_history_index + 1].Key]).Content.ToString() + "]: " + _history[_current_history_index + 1].Value;
            else bNext.ToolTip = "";

            Tracer.GlobalTracer.TraceInfo(string.Format("updating searching history: count = {0}, index = {1}", _history.Count, _current_history_index));
        }
        private bool _can_move_last()
        {
            return _current_history_index > 0;
        }
        private bool _can_move_next()
        {
            return _current_history_index < _history.Count - 1;
        }
        private void _append_history(int mode, string str)
        {
            while (_history.Count > _current_history_index + 1)
                _history.RemoveAt(_current_history_index + 1);
            _history.Add(new KeyValuePair<int, string>(mode, str));
            _current_history_index = _history.Count - 1;
            _update_ui();
        }
        #endregion

        /// <summary>
        /// 搜索特定类型
        /// </summary>
        /// <param name="type">类型序号</param>
        /// <param name="str">搜索的字符串</param>
        public void Search(int type, string str)
        {
            lock (_extern_lock)
            {
                if (type < 0 || type > 5 || string.IsNullOrEmpty(str)) return;
                cSearchType.SelectedIndex = type;
                tSearchString.Text = str;

                KeyEventArgs ke = new KeyEventArgs(Keyboard.PrimaryDevice, new HwndSource(0, 0, 0, 0, 0, "", IntPtr.Zero), 0, Key.Enter) { RoutedEvent = KeyUpEvent };
                tSearchString_KeyUp(tSearchString, ke);
                //_append_history(type, str);
            }
        }

        private void bPrev_Click(object sender, RoutedEventArgs e)
        {
            _move_last_history();
        }

        private void bNext_Click(object sender, RoutedEventArgs e)
        {
            _move_next_history();
        }
    }
}
