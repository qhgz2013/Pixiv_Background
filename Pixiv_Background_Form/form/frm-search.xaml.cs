using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    /// frmSearch.xaml 的交互逻辑
    /// </summary>
    public partial class frmSearch : Window
    {
        private DataStorage _sqldata;
        private Dictionary<IllustKey, string> _pathdata;
        public frmSearch(Dictionary<IllustKey, string> pathData, DataStorage sqlData)
        {
            InitializeComponent();
            _pathdata = pathData;
            _sqldata = sqlData;
            cSearchType.SelectedIndex = 0;
            tSearchString.Focus();
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
            }
        }

        //输出没有找到结果
        private void _show_not_found()
        {
            lDataPanel.Visibility = Visibility.Hidden;
            lNotFound.Visibility = Visibility.Visible;
        }
        //输出投稿数据
        private void _show_data_illust()
        {
            lDataPanel.Visibility = Visibility.Visible;
            lNotFound.Visibility = Visibility.Hidden;
            var from = _data_offset;
            var to = Math.Min(_data_offset + DEFAULT_FETCH_COUNT, _cached_illustKeys.Length);

            if (from == to) return;

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
                var ratio = 200.0 / imgdata.Height;
                var width = (int)(imgdata.Width * ratio);
                var thumb = new System.Drawing.Bitmap(width, 200);
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
                var ui = new PanelItem(thumbnail[i], illusts[i].Title, users[illusts[i].Author_ID].Name, true, true);
                var tag = new _temp_struct { illust = illusts[i], user = users[illusts[i].Author_ID], path = _pathdata[_cached_illustKeys[i + from]] };
                ui.Tag = tag;
                ui.SourceImageClick += (sender, e) =>
                {
                    var taginfo = (_temp_struct)((PanelItem)((Grid)((Image)sender).Parent).Parent).Tag;
                    var detail_ui = new frmDetailed(taginfo.illust, taginfo.user, taginfo.path);
                    detail_ui.TagClicked += _on_info_tag_clicked;
                    detail_ui.Show();
                };
                ui.TitleClick += _on_illust_title_clicked;
                ui.DescriptionClick += _on_illust_user_clicked;

                lDataPanel.Children.Add(ui);
            }

            _data_offset = to;
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
            lDataPanel.Visibility = Visibility.Visible;
            lNotFound.Visibility = Visibility.Hidden;
            var from = _data_offset;
            var to = Math.Min(_cached_users.Length, _data_offset + DEFAULT_FETCH_COUNT);
            if (from == to) return;
            for (int i = from; i < to; i++)
            {
                var item = _cached_users[i];
                var face = item.User_Face;
                if (face == null) face = new System.Drawing.Bitmap(1, 1);
                var ui = new PanelItem(face, item.Name, null, true, false);
                ui.Tag = item;
                ui.SourceImageClick += _on_user_clicked;
                ui.DescriptionClick += _on_user_clicked;
                ui.TitleClick += _on_user_clicked;
                lDataPanel.Children.Add(ui);
            }
            _data_offset = to;
        }
        //点击用户信息
        private void _on_user_clicked(object sender, MouseEventArgs e)
        {
            var taginfo = (User)((PanelItem)((Grid)((Image)sender).Parent).Parent).Tag;
            cSearchType.SelectedIndex = 3;
            tSearchString.Text = taginfo.Name.ToString();

            KeyEventArgs ke = new KeyEventArgs(Keyboard.PrimaryDevice, Keyboard.PrimaryDevice.ActiveSource, 0, Key.Enter) { RoutedEvent = KeyUpEvent };
            tSearchString_KeyUp(tSearchString, ke);
        }
        //在投稿里点击标题
        private void _on_illust_title_clicked(object sender, EventArgs e)
        {
            var taginfo = (_temp_struct)((PanelItem)((Grid)((Label)sender).Parent).Parent).Tag;
            cSearchType.SelectedIndex = 1;
            tSearchString.Text = taginfo.illust.Title.ToString();

            KeyEventArgs ke = new KeyEventArgs(Keyboard.PrimaryDevice, Keyboard.PrimaryDevice.ActiveSource, 0, Key.Enter) { RoutedEvent = KeyUpEvent };
            tSearchString_KeyUp(tSearchString, ke);
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
            if (Math.Abs(scrollViewer.VerticalOffset - scrollViewer.ScrollableHeight) < 0.01)
            {
                if (_cached_illusts != null && _cached_illusts.Length > 0)
                    _show_data_illust();
                else if (_cached_users != null && _cached_users.Length > 0)
                    _show_data_user();
            }
        }
    }
}
