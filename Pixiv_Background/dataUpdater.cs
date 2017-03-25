using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.Text.RegularExpressions;
using System.Data.SQLite;
using VBUtil.Utils.NetUtils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Drawing;
using System.Data;
using System.Net;
using System.Diagnostics;

/*
* 目前数据库的表格变量定义 [v1.0.6]
* 
* TABLE DbVars(string Key [PRIMARY KEY], string Value)
*       用于存放数据库相关信息，如版本，路径等。 Key:数值名称 Value:数值内容
* TABLE User(uint ID [PRIMARY KEY], string Name, string Description, Byte[] User_Face, string User_Face_Url, string Home_Page,
*       用于存放画师信息，ID:画师ID，      Name:画师名称，Description:画师描述（html代码）, User_Face:画师头像（二进制图片）,User_Face_Url:画师头像的url 下载失败可以从这里直接下载, Home_Page: 画师的个人主页
*            string Gender, string Personal_Tag, string Address, string Birthday, string Job, int Follow_Users, int Follower, int Illust_Bookmark_Public, int Mypixiv_Users, int Total_Illusts, int Total_Novels, string Twitter, 
*            Gender:性别，男/女,   Personal_Tag:个人的标签, Address:地址, Birthday:生日, Job:职业, Follow_Users:关注着, Follower:被关注着, Illust_Bookmark_Public:公开收藏数, Mypixiv_Users:好p友数, Total_Illusts: 总插画投稿数, int Total_Novels:总小说投稿数, Twitter:推,
*            int HTTP_Status [NOT NULL], ulong Last_Update [NOT NULL], ulong Last_Success_Update[NOT NULL])
*            HTTP_Status:http状态,         Last_Update:最后更新的时间    Last_Success_Update:最后成功更新的时间
* 
* TABLE Illust(uint ID [PRIMARY KEY], uint Author_ID [NOT NULL], uint Page [NOT NULL [1]], string Title, string Description, string Tag, string Tool,
*       用于存放投稿信息，ID:作品ID，      Author_ID:画师ID，         Page:投稿分p数              Title:投稿标题，Description:投稿的描述,Tag:投稿标签，Tool:绘图工具
*              int Click [NOT NULL [0]], int Bookmark_Count, int Comment_Count, int Width [NOT NULL [0]], int Height [NOT NULL [0]] int Rate_Count [NOT NULL [0]], int Score [NOT NULL [0]],
*                   Click:点击数，           Bookmark_Count:收藏数 Comment_Count:评论数 Width:作品宽度像素,   Height:作品高度像素,      Rate_Count:用户评分数，        Score:用户评分
*              ulong Submit_Time [NOT NULL [0]], int HTTP_Status [NOT NULL [0]], ulong Last_Update [NOT NULL [0]], ulong Last_Success_Update [NOT NULL [0]], byte Origin [NOT NULL [0]])
*                    Submit_Time:投稿时间，          HTTP_Status:获取投稿信息时的http状态码，Last_Update:最后更新投稿信息的时间, Last_Success_Update:最后成功更新的时间, Origin:数据来源
* 
* 状态附加定义：-1代表该内容正在下载中（多线程时的占用标识）
* */


namespace Pixiv_Background
{

    #region SQL Data Structure Definations
    //画师信息
    [Serializable]
    public struct User
    {
        //画师ID
        public uint ID;
        //画师名称
        public string Name;
        //画师描述（html代码）
        public string Description;
        //画师头像
        public Image User_Face;
        //画师头像的url
        public string User_Face_Url;
        //主页
        public string Home_Page;
        //性别
        public string Gender;
        //个人标签
        public string Personal_Tag;
        //地址
        public string Address;
        //生日
        public string Birthday;
        //职业
        public string Job;
        //关注其他画师的人数
        public int Follow_Users;
        //被他人关注的人数
        public int Follower;
        //公开的收藏数
        public int Illust_Bookmark_Public;
        //好p友数
        public int Mypixiv_Users;
        //总插画投稿数
        public int Total_Illusts;
        //总漫画投稿数
        public int Total_Novels;
        //Twitter
        public string Twitter;
        //http状态
        public int HTTP_Status;
        //最后更新的时间
        public ulong Last_Update;
        //最后成功更新的时间
        public ulong Last_Success_Update;
    }
    //投稿信息
    [Serializable]
    public struct Illust
    {
        //作品ID
        public uint ID;
        //画师ID
        public uint Author_ID;
        //投稿分p
        public uint Page;
        //投稿标题
        public string Title;
        //投稿的描述
        public string Description;
        //投稿标签
        public string Tag;
        //工具
        public string Tool;
        //点击数
        public int Click;
        //收藏数
        public int Bookmark_Count;
        //评论数
        public int Comment_Count;
        //作品像素
        public Size Size;
        //评分次数
        public int Rate_Count;
        //总分
        public int Score;
        //投稿时间
        public ulong Submit_Time;
        //获取投稿信息时的http状态码
        public int HTTP_Status;
        //最后更新投稿信息的时间
        public ulong Last_Update;
        //最后成功更新的时间
        public ulong Last_Success_Update;
        //数据来源
        public DataOrigin Origin;
    }

    public enum DataOrigin
    {
        Pixiv_Html, SauceNao_API, Pixiv_App_API
    }

    public enum DataUpdateMode
    {
        //flg : 0 0(Force mode) 0(Sync mode) 0(Async mode)
        No_Update, Async_Update, Sync_Update, Force_Update = 4, Force_Async_Update, Force_Sync_Update
    }

    #endregion


    public class dataUpdater
    {
        //类成员定义
        #region Member Definations

        //p站图片路径
        //private string m_path;
        //是否包含子文件夹
        private bool m_include_subdir;

        //sql多线程同步锁, 负责sql，m_illust_list和m_user_list的读写
        private ReaderWriterLock m_sqlThreadLock;
        //sql相关
        private SQLiteConnection m_dbConnection;
        private SQLiteCommand m_dbCommand;
        private SQLiteTransaction m_dbTransaction;

        //投稿列表
        private Dictionary<uint, int> m_illust_list;
        //用户列表
        private Dictionary<uint, int> m_user_list;

        //无视403 404错误
        private bool m_ignore_non_200_status;
        //数据更新列表
        private List<uint> m_illust_query_list;
        private List<uint> m_user_query_list;
        private int m_query_count;
        private int m_query_finished;
        private int m_querying_count;
        //数据更新多线程同步锁，负责数据更新的表m_illust_query_list和m_user_query_list，以及更新列表的数量m_query_count的读写
        private ReaderWriterLock m_dataThreadLock;

        //tt变量，从pixiv上获取
        //private string m_var_tt;

        //多线程模式
        private Thread[] m_illust_thd;
        private Thread[] m_user_thd;
        private Thread m_monitor_thd;
        //操作终止标识
        private bool m_abort_flag;
        //线程数统计，用于确保线程数量正确
        private int m_illust_working_thd_count;
        private int m_user_working_thd_count;

        #endregion //Member Definations

        //类常量定义
        #region Constant Definations

        //常量定义：当前版本和最大获取投稿信息的线程数
        private const string M_CURRENT_DBVERSION = "1.0.6";
        //最大获取投稿信息的线程数
        private const int M_MAX_ILLUST_SYNC_THREAD = 2;
        //最大获取用户信息的线程数
        private const int M_MAX_USER_SYNC_THREAD = 2;
        //多个Tag或者绘画工具的分隔符
        private const string multi_data_split_string = ",";
        //最小数据更新周期（单位：秒）
        private const int M_MIN_AUTOUPDATE_INTERVAL = 15 * 24 * 60 * 60; //15 days
        //client id
        private const string M_CLIENT_ID = "MOBrBDS8blbauoSck0ZfDbtuzpyT";
        //client secret
        private const string M_CLIENT_SECRET = "lsACyCD94FhDUtGTXi3QzcFE2uU1hqtDaKeqrdwj";
        private const string M_APP_USER_AGENT = "PixivAndroidApp/5.0.54 (Android 6.0.1; MI 5s)";
        #endregion //Constant Definations

        //构造函数及初始化
        //todo: 分离初始化时的创建sql，移到patch块
        #region Constructor

        /// <summary>
        /// 默认构造函数，用于初始化Pixiv的数据库信息
        /// </summary>
        /// <param name="path">本地含p站图片的目录</param>
        /// <param name="include_sub_dir">是否包含子文件夹</param>
        /// <param name="ignore_non_200">是否忽略未成功获取的投稿信息</param>
        /// <remarks>STA</remarks>
        public dataUpdater(bool include_sub_dir = true, bool ignore_non_200 = false)
        {
            Debug.Print("Hello! Welcome using my pixiv class!\r\nHope you enjoy it!");
            //初始化成员变量
            m_include_subdir = include_sub_dir;
            m_ignore_non_200_status = ignore_non_200;
            m_sqlThreadLock = new ReaderWriterLock();
            m_dataThreadLock = new ReaderWriterLock();

            m_illust_thd = new Thread[M_MAX_ILLUST_SYNC_THREAD];
            m_user_thd = new Thread[M_MAX_USER_SYNC_THREAD];

            _create_monitor_thread();

            m_illust_query_list = new List<uint>();
            m_user_query_list = new List<uint>();

            Debug.Print("Started SQL Database Initialize");
            InitializeStarted?.Invoke();
            //初始化数据库
            _init_database();
            Debug.Print("Ended SQL Database Initialize");
            InitializeEnded?.Invoke();

            //开始监控线程
            m_monitor_thd.Start();
        }

        //初始化数据库 [STA]
        private void _init_database()
        {
            string create_db = "Data Source=appdata.db; Version=3;"; //declare using SQL v3
            bool file_exist = File.Exists("appdata.db");
            if (!file_exist)
            {
                Debug.Print("System found the database does not exist, the program will create a new one.");
                File.Create("appdata.db").Close();
            }

            Debug.Print("Connecting to SQL database.");
            m_dbConnection = new SQLiteConnection(create_db);
            m_dbConnection.Open();

            m_dbCommand = new SQLiteCommand(m_dbConnection);

            m_illust_list = new Dictionary<uint, int>();
            m_user_list = new Dictionary<uint, int>();

            //not exist: creating basic structure
            if (!file_exist)
            {
                Debug.Print("Creating basic table and inserting variables into database.");

                string create_var_table = "CREATE TABLE DbVars(Key VARCHAR PRIMARY KEY, Value VARCHAR)";
                string create_user_table = "CREATE TABLE User(ID INT PRIMARY KEY, Name VARCHAR, Description TEXT, User_Face IMAGE, User_Face_Url VARCHAR, Home_Page VARCHAR, Gender VARCHAR, Personal_Tag VARCHAR, Address VARCHAR, Birthday VARCHAR, Job VARCHAR, Follow_Users INT, Follower INT, Illust_Bookmark_Public INT, Mypixiv_Users INT, Total_Illusts INT, Total_Novels INT, Twitter VARCHAR, HTTP_Status INT NOT NULL, Last_Update BIGINT NOT NULL DEFAULT 0, Last_Success_Update BIGINT NOT NULL DEFAULT 0)";
                string create_illust_table = "CREATE TABLE Illust(ID INT PRIMARY KEY, Author_ID INT NOT NULL, Page INT NOT NULL DEFAULT 1, Title VARCHAR, Description TEXT, Tag VARCHAR, Tool VARCHAR, Click INT NOT NULL DEFAULT 0, Bookmark_Count INT NOT NULL DEFAULT 0, Comment_Count INT NOT NULL DEFAULT 0, Rate_Count INT NOT NULL DEFAULT 0, Score INT NOT NULL DEFAULT 0, Width INT NOT NULL DEFAULT 0, Height INT NOT NULL DEFAULT 0, Submit_Time BIGINT NOT NULL DEFAULT 0, HTTP_Status INT NOT NULL DEFAULT 0, Last_Update BIGINT NOT NULL DEFAULT 0, Last_Success_Update BIGINT NOT NULL DEFAULT 0, Origin TINYINT NOT NULL DEFAULT 0)";
                string write_version_info = "INSERT INTO DbVars VALUES('Version', '" + M_CURRENT_DBVERSION + "')";
                m_dbCommand.CommandText = create_var_table;
                m_dbCommand.ExecuteNonQuery();
                m_dbCommand.CommandText = write_version_info;
                m_dbCommand.ExecuteNonQuery();
                m_dbCommand.CommandText = create_user_table;
                m_dbCommand.ExecuteNonQuery();
                m_dbCommand.CommandText = create_illust_table;
                m_dbCommand.ExecuteNonQuery();
            }
            //exist:fetching data to indexer (id and update time)
            else
            {
                Debug.Print("Loading SQL database config.");
                //检查是否存在数据表
                var check_table_count = "SELECT COUNT(*) FROM sqlite_master WHERE type='table'";
                m_dbCommand.CommandText = check_table_count;
                int count = Convert.ToInt32(m_dbCommand.ExecuteScalar());
                if (count == 0)
                {
                    Debug.Print("We found that the database contains no table, it should not be the normal state, deleting and re-initing it");
                    m_dbCommand.Dispose();
                    m_dbCommand = null;
                    if (m_dbTransaction != null)
                    {
                        m_dbTransaction.Rollback();
                        m_dbTransaction.Dispose();
                        m_dbTransaction = null;
                    }
                    m_dbConnection.Close();
                    m_dbConnection.Dispose();
                    m_dbConnection = null;
                    File.Delete("appdata.db");
                    _init_database();
                    return;
                }
                //comparing current path and db path, if not matches, it will be deleted and rebuilded
                string get_db_version = "SELECT Value FROM DbVars WHERE Key='Version'";
                m_dbCommand.CommandText = get_db_version;
                var dr0 = m_dbCommand.ExecuteReader();
                dr0.Read();
                string db_version = dr0.GetString(0);
                dr0.Close();

                Debug.Print("Database version:" + db_version + " (Current:" + M_CURRENT_DBVERSION + ")");
                //更新数据库
                if (db_version != M_CURRENT_DBVERSION)
                {
                    dbPatcher.Patch(db_version, M_CURRENT_DBVERSION, m_dbConnection);
                }

                Debug.Print("Initialize data to RAM.");

                string get_illust_id = "SELECT ID, HTTP_Status FROM Illust";
                m_dbCommand.CommandText = get_illust_id;
                var dr = m_dbCommand.ExecuteReader();
                while (dr.Read())
                {
                    uint id = (uint)dr.GetInt32(0);
                    int http_status = dr.GetInt32(1);
                    m_illust_list.Add(id, http_status);
                    if (http_status == 0 || http_status == -2 || (http_status > 0 && http_status != (int)HttpStatusCode.OK && !m_ignore_non_200_status))
                    {
                        m_illust_query_list.Add(id);
                        m_query_count++;
                    }
                }
                dr.Close();

                string get_user_id = "SELECT ID, HTTP_Status FROM User";
                m_dbCommand.CommandText = get_user_id;
                dr = m_dbCommand.ExecuteReader();
                while (dr.Read())
                {
                    uint id = (uint)dr.GetInt32(0);
                    int http_status = dr.GetInt32(1);
                    m_user_list.Add(id, http_status);
                    if (http_status == 0 || http_status == -2 || (http_status > 0 && http_status != (int)HttpStatusCode.OK && !m_ignore_non_200_status))
                    {
                        m_user_query_list.Add(id);
                        m_query_count++;
                    }
                }
                dr.Close();
            }
        }

        #endregion //Constructor

        //事件定义
        #region Event Definations

        //事件定义
        public delegate void NoArgEventHandler();
        //开始初始化数据（这个应该不会被执行到，因为在构造函数时已经调用初始化函数了）
        public event NoArgEventHandler InitializeStarted;
        //完成数据的初始化
        public event NoArgEventHandler InitializeEnded;

        //开始更新本地文件列表
        public event NoArgEventHandler UpdateLocalFileListStarted;
        //更新本地文件列表结束
        public event NoArgEventHandler UpdateLocalFileListEnded;

        //登陆成功
        public static event NoArgEventHandler LoginSucceeded;
        public delegate void StrArgEventHandler(string arg);
        //登陆失败，arg:登陆失败后的html代码，Todo：解析html代码
        public static event StrArgEventHandler LoginFailed;

        public delegate void FetchDataStartedHandler(uint id, uint currentTask, uint totalTask);
        public delegate void FetchIllustEndedHandler(uint id, uint currentTask, uint totalTask, Illust data);
        public delegate void FetchUserEndedHandler(uint id, uint currentTask, uint TotalTask, User data);
        //开始获取投稿数据（当前的id,当前任务在任务队列的位置，总任务队列的数量）
        public event FetchDataStartedHandler FetchIllustStarted;
        //成功获取单个投稿数据（参数同上，多了个投稿的数据）
        public event FetchIllustEndedHandler FetchIllustSucceeded;
        //获取单个投稿数据失败
        public event FetchIllustEndedHandler FetchIllustFailed;
        public event FetchDataStartedHandler FetchUserStarted;
        public event FetchUserEndedHandler FetchUserSucceeded;
        public event FetchUserEndedHandler FetchUserFailed;

        //需要登陆后才能操作
        public event NoArgEventHandler LoginRequired;

        #endregion //Event Definations

        //数据导入
        #region Data Import

        #region From Path
        //从path的本地目录中跟新文件列表 [STA]
        private void _update_file_list(string path)
        {
            Debug.Print("Updating file from directory: " + path);

            var dir_info = new DirectoryInfo(path);
            //在pixiv默认保存的文件名中，会符合以下的正则匹配的表达式： (\d+)_p(\d+).(jpg|png|gif) 如233333_p0.jpg
            //开启sql的transaction，这样不会在执行修改指令时就立刻执行，效率+++
            if (!dir_info.Exists) return;

            foreach (var item in dir_info.GetFiles())
            {
                const string regex_pattern = @"(?<id>\d+)_p(?<page>\d+)\.(?<ext>jpg|png)";
                var match = Regex.Match(item.Name, regex_pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    uint id = uint.Parse(match.Result("${id}"));
                    uint page = uint.Parse(match.Result("${page}"));

                    var exist_id = m_illust_list.ContainsKey(id);

                    //在sql中不存在该作品，应该就是新增的了，加入到sql和内存列表中
                    if (!exist_id)
                    {
                        //写入数据
                        Illust illust = new Illust();
                        illust.ID = id;
                        __auto_insert_illust(illust);
                        System.Diagnostics.Debug.Print("Fetching: Illust " + id);
                    }
                }
            }
            //递归调用，获取子目录
            if (m_include_subdir)
            {
                foreach (var item in dir_info.GetDirectories())
                {
                    _update_file_list(item.FullName);
                }
            }
        }

        //update local database from the current directory.
        public void UpdateFileList(string path)
        {
            ThreadPool.QueueUserWorkItem(
                (object obj) =>
                {

                    UpdateLocalFileListStarted?.Invoke();

                    m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                    m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);
                    if (m_dbTransaction == null)
                        m_dbTransaction = m_dbConnection.BeginTransaction();

                    try
                    {
                        _update_file_list(path);
                    }
                    catch (Exception ex)
                    {

                        Debug.Print("Error occurred while updating local file list: " + ex.ToString());
                        throw;
                    }

                    if (m_dbTransaction != null)
                    {
                        m_dbTransaction.Commit();
                        m_dbTransaction = null;
                    }
                    m_sqlThreadLock.ReleaseWriterLock();

                    //making it to a list!~
                    _update_query_list();
                    m_dataThreadLock.ReleaseWriterLock();

                    UpdateLocalFileListEnded?.Invoke();

                    //syncing data
                    _create_monitor_thread();
                });
        }
        #endregion //From Path


        #endregion //Data Import

        //多线程调度控制
        #region Multi-Thread Access Control
        //获取数据线程的回调函数 [MTA]
        private void _sync_illust_callback()
        {
            if (m_illust_working_thd_count >= M_MAX_ILLUST_SYNC_THREAD) return;
            //Debug.Print("Sync Illust Thread Created");
            m_illust_working_thd_count++;

            uint id;
            do
            {
                id = 0;
                m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);

                if (m_illust_query_list.Count != 0)
                {
                    id = m_illust_query_list[0];
                    m_querying_count++;
                    m_illust_query_list.RemoveAt(0);
                }

                m_dataThreadLock.ReleaseWriterLock();

                if (id == 0) break;

                //开始获取
                Debug.Print("Started fetching illust id=" + id);
                FetchIllustStarted?.Invoke(id, (uint)m_query_finished, (uint)m_query_count);

                m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                Illust illust = __get_illust(id);
                m_sqlThreadLock.ReleaseWriterLock();
                //Illust illust = new Illust();
                try
                {
                    //解析
                    _parse_illust_info(id, out illust);

                    m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                    __auto_insert_illust(illust);
                    if (illust.Author_ID != 0)
                    {
                        //是否存在该用户，若不存在则写入
                        bool exist_user = m_user_list.ContainsKey(illust.Author_ID);
                        if (!exist_user)
                        {
                            User user = new User();
                            user.ID = illust.Author_ID;
                            __auto_insert_user(user);

                            m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);
                            //加入到查询列表
                            m_user_query_list.Add(illust.Author_ID);
                            m_query_count++;
                            m_dataThreadLock.ReleaseWriterLock();
                        }
                    }
                    m_sqlThreadLock.ReleaseWriterLock();

                    FetchIllustSucceeded?.Invoke(id, (uint)m_query_finished + 1, (uint)m_query_count, illust);
                }
                catch (Exception ex) //获取投稿时出错
                {
                    Debug.Print("Exception occured while fetching illust info: \n" + ex.ToString());

                    //理论上在获取投稿信息时会自动把http code给补上，所以不需要再检查response了
                    m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                    __auto_insert_illust(illust);
                    m_sqlThreadLock.ReleaseWriterLock();
                    //throw;
                    FetchIllustFailed?.Invoke(id, (uint)m_query_finished + 1, (uint)m_query_count, illust);
                }
                finally
                {
                    m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);
                    m_query_finished++;
                    m_querying_count--;
                    m_dataThreadLock.ReleaseWriterLock();
                }
            } while (id != 0 && !m_abort_flag);

            m_illust_working_thd_count--;
            //Debug.Print("Sync Illust Thread Exited");
        }
        private void _sync_user_callback()
        {
            if (m_user_working_thd_count >= M_MAX_USER_SYNC_THREAD) return;
            //Debug.Print("Sync User Thread Created");
            m_user_working_thd_count++;

            uint id;
            do
            {
                id = 0;
                m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);

                if (m_user_query_list.Count != 0)
                {
                    id = m_user_query_list[0];
                    m_user_query_list.RemoveAt(0);
                    m_querying_count++;
                }

                m_dataThreadLock.ReleaseWriterLock();

                if (id == 0) break;

                //开始获取
                Debug.Print("Started fetching user id=" + id);
                FetchUserStarted?.Invoke(id, (uint)m_query_finished, (uint)m_query_count);

                m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                User user = __get_user(id);
                m_sqlThreadLock.ReleaseWriterLock();
                //User user = new User();
                try
                {
                    //解析
                    _parse_user_info(id, out user);

                    m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                    __auto_insert_user(user);
                    m_sqlThreadLock.ReleaseWriterLock();

                    FetchUserSucceeded?.Invoke(id, (uint)m_query_finished + 1, (uint)m_query_count, user);
                }
                catch (Exception ex) //获取投稿时出错
                {
                    Debug.Print("Exception occured while fetching illust info: \n" + ex.ToString());

                    //理论上在获取投稿信息时会自动把http code给补上，所以不需要再检查response了
                    m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                    __auto_insert_user(user);
                    m_sqlThreadLock.ReleaseWriterLock();
                    //throw;
                    FetchUserFailed?.Invoke(id, (uint)m_query_finished + 1, (uint)m_query_count, user);
                }
                finally
                {
                    m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);
                    m_query_finished++;
                    m_querying_count--;
                    m_dataThreadLock.ReleaseWriterLock();
                }
            } while (id != 0 && !m_abort_flag);

            m_user_working_thd_count--;
            //Debug.Print("Sync User Thread Exited");
        }
        private void _monitor_callback()
        {
            do
            {
                if (Is_Logined)
                {

                    try
                    {
                        m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);
                        int illust_count = m_illust_query_list.Count;
                        m_dataThreadLock.ReleaseWriterLock();

                        if (m_illust_working_thd_count < M_MAX_ILLUST_SYNC_THREAD && illust_count != 0)
                        {

                            //Debug.Print("Multi-thread access for illust started!");
                            if (m_dbTransaction == null)
                            {
                                m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                                m_dbTransaction = m_dbConnection.BeginTransaction();
                                m_sqlThreadLock.ReleaseWriterLock();
                            }

                            for (int i = m_illust_working_thd_count; i < M_MAX_ILLUST_SYNC_THREAD; i++)
                            {
                                m_illust_thd[i] = new Thread(_sync_illust_callback);
                                m_illust_thd[i].Name = "Illust Work Thread (#" + i + ")";
                                m_illust_thd[i].IsBackground = false;
                                m_illust_thd[i].Start();
                            }

                        }

                        m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);
                        int user_count = m_user_query_list.Count;
                        m_dataThreadLock.ReleaseWriterLock();

                        if (m_user_working_thd_count < M_MAX_USER_SYNC_THREAD && user_count != 0)
                        {
                            //Debug.Print("Multi-thread access for user started!");
                            if (m_dbTransaction == null)
                            {
                                m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                                m_dbTransaction = m_dbConnection.BeginTransaction();
                                m_sqlThreadLock.ReleaseWriterLock();
                            }

                            for (int i = m_user_working_thd_count; i < M_MAX_USER_SYNC_THREAD; i++)
                            {
                                m_user_thd[i] = new Thread(_sync_user_callback);
                                m_user_thd[i].Name = "User Work Thread (#" + i + ")";
                                m_user_thd[i].IsBackground = false;
                                m_user_thd[i].Start();
                            }
                        }

                    }
                    catch (ThreadAbortException)
                    {
                        //thread aborting, stopping all working thread
                        m_abort_flag = true;
                    }
                    catch (Exception ex)
                    {
                        Debug.Print("Unexpected exception happed in monitor thread:");
                        Debug.Print(ex.ToString());
                    }

                    _join_all_thread(1000);

                    //线程为空，提交数据
                    if (m_illust_working_thd_count == 0 && m_user_working_thd_count == 0)
                    {
                        if (m_dbTransaction != null)
                        {
                            Debug.Print("Thread All Exited, Commiting data");
                            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                            m_dbTransaction.Commit();
                            m_dbTransaction = null;
                            m_sqlThreadLock.ReleaseWriterLock();
                        }
                    }

                    //更新列表为空，线程空闲，清空队列数量缓存
                    m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);
                    int ic = m_illust_query_list.Count, uc = m_user_query_list.Count;
                    m_dataThreadLock.ReleaseLock();

                    if (ic != 0 && uc != 0 && m_illust_working_thd_count == 0 && m_user_working_thd_count == 0)
                    {
                        Debug.Print("Query completed, resetting query list size");
                        m_query_count = 0;
                        m_query_finished = 0;
                    }

                    //Debug.Print("[Debug] - m_illust_working_thd_count = " + m_illust_working_thd_count + ", m_user_working_thd_count = " + m_user_working_thd_count + ", m_illust_query_list = " + ic + ", m_user_query_list = " + uc);

                    Thread.Sleep(1000);
                }
            } while (!m_abort_flag);

            Debug.Print("Abort signal received, waiting all thread to be aborted...");
            _join_all_thread();

            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
            if (m_dbTransaction != null)
            {
                Debug.Print("Commiting data");
                m_dbTransaction.Commit();
                m_dbTransaction = null;
            }
            m_sqlThreadLock.ReleaseWriterLock();
            //线程安全退出，取消终止标识
            Debug.Print("Monitor Thread exited");
            m_monitor_thd = null;
            m_abort_flag = false;
        }

        private void _create_monitor_thread()
        {
            if (m_monitor_thd == null)
            {
                m_monitor_thd = new Thread(_monitor_callback);
                m_monitor_thd.IsBackground = false;
                m_monitor_thd.Name = "Pixiv Data Monitor Thread";
            }
        }
        //从list中更新数据到query list [STA]
        private void _update_query_list()
        {
            m_illust_query_list.Clear();
            m_user_query_list.Clear();
            foreach (var item in m_illust_list)
            {
                if (item.Value == 0 || item.Value == -2 || (item.Value > 0 && item.Value != (int)HttpStatusCode.OK && !m_ignore_non_200_status))
                    m_illust_query_list.Add(item.Key);
            }
            foreach (var item in m_user_list)
            {
                if (item.Value == 0 || item.Value == -2 || (item.Value > 0 && item.Value != (int)HttpStatusCode.OK && !m_ignore_non_200_status))
                    m_user_query_list.Add(item.Key);
            }
            m_query_count = m_illust_query_list.Count + m_user_query_list.Count + m_querying_count;
            m_query_finished = 0;
            Debug.Print("Illust queue: " + m_illust_query_list.Count + ", User queue: " + m_user_query_list.Count + ", Querying: " + m_querying_count);
        }

        private void _join_all_thread(int timeout = int.MaxValue)
        {
            for (int i = 0; i < m_illust_thd.Length; i++)
                m_illust_thd[i]?.Join(timeout);
            for (int i = 0; i < m_user_thd.Length; i++)
                m_user_thd[i]?.Join(timeout);
        }
        #endregion //Multi-Thread Access Control

        //sql读写操作[STA]
        #region SQL Operations

        #region SQL Operations For Illust

        //向sql中自动插入投稿数据
        private bool __insert_illust(Illust illust)
        {
            var insert_str = "INSERT INTO Illust(ID, Author_ID, Page, Origin, Title, Description, Tag, Tool, Click, Bookmark_Count, Comment_Count, Width, Height, Rate_Count, Score, Submit_Time, HTTP_Status, Last_Update, Last_Success_Update) VALUES(@ID, @Author_ID, @Page, @Origin";
            m_dbCommand.Parameters.Add("@ID", DbType.Int32);
            m_dbCommand.Parameters["@ID"].Value = illust.ID;
            m_dbCommand.Parameters.Add("@Author_ID", DbType.Int32);
            m_dbCommand.Parameters["@Author_ID"].Value = illust.Author_ID;
            m_dbCommand.Parameters.Add("@Page", DbType.Int32);
            m_dbCommand.Parameters["@Page"].Value = illust.Page;
            m_dbCommand.Parameters.Add("@Origin", DbType.Byte);
            m_dbCommand.Parameters["@Origin"].Value = illust.Origin;

            if (!string.IsNullOrEmpty(illust.Title))
            {
                insert_str += ", @Title";
                m_dbCommand.Parameters.Add("@Title", DbType.String);
                m_dbCommand.Parameters["@Title"].Value = illust.Title;
            }
            else
                insert_str += ", NULL";
            if (!string.IsNullOrEmpty(illust.Description))
            {
                insert_str += ", @Description";
                m_dbCommand.Parameters.Add("@Description", DbType.String);
                m_dbCommand.Parameters["@Description"].Value = illust.Description;
            }
            else
                insert_str += ", NULL";
            if (!string.IsNullOrEmpty(illust.Tag))
            {
                insert_str += ", @Tag";
                m_dbCommand.Parameters.Add("@Tag", DbType.String);
                m_dbCommand.Parameters["@Tag"].Value = illust.Tag;
            }
            else
                insert_str += ", NULL";

            if (!string.IsNullOrEmpty(illust.Tool))
            {
                insert_str += ", @Tool";
                m_dbCommand.Parameters.Add("@Tool", DbType.String);
                m_dbCommand.Parameters["@Tool"].Value = illust.Tool;
            }
            else
                insert_str += ", NULL";

            insert_str += ", @Click, @Bookmark_Count, @Comment_Count, @Width, @Height, @Rate_Count, @Score, @Submit_Time, @HTTP_Status, @Last_Update, @Last_Success_Update)";

            m_dbCommand.Parameters.Add("@Click", DbType.Int32);
            m_dbCommand.Parameters["@Click"].Value = illust.Click;
            m_dbCommand.Parameters.Add("@Bookmark_Count", DbType.Int32);
            m_dbCommand.Parameters["@Bookmark_Count"].Value = illust.Bookmark_Count;
            m_dbCommand.Parameters.Add("@Comment_Count", DbType.Int32);
            m_dbCommand.Parameters["@Comment_Count"].Value = illust.Comment_Count;
            m_dbCommand.Parameters.Add("@Width", DbType.Int32);
            m_dbCommand.Parameters["@Width"].Value = illust.Size.Width;
            m_dbCommand.Parameters.Add("@Height", DbType.Int32);
            m_dbCommand.Parameters["@Height"].Value = illust.Size.Height;
            m_dbCommand.Parameters.Add("@Rate_Count", DbType.Int32);
            m_dbCommand.Parameters["@Rate_Count"].Value = illust.Rate_Count;
            m_dbCommand.Parameters.Add("@Score", DbType.Int32);
            m_dbCommand.Parameters["@Score"].Value = illust.Score;
            m_dbCommand.Parameters.Add("@Submit_Time", DbType.Int64);
            m_dbCommand.Parameters["@Submit_Time"].Value = illust.Submit_Time;
            m_dbCommand.Parameters.Add("@HTTP_Status", DbType.Int32);
            m_dbCommand.Parameters["@HTTP_Status"].Value = illust.HTTP_Status;
            m_dbCommand.Parameters.Add("@Last_Update", DbType.Int64);
            m_dbCommand.Parameters["@Last_Update"].Value = illust.Last_Update;
            m_dbCommand.Parameters.Add("@Last_Success_Update", DbType.Int64);
            m_dbCommand.Parameters["@Last_Success_Update"].Value = illust.Last_Success_Update;

            m_dbCommand.CommandText = insert_str;
            try
            {
                m_dbCommand.ExecuteNonQuery();
                m_illust_list.Add(illust.ID, illust.HTTP_Status);
                return true;
            }
            catch (Exception ex)
            {
                Debug.Print("Error occured when updating illust data!:" + ex.ToString());
                return false;
            }
            finally
            {
                m_dbCommand.Parameters.Clear();
            }
        }
        private bool __update_illust(Illust illust, bool force_mode = false)
        {
            var update_str = "UPDATE Illust SET HTTP_Status=@HTTP_Status, Last_Update=@Last_Update";
            m_dbCommand.Parameters.Add("@HTTP_Status", DbType.Int32);
            m_dbCommand.Parameters["@HTTP_Status"].Value = illust.HTTP_Status;
            m_dbCommand.Parameters.Add("@Last_Update", DbType.Int32);
            m_dbCommand.Parameters["@Last_Update"].Value = illust.Last_Update;

            //数据保护：非200时不覆盖写入已存数据
            if (force_mode || illust.HTTP_Status == (int)HttpStatusCode.OK)
            {
                update_str += ", Author_ID=@Author_ID, Submit_Time=@Submit_Time, Last_Success_Update=@Last_Success_Update, Page=@Page, Origin=@Origin";
                m_dbCommand.Parameters.Add("@Author_ID", DbType.Int32);
                m_dbCommand.Parameters["@Author_ID"].Value = illust.Author_ID;
                m_dbCommand.Parameters.Add("@Submit_Time", DbType.Int64);
                m_dbCommand.Parameters["@Submit_Time"].Value = illust.Submit_Time;
                m_dbCommand.Parameters.Add("@Last_Success_Update", DbType.Int64);
                m_dbCommand.Parameters["@Last_Success_Update"].Value = illust.Last_Success_Update;
                m_dbCommand.Parameters.Add("@Page", DbType.Int32);
                m_dbCommand.Parameters["@Page"].Value = illust.Page;
                m_dbCommand.Parameters.Add("@Origin", DbType.Byte);
                m_dbCommand.Parameters["@Origin"].Value = illust.Origin;

                if (illust.Click > 0)
                {
                    update_str += ",Click=@Click";
                    m_dbCommand.Parameters.Add("@Click", DbType.Int32);
                    m_dbCommand.Parameters["@Click"].Value = illust.Click;
                }
                if (illust.Bookmark_Count > 0)
                {
                    update_str += ",Bookmark_Count=@Bookmark_Count";
                    m_dbCommand.Parameters.Add("@Bookmark_Count", DbType.Int32);
                    m_dbCommand.Parameters["@Bookmark_Count"].Value = illust.Bookmark_Count;
                }
                if (illust.Comment_Count > 0)
                {
                    update_str += ",Comment_Count=@Comment_Count";
                    m_dbCommand.Parameters.Add("@Comment_Count", DbType.Int32);
                    m_dbCommand.Parameters["@Comment_Count"].Value = illust.Comment_Count;
                }
                if (illust.Click > 0)
                {
                    update_str += ",Click=@Click";
                    m_dbCommand.Parameters.Add("@Click", DbType.Int32);
                    m_dbCommand.Parameters["@Click"].Value = illust.Click;
                }
                if (illust.Size.Width > 0)
                {
                    update_str += ",Width=@Width";
                    m_dbCommand.Parameters.Add("@Width", DbType.Int32);
                    m_dbCommand.Parameters["@Width"].Value = illust.Size.Width;
                }
                if (illust.Size.Height > 0)
                {
                    update_str += ",Height=@Height";
                    m_dbCommand.Parameters.Add("@Height", DbType.Int32);
                    m_dbCommand.Parameters["@Height"].Value = illust.Size.Height;
                }
                if (illust.Rate_Count > 0)
                {
                    update_str += ",Rate_Count=@Rate_Count";
                    m_dbCommand.Parameters.Add("@Rate_Count", DbType.Int32);
                    m_dbCommand.Parameters["@Rate_Count"].Value = illust.Rate_Count;
                }
                if (illust.Score > 0)
                {
                    update_str += ",Score=@Score";
                    m_dbCommand.Parameters.Add("@Score", DbType.Int32);
                    m_dbCommand.Parameters["@Score"].Value = illust.Score;
                }
                if (!string.IsNullOrEmpty(illust.Title))
                {
                    update_str += ",Title=@Title";
                    m_dbCommand.Parameters.Add("@Title", DbType.String);
                    m_dbCommand.Parameters["@Title"].Value = illust.Title;
                }
                if (!string.IsNullOrEmpty(illust.Description))
                {
                    update_str += ",Description=@Description";
                    m_dbCommand.Parameters.Add("@Description", DbType.String);
                    m_dbCommand.Parameters["@Description"].Value = illust.Description;
                }
                if (!string.IsNullOrEmpty(illust.Tag))
                {
                    update_str += ",Tag=@Tag";
                    m_dbCommand.Parameters.Add("@Tag", DbType.String);
                    m_dbCommand.Parameters["@Tag"].Value = illust.Tag;
                }
                if (!string.IsNullOrEmpty(illust.Tool))
                {
                    update_str += ",Tool=@Tool";
                    m_dbCommand.Parameters.Add("@Tool", DbType.String);
                    m_dbCommand.Parameters["@Tool"].Value = illust.Tool;
                }

            }

            update_str += " WHERE ID=@ID";
            m_dbCommand.Parameters.Add("@ID", DbType.Int32);
            m_dbCommand.Parameters["@ID"].Value = illust.ID;

            m_dbCommand.CommandText = update_str;
            try
            {
                m_dbCommand.ExecuteNonQuery();
                m_illust_list[illust.ID] = illust.HTTP_Status;
                return true;
            }
            catch (Exception ex)
            {
                Debug.Print("Error occured when updating illust data!:" + ex.ToString());
                return false;
            }
            finally
            {
                m_dbCommand.Parameters.Clear();
            }
        }
        private bool __auto_insert_illust(Illust illust)
        {
            if (m_illust_list.ContainsKey(illust.ID))
                return __update_illust(illust);
            else
                return __insert_illust(illust);
        }
        private Illust __get_illust(uint id)
        {
            var get_value_str = "SELECT ID, Author_ID, Title, Description, Tag, Tool, Click, Bookmark_Count, Comment_Count, Width, Height, Rate_Count, Score, Submit_Time, HTTP_Status, Last_Update, Last_Success_Update, Page, Origin FROM Illust WHERE ID=" + id;
            var ret = new Illust();
            if (id == 0) return ret;
            try
            {
                m_dbCommand.CommandText = get_value_str;
                var dr = m_dbCommand.ExecuteReader();
                bool suc = dr.Read();
                if (!suc)
                {
                    ret.ID = id;
                    return ret;
                }
                ret.ID = (uint)dr.GetInt32(0);
                ret.Author_ID = (uint)dr.GetInt32(1);
                ret.Title = dr.IsDBNull(2) ? "" : dr.GetString(2);
                ret.Description = dr.IsDBNull(3) ? "" : dr.GetString(3);
                ret.Tag = dr.IsDBNull(4) ? "" : dr.GetString(4);
                ret.Tool = dr.IsDBNull(5) ? "" : dr.GetString(5);
                ret.Click = dr.GetInt32(6);
                ret.Bookmark_Count = dr.GetInt32(7);
                ret.Comment_Count = dr.GetInt32(8);
                ret.Size = new Size(dr.GetInt32(9), dr.GetInt32(10));
                ret.Rate_Count = dr.GetInt32(11);
                ret.Score = dr.GetInt32(12);
                ret.Submit_Time = (ulong)dr.GetInt64(13);
                ret.HTTP_Status = dr.GetInt32(14);
                ret.Last_Update = (ulong)dr.GetInt64(15);
                ret.Last_Success_Update = (ulong)dr.GetInt64(16);
                ret.Page = (uint)dr.GetInt32(17);
                ret.Origin = (DataOrigin)dr.GetByte(18);
                dr.Close();
            }
            catch (Exception)
            {

            }
            return ret;
        }

        #endregion //SQL Operations For Illust


        #region SQL Operations For User

        //插入用户信息到sql和内存列表中（自动跳过null参数） [STA]
        private bool __insert_user(User user)
        {
            var insert_user_data = "INSERT INTO User(ID, Name, Description, User_Face, User_Face_Url, Home_Page, Gender, Personal_Tag, Address, Birthday, Job, Follow_Users, Follower, Illust_Bookmark_Public, Mypixiv_Users, Total_Illusts, Total_Novels, Twitter, HTTP_Status, Last_Update, Last_Success_Update) VALUES(@ID";
            m_dbCommand.Parameters.Add("@ID", DbType.Int32);
            m_dbCommand.Parameters["@ID"].Value = user.ID;
            if (!string.IsNullOrEmpty(user.Name))
            {
                insert_user_data += ",@Name";
                m_dbCommand.Parameters.Add("@Name", DbType.String);
                m_dbCommand.Parameters["@Name"].Value = user.Name;
            }
            else
                insert_user_data += ",NULL";

            if (!string.IsNullOrEmpty(user.Description))
            {
                insert_user_data += ",@Description";
                m_dbCommand.Parameters.Add("@Description", DbType.String);
                m_dbCommand.Parameters["@Description"].Value = user.Description;
            }
            else
                insert_user_data += ",NULL";

            if (user.User_Face != null)
            {
                var mm = new MemoryStream();
                user.User_Face.Save(mm, user.User_Face.RawFormat);
                mm.Position = 0;
                byte[] buf = new byte[mm.Length];
                mm.Read(buf, 0, (int)mm.Length);

                insert_user_data += ",@User_Face";
                m_dbCommand.Parameters.Add("@User_Face", DbType.Binary);
                m_dbCommand.Parameters["@User_Face"].Value = buf;
            }
            else
                insert_user_data += ",NULL";

            if (!string.IsNullOrEmpty(user.User_Face_Url))
            {
                insert_user_data += ",@User_Face_Url";
                m_dbCommand.Parameters.Add("@User_Face_Url", DbType.String);
                m_dbCommand.Parameters["@User_Face_Url"].Value = user.User_Face_Url;
            }
            else
                insert_user_data += ",NULL";

            if (!string.IsNullOrEmpty(user.Home_Page))
            {
                insert_user_data += ",@Home_Page";
                m_dbCommand.Parameters.Add("@Home_Page", DbType.String);
                m_dbCommand.Parameters["@Home_Page"].Value = user.Home_Page;
            }
            else
                insert_user_data += ",NULL";

            if (!string.IsNullOrEmpty(user.Gender))
            {
                insert_user_data += ",@Gender";
                m_dbCommand.Parameters.Add("@Gender", DbType.String);
                m_dbCommand.Parameters["@Gender"].Value = user.Gender;
            }
            else
                insert_user_data += ",NULL";

            if (!string.IsNullOrEmpty(user.Personal_Tag))
            {
                insert_user_data += ",@Personal_Tag";
                m_dbCommand.Parameters.Add("@Personal_Tag", DbType.String);
                m_dbCommand.Parameters["@Personal_Tag"].Value = user.Personal_Tag;
            }
            else
                insert_user_data += ",NULL";

            if (!string.IsNullOrEmpty(user.Address))
            {
                insert_user_data += ",@Address";
                m_dbCommand.Parameters.Add("@Address", DbType.String);
                m_dbCommand.Parameters["@Address"].Value = user.Address;
            }
            else
                insert_user_data += ",NULL";

            if (user.Birthday != null)
            {
                insert_user_data += ",@Birthday";
                m_dbCommand.Parameters.Add("@Birthday", DbType.String);
                m_dbCommand.Parameters["@Birthday"].Value = user.Birthday;
            }
            else
                insert_user_data += ",NULL";

            if (user.Job != null)
            {
                insert_user_data += ",@Job";
                m_dbCommand.Parameters.Add("@Job", DbType.String);
                m_dbCommand.Parameters["@Job"].Value = user.Job;
            }
            else
                insert_user_data += ",NULL";

            insert_user_data += ",@Follow_Users,@Follower,@Illust_Bookmark_Public,@Mypixiv_Users,@Total_Illusts,@Total_Novels";
            m_dbCommand.Parameters.Add("@Follow_Users", DbType.Int32);
            m_dbCommand.Parameters["@Follow_Users"].Value = user.Follow_Users;
            m_dbCommand.Parameters.Add("@Follower", DbType.Int32);
            m_dbCommand.Parameters["@Follower"].Value = user.Follower;
            m_dbCommand.Parameters.Add("@Illust_Bookmark_Public", DbType.Int32);
            m_dbCommand.Parameters["@Illust_Bookmark_Public"].Value = user.Illust_Bookmark_Public;
            m_dbCommand.Parameters.Add("@Mypixiv_Users", DbType.Int32);
            m_dbCommand.Parameters["@Mypixiv_Users"].Value = user.Mypixiv_Users;
            m_dbCommand.Parameters.Add("@Total_Illusts", DbType.Int32);
            m_dbCommand.Parameters["@Total_Illusts"].Value = user.Total_Illusts;
            m_dbCommand.Parameters.Add("@Total_Novels", DbType.Int32);
            m_dbCommand.Parameters["@Total_Novels"].Value = user.Total_Novels;

            if (!string.IsNullOrEmpty(user.Twitter))
            {
                insert_user_data += ",@Twitter";
                m_dbCommand.Parameters.Add("@Twitter", DbType.String);
                m_dbCommand.Parameters["@Twitter"].Value = user.Twitter;
            }
            else
                insert_user_data += ",NULL";

            insert_user_data += ",@HTTP_Status,@Last_Update,@Last_Success_Update)";
            m_dbCommand.Parameters.Add("@HTTP_Status", DbType.Int32);
            m_dbCommand.Parameters["@HTTP_Status"].Value = user.HTTP_Status;
            m_dbCommand.Parameters.Add("@Last_Update", DbType.Int64);
            m_dbCommand.Parameters["@Last_Update"].Value = user.Last_Update;
            m_dbCommand.Parameters.Add("@Last_Success_Update", DbType.Int64);
            m_dbCommand.Parameters["@Last_Success_Update"].Value = user.Last_Success_Update;

            m_dbCommand.CommandText = insert_user_data;
            try
            {
                m_dbCommand.ExecuteNonQuery();
                m_user_list.Add(user.ID, 0);
                return true;
            }
            catch (Exception ex)
            {
                Debug.Print("Error occured when inserting user data!:" + ex.ToString());
                return false;
            }
            finally
            {
                m_dbCommand.Parameters.Clear();
            }
        }
        //更新用户信息到sql和内存中（自动跳过null参数） [STA]
        private bool __update_user(User user, bool force_mode = false)
        {
            var update_user_data = "UPDATE User SET HTTP_Status=@HTTP_Status, Last_Update=@Last_Update";
            m_dbCommand.Parameters.Add("@HTTP_Status", DbType.Int32);
            m_dbCommand.Parameters["@HTTP_Status"].Value = user.HTTP_Status;
            m_dbCommand.Parameters.Add("@Last_Update", DbType.Int64);
            m_dbCommand.Parameters["@Last_Update"].Value = user.Last_Update;

            //数据保护：非200时不覆盖写入已存数据
            if (force_mode || user.HTTP_Status == (int)HttpStatusCode.OK)
            {
                update_user_data += ", Last_Success_Update=@Last_Success_Update";
                m_dbCommand.Parameters.Add("@Last_Success_Update", DbType.Int64);
                m_dbCommand.Parameters["@Last_Success_Update"].Value = user.Last_Success_Update;

                if (!string.IsNullOrEmpty(user.Name))
                {
                    update_user_data += ",Name=@Name";
                    m_dbCommand.Parameters.Add("@Name", DbType.String);
                    m_dbCommand.Parameters["@Name"].Value = user.Name;
                }

                if (!string.IsNullOrEmpty(user.Description))
                {
                    update_user_data += ",Description=@Description";
                    m_dbCommand.Parameters.Add("@Description", DbType.String);
                    m_dbCommand.Parameters["@Description"].Value = user.Description;
                }

                if (user.User_Face != null)
                {
                    var mm = new MemoryStream();
                    user.User_Face.Save(mm, user.User_Face.RawFormat);
                    mm.Position = 0;
                    byte[] buf = new byte[mm.Length];
                    mm.Read(buf, 0, (int)mm.Length);

                    update_user_data += ",User_Face=@User_Face";
                    m_dbCommand.Parameters.Add("@User_Face", DbType.Binary);
                    m_dbCommand.Parameters["@User_Face"].Value = buf;
                }

                if (!string.IsNullOrEmpty(user.User_Face_Url))
                {
                    update_user_data += ",User_Face_Url=@User_Face_Url";
                    m_dbCommand.Parameters.Add("@User_Face_Url", DbType.String);
                    m_dbCommand.Parameters["@User_Face_Url"].Value = user.User_Face_Url;
                }

                if (!string.IsNullOrEmpty(user.Home_Page))
                {
                    update_user_data += ",Home_Page=@Home_Page";
                    m_dbCommand.Parameters.Add("@Home_Page", DbType.String);
                    m_dbCommand.Parameters["@Home_Page"].Value = user.Home_Page;
                }

                if (!string.IsNullOrEmpty(user.Gender))
                {
                    update_user_data += ",Gender=@Gender";
                    m_dbCommand.Parameters.Add("@Gender", DbType.String);
                    m_dbCommand.Parameters["@Gender"].Value = user.Gender;
                }

                if (!string.IsNullOrEmpty(user.Address))
                {
                    update_user_data += ",Address=@Address";
                    m_dbCommand.Parameters.Add("@Address", DbType.String);
                    m_dbCommand.Parameters["@Address"].Value = user.Address;
                }

                if (!string.IsNullOrEmpty(user.Birthday))
                {
                    update_user_data += ",Birthday=@Birthday";
                    m_dbCommand.Parameters.Add("@Birthday", DbType.String);
                    m_dbCommand.Parameters["@Birthday"].Value = user.Birthday;
                }

                if (user.Follow_Users > 0)
                {
                    update_user_data += ",Follow_Users=@Follow_Users";
                    m_dbCommand.Parameters.Add("@Follow_Users", DbType.Int32);
                    m_dbCommand.Parameters["@Follow_Users"].Value = user.Follow_Users;
                }

                if (user.Follower > 0)
                {
                    update_user_data += ",Follower=@Follower";
                    m_dbCommand.Parameters.Add("@Follower", DbType.Int32);
                    m_dbCommand.Parameters["@Follower"].Value = user.Follower;
                }

                if (user.Illust_Bookmark_Public > 0)
                {
                    update_user_data += ",Illust_Bookmark_Public=@Illust_Bookmark_Public";
                    m_dbCommand.Parameters.Add("@Illust_Bookmark_Public", DbType.Int32);
                    m_dbCommand.Parameters["@Illust_Bookmark_Public"].Value = user.Illust_Bookmark_Public;
                }

                if (user.Mypixiv_Users > 0)
                {
                    update_user_data += ",Mypixiv_Users=@Mypixiv_Users";
                    m_dbCommand.Parameters.Add("@Mypixiv_Users", DbType.Int32);
                    m_dbCommand.Parameters["@Mypixiv_Users"].Value = user.Mypixiv_Users;
                }

                if (user.Total_Illusts > 0)
                {
                    update_user_data += ",Total_Illusts=@Total_Illusts";
                    m_dbCommand.Parameters.Add("@Total_Illusts", DbType.Int32);
                    m_dbCommand.Parameters["@Total_Illusts"].Value = user.Total_Illusts;
                }

                if (user.Total_Novels > 0)
                {
                    update_user_data += ",Total_Novels=@Total_Novels";
                    m_dbCommand.Parameters.Add("@Total_Novels", DbType.Int32);
                    m_dbCommand.Parameters["@Total_Novels"].Value = user.Total_Novels;
                }

                if (!string.IsNullOrEmpty(user.Twitter))
                {
                    update_user_data += ",Twitter=@Twitter";
                    m_dbCommand.Parameters.Add("@Twitter", DbType.String);
                    m_dbCommand.Parameters["@Twitter"].Value = user.Twitter;
                }

                if (!string.IsNullOrEmpty(user.Personal_Tag))
                {
                    update_user_data += ",Personal_Tag=@Personal_Tag";
                    m_dbCommand.Parameters.Add("@Personal_Tag", DbType.String);
                    m_dbCommand.Parameters["@Personal_Tag"].Value = user.Personal_Tag;
                }
            }

            update_user_data += " WHERE ID=@ID";
            m_dbCommand.Parameters.Add("@ID", DbType.Int32);
            m_dbCommand.Parameters["@ID"].Value = user.ID;
            m_dbCommand.CommandText = update_user_data;
            try
            {
                m_dbCommand.ExecuteNonQuery();
                m_user_list[user.ID] = user.HTTP_Status;
                return true;
            }
            catch (Exception ex)
            {
                Debug.Print("Error occured when updating user data!:" + ex.ToString());
                return false;
            }
            finally
            {
                m_dbCommand.Parameters.Clear();
            }
        }
        //自动选择插入还是更新用户信息了额 [STA]
        private bool __auto_insert_user(User user)
        {
            if (m_user_list.ContainsKey(user.ID))
                return __update_user(user);
            else
                return __insert_user(user);
        }
        private User __get_user(uint id)
        {

            var get_user_str = "SELECT ID, Name, Description, User_Face, User_Face_Url, Home_Page, Gender, Personal_Tag, Address, Birthday, Job, Follow_Users, Follower, Illust_Bookmark_Public, Mypixiv_Users, Total_Illusts, Total_Novels, Twitter, HTTP_Status, Last_Update, Last_Success_Update FROM User WHERE ID=" + id;
            var ret = new User();
            if (id == 0) return ret;
            try
            {
                m_dbCommand.CommandText = get_user_str;
                var dr = m_dbCommand.ExecuteReader();
                var suc = dr.Read();
                if (!suc)
                {
                    ret.ID = id;
                    dr.Close();
                    return ret;
                }
                ret.ID = (uint)dr.GetInt32(0);
                ret.Name = dr.IsDBNull(1) ? "" : dr.GetString(1);
                ret.Description = dr.IsDBNull(2) ? "" : dr.GetString(2);
                if (!dr.IsDBNull(3))
                {
                    byte[] img_buf = (byte[])dr[3];
                    var mm = new MemoryStream();
                    mm.Write(img_buf, 0, img_buf.Length);
                    mm.Position = 0;
                    ret.User_Face = Image.FromStream(mm);
                }
                ret.User_Face_Url = dr.IsDBNull(4) ? "" : dr.GetString(4);
                ret.Home_Page = dr.IsDBNull(5) ? "" : dr.GetString(5);
                ret.Gender = dr.IsDBNull(6) ? "" : dr.GetString(6);
                ret.Personal_Tag = dr.IsDBNull(7) ? "" : dr.GetString(7);
                ret.Address = dr.IsDBNull(8) ? "" : dr.GetString(8);
                ret.Birthday = dr.IsDBNull(9) ? "" : dr.GetString(9);
                ret.Job = dr.IsDBNull(10) ? "" : dr.GetString(10);
                ret.Follow_Users = dr.IsDBNull(11) ? 0 : dr.GetInt32(11);
                ret.Follower = dr.IsDBNull(12) ? 0 : dr.GetInt32(12);
                ret.Illust_Bookmark_Public = dr.IsDBNull(13) ? 0 : dr.GetInt32(13);
                ret.Mypixiv_Users = dr.IsDBNull(14) ? 0 : dr.GetInt32(14);
                ret.Total_Illusts = dr.IsDBNull(15) ? 0 : dr.GetInt32(15);
                ret.Total_Novels = dr.IsDBNull(16) ? 0 : dr.GetInt32(16);
                ret.Twitter = dr.IsDBNull(17) ? "" : dr.GetString(17);
                ret.HTTP_Status = dr.IsDBNull(18) ? 0 : dr.GetInt32(18);
                ret.Last_Update = dr.IsDBNull(19) ? 0 : (ulong)dr.GetInt64(19);
                ret.Last_Success_Update = dr.IsDBNull(20) ? 0 : (ulong)dr.GetInt64(20);

                dr.Close();
            }
            catch (Exception)
            {
                
            }
            return ret;
        }

        #endregion //SQL Operations For User

        #endregion //SQL Operations

        //数据解析
        #region Data Parser

        /// <summary>
        /// 从网页上获取投稿信息 [MTA]
        /// </summary>
        /// <param name="id">投稿的id</param>
        /// <param name="illust">[输出]投稿信息</param>
        private void _parse_illust_info(uint id, out Illust illust)
        {
            //初始化
            illust = new Illust();
            illust.ID = id;
            illust.Origin = DataOrigin.Pixiv_App_API;
            illust.Last_Update = (ulong)VBUtil.Utils.Others.ToUnixTimestamp(DateTime.Now);

            var ns = new NetStream(RetryTimes: 1, readWriteTimeout: 15000);
            ns.Timeout = 30000;
            //获取基本的信息
            try
            {
                var url = "https://app-api.pixiv.net/v1/illust/detail?illust_id=" + id;

                RefreshToken();
                var header_param = new Parameters();
                if (!string.IsNullOrEmpty(_access_token))
                {
                    header_param.Add("Authorization", "Bearer " + _access_token);
                }

                ns.HttpGet(url, headerParam: header_param);

                //解析开始~
                #region Parsing
                var str_json = ns.ReadResponseString();
                var json = JsonConvert.DeserializeObject(str_json) as JObject;
                json = json.Value<JObject>("illust");

                illust.Description = json.Value<string>("caption");
                var create_time = json.Value<DateTime>("create_date");
                //set to GMT+09:00
                create_time = create_time.ToUniversalTime().AddHours(9);
                illust.Submit_Time = (ulong)VBUtil.Utils.Others.ToUnixTimestamp(create_time);
                illust.Size = new Size(json.Value<int>("width"), json.Value<int>("height"));
                illust.Page = json.Value<uint>("page_count");

                illust.Tag = "";
                //parsing tag
                var tag_list = json.Value<JArray>("tags");
                foreach (JObject item in tag_list)
                {
                    illust.Tag += item.Value<string>("name") + ",";
                }
                if (illust.Tag.Length > 0) illust.Tag = illust.Tag.Substring(0, illust.Tag.Length - 1);

                illust.Title = json.Value<string>("title");

                //parsing tool
                illust.Tool = "";
                var tool_list = json.Value<JArray>("tools");
                foreach (string item in tool_list)
                {
                    illust.Tool += item + ",";
                }
                if (illust.Tool.Length > 0) illust.Tool = illust.Tool.Substring(0, illust.Tool.Length - 1);

                illust.Bookmark_Count = json.Value<int>("total_bookmarks");
                illust.Comment_Count = json.Value<int>("total_comments");
                illust.Click = json.Value<int>("total_view");

                illust.Author_ID = json["user"].Value<uint>("id");

                illust.Rate_Count = 0;
                illust.Score = 0;

                #endregion //Parsing
                
                illust.HTTP_Status = (int)(ns.HTTP_Response.StatusCode);
                ns.Close();
                illust.Last_Success_Update = illust.Last_Update;

            }
            catch (WebException ex)
            {
                var response = ex.Response;
                if (response != null)
                {
                    illust.HTTP_Status = (int)((HttpWebResponse)(response)).StatusCode;
                    Debug.Print("Pixiv server returned HTTP " + illust.HTTP_Status + " while accessing id #" + illust.ID);
                }
                throw;
            }
            catch (Exception ex)
            {
                illust.HTTP_Status = -2;
                throw ex;
            }
            finally
            {
                ns.Close();
            }
        }
        private void _parse_user_info(uint id, out User user)
        {
            user = new User();
            user.ID = id;
            user.Last_Update = (ulong)VBUtil.Utils.Others.ToUnixTimestamp(DateTime.Now);

            var ns = new NetStream(RetryTimes: 1, readWriteTimeout: 15000);
            ns.Timeout = 30000;

            try
            {
                var url = "https://app-api.pixiv.net/v1/user/detail?user_id=" + id;

                RefreshToken();
                var header_param = new Parameters();
                if (!string.IsNullOrEmpty(_access_token))
                {
                    header_param.Add("Authorization", "Bearer " + _access_token);
                }

                ns.HttpGet(url, headerParam: header_param);

                //解析开始~
                #region Parsing
                var str_json = ns.ReadResponseString();
                var json = JsonConvert.DeserializeObject(str_json) as JObject;

                user.Birthday = json["profile"].Value<string>("birth");
                user.Gender = json["profile"].Value<string>("gender");
                user.Job = json["profile"].Value<string>("job");
                user.Address = json["profile"].Value<string>("region");
                user.Follow_Users = json["profile"].Value<int>("total_follow_users");
                user.Follower = json["profile"].Value<int>("total_follower");
                user.Illust_Bookmark_Public = json["profile"].Value<int>("total_illust_bookmarks_public");
                user.Total_Illusts = json["profile"].Value<int>("total_illusts");
                user.Mypixiv_Users = json["profile"].Value<int>("total_mypixiv_users");
                user.Total_Novels = json["profile"].Value<int>("total_novels");
                user.Twitter = json["profile"].Value<string>("twitter_account");
                user.Home_Page = json["profile"].Value<string>("webpage");
                user.Name = json["user"].Value<string>("name");
                user.Description = json["user"].Value<string>("comment");
                user.User_Face_Url = json["user"]["profile_image_urls"].Value<string>("medium");
                #endregion //Parsing

                //downloading user_image
                ns.Close();
                user.User_Face = _util_download_image_from_url(user.User_Face_Url, user.ID, out user.HTTP_Status);
                //user.HTTP_Status = (int)(ns.HTTP_Response.StatusCode);
                ns.Close();
                user.Last_Success_Update = user.Last_Update;

            }
            catch (WebException ex)
            {
                var response = ex.Response;
                if (response != null)
                {
                    user.HTTP_Status = (int)((HttpWebResponse)(response)).StatusCode;
                    Debug.Print("Pixiv server returned HTTP " + user.HTTP_Status + " while accessing uid #" + user.ID);
                }
            }
            catch (Exception)
            {
                user.HTTP_Status = -2;
                throw;
            }
            finally
            {
                ns.Close();
            }
        }
        //从指定的url处获取图像
        private Image _util_download_image_from_url(string url, uint user_id, out int http_status)
        {
            var ns = new NetStream(RetryTimes: 1, readWriteTimeout: 15000);
            ns.Timeout = 30000;
            try
            {
                //using xhr
                var header_param = new Parameters();
                header_param.Add("X-Request-With", "XmlHttpRequest");
                header_param.Add("Origin", "http://www.pixiv.net");
                header_param.Add("Referer", "http://www.pixiv.net/member.php?id=" + user_id);

                var ss = new MemoryStream();
                ns.HttpGet(url, header_param);

                int nr = 0;
                byte[] buf = new byte[16384];
                do
                {
                    nr = ns.Stream.Read(buf, 0, 16384);
                    ss.Write(buf, 0, nr);
                } while (nr != 0);
                http_status = (int)ns.HTTP_Response.StatusCode;
                ns.Close();
                ss.Position = 0;
                return Image.FromStream(ss);
            }
            catch (WebException ex)
            {
                if (ex.Response != null)
                {
                    var response = (HttpWebResponse)ex.Response;
                    http_status = (int)response.StatusCode;
                }
                else
                    http_status = -2;
                //throw ex;
            }
            catch (Exception ex)
            {
                http_status = -2;
                //throw ex;
            }
            return null; // new Bitmap(1, 1);
        }

        #endregion //Data Parser

        //静态函数（登陆接口）
        #region Static Functions


        private static string _access_token;
        private static string _device_token;
        private static string _refresh_token;
        private static DateTime _expire_time;
        private static bool __token_loaded = _loadToken();
        private static bool _loadToken(string path = "token.dat")
        {
            if (__token_loaded) return true;

            var stream = new StreamReader(path, Encoding.UTF8);
            var str_json = stream.ReadLine();
            stream.Close();

            var json = JsonConvert.DeserializeObject(str_json) as JObject;
            _access_token = json.Value<string>("access_token");
            _device_token = json.Value<string>("device_token");
            _refresh_token = json.Value<string>("refresh_token");
            _expire_time = VBUtil.Utils.Others.FromUnixTimeStamp(json.Value<double>("expire_time"));
            return true;
        }

        private static bool _saveToken(string path = "token.dat")
        {
            var json = new JObject();
            json.Add("access_token", _access_token);
            json.Add("device_token", _device_token);
            json.Add("refresh_token", _refresh_token);
            json.Add("expire_time", VBUtil.Utils.Others.ToUnixTimestamp(_expire_time));

            var stream = new StreamWriter(path, false, Encoding.UTF8);
            stream.WriteLine(JsonConvert.SerializeObject(json));
            stream.Close();
            return true;
        }
        //2016-11-01 16:50:30 test succeeded.
        /// <summary>
        /// login to Pixiv.net, using official api (captured by Fiddler)
        /// </summary>
        /// <param name="username">Your user name(possibly an e-mail address)</param>
        /// <param name="password">Your password</param>
        /// <remarks>[MTA] [no throw]</remarks>
        public static void Login(string username, string password)
        {
            string login_request_url = "https://oauth.secure.pixiv.net/auth/token";

            var ns = new NetStream(RetryTimes: 1, readWriteTimeout: 15000);
            ns.Timeout = 30000;
            try
            {
                Debug.Print("Initializing login variables.");
                var post_param = new Parameters();
                post_param.Add("client_id", M_CLIENT_ID);
                post_param.Add("client_secret", M_CLIENT_SECRET);
                post_param.Add("grant_type", "password");
                post_param.Add("username", username);
                post_param.Add("password", password);
                post_param.Add("device_token", "pixiv");
                post_param.Add("get_secure_url", "true");

                var header_param = new Parameters();
                header_param.Add("User-Agent", M_APP_USER_AGENT);
                header_param.Add("Accept-Language", "zh_CN");
                header_param.Add("App-OS", "android");
                header_param.Add("App-OS-Version", "6.0.1");
                header_param.Add("App-Version", "5.0.54");
                header_param.Add("Accept-Encoding", "gzip");

                Debug.Print("Posting login data");
                ns.HttpPost(login_request_url, post_param, headerParam: header_param);

                var response_str = ns.ReadResponseString();

                var response_json = JsonConvert.DeserializeObject(response_str) as JObject;
                
                ns.Close();
                _access_token = response_json["response"].Value<string>("access_token");
                _device_token = response_json["response"].Value<string>("device_token");
                _refresh_token = response_json["response"].Value<string>("refresh_token");
                _expire_time = DateTime.Now.AddSeconds(response_json["response"].Value<int>("expires_in"));

                _saveToken();
                Debug.Print("Login succeeded.");
                LoginSucceeded?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.Print("Login failed: \n" + ex.ToString());
                LoginFailed?.Invoke(ex.ToString());
            }
        }
        public static void RefreshToken()
        {
            if (DateTime.Now < _expire_time) return;
            if (string.IsNullOrEmpty(_refresh_token) || string.IsNullOrEmpty(_device_token)) return;

            string login_request_url = "https://oauth.secure.pixiv.net/auth/token";

            var ns = new NetStream(RetryTimes: 1, readWriteTimeout: 15000);
            ns.Timeout = 30000;
            try
            {
                Debug.Print("Initializing login variables.");
                var post_param = new Parameters();
                post_param.Add("client_id", M_CLIENT_ID);
                post_param.Add("client_secret", M_CLIENT_SECRET);
                post_param.Add("grant_type", "refresh_token");
                post_param.Add("refresh_token", _refresh_token);
                post_param.Add("device_token", _device_token);
                post_param.Add("get_secure_url", "true");

                var header_param = new Parameters();
                header_param.Add("User-Agent", M_APP_USER_AGENT);
                header_param.Add("Accept-Language", "zh_CN");
                header_param.Add("App-OS", "android");
                header_param.Add("App-OS-Version", "6.0.1");
                header_param.Add("App-Version", "5.0.54");
                header_param.Add("Accept-Encoding", "gzip");

                Debug.Print("Posting login data");
                ns.HttpPost(login_request_url, post_param, headerParam: header_param);

                var response_str = ns.ReadResponseString();

                var response_json = JsonConvert.DeserializeObject(response_str) as JObject;

                ns.Close();
                _access_token = response_json["response"].Value<string>("access_token");
                _device_token = response_json["response"].Value<string>("device_token");
                _refresh_token = response_json["response"].Value<string>("refresh_token");
                _expire_time = DateTime.Now.AddSeconds(response_json["response"].Value<int>("expires_in"));

                _saveToken();
                Debug.Print("Login succeeded.");
                LoginSucceeded?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.Print("Login failed: \n" + ex.ToString());
                LoginFailed?.Invoke(ex.ToString());
            }
        }
        #endregion //Static Functions


        //公有函数
        #region Public Functions


        //获取投稿信息 [MTA] [throwable]
        public Illust GetIllustInfo(uint id, DataUpdateMode mode = DataUpdateMode.Async_Update)
        {
            if (id == 0) return new Illust();

            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
            var info = __get_illust(id);
            m_sqlThreadLock.ReleaseWriterLock();

            if (mode != DataUpdateMode.No_Update)
            {
                var last_update = VBUtil.Utils.Others.FromUnixTimeStamp(info.Last_Update);
                if ((mode & DataUpdateMode.Force_Update) != 0 || last_update.AddSeconds(M_MIN_AUTOUPDATE_INTERVAL) <= DateTime.Now)
                {
                    //illust needs to be updated
                    
                    m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);
                    if (m_illust_list.ContainsKey(id))
                    {
                        m_illust_list[id] = 0;
                    }
                    else
                    {
                        __auto_insert_illust(info);
                    }
                    _update_query_list();
                    m_dataThreadLock.ReleaseWriterLock();

                    if ((mode & DataUpdateMode.Sync_Update) != 0)
                    {
                        //sync mode
                        _join_all_thread();
                        m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                        info = __get_illust(id);
                        m_sqlThreadLock.ReleaseWriterLock();
                    }
                    //default: async mode
                }

            }

            return info;
        }
        //获取用户信息 [MTA] [throwable] todo: fixed table section
        public User GetUserInfo(uint id, DataUpdateMode mode = DataUpdateMode.Async_Update)
        {
            if (id == 0) return new User();

            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
            var info = __get_user(id);
            m_sqlThreadLock.ReleaseWriterLock();

            if (mode != DataUpdateMode.No_Update)
            {
                var last_update = VBUtil.Utils.Others.FromUnixTimeStamp(info.Last_Update);
                if ((mode & DataUpdateMode.Force_Update) != 0 || last_update.AddSeconds(M_MIN_AUTOUPDATE_INTERVAL) <= DateTime.Now)
                {
                    //illust needs to be updated


                    m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);
                    if (m_user_list.ContainsKey(id))
                    {
                        m_user_list[id] = 0;
                    }
                    else
                    {
                        __auto_insert_user(info);
                    }
                    _update_query_list();
                    m_dataThreadLock.ReleaseWriterLock();

                    if ((mode & DataUpdateMode.Sync_Update) != 0)
                    {
                        //sync mode
                        _join_all_thread();
                        m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                        info = __get_user(id);
                        m_sqlThreadLock.ReleaseWriterLock();
                    }
                    //default: async mode
                }

            }

            return info;
        }
        //中止工作线程
        public void AbortWorkingThread(bool wait = false)
        {
            if (m_monitor_thd == null) return;
            m_abort_flag = true;
            if (wait) m_monitor_thd.Join();
        }
        //强制重新更新数据
        public void ForceUpdateAllData()
        {
            ThreadPool.QueueUserWorkItem((object obj) =>
            {
                AbortWorkingThread(true);
                m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);

                if (m_dbTransaction == null)
                    m_dbTransaction = m_dbConnection.BeginTransaction();

                var set_illust_status = "UPDATE Illust SET HTTP_Status = 0";
                var set_user_status = "UPDATE User SET HTTP_Status = 0";
                m_dbCommand.CommandText = set_illust_status;
                m_dbCommand.ExecuteNonQuery();
                m_dbCommand.CommandText = set_user_status;
                m_dbCommand.ExecuteNonQuery();

                m_illust_query_list.Clear();

                for (int i = 0; i < m_illust_list.Count; i++)
                {
                    m_illust_list[m_illust_list.ElementAt(i).Key] = 0;
                }
                for (int i = 0; i < m_user_list.Count; i++)
                {
                    m_user_list[m_user_list.ElementAt(i).Key] = 0;
                }

                _update_query_list();

                m_dbTransaction.Commit();
                m_dbTransaction = null;

                m_dataThreadLock.ReleaseWriterLock();
                m_sqlThreadLock.ReleaseWriterLock();

                _create_monitor_thread();
            });
        }
        public void SetIllustInfo(Illust illust)
        {
            if (illust.ID == 0) return;
            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
            m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);

            __auto_insert_illust(illust);
            _update_query_list();

            m_dataThreadLock.ReleaseWriterLock();
            m_sqlThreadLock.ReleaseWriterLock();
        }
        public void SetUserInfo(User user)
        {
            if (user.ID == 0) return;
            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
            m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);

            __auto_insert_user(user);
            _update_query_list();

            m_dataThreadLock.ReleaseWriterLock();
            m_sqlThreadLock.ReleaseWriterLock();
        }
        #endregion //Public Functions


        //公有属性
        #region Public Properties
        //数据库中的投稿数量
        public int Illust_Count { get { return m_illust_list.Count; } }
        //数据库中的用户数量
        public int User_Count { get { return m_user_list.Count; } }
        public static bool Is_Logined { get { return (!string.IsNullOrEmpty(_access_token)); } }
        #endregion //Public Properties
    }
}
