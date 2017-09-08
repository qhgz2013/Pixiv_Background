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
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Pixiv_Background_Form
{
    /// <summary>
    /// frmDetailed.xaml 的交互逻辑
    /// </summary>
    public partial class frmDetailed : Window
    {
        private Illust _illust;
        private User _user;
        private System.Drawing.Image _image;
        public frmDetailed(Illust illustInfo, User userInfo, string imagePath)
        {
            InitializeComponent();
            _illust = illustInfo;
            _user = userInfo;
            var fs = File.OpenRead(imagePath);
            var buffer = util.ReadBytes(fs, (int)fs.Length);
            fs.Close();
            var ms = new MemoryStream(buffer);
            ms.Seek(0, SeekOrigin.Begin);
            var imgsrc = new BitmapImage();
            imgsrc.BeginInit();
            imgsrc.StreamSource = ms;
            imgsrc.EndInit();
            WpfAnimatedGif.ImageBehavior.SetAnimatedSource(iSourceImage, imgsrc);

            _set_illust_info();
            _set_user_info();
        }

        public event RoutedEventHandler TagClicked;
        private TextBlock _create_tag_hyperlink(string[] tags)
        {
            var ret_tb = new TextBlock();
            ret_tb.TextWrapping = TextWrapping.Wrap;
            foreach (var tag in tags)
            {
                var hl = new Hyperlink();
                hl.Inlines.Add(tag);
                hl.Foreground = new SolidColorBrush((Color)FindResource("HighlightColor"));
                hl.MouseEnter += delegate
                {
                    var brush = new SolidColorBrush((Color)FindResource("HighlightColor"));
                    var da = new ColorAnimation(Colors.Orange, TimeSpan.FromMilliseconds(300));
                    hl.Foreground = brush;
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, da);
                };
                hl.MouseLeave += delegate
                {
                    var brush = new SolidColorBrush(Colors.Orange);
                    var da = new ColorAnimation((Color)FindResource("HighlightColor"), TimeSpan.FromMilliseconds(300));
                    hl.Foreground = brush;
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, da);
                };
                hl.Click += (_sender, _e) =>
                {
                    TagClicked?.Invoke(_sender, _e);
                };
                hl.TextDecorations = null;

                ret_tb.Inlines.Add(hl);
                var tb = new TextBlock();
                tb.Inlines.Add(",");
                tb.Foreground = new SolidColorBrush((Color)FindResource("MyGrayColor"));
                tb.Padding = new Thickness(3, 0, 3, 0);
                ret_tb.Inlines.Add(tb);
            }
            ret_tb.Inlines.Remove(ret_tb.Inlines.LastInline);
            return ret_tb;
        }

        private string _get_status_code(int code)
        {
            if (code > 0) return ((System.Net.HttpStatusCode)code).ToString();
            switch (code)
            {
                case 0:
                    return "queueing";
                case -1:
                    return "fetching";
                case -2:
                    return "network error";
                default:
                    return "unknown";
            }
        }
        private void _set_illust_info()
        {
            var tbtitle = new TextBlock();
            tbtitle.Inlines.Add(_illust.Title);
            tbtitle.TextWrapping = TextWrapping.WrapWithOverflow;
            lTitle.Content = tbtitle;
            var html_data = html_parser.parseHTML(_illust.Description);
            html_data.Width = lDescription.Width;
            lDescription.Content = html_data;
            lTag.Content = _create_tag_hyperlink(_illust.Tag.Split(','));
            var sb = new StringBuilder();
            sb.AppendFormat("ID: {0}\r\nHTTP状态码: {1} ({2})\r\n数据源: {3} ({4})\r\n", _illust.ID, _illust.HTTP_Status, _get_status_code(_illust.HTTP_Status), (int)_illust.Origin, _illust.Origin.ToString());
            sb.AppendFormat("投稿时间: {0} ({1})\r\n", _illust.Submit_Time, util.FromUnixTimestamp(_illust.Submit_Time).ToString());
            sb.AppendFormat("点击数: {0}\r\n收藏数: {1}\r\n评分次数&分数（旧版）: {2} ({3})\r\n评论数: {4}\r\n", _illust.Click, _illust.Bookmark_Count, _illust.Rate_Count, _illust.Score, _illust.Comment_Count);
            sb.AppendFormat("原始图像大小: {0}×{1}\r\n投稿分P: {2}\r\n绘画工具: {3}\r\n", _illust.Size.Width, _illust.Size.Height, _illust.Page, _illust.Tool);
            sb.AppendFormat("最后更新时间戳: {0} ({1})\r\n最后成功更新时间戳: {2} ({3})", _illust.Last_Update, util.FromUnixTimestamp(_illust.Last_Update).ToString(), _illust.Last_Success_Update, util.FromUnixTimestamp(_illust.Last_Success_Update).ToString());
            var tb = new TextBlock();
            tb.Inlines.Add(sb.ToString());
            tb.TextWrapping = TextWrapping.WrapWithOverflow;
            lIllustStat.Content = tb;
        }

        private void _set_user_info()
        {
            var tbname = new TextBlock();
            tbname.Inlines.Add(_user.Name);
            tbname.TextWrapping = TextWrapping.WrapWithOverflow;
            lUserName.Content = tbname;
            var tbid = new TextBlock();
            tbid.Inlines.Add("ID: " + _user.ID);
            tbid.TextWrapping = TextWrapping.WrapWithOverflow;
            lUserID.Content = tbid;
            var html_data = html_parser.parseHTML(_user.Description);
            html_data.Width = lUserDescription.Width;
            lUserDescription.Content = html_data;
            var sb = new StringBuilder();
            sb.AppendFormat("HTTP状态码: {0} ({1})\r\n", _user.HTTP_Status, _get_status_code(_user.HTTP_Status));
            sb.AppendFormat("关注着的画师: {0}\r\n关注者: {1}\r\n好P友: {2}\r\n", _user.Follow_Users, _user.Follower, _user.Mypixiv_Users);
            sb.AppendFormat("已投稿的插画: {0}\r\n已投稿的小说: {1}\r\n公开收藏数: {2}\r\n", _user.Total_Illusts, _user.Total_Novels, _user.Illust_Bookmark_Public);
            sb.AppendFormat("性别: {2}\r\n生日: {0}\r\n地址: {1}\r\n职业: {2}\r\n", _user.Birthday, _user.Address, _user.Gender, _user.Job);
            sb.AppendFormat("Twitter: {0}\r\n主页: {1}\r\n个人标签: {2}\r\n", _user.Twitter, _user.Home_Page, _user.Personal_Tag);
            sb.AppendFormat("最后更新时间戳: {0} ({1})\r\n最后成功更新时间戳: {2} ({3})", _user.Last_Update, util.FromUnixTimestamp(_user.Last_Update).ToString(), _user.Last_Success_Update, util.FromUnixTimestamp(_user.Last_Success_Update).ToString());
            var tb = new TextBlock();
            tb.Inlines.Add(sb.ToString());
            tb.TextWrapping = TextWrapping.WrapWithOverflow;
            lUserStat.Content = tb;
            var ms = new MemoryStream();
            if (_user.User_Face != null)
            {
                _user.User_Face.Save(ms, _user.User_Face.RawFormat);
                ms.Seek(0, SeekOrigin.Begin);
                var imgsrc = new BitmapImage();
                imgsrc.BeginInit();
                imgsrc.StreamSource = ms;
                imgsrc.EndInit();
                WpfAnimatedGif.ImageBehavior.SetAnimatedSource(iUserImage, imgsrc);
            }
        }
    }
}
