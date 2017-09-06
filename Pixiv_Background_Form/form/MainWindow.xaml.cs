using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.IO;
using System.Threading;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Diagnostics;

namespace Pixiv_Background_Form
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        //窗体的构造函数以及初始化函数
        #region Form Initialize
        public MainWindow()
        {
            InitializeComponent();

            //登陆部分
            _auth = new PixivAuth();
            //API调用部分
            _api = new API(_auth);
            //数据库保存部分
            _database = new DataStorage(_api, true, _auth);
            _background_queue = new Dictionary<IllustKey, string>();

            _load_last_data();
            _update_ui_info();

            _auth.LoginFailed += (str =>
            {
                Dispatcher.Invoke(new ThreadStart(delegate
                {
                    System.Windows.Forms.MessageBox.Show("登陆失败: " + str);
                    Close();
                }));
            });

            if (!_auth.IsLogined)
            {
                var frm_login = new frmLogin();
                frm_login.ShowDialog();
                if (frm_login.canceled)
                {
                    Close();
                    return;
                }
                var user = frm_login.user_name;
                var pass = frm_login.pass_word;
                try { _auth.Login(user, pass); }
                catch { Close(); return; }
            }

            Settings.WallPaperChangeEvent += _on_wallpaper_changed;
            Settings.PathsChanged += _on_paths_changed;
            _database.FetchIllustSucceeded += _on_illust_query_finished;
            _database.FetchIllustFailed += _on_illust_query_finished;
            _database.FetchUserSucceeded += _on_user_query_finished;
            _database.FetchUserFailed += _on_user_query_finished;

            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (var item in Settings.Paths)
                {
                    _load_path(item.Directory, item.IncludingSubDir);
                }
            });
        }
        private bool _frm_created = false;
        private void frmMain_Loaded(object sender, RoutedEventArgs e)
        {
            frmMain.Left = SystemParameters.WorkArea.Width - frmMain.ActualWidth;
            frmMain.Top = 0;
            _frm_created = true;
        }
        private void frmMain_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_database != null) _database.AbortWorkingThread();
        }

        //当前壁纸的信息
        [Serializable]
        public struct _temp_serialize_struct
        {
            public Illust illust;
            public User user;
            public System.Drawing.Size imageSolution;
            public int page;
        }
        private _temp_serialize_struct _last_data;
        private void _load_last_data()
        {
            var filename = "tempBackground.dat";
            if (File.Exists(filename))
            {
                var stream = File.OpenRead(filename);
                var fmt = new BinaryFormatter();

                _last_data = (_temp_serialize_struct)fmt.Deserialize(stream);
                stream.Close();
            }
        }
        private void _save_last_data()
        {
            var filename = "tempBackground.dat";
            var stream = File.OpenWrite(filename);
            try
            {
                var fmt = new BinaryFormatter();
                fmt.Serialize(stream, _last_data);
            }
            finally
            {
                stream.Close();
            }
        }
        #endregion //Form Initialize

        //轮流选出多个照片
        private IllustKey[] _get_key_using_queue(int count)
        {
            if (_background_queue.Count == 0) return new IllustKey[0];
            var queued_illusts = Settings.IllustQueue;
            var ret = new IllustKey[count];
            var unused_list = new List<IllustKey>();
            var itor = _background_queue.Keys.Except(queued_illusts);
            unused_list.AddRange(itor);

            var rnd = new Random();

            for (int i = 0; i < count; i++)
            {
                if (unused_list.Count == 0)
                {
                    queued_illusts.Clear();
                    Settings.IllustQueue = queued_illusts;

                    var data = _get_key_using_queue(count - i);
                    Array.Copy(data, 0, ret, i, data.Length);
                    return ret;
                }
                else
                {
                    var index = rnd.Next(unused_list.Count);
                    ret[i] = unused_list[index];
                    unused_list.RemoveAt(index);
                    queued_illusts.Add(ret[i]);
                }
            }
            Settings.IllustQueue = queued_illusts;
            return ret;
        }
        private System.Drawing.Image _upscaling_using_waifu2x(System.Drawing.Image source, double ratio)
        {
            string img_in_path = Guid.NewGuid().ToString() + ".png";
            string img_out_path = Guid.NewGuid().ToString() + ".png";
            string cache_dir = Path.GetTempPath();

            img_in_path = Path.Combine(cache_dir, img_in_path);
            img_out_path = Path.Combine(cache_dir, img_out_path);

            //fixed ratio floor (2159.99999999999876 --> 2159)
            const double ratio_step = 0.001;
            while (source.Width * ratio < (int)(source.Width * ratio) || source.Height * ratio < (int)(source.Height * ratio))
                ratio += ratio_step;

            try
            {
                source.Save(img_in_path, System.Drawing.Imaging.ImageFormat.Png);
                var plugin = new Waifu2xPlugin(Settings.Waifu2xPath);
                plugin.UpscaleImage(img_in_path, img_out_path, scale_ratio: ratio, noise_level: 2);
                var stream = File.OpenRead(img_out_path);
                var data = util.ReadBytes(stream, (int)stream.Length);
                stream.Close();
                var ms = new MemoryStream(data);
                return System.Drawing.Image.FromStream(ms);
            }
            catch (Exception ex)
            {
                Tracer.GlobalTracer.TraceError(ex);
            }
            finally
            {
                if (File.Exists(img_in_path))
                    File.Delete(img_in_path);
                if (File.Exists(img_out_path))
                    File.Delete(img_out_path);
            }

            return new System.Drawing.Bitmap((int)(source.Width * ratio), (int)(source.Height * ratio));
        }
        private object __on_wallpaper_changed_lock = new object();
        //变换壁纸的回调函数
        private void _on_wallpaper_changed(object sender, EventArgs e)
        {
            lock (__on_wallpaper_changed_lock)
            {
                try
                {
                    if (_background_queue.Count == 0) return;
                    //当前壁纸id和page
                    IllustKey[] cur_wallpaper = null;
                    //要显示的壁纸的数量
                    var screen_count = Settings.EnableMultiMonitorDifferentWallpaper ? System.Windows.Forms.Screen.AllScreens.Length : 1;
                    //轮流展示or直接随机出一张新的壁纸
                    if (Settings.EnableIllustQueue)
                        cur_wallpaper = _get_key_using_queue(screen_count);
                    else
                    {
                        cur_wallpaper = new IllustKey[screen_count];
                        var rnd = new Random();
                        for (int i = 0; i < cur_wallpaper.Length; i++)
                        {
                            cur_wallpaper[i] = _background_queue.Keys.ElementAt(rnd.Next(_background_queue.Keys.Count));
                        }
                    }

                    //加载所需的壁纸到内存中
                    var imgs = new System.Drawing.Image[cur_wallpaper.Length];
                    var ratios = new double[cur_wallpaper.Length];
                    var screens = ScreenWatcher.GetTransformedScreenBoundary();
                    for (int i = 0; i < imgs.Length; i++)
                    {
                        //直接读取到内存中再进行图片加载，避免IO占用or冲突
                        var s1 = File.OpenRead(_background_queue[cur_wallpaper[i]]);
                        var ms = new MemoryStream();
                        var buf = util.ReadBytes(s1, (int)s1.Length);
                        s1.Close();
                        ms.Write(buf, 0, buf.Length);
                        imgs[i] = System.Drawing.Image.FromStream(ms);
                    }

                    //更新信息
                    _last_data.illust = _database.GetIllustInfo(cur_wallpaper[0].id);
                    _last_data.user = _database.GetUserInfo(_last_data.illust.Author_ID);
                    _last_data.imageSolution = imgs[0].Size;
                    _last_data.page = (int)cur_wallpaper[0].page;
                    _save_last_data();

                    //更新当前信息
                    Dispatcher.Invoke(new ThreadStart(delegate
                    {
                        _update_ui_info();
                    }));

                    //开启缓存渲染
                    if (Settings.EnableBuffering)
                    {
                        //缩放壁纸
                        for (int i = 0; i < imgs.Length; i++)
                        {
                            var src_width = imgs[i].Width;
                            var src_height = imgs[i].Height;
                            var dst_width = screens[i].Width;
                            var dst_height = screens[i].Height;

                            ratios[i] = Math.Min(dst_width * 1.0 / src_width, dst_height * 1.0 / src_height);
                            if (Settings.EnableWaifu2xUpscaling && ratios[i] >= Settings.Waifu2xUpscaleThreshold)
                                imgs[i] = _upscaling_using_waifu2x(imgs[i], ratios[i]);
                            else
                            {
                                var new_img = new System.Drawing.Bitmap((int)(imgs[i].Width * ratios[i]), (int)(imgs[i].Height * ratios[i]));
                                var gr1 = System.Drawing.Graphics.FromImage(new_img);

                                gr1.DrawImage(imgs[i], new System.Drawing.Rectangle(0, 0, new_img.Width, new_img.Height), new System.Drawing.Rectangle(new System.Drawing.Point(), imgs[i].Size), System.Drawing.GraphicsUnit.Pixel);
                                imgs[i] = new_img;
                            }

                        }

                        var bmp_size = ScreenWatcher.GetTotalSize();
                        if (bmp_size.Width < 0 || bmp_size.Height < 0) return;
                        var bmp = new System.Drawing.Bitmap(bmp_size.Width, bmp_size.Height);

                        var gr = System.Drawing.Graphics.FromImage(bmp);
                        gr.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        gr.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

                        //多屏壁纸
                        for (int i = 0; i < screens.Length; i++)
                        {
                            var index = Settings.EnableMultiMonitorDifferentWallpaper ? i : 1;
                            var screen_rect = screens[index];
                            var destRect = new System.Drawing.Rectangle(
                                screen_rect.X + (screen_rect.Width - imgs[index].Width) / 2,
                                screen_rect.Y + (screen_rect.Height - imgs[index].Height) / 2,
                                imgs[index].Width,
                                imgs[index].Height
                                );
                            gr.DrawImage(imgs[index],
                                destRect, //destRect
                                new System.Drawing.Rectangle(new System.Drawing.Point(), imgs[index].Size), //srcRect
                                System.Drawing.GraphicsUnit.Pixel);
                        }

                        gr.Dispose();
                        bmp.Save("tempBackground.bmp", System.Drawing.Imaging.ImageFormat.Bmp);
                    }
                    else
                    {
                        //没有开启缓存渲染的话就直接复制到该目录下
                        imgs[0].Save("tempBackground.bmp", System.Drawing.Imaging.ImageFormat.Bmp);

                    }

                    //动画效果
                    if (Settings.EnableSlideAnimation)
                    {
                        Desktop.SetWallpaperUsingActiveDesktop(Path.Combine(System.Environment.CurrentDirectory, "tempBackground.bmp"));
                    }
                    else
                    {
                        Desktop.SetWallpaperUsingSystemParameterInfo(Path.Combine(System.Environment.CurrentDirectory, "tempBackground.bmp"));
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        //异步更新信息的回调函数
        private void _on_illust_query_finished(object sender, FetchIllustEventArgs e)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var ilsinfo = _database.GetIllustInfo(e.ID, DataUpdateMode.No_Update);
                    if (ilsinfo.ID != 0 && ilsinfo.Origin != DataOrigin.SauceNao_API && (ilsinfo.HTTP_Status == 403 || ilsinfo.HTTP_Status == 404))
                    {
                        var key = _background_queue.Keys.FirstOrDefault(o => o.id == ilsinfo.ID);
                        if (key.id != 0)
                        {
                            var path = _background_queue[key];
                            Illust new_illust; User new_user;
                            saucenaoAPI.QueryImage(path, out new_illust, out new_user);

                            if (new_illust.ID != 0)
                            {
                                _database.SetIllustInfo(new_illust);
                                ilsinfo = _database.GetIllustInfo(new_illust.ID);
                            }
                            if (new_user.ID != 0)
                            {
                                var usrinfo = _database.GetUserInfo(new_user.ID);
                                if (_last_data.user.ID == usrinfo.ID)
                                    _last_data.user = usrinfo;
                            }
                        }
                    }
                    if (_last_data.illust.ID == ilsinfo.ID)
                    {
                        _last_data.illust = ilsinfo;
                        Dispatcher.Invoke(new ThreadStart(delegate
                        {
                            _update_ui_info();
                        }));
                    }
                }
                catch (Exception ex)
                {
                    Tracer.GlobalTracer.TraceError(ex);
                }
            });
        }

        private void _on_user_query_finished(object sender, FetchUserEventArgs e)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var usrinfo = _database.GetUserInfo(e.ID, DataUpdateMode.No_Update);
                    _last_data.user = usrinfo;
                    Dispatcher.Invoke(new ThreadStart(delegate
                    {
                        _update_ui_info();
                    }));
                }
                catch (Exception ex)
                {
                    Tracer.GlobalTracer.TraceError(ex);
                }
            });
        }

        private object __on_paths_changed_lock = new object();
        private void _on_paths_changed(object sender, EventArgs e)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                lock (__on_paths_changed_lock)
                {
                    _background_queue.Clear();
                    foreach (var item in Settings.Paths)
                    {
                        _load_path(item.Directory, item.IncludingSubDir);
                    }
                }
            });
        }

        #region Path Loader
        //匹配图片的正则表达式语法，id page必须，ext可选
        private const string _image_ptn = "(?<id>\\d+)_p(?<page>\\d+)\\.(?<ext>[a-zA-Z0-9]+)";
        private void _load_path(string path, bool recursive)
        {
            var dirinfo = new DirectoryInfo(path);
            if (!dirinfo.Exists) return;
            _database.UpdateFileList(path);

            foreach (var item in dirinfo.GetFiles())
            {
                var reg = Regex.Match(item.Name, _image_ptn);
                if (reg.Success)
                {
                    var id = uint.Parse(reg.Result("${id}"));
                    var page = int.Parse(reg.Result("${page}"));
                    var ext = reg.Result("${ext}");

                    var key = new IllustKey { id = id, page = (uint)page };
                    if (!_background_queue.ContainsKey(key))
                        _background_queue.Add(key, item.FullName);
                }
            }

            if (recursive)
            {
                foreach (var item in dirinfo.GetDirectories())
                {
                    _load_path(item.FullName, true);
                }
            }
        }
        #endregion


        //变量定义
        #region Member Definations
        //背景图片的候选列表
        private Dictionary<IllustKey, string> _background_queue;
        //数据保存和登陆验证
        private DataStorage _database;
        private API _api;
        private PixivAuth _auth;
        #endregion //Member Definations


        //开始的加载动画[STA]
        #region Loading Animation
        //用于加载loading界面的几个变量，一个用于保存目前的页面内容
        private loading_animation _uc_loading;
        private object _last_content;
        private void _begin_loading_effect()
        {
            var del = new ThreadStart(() =>
              {
                  _uc_loading = new loading_animation();
                  _last_content = this.Content;
                  this.Content = _uc_loading;
              });
            this.Dispatcher.Invoke(del);
        }
        private void _stop_loading_effect()
        {
            var del = new ThreadStart(() =>
            {
                this.Content = _last_content;
            });
            this.Dispatcher.Invoke(del);
        }
        #endregion //Loading Animation

        //通用函数
        #region Utility Functions
        /// <summary>
        /// 在网页中打开指定的pixiv投稿id
        /// </summary>
        /// <param name="id">投稿id</param>
        private void _open_illust(uint id)
        {
            if (id == 0) return;
            Process.Start("http://www.pixiv.net/i/" + id);
        }
        /// <summary>
        /// 在网页中打开指定的pixiv画师id
        /// </summary>
        /// <param name="id">画师id</param>
        private void _open_user(uint id)
        {
            if (id == 0) return;
            Process.Start("http://www.pixiv.net/u/" + id);
        }
        //随机显示字符串数组中的任意字符串
        private string _random_text(string[] origin)
        {
            var r = new Random();
            return origin[r.Next(origin.Length)];
        }
        //html反转义字符
        private string _escape_xml_char(string str_in)
        {
            return System.Net.WebUtility.HtmlDecode(str_in);
        }
        //根据Tag创建相应的链接和地址
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
                hl.Click += delegate
                {
                    Process.Start("http://www.pixiv.net/search.php?s_mode=s_tag_full&word=" + Uri.EscapeDataString(tag));
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
        /// <summary>
        /// update ui, call by main thread
        /// </summary>
        private void _update_ui_info()
        {
            //_external_save_lock.AcquireWriterLock(Timeout.Infinite);
            //image solution
            Image_Size.Content = _last_data.imageSolution.Width + "×" + _last_data.imageSolution.Height;
            if (_last_data.illust.Page == 1 && (_last_data.illust.Size.Width != _last_data.imageSolution.Width && _last_data.illust.Size.Height != _last_data.imageSolution.Height)) { Image_Size.Content += " [Origin: " + _last_data.illust.Size.Width + "×" + _last_data.illust.Size.Height + "]"; }

            //_last_data.illust id
            illust_id_open.Inlines.Clear();
            illust_id_open.Inlines.Add("ID=" + _last_data.illust.ID + " #" + (_last_data.page + 1) + "/" + _last_data.illust.Page);

            //title
            illust_open.Inlines.Clear();
            if (!string.IsNullOrEmpty(_last_data.illust.Title))
                illust_open.Inlines.Add(_escape_xml_char(_last_data.illust.Title));
            else
            {
                string title_reason;
                switch (_last_data.illust.HTTP_Status)
                {
                    case 404:
                        title_reason = "[404] " + _random_text(new string[] { "该投稿已被删除", "嗷～～投稿它～消失了～", "You are late :-(", "再怎么找它都不会出现了……" });
                        break;
                    case 403:
                        title_reason = "[403] " + _random_text(new string[] { "该投稿无法浏览", "该投稿仅好p友可见哦～", "明明……它都在那了……你却无法触摸它" });
                        break;
                    case -2:
                    case 0:
                        title_reason = "获取投稿信息中...";
                        break;
                    case 200: //status correct but empty/null
                        title_reason = "";
                        break;
                    default:
                        title_reason = "[" + _last_data.illust.HTTP_Status + "]: 未知错误";
                        break;
                }
                illust_open.Inlines.Add(title_reason);
            }
            //post time
            if (_last_data.illust.Submit_Time != 0)
            {
                var time = util.FromUnixTimestamp(_last_data.illust.Submit_Time);
                Post_Time.Content = time.ToString("yyyy-MM-dd HH:mm");
            }
            else
                Post_Time.Content = "";

            //author name
            user_open.Inlines.Clear();
            if (_last_data.user.Name != null)
                user_open.Inlines.Add(_escape_xml_char(_last_data.user.Name));
            else
                user_open.Inlines.Add("");

            //description
            Description.Children.Clear();
            if (_last_data.illust.Description != null)
                Description.Children.Add(html_parser.parseHTML(_last_data.illust.Description));

            //status
            if (_last_data.illust.HTTP_Status == 200)
                Border1.Background = Brushes.AliceBlue;
            else
                Border1.Background = Brushes.Red;

            //author image
            if (_last_data.user.User_Face != null)
            {
                var ss = new MemoryStream();
                _last_data.user.User_Face.Save(ss, _last_data.user.User_Face.RawFormat);
                ss.Position = 0;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ss;
                bmp.EndInit();

                WpfAnimatedGif.ImageBehavior.SetAnimatedSource(Author_Image, bmp);
            }
            else
                Author_Image.Source = null;

            //click
            Click.Content = _last_data.illust.Click.ToString("#,##0");

            //bookmark
            Favor.Content = _last_data.illust.Bookmark_Count.ToString("#,##0");

            //tags
            Tags.Inlines.Clear();
            if (_last_data.illust.Tag != null)
                Tags.Inlines.Add(_create_tag_hyperlink(_escape_xml_char(_last_data.illust.Tag).Split(',')));

            //tools
            Tools.Inlines.Clear();
            if (_last_data.illust.Tool != null)
                Tools.Inlines.Add(_escape_xml_char(_last_data.illust.Tool));

            //update time
            string str_last_update = (_last_data.illust.Last_Update > 0) ? util.FromUnixTimestamp(_last_data.illust.Last_Update).ToString("yyyy-MM-dd HH:mm:ss") : "无";
            string str_last_success_update = (_last_data.illust.Last_Success_Update > 0) ? util.FromUnixTimestamp(_last_data.illust.Last_Success_Update).ToString("yyyy-MM-dd HH:mm:ss") : "无";
            Update_Time.Content = string.Format("最后更新时间：{0} | 最后成功更新时间：{1}", str_last_update, str_last_success_update);

            //_last_data.user id
            UserID.Content = "用户描述: (ID: " + _last_data.user.ID + ")";

            //_last_data.user description
            UserDescription.Children.Clear();
            if (_last_data.user.Description != null)
                UserDescription.Children.Add(html_parser.parseHTML(_last_data.user.Description));

            //_last_data.user data
            UserData.Content = "关注着: " + _last_data.user.Follow_Users.ToString("#,##0") + ", 关注者: " + _last_data.user.Follower.ToString("#,##0") + ", 好P友: " + _last_data.user.Mypixiv_Users.ToString("#,##0") + "\r\n"
            + "投稿漫画数: " + _last_data.user.Total_Novels.ToString("#,##0") + ", 投稿插画数: " + _last_data.user.Total_Illusts.ToString("#,##0") + ", 公开收藏数: " + _last_data.user.Illust_Bookmark_Public.ToString("#,##0");

            //updating height
            if (_frm_created)
            {
                var fact = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M11;
                double height_pro = MainWindow_Layout.ActualHeight;
                height_pro = Math.Round(height_pro * fact) / fact;

                if (((Run)Show_More_Click.Inlines.FirstInline).Text == "简略信息")
                {
                    Height = height_pro;
                }
            }
            //_external_save_lock.ReleaseWriterLock();
        }
        #endregion //Utility Functions

        private void Show_More_Click_click(object sender, RoutedEventArgs e)
        {
            var fact = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M11;
            double height_mini = 166;// 168;
            double height_pro = MainWindow_Layout.ActualHeight;
            height_mini = Math.Round(height_mini * fact) / fact;
            height_pro = Math.Round(height_pro * fact) / fact;

            if (Math.Abs(Height - height_mini) < 0.01)
            {
                Height = height_pro;
                Show_More_Click.Inlines.Clear();
                Show_More_Click.Inlines.Add("简略信息");
            }
            else if (Math.Abs(Height - height_pro) < 0.01)
            {
                Height = height_mini;
                Show_More_Click.Inlines.Clear();
                Show_More_Click.Inlines.Add("详细信息");
            }
        }

        private bool _refresh_background_working = false;
        private void Refresh_Background_Click(object sender, RoutedEventArgs e)
        {
            if (_refresh_background_working) return;
            _refresh_background_working = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                _on_wallpaper_changed(sender, e);
                Settings.NextUpdateTimestamp = util.ToUnixTimestamp(DateTime.Now) + Settings.WallpaperChangeTime;
                _refresh_background_working = false;
            });
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var frmsetting = new frmSetting();
            frmsetting.ShowDialog();
        }

        private void frmMain_MouseMove(object sender, MouseEventArgs e)
        {

        }

        private void frmMain_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void illust_open_Click(object sender, RoutedEventArgs e)
        {
            _open_illust(_last_data.illust.ID);
        }

        private void user_open_Click(object sender, RoutedEventArgs e)
        {
            _open_user(_last_data.user.ID);
        }

        private void btn_user_image_Click(object sender, RoutedEventArgs e)
        {
            _open_user(_last_data.user.ID);
        }
    }
}
