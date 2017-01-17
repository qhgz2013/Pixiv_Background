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
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using System.Threading;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Pixiv_Background;
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
            ThreadPool.QueueUserWorkItem((object obj) => { _data_initialize(); }); //异步初始化数据
        }

        private void frmMain_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _database.AbortWorkingThread();
            VBUtil.Utils.NetUtils.Global.SaveCookie();
            _save_background_info();
        }

        //读取已保存的变量
        //包含的变量: _illust_info, _illust_page, _user_info, _image_solution, _background_path
        private void _load_background_info()
        {
            var fi = new FileInfo("tempBackground.dat");
            if (fi.Exists && fi.Length > 0)
            {
                try
                {
                    var bf = new BinaryFormatter();
                    var fs = fi.OpenRead();
                    _temp_serialize_struct data = (_temp_serialize_struct)bf.Deserialize(fs);
                    fs.Close();
                    _illust_info = data.illust;
                    _illust_page = data.page;
                    _user_info = data.user;
                    _image_solution = data.imageSolution;
                    _background_path = data.backgroundPath;

                    if (string.IsNullOrEmpty(_background_path))
                    {
                        this.Dispatcher.Invoke(new NoArgSTA(() => { Update_From_Path.DoClick(); }));
                    }
                    _show_current_msg();
                }
                catch (Exception)
                {
                }
            }
        }
        //保存当前变量的信息
        private void _save_background_info()
        {
            var fi = new FileStream("tempBackground.dat", FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            var bf = new BinaryFormatter();
            var data = new _temp_serialize_struct();
            data.illust = _illust_info;
            data.user = _user_info;
            data.page = _illust_page;
            data.imageSolution = _image_solution;
            data.backgroundPath = _background_path;
            bf.Serialize(fi, data);
            fi.Close();
        }

        //异步初始化数据
        #region Async Initialize
        private void _data_initialize()
        {
            _begin_loading_effect(); //显示loading界面

            VBUtil.Utils.NetUtils.Global.LoadCookie();

            _background_queue = new List<string>();
            _operationLock = new ReaderWriterLock();

            bool ignore_non_200 = true;
            dataUpdater.LoginSucceeded += _loginSucceeded;
            dataUpdater.LoginFailed += _loginFailed;
            _database = new dataUpdater(true, ignore_non_200);

            _database.LoginRequired += _doLogin;
            _database.FetchIllustSucceeded += _database_FetchDataEnded;
            _database.FetchIllustFailed += _database_FetchDataEnded;
            _database.FetchUserSucceeded += _database_FetchDataEnded;
            _database.FetchUserFailed += _database_FetchDataEnded;

            _load_background_info();

            //test area
            var callback = new TimerCallback(Timer_callback);
            _callback_timer = new Timer(callback);
            _callback_timer.Change(0, 100);

            //file updating
            if (!string.IsNullOrEmpty(_background_path))
            {
                _database.UpdateFileList(_background_path);
                _add_dir(_background_path);
            }

            _illust_info = _database.GetIllustInfo(_illust_info.ID);
            _user_info = _database.GetUserInfo(_illust_info.Author_ID);
            _show_current_msg();

            //新开线程进行背景图操作
            _bgthread = new Thread(_bgthread_callback);
            _bgthread.Name = "Background Change Thread";
            _bgthread.IsBackground = true;
            _bgthread.Start();


            _stop_loading_effect(); //调用回本界面
        }
        #endregion //Async Initialize

        #endregion //Form Initialize


        //常量定义
        #region Constant Definations
        //是否包含子文件夹
        private const bool _include_sub_dir = false;
        //背景图片更改的时间（单位：min）
        private const int _time_change_minutes = 10;
        //匹配图片的正则表达式语法，id page必须，ext可选
        private const string _image_ptn = "(?<id>\\d+)_p(?<page>\\d+)\\.(?<ext>[a-zA-Z0-9]+)";
        #endregion //Constant Definations


        //变量定义
        #region Member Definations
        //背景图片的候选列表
        private List<string> _background_queue;
        private dataUpdater _database;
        private Thread _bgthread;

        //下一次更新的时间
        private DateTime _next_update_time;
        //保存的变量
        private Illust _illust_info;
        private uint _illust_page;
        private User _user_info;
        private System.Drawing.Size _image_solution;
        private string _background_path;

        [Serializable]
        private struct _temp_serialize_struct
        {
            public Illust illust;
            public User user;
            public uint page;
            public System.Drawing.Size imageSolution;
            public string backgroundPath;
        }

        //当前是否显示详细信息
        private bool _detailed = false;
        //当前是否开启滑动隐藏
        private bool _enabled_slide_hide = false;
        //线程锁
        private ReaderWriterLock _operationLock;
        //跨线程委托（无参的lambda表达式）
        private delegate void NoArgSTA();
        #endregion //Member Definations


        //数据库的事件回调
        #region Event Callback

        //调用结束
        private void _database_FetchDataEnded(uint id, uint currentTask, uint totalTask, Illust data)
        {
            _update_progress_bar(currentTask, totalTask);
            if (data.ID == _illust_info.ID)
            {
                //updating current illust info
                _illust_info = data;
                _save_background_info();
                _show_current_msg();
            }
        }
        private void _database_FetchDataEnded(uint id, uint currentTask, uint totalTask, User data)
        {
            _update_progress_bar(currentTask, totalTask);
            if (data.ID == _user_info.ID)
            {
                _user_info = data;
                _save_background_info();
                _show_current_msg();
            }
        }
        private void _update_progress_bar(uint currentTask, uint totalTask)
        {
            var del = new NoArgSTA(() =>
              {
                  if (currentTask != totalTask)
                  {
                      var percent = currentTask * 1.0 / totalTask;
                      ProgressBar.Opacity = 1;
                      ProgressBar.Width = percent * (frmMain.ActualWidth - 16);
                  }
                  else
                  {
                      ProgressBar.Opacity = 0;
                  }
              });
            Debug.Print("Task finished: " + currentTask + " / " + totalTask);
            this.Dispatcher.Invoke(del);
        }

        //状态回调[STA]：登陆
        private void _doLogin()
        {
            //show login dialog here .... no more password

            this.Dispatcher.Invoke(new NoArgSTA(() =>
            {
                var test = new frmLogin();
                test.ShowDialog();

                var userName = test.user_name;
                var passWord = test.pass_word;
                var canceled = test.canceled;
                if (!canceled)
                {
                    dataUpdater.Login(userName, passWord);
                    //restart init
                    _database.UpdateFileList(_background_path);
                }
                else
                {
                    Close();
                }
            }));
        }
        private void _loginSucceeded() { }
        private void _loginFailed(string reason) { }
        #endregion //Event Callback


        //线程工作函数
        #region Working Thread
        private void _bgthread_callback()
        {
            _next_update_time = DateTime.Now.Date;
            while (_next_update_time < DateTime.Now)
            {
                _next_update_time = _next_update_time.AddMinutes(_time_change_minutes);
            }
            var r = new Random();
            do
            {
                var time = DateTime.Now;
                try
                {
                    Thread.Sleep((int)(_next_update_time - time).TotalMilliseconds);
                }
                catch (Exception)
                {
                    //Debug.Print(ex.ToString());
                }

                _operationLock.AcquireWriterLock(Timeout.Infinite);

                try
                {
                    var index = r.Next(_background_queue.Count);
                    var cur_file = _background_queue[index];
                    var fi = new FileInfo(cur_file);
                    if (fi.Exists)
                    {
                        var match = Regex.Match(fi.Name, _image_ptn);

                        var illust_id = uint.Parse(match.Result("${id}"));
                        _illust_page = uint.Parse(match.Result("${page}"));

                        _set_desktop_background(cur_file);

                        _illust_info = _database.GetIllustInfo(illust_id);
                        var user_id = _illust_info.Author_ID;
                        _user_info = _database.GetUserInfo(_illust_info.Author_ID);

                        _save_background_info();
                        _show_current_msg();
                    }

                    _next_update_time = DateTime.Now.AddMinutes(_time_change_minutes);
                }
                catch (Exception ex)
                {
                    Debug.Print(ex.ToString());
                }
                finally
                {
                    _operationLock.ReleaseWriterLock();
                }
            } while (true);
        }
        //更改桌面壁纸
        private void _set_desktop_background(string imgPath)
        {
            var img = System.Drawing.Image.FromFile(imgPath);
            var path = System.Environment.CurrentDirectory;
            path += @"\tempBackground.bmp";

            const bool use_fast_mode = true;

            if (!use_fast_mode)
            {

                //get the scaling factor
                //see http://james-ramsden.com/c-get-dpi-screen/
                double factor = 1.0;
                //using STA invoke
                this.Dispatcher.Invoke(new NoArgSTA(() =>
                {
                    factor = System.Windows.PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M11;
                }));
                var tmpbmp = new System.Drawing.Bitmap((int)(SystemParameters.WorkArea.Width * factor), (int)(SystemParameters.WorkArea.Height * factor));
                var gr = System.Drawing.Graphics.FromImage(tmpbmp);
                gr.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                gr.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                //计算缩放大小
                double width_mul = 1.0 * tmpbmp.Width / img.Width;
                double height_mul = 1.0 * tmpbmp.Height / img.Height;
                int new_width, new_height;
                if (width_mul > height_mul)
                {
                    //按高度缩放
                    new_height = tmpbmp.Height;
                    new_width = (int)(img.Width * height_mul);
                }
                else
                {
                    new_width = tmpbmp.Width;
                    new_height = (int)(img.Height * width_mul);
                }
                gr.DrawImage(img, (tmpbmp.Width - new_width) / 2, (tmpbmp.Height - new_height) / 2, new_width, new_height);


                tmpbmp.Save(path, System.Drawing.Imaging.ImageFormat.Bmp);
                gr.Dispose();
                tmpbmp.Dispose();
            }
            else
            {
                img.Save(path, System.Drawing.Imaging.ImageFormat.Bmp);
            }

            SystemParametersInfo(20, 0, path, 2);

            _image_solution = img.Size;

            img.Dispose();
        }

        #endregion //Working Thread


        //开始的加载动画[STA]
        #region Loading Animation
        //用于加载loading界面的几个变量，一个用于保存目前的页面内容
        private loading_animation _uc_loading;
        private object _last_content;
        private void _begin_loading_effect()
        {
            var del = new NoArgSTA(() =>
              {
                  _uc_loading = new loading_animation();
                  _last_content = this.Content;
                  this.Content = _uc_loading;
              });
            this.Dispatcher.Invoke(del);
        }
        private void _stop_loading_effect()
        {
            var del = new NoArgSTA(() =>
            {
                this.Content = _last_content;
            });
            this.Dispatcher.Invoke(del);
        }
        #endregion //Loading Animation


        //界面操作
        #region UI Functions
        //大标题点击：打开投稿页面
        private void illust_open_Click(object sender, RoutedEventArgs e)
        {
            _open_illust(_illust_info.ID);
        }
        //窗体点击：默认拖动
        private void frmMain_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) this.DragMove();
        }
        //用户名称点击：打开用户页面
        private void user_open_Click(object sender, RoutedEventArgs e)
        {
            _open_illust(_illust_info.ID);
        }
        //用户图像点击：打开用户
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            _open_user(_user_info.ID);
        }
        private Guid _refresh_background_click_guid;
        //刷新背景点击：刷新背景
        private void Refresh_Background_Click(object sender, RoutedEventArgs e)
        {
            _refresh_background_click_guid = Guid.NewGuid();
            ThreadPool.QueueUserWorkItem((object obj) =>
            {
                Guid current_task = _refresh_background_click_guid;
                _operationLock.AcquireWriterLock(Timeout.Infinite);
                _operationLock.ReleaseWriterLock(); //waiting background thread complete current task

                if (_refresh_background_click_guid == current_task)
                    _bgthread.Interrupt();
                //verifying async token and update
            });
        }
        //显示详细信息点击：更改窗体大小
        private void Show_More_Click_click(object sender, RoutedEventArgs e)
        {
            Show_More_Click.Inlines.Clear();
            if (_detailed)
            {
                this.Width = 410;
                this.Height = 170;
                Show_More_Click.Inlines.Add("详细信息");
            }
            else
            {
                this.Width = 620;
                this.Height = 460;
                Show_More_Click.Inlines.Add("简略信息");
            }
            _detailed = !_detailed;
        }
        //更改背景目录点击：弹出目录选择框
        private void Update_From_Path_Click(object sender, RoutedEventArgs e)
        {
            var fbd = new System.Windows.Forms.FolderBrowserDialog();
            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (_database != null)
                {
                    _background_path = fbd.SelectedPath;
                    _database.UpdateFileList(_background_path);
                }
            }

        }

        //鼠标悬停
        private void Slide_Hide_Click(object sender, RoutedEventArgs e)
        {
            //Debug.Print("Slide_Hide clicked");
            if (_enabled_slide_hide)
            {
                _enabled_slide_hide = false;
                Slide_Hide.Inlines.Clear();
                Slide_Hide.Inlines.Add("开启滑动透明");

            }
            else
            {
                _last_mouse_move = DateTime.Now;
                _enabled_slide_hide = true;
                Slide_Hide.Inlines.Clear();
                Slide_Hide.Inlines.Add("取消滑动透明");
            };
        }
        #endregion //UI Functions


        //通用函数
        #region Utility Functions
        private void _open_illust(uint id)
        {
            if (id == 0) return;
            System.Diagnostics.Process.Start("http://www.pixiv.net/i/" + id);
        }
        private void _open_user(uint id)
        {
            if (id == 0) return;
            System.Diagnostics.Process.Start("http://www.pixiv.net/u/" + id);
        }
        //随机显示字符串数组中的任意字符串
        private string _random_text(string[] origin)
        {
            var r = new Random();
            return origin[r.Next(origin.Length)];
        }
        private string _escape_xml_char(string str_in)
        {
            return System.Net.WebUtility.HtmlDecode(str_in);
        }
        [DllImport("user32.dll")]
        private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);
        //显示当前投稿信息
        private void _show_current_msg()
        {
            //todo: 修改非200时的显示
            var del = new NoArgSTA(() =>
            {
                Illust illust = _illust_info;
                User user = _user_info;
                //image solution
                Image_Size.Content = _image_solution.Width + " x " + _image_solution.Height;

                //author id & illust id
                user_id_open.Inlines.Clear();
                user_id_open.Inlines.Add("ID=" + illust.ID + " #" + _illust_page);

                //title
                illust_open.Inlines.Clear();
                if (!string.IsNullOrEmpty(illust.Title))
                    illust_open.Inlines.Add(_escape_xml_char(illust.Title));
                else
                {
                    string title_reason;
                    switch (illust.HTTP_Status)
                    {
                        case 404:
                            title_reason = "[404] " + _random_text(new string[] { "该投稿已被删除", "嗷～～投稿它～消失了～", "You are late :-(", "再怎么找它都不会出现了……" });
                            break;
                        case 403:
                            title_reason = "[403] " + _random_text(new string[] { "该投稿无法浏览", "该投稿仅好p友可见哦～", "明明……它都在那了……你却无法触摸它" });
                            break;
                        case 0:
                            title_reason = "获取投稿信息中...(sorry,刷新模块还没写)";
                            break;
                        case 200: //status correct but empty/null
                            title_reason = "";
                            break;
                        default:
                            title_reason = "[" + illust.HTTP_Status + "]: 未知错误";
                            break;
                    }
                    illust_open.Inlines.Add(title_reason);
                }
                //post time
                if (illust.Submit_Time != 0)
                {
                    var time = VBUtil.Utils.Others.FromUnixTimeStamp(illust.Submit_Time);
                    Post_Time.Content = time.ToString("yyyy-MM-dd HH:mm");
                }
                else
                    Post_Time.Content = "";

                //author name
                user_open.Inlines.Clear();
                if (user.Name != null)
                    user_open.Inlines.Add(_escape_xml_char(user.Name));
                else
                    user_open.Inlines.Add("");

                //description
                Description.Children.Clear();
                if (illust.Description != null)
                    Description.Children.Add(html_parser.parseHTML(illust.Description));

                //status
                if (illust.HTTP_Status == 200)
                    Border1.Background = Brushes.AliceBlue;
                else
                    Border1.Background = Brushes.Red;

                //author image
                if (user.HTTP_Status == 200)
                {
                    var ss = new MemoryStream();
                    user.User_Face.Save(ss, user.User_Face.RawFormat);
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
                Click.Content = illust.Click.ToString("#,##0");
                //rating
                string rating_str;
                if (illust.Rate_Count > 0)
                {
                    rating_str = Math.Round((double)(illust.Score) / illust.Rate_Count, 2).ToString();
                    rating_str += " (" + illust.Score.ToString("#,##0") + "/" + illust.Rate_Count.ToString("#,##0") + ")";
                }
                else
                    rating_str = "No Rating Data";
                Favor.Content = rating_str;
                //tags
                if (illust.Tag != null)
                    Tags.Content = _escape_xml_char(illust.Tag);
                else
                    Tags.Content = "";
                //tools
                if (illust.Tool != null)
                    Tools.Content = _escape_xml_char(illust.Tool);
                else
                    Tools.Content = "";
                //user description
                //UserDescription.Children.Clear();
                //if (user.Description != null)
                //    UserDescription.Children.Add(html_parser.parseHTML(user.Description));

                string str_last_update = (_illust_info.Last_Update > 0) ? VBUtil.Utils.Others.FromUnixTimeStamp(_illust_info.Last_Update).ToString("yyyy-MM-dd HH:mm:ss") : "无";
                string str_last_success_update = (_illust_info.Last_Success_Update > 0) ? VBUtil.Utils.Others.FromUnixTimeStamp(_illust_info.Last_Success_Update).ToString("yyyy-MM-dd HH:mm:ss") : "无";
                Update_Time.Content = string.Format("最后更新时间：{0} | 最后成功更新时间：{1}", str_last_update, str_last_success_update);
            });
            this.Dispatcher.Invoke(del);
        }


        private void _add_dir(string path)
        {
            var dir = new DirectoryInfo(path);
            if (dir.Exists)
            {
                foreach (var item in dir.GetFiles())
                {
                    var match = Regex.Match(item.Name, _image_ptn);
                    if (match.Success)
                    {
                        _background_queue.Add(item.FullName);
                    }
                }

                if (_include_sub_dir)
                {
                    foreach (var item in dir.GetDirectories())
                    {
                        _add_dir(item.FullName);
                    }
                }
            }
        }

        #endregion //Utility Functions


        //test module for slide hide
        private DateTime _last_mouse_move = DateTime.MinValue;
        private Timer _callback_timer;
        private double _origin_alpha = 1.0;
        private double _destination_alpha = 0.5;
        private void frmMain_MouseMove(object sender, MouseEventArgs e)
        {
            if (_enabled_slide_hide)
            {
                if (Opacity == _origin_alpha)
                {
                    var da = new DoubleAnimation(_origin_alpha, _destination_alpha, new TimeSpan(0, 0, 0, 0, 300));
                    this.BeginAnimation(OpacityProperty, da);
                }

                _last_mouse_move = DateTime.Now;
            }
        }
        private void Timer_callback(object sender)
        {
            var del = new NoArgSTA(() =>
            {
                if ((!_enabled_slide_hide || _enabled_slide_hide && _last_mouse_move.AddSeconds(2) <= DateTime.Now) && Opacity == _destination_alpha)
                {
                    var da = new DoubleAnimation(_destination_alpha, _origin_alpha, new TimeSpan(0, 0, 0, 0, 300));
                    this.BeginAnimation(OpacityProperty, da);
                    //_enabled_slide_hide = false;
                }
            });
            this.Dispatcher.Invoke(del);
        }
        
        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
