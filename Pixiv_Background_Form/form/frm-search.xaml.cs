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
        private class _eql_temp : IEqualityComparer<IllustKey>
        {
            public bool Equals(IllustKey x, IllustKey y)
            {
                return x.id == y.id;
            }

            public int GetHashCode(IllustKey obj)
            {
                return base.GetHashCode();
            }
        }
        private void tSearchString_KeyUp(object sender, KeyEventArgs e)
        {
            if (string.IsNullOrEmpty(tSearchString.Text)) return;
            if (e.Key == Key.Enter)
            {
                e.Handled = true;

                var checked_data = cSearchType.SelectedIndex;
                Illust[] data1 = null;
                User[] data2 = null;
                switch (checked_data)
                {
                    case 0:
                        //投稿ID
                        data1 = _sqldata.GetIllustByFuzzyID(tSearchString.Text);
                        break;
                    case 1:
                        //投稿标题
                        data1 = _sqldata.GetIllustByTitle(tSearchString.Text);
                        break;
                    case 2:
                        //投稿Tag
                        data1 = _sqldata.GetIllustByTag(tSearchString.Text);
                        break;
                    case 3:
                        //投稿作者名称
                        data1 = _sqldata.GetIllustByAuthorName(tSearchString.Text);
                        break;
                    case 4:
                        //用户ID
                        data2 = _sqldata.GetUserByFuzzyID(tSearchString.Text);
                        break;
                    case 5:
                        //用户名称
                        data2 = _sqldata.GetUserByName(tSearchString.Text);
                        break;
                    default:
                        break;
                }

                if (data1 != null && data1.Length > 0)
                    _show_data(data1);
                else if (data2 != null && data2.Length > 0)
                    _show_data(data2);
                else
                    _show_not_found();
            }
        }

        private void _show_not_found()
        {
            lDataPanel.Visibility = Visibility.Hidden;
            lNotFound.Visibility = Visibility.Visible;
        }
        private void _show_data(Illust[] data)
        {
            lDataPanel.Visibility = Visibility.Visible;
            lNotFound.Visibility = Visibility.Hidden;
            var existed_image_paths = new List<IllustKey>(_pathdata.Keys.Intersect(_tokey(data), new _eql_temp()));
            var thumbnail = new System.Drawing.Image[existed_image_paths.Count];

            var illusts = new Illust[existed_image_paths.Count];
            var users = new Dictionary<uint, User>();

            var user_lock = new object();
            //parallel execution
            var future = Parallel.For(0, existed_image_paths.Count, i =>
            {
                var item = existed_image_paths[i];

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

                thumbnail[i] = thumb;
                illusts[i] = data.First(o => o.ID == existed_image_paths[i].id);

                lock (user_lock)
                {
                    if (!users.ContainsKey(illusts[i].Author_ID))
                        users.Add(illusts[i].Author_ID, _sqldata.GetUserInfo(illusts[i].Author_ID, DataUpdateMode.No_Update));
                }
            });

            lDataPanel.Children.Clear();
            for (int i = 0; i < thumbnail.Length; i++)
            {
                var ui = new PanelItem(thumbnail[i], illusts[i].Title, users[illusts[i].Author_ID].Name, true, true);
                var tag = new _temp_struct { illust = illusts[i], user = users[illusts[i].Author_ID], path = _pathdata[existed_image_paths[i]] };
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
        }

        private struct _temp_struct
        {
            public Illust illust;
            public User user;
            public string path;
        }
        private void _show_data(User[] data)
        {
            lDataPanel.Visibility = Visibility.Visible;
            lNotFound.Visibility = Visibility.Hidden;
            lDataPanel.Children.Clear();
            foreach (var item in data)
            {
                var ui = new PanelItem(item.User_Face, item.Name, null, true, false);
                ui.Tag = item;
                ui.SourceImageClick += _on_user_clicked;
                ui.DescriptionClick += _on_user_clicked;
                ui.TitleClick += _on_user_clicked;
                lDataPanel.Children.Add(ui);
            }
        }

        private void _on_user_clicked(object sender, MouseEventArgs e)
        {
            var taginfo = (User)((PanelItem)((Grid)((Image)sender).Parent).Parent).Tag;
            cSearchType.SelectedIndex = 3;
            tSearchString.Text = taginfo.Name.ToString();

            KeyEventArgs ke = new KeyEventArgs(Keyboard.PrimaryDevice, Keyboard.PrimaryDevice.ActiveSource, 0, Key.Enter) { RoutedEvent = KeyUpEvent };
            tSearchString_KeyUp(tSearchString, ke);
        }

        private void _on_illust_title_clicked(object sender, EventArgs e)
        {
            var taginfo = (_temp_struct)((PanelItem)((Grid)((Image)sender).Parent).Parent).Tag;
            cSearchType.SelectedIndex = 1;
            tSearchString.Text = taginfo.illust.Title.ToString();

            KeyEventArgs ke = new KeyEventArgs(Keyboard.PrimaryDevice, Keyboard.PrimaryDevice.ActiveSource, 0, Key.Enter) { RoutedEvent = KeyUpEvent };
            tSearchString_KeyUp(tSearchString, ke);
        }

        private void _on_illust_user_clicked(object sender, EventArgs e)
        {
            var taginfo = (_temp_struct)((PanelItem)((Grid)((Label)sender).Parent).Parent).Tag;
            cSearchType.SelectedIndex = 3;
            tSearchString.Text = taginfo.user.Name.ToString();

            KeyEventArgs ke = new KeyEventArgs(Keyboard.PrimaryDevice, Keyboard.PrimaryDevice.ActiveSource, 0, Key.Enter) { RoutedEvent = KeyUpEvent };
            tSearchString_KeyUp(tSearchString, ke);
        }
        private void _on_info_tag_clicked(object sender, RoutedEventArgs e)
        {
            cSearchType.SelectedIndex = 2;
            tSearchString.Text = ((Run)((Hyperlink) sender).Inlines.FirstInline).Text;
            Focus();
            KeyEventArgs ke = new KeyEventArgs(Keyboard.PrimaryDevice, Keyboard.PrimaryDevice.ActiveSource, 0, Key.Enter) { RoutedEvent = KeyUpEvent };
            tSearchString_KeyUp(tSearchString, ke);
        }
    }
}
