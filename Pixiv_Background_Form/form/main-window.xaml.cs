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
using Microsoft.Win32;

namespace Pixiv_Background_Form
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        //窗体的构造函数以及初始化函数
        #region Form Initialize
        private Thread _initialize_thread = null;
        public MainWindow()
        {
            InitializeComponent();
            ScreenWatcher.GetPrimaryScreenBoundary(); //initialize, executing static constructor
            //登陆部分
            _auth = new PixivAuth();
            //API调用部分
            _api = new API(_auth);

            _load_last_data();

            _drag_thd = new Thread(__drag_thd_callback);
            _drag_thd.Name = "Drag Thread";
            _drag_thd.IsBackground = true;
            _drag_thd.SetApartmentState(ApartmentState.STA);
            _drag_thd.Start();

            ThreadPool.QueueUserWorkItem(delegate
            {
                _initialize_thread = Thread.CurrentThread;
                //数据库保存部分
                _database = new DataStorage(_api, true, _auth);
                _background_queue = new Dictionary<IllustKey, string>();

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
                    Dispatcher.Invoke(new ThreadStart(delegate
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
                    }));
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
                frmSearch.Instantiate(_background_queue, _database);

                _initialize_thread = null;
            });
        }
        //private bool _frm_created = false;
        private int _register_appbar_message;
        private bool _is_on_full_screen;
        private void frmMain_Loaded(object sender, RoutedEventArgs e)
        {
            MainWindow_Layout.ColumnDefinitions[0].Width = GridLength.Auto;
            Width = 129 + 8;
            Height = 26;
            Left = SystemParameters.WorkArea.Width - frmMain.ActualWidth;
            Top = 20;
            //_frm_created = true;
            var source = PresentationSource.FromVisual(this);

            //hide in alt+tab
            var helper = new System.Windows.Interop.WindowInteropHelper((Window)sender);
            int exStyle = (int)WinAPI.GetWindowLong(helper.Handle, (int)WinAPI.GetWindowLongFields.GWL_EXSTYLE);
            exStyle |= (int)WinAPI.ExtendedWindowStyles.WS_EX_TOOLWINDOW;
            WinAPI.SetWindowLong(helper.Handle, (int)WinAPI.GetWindowLongFields.GWL_EXSTYLE, exStyle);

            //registering full screen message dispatch
            WinAPI.RegisterAppBar(helper.Handle, out _register_appbar_message);
            var hwndsrc = System.Windows.Interop.HwndSource.FromHwnd(helper.Handle);
            hwndsrc.AddHook(new System.Windows.Interop.HwndSourceHook(handling_appbar));

        }
        private void frmMain_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Hide();
            var helper = new System.Windows.Interop.WindowInteropHelper((Window)sender);
            WinAPI.UnregisterAppBar(helper.Handle);

            if (_database != null) _database.AbortWorkingThread();
            Environment.Exit(0);
        }
        //overriding WndProc
        private IntPtr handling_appbar(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == _register_appbar_message)
            {
                if (wParam.ToInt32() == (int)WinAPI.ABNotify.ABN_FULLSCREENAPP)
                {
                    if ((int)lParam == 1)
                    {
                        _is_on_full_screen = true;
                        Tracer.GlobalTracer.TraceInfo("Entering full screen mode");
                    }
                    else
                    {
                        _is_on_full_screen = false;
                        Tracer.GlobalTracer.TraceInfo("Leaving full screen mode");
                    }
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }
        //当前壁纸的信息
        [Serializable]
        public struct _temp_serialize_struct
        {
            public Illust[] illust;
            public User[] user;
            public System.Drawing.Size[] imageSolution;
            public int[] page;
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
                    var ratios = new double[System.Windows.Forms.Screen.AllScreens.Length];
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
                    _last_data.illust = new Illust[cur_wallpaper.Length];
                    _last_data.user = new User[cur_wallpaper.Length];
                    _last_data.imageSolution = new System.Drawing.Size[cur_wallpaper.Length];
                    _last_data.page = new int[cur_wallpaper.Length];
                    for (int i = 0; i < cur_wallpaper.Length; i++)
                    {
                        _last_data.illust[i] = _database.GetIllustInfo(cur_wallpaper[i].id);
                        _last_data.user[i] = _database.GetUserInfo(_last_data.illust[i].Author_ID);
                        _last_data.imageSolution[i] = imgs[i].Size;
                        _last_data.page[i] = (int)cur_wallpaper[i].page;
                    }
                    _save_last_data();

                    //开启缓存渲染
                    if (Settings.EnableBuffering)
                    {
                        //更改注册表
                        try
                        {
                            var regkey_read = Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop");
                            var wallpaper_style = regkey_read.GetValue("WallpaperStyle", "1").ToString();
                            var tile_wallpaper = regkey_read.GetValue("TileWallpaper", "1").ToString();
                            regkey_read.Close();

                            if (wallpaper_style != "1" || tile_wallpaper != "1")
                            {
                                var regkey = Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop", true);
                                //壁纸的契合模式
                                //Name(EN)  Name(CN)  WallpaperStyle  TileWallpaper
                                //Fill      填充      10              0
                                //Fit       适应      6               0
                                //Span      跨区      22              0              （Windows 8+ only）
                                //Stretch   拉伸      2               0
                                //Tile      平铺      0               1
                                //Center    居中      0               0

                                regkey.SetValue("WallpaperStyle", "1");
                                regkey.SetValue("TileWallpaper", "1");
                                regkey.Close();
                            }
                        }
                        catch (Exception ex)
                        {
                            Tracer.GlobalTracer.TraceError("修改注册表失败：内部原因：\r\n" + ex.ToString());
                        }
                        //缩放壁纸
                        var tmp_imgs = new System.Drawing.Image[screens.Length];
                        for (int i = 0; i < tmp_imgs.Length; i++)
                        {
                            var index = Settings.EnableMultiMonitorDifferentWallpaper ? i : 0;
                            var src_width = imgs[index].Width;
                            var src_height = imgs[index].Height;
                            var dst_width = screens[i].Width;
                            var dst_height = screens[i].Height;

                            ratios[i] = Math.Min(dst_width * 1.0 / src_width, dst_height * 1.0 / src_height);
                            if (Settings.EnableWaifu2xUpscaling && (!Settings.DisableWaifu2xWhileFullScreen || !_is_on_full_screen) && ratios[i] >= Settings.Waifu2xUpscaleThreshold)
                                tmp_imgs[i] = _upscaling_using_waifu2x(imgs[i], ratios[i]);
                            else
                            {
                                var new_img = new System.Drawing.Bitmap((int)(imgs[index].Width * ratios[i]), (int)(imgs[index].Height * ratios[i]));
                                var gr1 = System.Drawing.Graphics.FromImage(new_img);

                                gr1.DrawImage(imgs[index], new System.Drawing.Rectangle(0, 0, new_img.Width, new_img.Height), new System.Drawing.Rectangle(new System.Drawing.Point(), imgs[index].Size), System.Drawing.GraphicsUnit.Pixel);
                                tmp_imgs[i] = new_img;
                            }

                        }
                        imgs = tmp_imgs;

                        var bmp_size = ScreenWatcher.GetTotalSize();
                        if (bmp_size.Width < 0 || bmp_size.Height < 0) return;
                        var bmp = new System.Drawing.Bitmap(bmp_size.Width, bmp_size.Height);

                        var gr = System.Drawing.Graphics.FromImage(bmp);
                        gr.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        gr.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

                        //多屏壁纸
                        for (int i = 0; i < screens.Length; i++)
                        {
                            var screen_rect = screens[i];
                            var destRect = new System.Drawing.Rectangle(
                                screen_rect.X + (screen_rect.Width - imgs[i].Width) / 2,
                                screen_rect.Y + (screen_rect.Height - imgs[i].Height) / 2,
                                imgs[i].Width,
                                imgs[i].Height
                                );
                            gr.DrawImage(imgs[i],
                                destRect, //destRect
                                new System.Drawing.Rectangle(new System.Drawing.Point(), imgs[i].Size), //srcRect
                                System.Drawing.GraphicsUnit.Pixel);
                        }

                        gr.Dispose();
                        bmp.Save("tempBackground.bmp", System.Drawing.Imaging.ImageFormat.Bmp);
                    }
                    else
                    {
                        //没有开启缓存渲染的话就直接复制到该目录下
                        imgs[0].Save("tempBackground.bmp", System.Drawing.Imaging.ImageFormat.Bmp);

                        //更改注册表
                        try
                        {
                            var regkey_read = Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop");
                            var wallpaper_style = regkey_read.GetValue("WallpaperStyle", "6").ToString();
                            var tile_wallpaper = regkey_read.GetValue("TileWallpaper", "0").ToString();
                            regkey_read.Close();

                            if (wallpaper_style != "6" || tile_wallpaper != "0")
                            {
                                var regkey = Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop", true);
                                regkey.SetValue("WallpaperStyle", "6");
                                regkey.SetValue("TileWallpaper", "0");
                                regkey.Close();
                            }
                        }
                        catch (Exception ex)
                        {
                            Tracer.GlobalTracer.TraceError("修改注册表失败：内部原因：\r\n" + ex.ToString());
                        }
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
                    //query from Pixiv failure (id = 0 or author id = 0)
                    var illust_invalid = ilsinfo.ID == 0 || ilsinfo.Author_ID == 0 || (ilsinfo.Size == new System.Drawing.Size(100, 100) && ilsinfo.Origin == DataOrigin.Pixiv_App_API);
                    //access failure
                    illust_invalid = illust_invalid || (ilsinfo.Origin != DataOrigin.SauceNao_API && (ilsinfo.HTTP_Status == 403 || ilsinfo.HTTP_Status == 404));
                    //network failure using saucenao
                    illust_invalid = illust_invalid || (ilsinfo.Origin == DataOrigin.SauceNao_API && ilsinfo.HTTP_Status != 200);
                    if (illust_invalid)
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
                                for (int i = 0; _last_data.user != null && i < _last_data.user.Length; i++)
                                {
                                    if (_last_data.user[i].ID == usrinfo.ID)
                                    {
                                        _last_data.user[i] = usrinfo;
                                        _save_last_data();
                                    }
                                }
                            }
                        }
                    }
                    for (int i = 0; _last_data.illust != null && i < _last_data.illust.Length; i++)
                    {
                        if (_last_data.illust[i].ID == ilsinfo.ID)
                        {
                            _last_data.illust[i] = ilsinfo;
                            _save_last_data();
                        }
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
                    for (int i = 0; _last_data.user != null && i < _last_data.user.Length; i++)
                    {
                        if (usrinfo.ID == _last_data.illust[i].Author_ID)
                        {
                            _last_data.user[i] = usrinfo;
                            _save_last_data();
                        }
                    }
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
                    else
                    {
                        Tracer.GlobalTracer.TraceInfo("key " + key.ToString() + " has already existed in path " + _background_queue[key] + " (current: " + item.FullName + ")");
                    }
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

        //通用函数
        #region Utility Functions
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
        //private void _update_ui_info()
        //{
        //    //_external_save_lock.AcquireWriterLock(Timeout.Infinite);
        //    //image solution
        //    Image_Size.Content = _last_data.imageSolution.Width + "×" + _last_data.imageSolution.Height;
        //    if (_last_data.illust.Page == 1 && (_last_data.illust.Size.Width != _last_data.imageSolution.Width && _last_data.illust.Size.Height != _last_data.imageSolution.Height)) { Image_Size.Content += " [Origin: " + _last_data.illust.Size.Width + "×" + _last_data.illust.Size.Height + "]"; }

        //    //_last_data.illust id
        //    illust_id_open.Inlines.Clear();
        //    illust_id_open.Inlines.Add("ID=" + _last_data.illust.ID + " #" + (_last_data.page + 1) + "/" + _last_data.illust.Page);

        //    //title
        //    illust_open.Inlines.Clear();
        //    if (!string.IsNullOrEmpty(_last_data.illust.Title))
        //        illust_open.Inlines.Add(_escape_xml_char(_last_data.illust.Title));
        //    else
        //    {
        //        string title_reason;
        //        switch (_last_data.illust.HTTP_Status)
        //        {
        //            case 404:
        //                title_reason = "[404] " + _random_text(new string[] { "该投稿已被删除", "嗷～～投稿它～消失了～", "You are late :-(", "再怎么找它都不会出现了……" });
        //                break;
        //            case 403:
        //                title_reason = "[403] " + _random_text(new string[] { "该投稿无法浏览", "该投稿仅好p友可见哦～", "明明……它都在那了……你却无法触摸它" });
        //                break;
        //            case -2:
        //            case 0:
        //                title_reason = "获取投稿信息中...";
        //                break;
        //            case 200: //status correct but empty/null
        //                title_reason = "";
        //                break;
        //            default:
        //                title_reason = "[" + _last_data.illust.HTTP_Status + "]: 未知错误";
        //                break;
        //        }
        //        illust_open.Inlines.Add(title_reason);
        //    }
        //    //post time
        //    if (_last_data.illust.Submit_Time != 0)
        //    {
        //        var time = util.FromUnixTimestamp(_last_data.illust.Submit_Time);
        //        Post_Time.Content = time.ToString("yyyy-MM-dd HH:mm");
        //    }
        //    else
        //        Post_Time.Content = "";

        //    //author name
        //    user_open.Inlines.Clear();
        //    if (_last_data.user.Name != null)
        //        user_open.Inlines.Add(_escape_xml_char(_last_data.user.Name));
        //    else
        //        user_open.Inlines.Add("");

        //    //description
        //    Description.Children.Clear();
        //    if (_last_data.illust.Description != null)
        //        Description.Children.Add(html_parser.parseHTML(_last_data.illust.Description));

        //    //status
        //    if (_last_data.illust.HTTP_Status == 200)
        //        Border1.Background = Brushes.AliceBlue;
        //    else
        //        Border1.Background = Brushes.Red;

        //    //author image
        //    if (_last_data.user.User_Face != null)
        //    {
        //        var ss = new MemoryStream();
        //        _last_data.user.User_Face.Save(ss, _last_data.user.User_Face.RawFormat);
        //        ss.Position = 0;

        //        var bmp = new BitmapImage();
        //        bmp.BeginInit();
        //        bmp.StreamSource = ss;
        //        bmp.EndInit();

        //        WpfAnimatedGif.ImageBehavior.SetAnimatedSource(Author_Image, bmp);
        //    }
        //    else
        //        Author_Image.Source = null;

        //    //click
        //    Click.Content = _last_data.illust.Click.ToString("#,##0");

        //    //bookmark
        //    Favor.Content = _last_data.illust.Bookmark_Count.ToString("#,##0");

        //    //tags
        //    Tags.Inlines.Clear();
        //    if (_last_data.illust.Tag != null)
        //        Tags.Inlines.Add(_create_tag_hyperlink(_escape_xml_char(_last_data.illust.Tag).Split(',')));

        //    //tools
        //    Tools.Inlines.Clear();
        //    if (_last_data.illust.Tool != null)
        //        Tools.Inlines.Add(_escape_xml_char(_last_data.illust.Tool));

        //    //update time
        //    string str_last_update = (_last_data.illust.Last_Update > 0) ? util.FromUnixTimestamp(_last_data.illust.Last_Update).ToString("yyyy-MM-dd HH:mm:ss") : "无";
        //    string str_last_success_update = (_last_data.illust.Last_Success_Update > 0) ? util.FromUnixTimestamp(_last_data.illust.Last_Success_Update).ToString("yyyy-MM-dd HH:mm:ss") : "无";
        //    Update_Time.Content = string.Format("最后更新时间：{0} | 最后成功更新时间：{1}", str_last_update, str_last_success_update);

        //    //_last_data.user id
        //    UserID.Content = "用户描述: (ID: " + _last_data.user.ID + ")";

        //    //_last_data.user description
        //    UserDescription.Children.Clear();
        //    if (_last_data.user.Description != null)
        //        UserDescription.Children.Add(html_parser.parseHTML(_last_data.user.Description));

        //    //_last_data.user data
        //    UserData.Content = "关注着: " + _last_data.user.Follow_Users.ToString("#,##0") + ", 关注者: " + _last_data.user.Follower.ToString("#,##0") + ", 好P友: " + _last_data.user.Mypixiv_Users.ToString("#,##0") + "\r\n"
        //    + "投稿漫画数: " + _last_data.user.Total_Novels.ToString("#,##0") + ", 投稿插画数: " + _last_data.user.Total_Illusts.ToString("#,##0") + ", 公开收藏数: " + _last_data.user.Illust_Bookmark_Public.ToString("#,##0");

        //    //updating height
        //    if (_frm_created)
        //    {
        //        var fact = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M11;
        //        double height_pro = MainWindow_Layout.ActualHeight;
        //        height_pro = Math.Round(height_pro * fact) / fact;

        //        if (((Run)Show_More_Click.Inlines.FirstInline).Text == "简略信息")
        //        {
        //            Height = height_pro;
        //        }
        //    }
        //    //_external_save_lock.ReleaseWriterLock();
        //}
        #endregion //Utility Functions

        private enum DockStatus
        {
            DockFailed, DockLeft, DockRight, DockTop = 4, DockBottom = 8
        }
        private DockStatus _dock_check(Point input_pos, out Point dock_pos)
        {
            const int dock_size = 30;
            var screens = ScreenWatcher.GetScreenBoundaryNoDpiAware(); //todo: multi screen supports
            dock_pos = input_pos;
            DockStatus ret = DockStatus.DockFailed;
            foreach (var item in screens)
            {
                //左边缘
                if (input_pos.X <= item.Left + dock_size && input_pos.X >= item.Left)
                {
                    dock_pos.X = item.Left;
                    ret |= DockStatus.DockLeft;
                }
                //右边缘
                if (input_pos.X + Width <= item.Right && input_pos.X + Width >= item.Right - dock_size)
                {
                    dock_pos.X = item.Right - Width;
                    ret |= DockStatus.DockRight;
                }
                //上边缘
                if (input_pos.Y <= item.Top + dock_size && input_pos.Y >= item.Top)
                {
                    dock_pos.Y = item.Top;
                    ret |= DockStatus.DockTop;
                }
                //下边缘
                if (input_pos.Y + Height <= item.Bottom && input_pos.Y + Height >= item.Bottom - dock_size)
                {
                    dock_pos.Y = item.Bottom - Height;
                    ret |= DockStatus.DockBottom;
                }
            }
            return ret;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }


        #region custom drag move implement
        //魔改版: https://www.codeproject.com/Questions/284995/DragMove-problem-help-pls
        bool _in_drag = false;
        //鼠标按下的位置（dpi aware）
        Point _anchor_point;
        //窗体的左上角位置（dpi aware）
        Point _src_location;
        //捕获窗体外部的MouseMove事件的线程以及回调函数
        Thread _drag_thd;
        object _mouse_down_sender;
        private void __drag_thd_callback()
        {
            do
            {
                if (_in_drag)
                {
                    var mouse_pos = System.Windows.Forms.Cursor.Position;

                    var scaled_pos = new Point(mouse_pos.X / ScreenWatcher.Scale, mouse_pos.Y / ScreenWatcher.Scale);
                    Dispatcher.Invoke(new ThreadStart(delegate
                    {
                        if (scaled_pos.X < Left || scaled_pos.Y < Top || scaled_pos.X > Left + Width || scaled_pos.Y > Top + Height)
                        {
                            __customDragMouseMove(scaled_pos);
                            //Debug.Print("capture outside window");
                        }

                        if (Mouse.LeftButton == MouseButtonState.Released && _mouse_down_sender != null)
                        {
                            _customDragMoveEnd(_mouse_down_sender, null);
                        }
                    }));
                }
                Thread.Sleep(25);
            } while (true);
        }
        private void _customDragMoveStart(object sender, MouseButtonEventArgs e)
        {
            _anchor_point = PointToScreen(e.GetPosition(this));
            _in_drag = true;
            _src_location = new Point(Left, Top);
            _anchor_point = new Point(_anchor_point.X / ScreenWatcher.Scale, _anchor_point.Y / ScreenWatcher.Scale);
            _mouse_down_sender = sender;
            if (e != null) e.Handled = true;
            //Debug.Print("src point: " + _src_location.ToString() + ", anchor_point: " + _anchor_point.ToString());
        }
        private void _customDragMoveEnd(object sender, MouseButtonEventArgs e)
        {
            _in_drag = false;
            _mouse_down_sender = null;
            if (e != null) e.Handled = true;
        }
        private void __customDragMouseMove(Point screenPos)
        {
            var vec = screenPos - _anchor_point;
            var dst = _src_location + vec;
            Point dock_pos;
            var stat = _dock_check(dst, out dock_pos);

            //Debug.Print(dst.ToString() + " ==> " + dock_pos.ToString());
            Left = dock_pos.X;
            Top = dock_pos.Y;
            if (stat != DockStatus.DockFailed)
            {

                if ((stat & DockStatus.DockTop) != 0)
                {
                    if (MainWindow_Layout.ColumnDefinitions[0].Width.Value != 0)
                        MainWindow_Layout.ColumnDefinitions[0].Width = new GridLength(0);
                    if (MainWindow_Layout.ColumnDefinitions[6].Width.Value != 0)
                        MainWindow_Layout.ColumnDefinitions[6].Width = new GridLength(0);
                    if (MainWindow_Layout.RowDefinitions[0].Height.Value != 0)
                        MainWindow_Layout.RowDefinitions[0].Height = new GridLength(0);
                    if (!MainWindow_Layout.RowDefinitions[2].Height.IsAuto)
                        MainWindow_Layout.RowDefinitions[2].Height = GridLength.Auto;
                    if (Math.Abs(Width - 129) > 0.5)
                        Width = 129;
                    if (Math.Abs(Height - 26 - 8) > 0.5)
                        Height = 26 + 8;
                }
                else if ((stat & DockStatus.DockBottom) != 0)
                {
                    if (MainWindow_Layout.ColumnDefinitions[0].Width.Value != 0)
                        MainWindow_Layout.ColumnDefinitions[0].Width = new GridLength(0);
                    if (MainWindow_Layout.ColumnDefinitions[6].Width.Value != 0)
                        MainWindow_Layout.ColumnDefinitions[6].Width = new GridLength(0);
                    if (!MainWindow_Layout.RowDefinitions[0].Height.IsAuto)
                        MainWindow_Layout.RowDefinitions[0].Height = GridLength.Auto;
                    if (MainWindow_Layout.RowDefinitions[2].Height.Value != 0)
                        MainWindow_Layout.RowDefinitions[2].Height = new GridLength(0);
                    if (Math.Abs(Width - 129) > 0.5)
                        Width = 129;
                    if (Math.Abs(Height - 26 - 8) > 0.5)
                        Height = 26 + 8;
                }
                else if ((stat & DockStatus.DockLeft) != 0)
                {
                    if (MainWindow_Layout.ColumnDefinitions[0].Width.Value != 0)
                        MainWindow_Layout.ColumnDefinitions[0].Width = new GridLength(0);
                    if (!MainWindow_Layout.ColumnDefinitions[6].Width.IsAuto)
                        MainWindow_Layout.ColumnDefinitions[6].Width = GridLength.Auto;
                    if (MainWindow_Layout.RowDefinitions[0].Height.Value != 0)
                        MainWindow_Layout.RowDefinitions[0].Height = new GridLength(0);
                    if (MainWindow_Layout.RowDefinitions[2].Height.Value != 0)
                        MainWindow_Layout.RowDefinitions[2].Height = new GridLength(0);
                    if (Math.Abs(Width - 129 - 8) > 0.5)
                        Width = 129 + 8;
                    if (Math.Abs(Height - 26) > 0.5)
                        Height = 26;
                }
                else if ((stat & DockStatus.DockRight) != 0)
                {
                    if (!MainWindow_Layout.ColumnDefinitions[0].Width.IsAuto)
                        MainWindow_Layout.ColumnDefinitions[0].Width = GridLength.Auto;
                    if (MainWindow_Layout.ColumnDefinitions[6].Width.Value != 0)
                        MainWindow_Layout.ColumnDefinitions[6].Width = new GridLength(0);
                    if (MainWindow_Layout.RowDefinitions[0].Height.Value != 0)
                        MainWindow_Layout.RowDefinitions[0].Height = new GridLength(0);
                    if (MainWindow_Layout.RowDefinitions[2].Height.Value != 0)
                        MainWindow_Layout.RowDefinitions[2].Height = new GridLength(0);
                    if (Math.Abs(Width - 129 - 8) > 0.5)
                        Width = 129 + 8;
                    if (Math.Abs(Height - 26) > 0.5)
                        Height = 26;
                }
            }
            else
            {
                if (MainWindow_Layout.ColumnDefinitions[0].Width.Value != 0)
                    MainWindow_Layout.ColumnDefinitions[0].Width = new GridLength(0);
                if (MainWindow_Layout.ColumnDefinitions[6].Width.Value != 0)
                    MainWindow_Layout.ColumnDefinitions[6].Width = new GridLength(0);
                if (MainWindow_Layout.RowDefinitions[0].Height.Value != 0)
                    MainWindow_Layout.RowDefinitions[0].Height = new GridLength(0);
                if (MainWindow_Layout.RowDefinitions[2].Height.Value != 0)
                    MainWindow_Layout.RowDefinitions[2].Height = new GridLength(0);
                if (Math.Abs(Width - 129) > 0.5)
                    Width = 129;
                if (Math.Abs(Height - 26) > 0.5)
                    Height = 26;
            }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            //base.OnMouseMove(e);
            Point current = PointToScreen(e.GetPosition(this));
            current = new Point(current.X / ScreenWatcher.Scale, current.Y / ScreenWatcher.Scale);
            if (_in_drag)
            {
                __customDragMouseMove(current);
            }
        }
        #endregion


        private void bInfo_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            ((Button)sender).Tag = e.Timestamp;
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _customDragMoveStart(sender, e);
            }
        }
        private void bInfo_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (((Button)sender).Tag == null) return;
            var last_ts = (int)((Button)sender).Tag;
            _customDragMoveEnd(sender, e);
            if (e.Timestamp - last_ts < 500)
            {
                //handling as click
                if (_background_queue == null && _initialize_thread != null)
                    _initialize_thread.Join();
                for (int i = 0; _last_data.illust != null && i < _last_data.illust.Length; i++)
                {
                    var frmInfo = new frmDetailed(_last_data.illust[i], _last_data.user[i], _background_queue[new IllustKey { id = _last_data.illust[i].ID, page = (uint)_last_data.page[i] }]);
                    frmInfo.Show();
                }
            }
        }

        private void bNext_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            ((Button)sender).Tag = e.Timestamp;
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _customDragMoveStart(sender, e);
            }
        }
        private bool _refresh_background_working = false;
        private void bNext_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {

            if (((Button)sender).Tag == null) return;
            var last_ts = (int)((Button)sender).Tag;
            if (e.Timestamp - last_ts < 500)
            {
                //handling as click
                if (_refresh_background_working) return;
                _refresh_background_working = true;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    _on_wallpaper_changed(sender, e);
                    Settings.NextUpdateTimestamp = util.ToUnixTimestamp(DateTime.Now);
                    _refresh_background_working = false;
                });
            }
        }

        private void bSearch_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            ((Button)sender).Tag = e.Timestamp;
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _customDragMoveStart(sender, e);
            }
        }
        private void bSearch_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (((Button)sender).Tag == null) return;
            var last_ts = (int)((Button)sender).Tag;
            if (e.Timestamp - last_ts < 500)
            {
                //handling as click
                //wait for initialization completed
                if (_initialize_thread != null)
                {
                    try
                    {
                        _initialize_thread.Join();
                    }
                    catch (Exception) { }
                }
                if (frmSearch.SingleInstantiation != null)
                {
                    frmSearch.SingleInstantiation.Dispatcher.Invoke(new ThreadStart(delegate
                    {
                        frmSearch.SingleInstantiation.Show();
                    }));
                }
            }
        }

        private void bSetting_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            ((Button)sender).Tag = e.Timestamp;
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _customDragMoveStart(sender, e);
            }
        }
        private void bSetting_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {

            if (((Button)sender).Tag == null) return;
            var last_ts = (int)((Button)sender).Tag;
            if (e.Timestamp - last_ts < 500)
            {
                //handling as click
                var frmsetting = new frmSetting();
                frmsetting.ShowDialog();
            }
        }

        private void bTopLayout_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow_Layout.RowDefinitions[1].Height == GridLength.Auto)
            {
                MainWindow_Layout.RowDefinitions[1].Height = new GridLength(0);
                Top += 26;
            }
            else
            {
                MainWindow_Layout.RowDefinitions[1].Height = GridLength.Auto;
                Top -= 26;
            }
        }
        private void bBottomLayout_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow_Layout.RowDefinitions[1].Height == GridLength.Auto)
                MainWindow_Layout.RowDefinitions[1].Height = new GridLength(0);
            else
                MainWindow_Layout.RowDefinitions[1].Height = GridLength.Auto;
        }
        private void bRightLayout_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow_Layout.ColumnDefinitions[1].Width == GridLength.Auto)
                for (int i = 1; i < 6; i++)
                    MainWindow_Layout.ColumnDefinitions[i].Width = new GridLength(0);
            else
                for (int i = 1; i < 6; i++)
                    MainWindow_Layout.ColumnDefinitions[i].Width = GridLength.Auto;
        }
        private void bLeftLayout_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow_Layout.ColumnDefinitions[1].Width == GridLength.Auto)
            {
                for (int i = 1; i < 6; i++)
                    MainWindow_Layout.ColumnDefinitions[i].Width = new GridLength(0);
                Left += 129;
            }
            else
            {
                for (int i = 1; i < 6; i++)
                    MainWindow_Layout.ColumnDefinitions[i].Width = GridLength.Auto;
                Left -= 129;
            }
        }
    }
}
