﻿using System;
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
* 目前数据库的表格变量定义 [v1.0.2]
* 
* TABLE DbVars(string Key [PRIMARY KEY], string Value)
*       用于存放数据库相关信息，如版本，路径等。 Key:数值名称 Value:数值内容
* TABLE User(uint ID [PRIMARY KEY], string Name, string Description, Byte[] User_Face, string User_Face_Url, string Home_Page,
*       用于存放画师信息，ID:画师ID，      Name:画师名称，Description:画师描述（html代码）, User_Face:画师头像（二进制图片）,User_Face_Url:画师头像的url 下载失败可以从这里直接下载, Home_Page: 画师的个人主页
*            string Gender, string Personal_Tags, string Address, ulong Birthday, string Twitter, int HTTP_Status [NOT NULL], ulong Last_Update [NOT NULL])
*            Gender:性别，男/女,   Personal_Tags:个人的标签, Address:地址, Birthday:生日, Twitter:推, HTTP_Status:http状态,         Last_Update:最后更新的时间
* 
* TABLE Illust(uint ID [PRIMARY KEY], uint Author_ID [NOT NULL], string Title, string Description, string Tag, string Tool,
*       用于存放投稿信息，ID:作品ID，      Author_ID:画师ID，           Title:投稿标题，Description:投稿的描述,Tag:投稿标签，Tool:绘图工具
*              int Click [NOT NULL [0]], int Width [NOT NULL [0]], int Height [NOT NULL [0]] int Rate_Count [NOT NULL [0]], int Score [NOT NULL [0]],
*                   Click:点击数，           Width:作品宽度像素,       Height:作品高度像素,      Rate_Count:用户评分数，        Score:用户评分
*              ulong Submit_Time [NOT NULL], int HTTP_Status [NOT NULL], ulong Last_Update [NOT NULL])
*                    Submit_Time:投稿时间，      HTTP_Status:获取投稿信息时的http状态码，Last_Update:最后更新投稿信息的时间
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
        public string Personal_Tags;
        //地址
        public string Address;
        //生日
        public string Birthday;
        //Twitter
        public string Twitter;
        //http状态
        public int HTTP_Status;
        //最后更新的时间
        public ulong Last_Update;
    }
    //投稿信息
    [Serializable]
    public struct Illust
    {
        //作品ID
        public uint ID;
        //画师ID
        public uint Author_ID;
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
    }

    #endregion


    public class dataUpdater
    {
        //类成员定义
        #region Member Definations

        //p站图片路径
        private string m_path;
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

        //无视获取失败
        private bool m_ignore_non_200_status;
        //数据搜集线程的调用函数
        //todo: 在列表中单独出一个未成功的列表，减少比较次数
        private List<uint> m_illust_query_list;
        private List<uint> m_user_query_list;
        private int m_query_count;
        private int m_query_finished;
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
        private int m_illust_working_thd_count;
        private int m_user_working_thd_count;

        #endregion

        //类常量定义
        #region Constant Definations

        //常量定义：当前版本和最大获取投稿信息的线程数
        private const string M_CURRENT_DBVERSION = "1.0.2";
        private const int M_MAX_ILLUST_SYNC_THREAD = 1;
        private const int M_MAX_USER_SYNC_THREAD = 0;
        const string multi_data_split_string = ",";
        #endregion

        //构造函数及初始化
        #region Constructor

        /// <summary>
        /// 默认构造函数，用于初始化Pixiv的数据库信息
        /// </summary>
        /// <param name="path">本地含p站图片的目录</param>
        /// <param name="include_sub_dir">是否包含子文件夹</param>
        /// <param name="ignore_non_200">是否忽略未成功获取的投稿信息</param>
        /// <remarks>STA</remarks>
        public dataUpdater(string path, bool include_sub_dir = true, bool ignore_non_200 = false)
        {
            Debug.Print("Hello! Welcome using my pixiv class!\r\nHope you enjoy it!");
            //验证路径合法
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException("path should not be NULL or Empty!");
            if (!Directory.Exists(path)) throw new ArgumentException("path does not exist in your computer!");
            //初始化成员变量
            Debug.Print("Validate initialial argument: [path] succeeded. :" + path);
            m_path = path;
            m_include_subdir = include_sub_dir;
            m_ignore_non_200_status = ignore_non_200;
            m_sqlThreadLock = new ReaderWriterLock();
            m_dataThreadLock = new ReaderWriterLock();

            m_illust_thd = new Thread[M_MAX_ILLUST_SYNC_THREAD];
            m_user_thd = new Thread[M_MAX_USER_SYNC_THREAD];

            m_monitor_thd = new Thread(_monitor_callback);
            m_monitor_thd.IsBackground = true;
            m_monitor_thd.Name = "Pixiv Data Monitor Thread";

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
                string create_user_table = "CREATE TABLE User(ID INT PRIMARY KEY, Name VARCHAR, Description TEXT, User_Face IMAGE, User_Face_Url VARCHAR, Home_Page VARCHAR, Gender VARCHAR, Personal_Tag VARCHAR, Address VARCHAR, Birthday VARCHAR, Twitter VARCHAR, HTTP_Status INT NOT NULL, Last_Update BIGINT NOT NULL)";
                string create_illust_table = "CREATE TABLE Illust(ID INT PRIMARY KEY, Author_ID INT NOT NULL, Title VARCHAR, Description TEXT, Tag VARCHAR, Tool VARCHAR, Click INT NOT NULL DEFAULT 0, Rate_Count INT NOT NULL DEFAULT 0, Score INT NOT NULL DEFAULT 0, Width INT NOT NULL DEFAULT 0, Height INT NOT NULL DEFAULT 0, Submit_Time BIGINT NOT NULL, HTTP_Status INT NOT NULL, Last_Update BIGINT NOT NULL)";
                string write_version_info = "INSERT INTO DbVars VALUES('Version', '" + M_CURRENT_DBVERSION + "')";
                string write_path_info = "INSERT INTO DbVars VALUES('Path', '" + m_path + "')";
                m_dbCommand.CommandText = create_var_table;
                m_dbCommand.ExecuteNonQuery();
                m_dbCommand.CommandText = write_version_info;
                m_dbCommand.ExecuteNonQuery();
                m_dbCommand.CommandText = write_path_info;
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
                string get_db_path = "SELECT Value FROM DbVars WHERE Key='Path'";
                m_dbCommand.CommandText = get_db_path;
                var dr0 = m_dbCommand.ExecuteReader();
                dr0.Read();
                string db_path = dr0.GetString(0);
                dr0.Close();
                string get_db_version = "SELECT Value FROM DbVars WHERE Key='Version'";
                m_dbCommand.CommandText = get_db_version;
                dr0 = m_dbCommand.ExecuteReader();
                dr0.Read();
                string db_version = dr0.GetString(0);
                dr0.Close();

                Debug.Print("Database version:" + db_version + " (Current:" + M_CURRENT_DBVERSION + ")");
                Debug.Print("Database path:" + db_path + " (Current:" + m_path + ")");

                if (db_path != m_path)
                {
                    Debug.Print("System found the path does not match, overwriting data.");

                    var update_path = "UPDATE DbVars SET Value='" + m_path + "' WHERE KEY='Path'";
                    m_dbCommand.CommandText = update_path;
                    m_dbCommand.ExecuteNonQuery();
                }

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

        #endregion

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

        #endregion

        //数据导入
        #region Data Import

        #region From Path
        //从path的本地目录中跟新文件列表 [MTA]
        private void _update_file_list(string path = null)
        {
            if (string.IsNullOrEmpty(path)) path = m_path;
            Debug.Print("Updating file from directory: " + path);

            var dir_info = new DirectoryInfo(path);
            //在pixiv默认保存的文件名中，会符合以下的正则匹配的表达式： (\d+)_p(\d+).(jpg|png|gif) 如233333_p0.jpg
            //开启sql的transaction，这样不会在执行修改指令时就立刻执行，效率+++

            foreach (var item in dir_info.GetFiles())
            {
                const string regex_pattern = @"(?<id>\d+)_p(?<page>\d+)\.(?<ext>jpg|png)";
                var match = Regex.Match(item.Name, regex_pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    uint id = uint.Parse(match.Result("${id}"));
                    uint page = uint.Parse(match.Result("${page}"));

                    //从互斥资源中读取（统一用写锁来保证同步算了）
                    m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                    var exist_id = m_illust_list.ContainsKey(id);
                    m_sqlThreadLock.ReleaseWriterLock();

                    //在sql中不存在该作品，应该就是新增的了，加入到sql和内存列表中
                    if (!exist_id)
                    {
                        //请求写入权限
                        m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                        //写入数据
                        Illust illust = new Illust();
                        illust.ID = id;
                        __auto_insert_illust(illust);
                        System.Diagnostics.Debug.Print("Fetching: Illust " + id);
                        //释放写入权限
                        m_sqlThreadLock.ReleaseWriterLock();
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
        public void UpdateFileList()
        {
            ThreadPool.QueueUserWorkItem(
                (object obj) =>
                {
                    UpdateLocalFileListStarted?.Invoke();

                    m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                    if (m_dbTransaction == null)
                        m_dbTransaction = m_dbConnection.BeginTransaction();
                    m_sqlThreadLock.ReleaseWriterLock();

                    _update_file_list();

                    m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                    if (m_dbTransaction != null)
                    {
                        m_dbTransaction.Commit();
                        m_dbTransaction = null;
                    }
                    m_sqlThreadLock.ReleaseWriterLock();

                    //making it to a list!~
                    m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);
                    m_illust_query_list.Clear();
                    foreach (var item in m_illust_list)
                    {
                        if (item.Value == 0 || item.Value == -2 || (item.Value > 0 && item.Value != (int)HttpStatusCode.OK && !m_ignore_non_200_status))
                            m_illust_query_list.Add(item.Key);
                    }
                    m_query_count = m_illust_query_list.Count;
                    m_query_finished = 0;
                    m_dataThreadLock.ReleaseWriterLock();

                    UpdateLocalFileListEnded?.Invoke();
                });
        }
        #endregion


        #endregion

        //多线程调度控制
        #region Multi-Thread Access Control
        //获取数据线程的回调函数 [MTA]
        private void _sync_illust_callback()
        {
            if (m_illust_working_thd_count >= M_MAX_ILLUST_SYNC_THREAD) return;
            m_illust_working_thd_count++;

            uint id;
            do
            {
                id = 0;
                m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);

                if (m_illust_query_list.Count != 0)
                {
                    id = m_illust_query_list[0];
                    m_illust_query_list.RemoveAt(0);
                }

                m_dataThreadLock.ReleaseWriterLock();

                if (id == 0) break;

                //开始获取
                Debug.Print("Started fetching illust id=" + id);
                FetchIllustStarted?.Invoke(id, (uint)m_query_finished, (uint)m_query_count);

                Illust illust = new Illust();
                try
                {
                    //解析
                    _parse_illust_info(id, out illust);

                    m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                    __auto_insert_illust(illust);
                    //是否存在该用户，若不存在则写入
                    bool exist_user = m_user_list.ContainsKey(illust.Author_ID);
                    if (!exist_user)
                    {
                        User user = new User();
                        user.ID = illust.Author_ID;
                        __auto_insert_user(user);

                    }
                    m_sqlThreadLock.ReleaseWriterLock();
                    if (!exist_user)
                    {
                        m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);
                        //加入到查询列表
                        m_user_query_list.Add(illust.Author_ID);
                        m_query_count++;
                        m_dataThreadLock.ReleaseWriterLock();
                    }

                    FetchIllustSucceeded?.Invoke(id, (uint)m_query_finished, (uint)m_query_count, illust);
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
                    m_dataThreadLock.ReleaseWriterLock();
                }
            } while (id != 0 && !m_abort_flag);

            m_illust_working_thd_count--;
        }
        private void _sync_user_callback()
        {
            if (m_user_working_thd_count >= M_MAX_USER_SYNC_THREAD) return;
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
                }

                m_dataThreadLock.ReleaseWriterLock();

                if (id == 0) break;

                //开始获取
                Debug.Print("Started fetching user id=" + id);
                FetchUserStarted?.Invoke(id, (uint)m_query_finished, (uint)m_query_count);

                User user = new User();
                try
                {
                    //解析
                    _parse_user_info(id, out user);
                    
                    m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                    __auto_insert_user(user);
                    m_sqlThreadLock.ReleaseWriterLock();
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
                    m_dataThreadLock.ReleaseWriterLock();
                }
            } while (id != 0 && !m_abort_flag);

            m_user_working_thd_count--;
        }
        private void _monitor_callback()
        {
            do
            {
                try
                {
                    m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);
                    int illust_count = m_illust_query_list.Count;
                    m_dataThreadLock.ReleaseWriterLock();

                    if (m_illust_working_thd_count == 0 && illust_count != 0 && M_MAX_ILLUST_SYNC_THREAD > 0)
                    {

                        Debug.Print("Multi-thread access for illust started!");
                        if (m_dbTransaction == null)
                        {
                            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                            m_dbTransaction = m_dbConnection.BeginTransaction();
                            m_sqlThreadLock.ReleaseWriterLock();
                        }

                        for (int i = 0; i < M_MAX_ILLUST_SYNC_THREAD; i++)
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

                    if (m_user_working_thd_count == 0 && user_count != 0 && M_MAX_USER_SYNC_THREAD > 0)
                    {
                        Debug.Print("Multi-thread access for user started!");
                        if (m_dbTransaction == null)
                        {
                            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                            m_dbTransaction = m_dbConnection.BeginTransaction();
                            m_sqlThreadLock.ReleaseWriterLock();
                        }

                        for (int i = 0; i < M_MAX_USER_SYNC_THREAD; i++)
                        {
                            m_user_thd[i] = new Thread(_sync_illust_callback);
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

                //更新列表为空，线程空闲，用sleep进行下一次列表检测
                //m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);
                //int ic = m_illust_query_list.Count, uc = m_user_query_list.Count;
                //m_dataThreadLock.ReleaseLock();
                //if (ic == 0 && uc == 0)
                //{
                    Thread.Sleep(1000);
                //}
            } while (!m_abort_flag);

            //线程安全退出，取消终止标识
            m_abort_flag = false;
        }
        #endregion

        //sql读写操作
        #region SQL Operations

        #region SQL Operations For Illust

        //向sql中自动插入投稿数据 [STA]
        private bool __insert_illust(Illust illust)
        {
            var insert_str = "INSERT INTO Illust(ID, Author_ID, Title, Description, Tag, Tool, Click, Width, Height, Rate_Count, Score, Submit_Time, HTTP_Status, Last_Update) VALUES(@ID, @Author_ID";
            m_dbCommand.Parameters.Add("@ID", DbType.Int32);
            m_dbCommand.Parameters["@ID"].Value = illust.ID;
            m_dbCommand.Parameters.Add("@Author_ID", DbType.Int32);
            m_dbCommand.Parameters["@Author_ID"].Value = illust.Author_ID;

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

            insert_str += ", @Click, @Width, @Height, @Rate_Count, @Score, @Submit_Time, @HTTP_Status, @Last_Update)";

            m_dbCommand.Parameters.Add("@Click", DbType.Int32);
            m_dbCommand.Parameters["@Click"].Value = illust.Click;
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
        private bool __update_illust(Illust illust)
        {
            var update_str = "UPDATE Illust SET HTTP_Status=@HTTP_Status, Last_Update=@Last_Update";
            m_dbCommand.Parameters.Add("@HTTP_Status", DbType.Int32);
            m_dbCommand.Parameters["@HTTP_Status"].Value = illust.HTTP_Status;
            m_dbCommand.Parameters.Add("@Last_Update", DbType.Int32);
            m_dbCommand.Parameters["@Last_Update"].Value = illust.Last_Update;

            //数据保护：非200时不覆盖写入已存数据
            if (illust.HTTP_Status == (int)HttpStatusCode.OK)
            {
                update_str += ", Author_ID=@Author_ID, Submit_Time=@Submit_Time";
                m_dbCommand.Parameters.Add("@Author_ID", DbType.Int32);
                m_dbCommand.Parameters["@Author_ID"].Value = illust.Author_ID;
                m_dbCommand.Parameters.Add("@Submit_Time", DbType.Int64);
                m_dbCommand.Parameters["@Submit_Time"].Value = illust.Submit_Time;

                if (illust.Click >= 0)
                {
                    update_str += ",Click=@Click";
                    m_dbCommand.Parameters.Add("@Click", DbType.Int32);
                    m_dbCommand.Parameters["@Click"].Value = illust.Click;
                }
                if (illust.Size.Width >= 0)
                {
                    update_str += ",Width=@Width";
                    m_dbCommand.Parameters.Add("@Width", DbType.Int32);
                    m_dbCommand.Parameters["@Width"].Value = illust.Size.Width;
                }
                if (illust.Size.Height >= 0)
                {
                    update_str += ",Height=@Height";
                    m_dbCommand.Parameters.Add("@Height", DbType.Int32);
                    m_dbCommand.Parameters["@Height"].Value = illust.Size.Height;
                }
                if (illust.Rate_Count >= 0)
                {
                    update_str += ",Rate_Count=@Rate_Count";
                    m_dbCommand.Parameters.Add("@Rate_Count", DbType.Int32);
                    m_dbCommand.Parameters["@Rate_Count"].Value = illust.Rate_Count;
                }
                if (illust.Score >= 0)
                {
                    update_str += ",Score=@Score";
                    m_dbCommand.Parameters.Add("@Score", DbType.Int32);
                    m_dbCommand.Parameters["@Score"].Value = illust.Score;
                }
                if (illust.Title != null)
                {
                    update_str += ",Title=@Title";
                    m_dbCommand.Parameters.Add("@Title", DbType.String);
                    m_dbCommand.Parameters["@Title"].Value = illust.Title;
                }
                if (illust.Description != null)
                {
                    update_str += ",Description=@Description";
                    m_dbCommand.Parameters.Add("@Description", DbType.String);
                    m_dbCommand.Parameters["@Description"].Value = illust.Description;
                }
                if (illust.Tag != null)
                {
                    update_str += ",Tag=@Tag";
                    m_dbCommand.Parameters.Add("@Tag", DbType.String);
                    m_dbCommand.Parameters["@Tag"].Value = illust.Tag;
                }
                if (illust.Tool != null)
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

        #endregion


        #region SQL Operations For User

        //插入用户信息到sql和内存列表中（自动跳过null参数） [STA]
        private bool __insert_user(User user)
        {
            var insert_user_data = "INSERT INTO User(ID, Name, Description, User_Face, User_Face_Url, Home_Page, Gender, Personal_Tags, Address, Birthday, Twitter, HTTP_Status, Last_Update) VALUES(@ID";
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

            if (!string.IsNullOrEmpty(user.Twitter))
            {
                insert_user_data += ",@Twitter";
                m_dbCommand.Parameters.Add("@Twitter", DbType.String);
                m_dbCommand.Parameters["@Twitter"].Value = user.Twitter;
            }
            else
                insert_user_data += ",NULL";

            insert_user_data += ",@HTTP_Status,@Last_Update)";
            m_dbCommand.Parameters.Add("@HTTP_Status", DbType.Int32);
            m_dbCommand.Parameters["@HTTP_Status"].Value = user.HTTP_Status;
            m_dbCommand.Parameters.Add("@Last_Update", DbType.Int64);
            m_dbCommand.Parameters["@Last_Update"].Value = user.Last_Update;

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
        private bool __update_user(User user)
        {
            var update_user_data = "UPDATE User SET HTTP_Status=@HTTP_Status, Last_Update=@Last_Update";
            m_dbCommand.Parameters.Add("@HTTP_Status", DbType.Int32);
            m_dbCommand.Parameters["@HTTP_Status"].Value = user.HTTP_Status;
            m_dbCommand.Parameters.Add("@Last_Update", DbType.Int64);
            m_dbCommand.Parameters["@Last_Update"].Value = user.Last_Update;

            //数据保护：非200时不覆盖写入已存数据
            if (user.HTTP_Status == (int)HttpStatusCode.OK)
            {
                if (user.Name != null)
                {
                    update_user_data += ",Name=@Name";
                    m_dbCommand.Parameters.Add("@Name", DbType.String);
                    m_dbCommand.Parameters["@Name"].Value = user.Name;
                }

                if (user.Description != null)
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

                if (user.User_Face_Url != null)
                {
                    update_user_data += ",User_Face_Url=@User_Face_Url";
                    m_dbCommand.Parameters.Add("@User_Face_Url", DbType.String);
                    m_dbCommand.Parameters["@User_Face_Url"].Value = user.User_Face_Url;
                }

                if (user.Home_Page != null)
                {
                    update_user_data += ",Home_Page=@Home_Page";
                    m_dbCommand.Parameters.Add("@Home_Page", DbType.String);
                    m_dbCommand.Parameters["@Home_Page"].Value = user.Home_Page;
                }

                if (user.Gender != null)
                {
                    update_user_data += ",Gender=@Gender";
                    m_dbCommand.Parameters.Add("@Gender", DbType.String);
                    m_dbCommand.Parameters["@Gender"].Value = user.Gender;
                }

                if (user.Address != null)
                {
                    update_user_data += ",Address=@Address";
                    m_dbCommand.Parameters.Add("@Address", DbType.String);
                    m_dbCommand.Parameters["@Address"].Value = user.Address;
                }

                if (user.Birthday != null)
                {
                    update_user_data += ",Birthday=@Birthday";
                    m_dbCommand.Parameters.Add("@Birthday", DbType.String);
                    m_dbCommand.Parameters["@Birthday"].Value = user.Birthday;
                }

                if (user.Twitter != null)
                {
                    update_user_data += ",Twitter=@Twitter";
                    m_dbCommand.Parameters.Add("@Twitter", DbType.String);
                    m_dbCommand.Parameters["@Twitter"].Value = user.Twitter;
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

        #endregion

        #endregion

        //数据解析
        #region Data Parser

        //单独获取未成功的用户头像，不建议调用额 [STA]
        private void _fetch_user_face_callback()
        {
            int current_index = -1;
            do
            {
                m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                //scanning to find an unsuccessful data
                int i;
                uint id;
                int count = m_user_list.Count;
                for (i = current_index + 1, current_index = -1; i < count && current_index == -1; i++)
                {
                    switch (m_user_list.ElementAt(i).Value)
                    {
                        case -1:
                        case 200:
                            continue;
                        case 0:
                            current_index = i;
                            break;
                        default:
                            if (!m_ignore_non_200_status) current_index = i;
                            break;
                    }
                }
                id = (current_index != -1) ? m_user_list.ElementAt(current_index).Key : 0;
                if (current_index != -1) m_user_list[id] = -1; //set used
                m_sqlThreadLock.ReleaseWriterLock();
                if (current_index == -1) break;

                //fetch start
                var info = GetUserInfo(id);
                try
                {
                    var img = _util_download_image_from_url(info.User_Face_Url, info.ID, out info.HTTP_Status);

                    var mm = new MemoryStream();
                    img.Save(mm, img.RawFormat);
                    mm.Position = 0;

                    byte[] buf = new byte[mm.Length];
                    mm.Read(buf, 0, (int)mm.Length);
                    mm.Close();
                    //succeeded fetch user image
                    var update_user = "UPDATE User SET User_Face=@User_Face, HTTP_Status=@HTTP_Status, Last_Update=@Last_Update WHERE ID=@ID";
                    m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                    m_dbCommand.CommandText = update_user;
                    m_dbCommand.Parameters.Add("@User_Face", DbType.Binary);
                    m_dbCommand.Parameters.Add("@HTTP_Status", DbType.Int32);
                    m_dbCommand.Parameters.Add("@Last_Update", DbType.Int64);
                    m_dbCommand.Parameters.Add("@ID", DbType.Int32);
                    m_dbCommand.Parameters["@User_Face"].Value = buf;
                    m_dbCommand.Parameters["@HTTP_Status"].Value = info.HTTP_Status;
                    m_dbCommand.Parameters["@Last_Update"].Value = info.Last_Update;
                    m_dbCommand.Parameters["@ID"].Value = info.ID;
                    m_dbCommand.ExecuteNonQuery();
                    m_dbCommand.Parameters.Clear();

                    m_sqlThreadLock.ReleaseWriterLock();
                }
                catch (Exception)
                {

                    //failed fetch user image
                    var update_user = "UPDATE User SET User_Face=NULL, HTTP_Status=@HTTP_Status, Last_Update=@Last_Update WHERE ID=@ID";
                    m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                    m_dbCommand.CommandText = update_user;
                    m_dbCommand.Parameters.Add("@HTTP_Status", DbType.Int32);
                    m_dbCommand.Parameters.Add("@Last_Update", DbType.Int64);
                    m_dbCommand.Parameters.Add("@ID", DbType.Int32);
                    m_dbCommand.Parameters["@HTTP_Status"].Value = info.HTTP_Status;
                    m_dbCommand.Parameters["@Last_Update"].Value = info.Last_Update;
                    m_dbCommand.Parameters["@ID"].Value = info.ID;
                    m_dbCommand.ExecuteNonQuery();
                    m_dbCommand.Parameters.Clear();

                    m_sqlThreadLock.ReleaseWriterLock();
                    //throw;
                }
            } while (current_index != -1);
        }
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
            illust.Last_Update = (ulong)VBUtil.Utils.Others.ToUnixTimestamp(DateTime.Now);

            var ns = new NetStream();
            ns.RetryTimes = 0;
            //获取基本的信息
            try
            {
                var url = "http://www.pixiv.net/member_illust.php?mode=medium&illust_id=" + id;
                ns.HttpGet(url);

                var http_str = ns.ReadResponseString();
                http_str = http_str.Replace("\r", "").Replace("\n", "");
                //解析开始~
                #region Parsing
                //万能的正则表达式啊！~赐予我力量吧

                //在html代码里匹配每一个数据
                string failed_section = "";

                //头像url user_face_url
                var str_ptr_user_face_url = "<img\\ssrc=\"(?<url>[^\"]*?)\"\\salt=\"\"\\sclass=\"user-image\">";
                var match = Regex.Match(http_str, str_ptr_user_face_url);
                string user_face_url = match.Success ? match.Result("${url}") : "";
                failed_section += match.Success ? "" : "user_face_url ";

                //用户名称 user_name
                var str_ptr_user_name = "<h1\\sclass=\"user\">(?<user_name>.*?)</h1>";
                match = Regex.Match(http_str, str_ptr_user_name);
                string user_name = match.Success ? match.Result("${user_name}") : "";
                failed_section += match.Success ? "" : "user_name ";

                //用户id user_id
                var str_ptr_user_id = "<input\\stype=\"hidden\"\\sname=\"user_id\"\\svalue=\"(?<user_id>\\d+)\">";
                match = Regex.Match(http_str, str_ptr_user_id);
                uint user_id = match.Success ? uint.Parse(match.Result("${user_id}")) : 0;
                failed_section += match.Success ? "" : "user_id ";

                //投稿相关的信息 [illust_meta]
                var str_ptr_illust_meta = "<ul\\sclass=\"meta\">(?<meta>.*?)</ul>";
                match = Regex.Match(http_str, str_ptr_illust_meta);
                string illust_meta = match.Success ? match.Result("${meta}") : "";
                failed_section += match.Success ? "" : "meta ";
                var str_ptr_illust_split = "<li>(?<info>.*?)</li>";

                //投稿时间 submit_time [submit_time_str]
                match = Regex.Match(illust_meta, str_ptr_illust_split);
                string submit_time_str = match.Result("${info}");
                match = match.NextMatch();

                //将投稿时间转变为DateTime类型
                var str_ptr_submit_time_split = "(?<year>\\d+)年(?<month>\\d+)月(?<day>\\d+)日\\s(?<hour>\\d+):(?<minute>\\d+)";
                var tmpMatch = Regex.Match(submit_time_str, str_ptr_submit_time_split);
                int year = int.Parse(tmpMatch.Result("${year}")), month = int.Parse(tmpMatch.Result("${month}")), day = int.Parse(tmpMatch.Result("${day}")), hour = int.Parse(tmpMatch.Result("${hour}")), minute = int.Parse(tmpMatch.Result("${minute}"));
                DateTime submit_time = new DateTime(year, month, day, hour, minute, 0);
                ulong submit_unix_timestamp = (ulong)VBUtil.Utils.Others.ToUnixTimestamp(submit_time);

                //作品分辨率 illust_size [illust_size_str]
                string illust_size_str = match.Result("${info}");
                match = match.NextMatch();
                var str_ptr_split_image_size = "(?<width>\\d+)×(?<height>\\d+)";
                tmpMatch = Regex.Match(illust_size_str, str_ptr_split_image_size);
                Size illust_size = new Size(int.Parse(tmpMatch.Result("${width}")), int.Parse(tmpMatch.Result("${height}")));

                //工具（如果可能有的话） tools
                var str_ptr_tool_info = "<ul\\sclass=\"tools\">(?<subtool>.*?)</ul>";
                string tools = "";
                while (match.Success)
                {
                    tmpMatch = Regex.Match(match.Result("${info}"), str_ptr_tool_info);
                    if (tmpMatch.Success)
                    {
                        tmpMatch = Regex.Match(tmpMatch.Result("${subtool}"), str_ptr_illust_split);
                        while (tmpMatch.Success)
                        {
                            tools += tmpMatch.Result("${info}") + multi_data_split_string;
                            tmpMatch = tmpMatch.NextMatch();
                        }
                        break;
                    }
                    match = match.NextMatch();
                }
                tools = tools.Length >= multi_data_split_string.Length ? tools.Substring(0, tools.Length - multi_data_split_string.Length) : tools;
                //failed_section += match.Success ? "" : "tools "; //optional

                //点击 click
                var str_ptr_click = "<dd\\sclass=\"view-count\">(?<click>\\d+)</dd>";
                match = Regex.Match(http_str, str_ptr_click);
                int click = match.Success ? int.Parse(match.Result("${click}")) : 0;
                failed_section += match.Success ? "" : "clicks ";

                //评分数 rate_count
                var str_ptr_rate_count = "<dd\\sclass=\"rated-count\">(?<rate_count>\\d+)</dd>";
                match = Regex.Match(http_str, str_ptr_rate_count);
                int rate_count = match.Success ? int.Parse(match.Result("${rate_count}")) : 0;
                failed_section += match.Success ? "" : "rate_count ";

                //评分 score
                var str_ptr_rate = "<dd\\sclass=\"score-count\">(?<score>\\d+)</dd>";
                match = Regex.Match(http_str, str_ptr_rate);
                int score = match.Success ? int.Parse(match.Result("${score}")) : 0;
                failed_section += match.Success ? "" : "score ";

                //标签 tags
                var str_ptr_tag_all = "<ul\\sclass=\"tags\">(?<tags>.*?)</ul>";
                var str_ptr_tag_split = "<a\\shref=\"(?<tag_url>[^\"]*?)\"\\sclass=\"text\">(?<tag>.*?)</a>";
                match = Regex.Match(http_str, str_ptr_tag_all);
                string tags = "";
                if (match.Success)
                {
                    var tmp_str = match.Result("${tags}");
                    var match1 = Regex.Match(tmp_str, str_ptr_tag_split);
                    while (match1.Success)
                    {
                        var tag_href_url = match1.Result("${tag_url}");
                        var tag = match1.Result("${tag}");
                        tags += tag + multi_data_split_string;
                        match1 = match1.NextMatch();
                    }
                }
                tags = tags.Length >= multi_data_split_string.Length ? tags.Substring(0, tags.Length - multi_data_split_string.Length) : tags;
                //failed_section += match.Success ? "" : "tags ";

                //标题 title
                var str_ptr_title = "</ul><h1\\sclass=\"title\">(?<title>.*?)</h1><p\\sclass=\"caption\">";
                match = Regex.Match(http_str, str_ptr_title);
                string title = match.Success ? match.Result("${title}") : "";
                failed_section += match.Success ? "" : "title ";

                //描述 description
                var str_ptr_description = "<div\\sclass=\"_unit\\s_work-detail-unit\">.*?<p\\sclass=\"caption\">(?<description>.*?)</p><div";
                match = Regex.Match(http_str, str_ptr_description);
                string description = match.Success ? match.Result("${description}") : "";
                //failed_section += match.Success ? "" : "description "; //optional
                #endregion

                if (failed_section.Length > 0) throw new Exception("Failed to get the following data: " + failed_section);
                //making it a full structure
                illust.Author_ID = user_id;
                illust.Title = title;
                illust.Description = description;
                illust.Tag = tags;
                illust.Click = click;
                illust.Submit_Time = submit_unix_timestamp;
                illust.HTTP_Status = (int)ns.HTTP_Response.StatusCode;
                illust.Tool = tools;
                illust.Size = illust_size;
                illust.Rate_Count = rate_count;
                illust.Score = score;
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
            catch (Exception)
            {
                illust.HTTP_Status = -2;
                throw;
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

            var ns = new NetStream();
            ns.RetryTimes = 0;

            try
            {
                var url = "http://www.pixiv.net/member.php?id=" + id;
                ns.HttpGet(url);

                var http_str = ns.ReadResponseString();
                http_str = http_str.Replace("\r", "").Replace("\n", "");

                #region Parsing

                //用户头像 [user_face_url]
                var str_ptr_user_face_url = "<img\\ssrc=\"(?<user_face_url>.*?)\"\\salt=\"\"\\sclass=\"user-image\">";
                var match = Regex.Match(http_str, str_ptr_user_face_url);
                string user_face_url = match.Success ? match.Result("${user_face_url}") : "";

                //用户头像图片 [user_face]
                int user_face_http_status = 0;
                var user_face = _util_download_image_from_url(user_face_url, id, out user_face_http_status);

                //用户名称 [name]
                var str_ptr_name = "<h1\\sclass=\"user\">(?<name>.*?)</h1>";
                match = Regex.Match(http_str, str_ptr_name);
                string name = match.Success ? match.Result("${name}") : "";

                //用户描述 [description]
                var str_ptr_description = "<tr\\sclass=\"profile-comment\">.*?</td><td\\sclass=\"td2\">(?<description>.*?)</td></tr>";
                match = Regex.Match(http_str, str_ptr_description);
                string description = match.Success ? match.Result("${description}") : "";

                //个人主页 [home_page]
                var str_ptr_home_page = "<tr\\sclass=\"profile-web\">.*?</td><td\\sclass=\"td2\">(?<home_page>.*?)</td></tr>";
                match = Regex.Match(http_str, str_ptr_home_page);
                string home_page = match.Success ? match.Result("${home_page}") : "";

                //性别 [gender]
                var str_ptr_gender = "<tr><td\\sclass=\"td1\">性别</td><td\\sclass=\"td2\">(?<gender>.*?)</td></tr>";
                match = Regex.Match(http_str, str_ptr_gender);
                string gender = match.Success ? match.Result("${gender}") : "";

                //地址 [address]
                var str_ptr_address = "<tr><td\\sclass=\"td1\">地址</td><td\\sclass=\"td2\">(?<address>.*?)</td></tr>";
                match = Regex.Match(http_str, str_ptr_address);
                string address = match.Success ? match.Result("${address}") : "";

                //生日 [Birthday]
                var str_ptr_birthday = "<tr><td\\sclass=\"td1\">生日</td><td\\sclass=\"td2\">(?<birthday>.*?)</td></tr>";
                match = Regex.Match(http_str, str_ptr_birthday);
                string birthday = match.Success ? match.Result("${birthday}") : "";

                //Twitter [twitter]
                var str_ptr_twitter = "<tr\\sclass=\"profile-twitter\">.*?</td><td\\sclass=\"td2\">(?<twitter>.*?)</td></tr>";
                match = Regex.Match(http_str, str_ptr_twitter);
                string twitter = match.Success ? match.Result("${twitter}") : "";

                //个人标签 [tag]
                var str_ptr_tag = "<tr><td\\sclass=\"td1\">个人标签</td><td\\sclass=\"td2\">(?<tags>.*?)</td></tr>";
                match = Regex.Match(http_str, str_ptr_tag);
                string tag = "";
                if (match.Success)
                {
                    var str_ptr_tag_split = "<a\\shref=\"(?<href_url>.*?)\">(?<tag>.*?)</a>";
                    match = Regex.Match(match.Result("${tags}"), str_ptr_tag_split);
                    while (match.Success)
                    {
                        tag += match.Result("${tag}") + multi_data_split_string;
                        match = match.NextMatch();
                    }
                    if (tag.Length > multi_data_split_string.Length) tag = tag.Substring(0, tag.Length - multi_data_split_string.Length);
                }
                #endregion


                user.Name = name;
                user.User_Face_Url = user_face_url;
                user.User_Face = user_face;
                user.HTTP_Status = (int)ns.HTTP_Response.StatusCode;
                user.Home_Page = home_page;
                user.Gender = gender;
                user.Address = address;
                user.Birthday = birthday;
                user.Twitter = twitter;
                user.Personal_Tags = tag;
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
            var ns = new NetStream();
            try
            {
                //using xhr
                var header_param = new Parameters();
                header_param.Add("X-Request-With", "XmlHttpRequest");
                header_param.Add("Origin", "http://www.pixiv.net");
                header_param.Add("Referer", "http://www.pixiv.net/whitecube/user/" + user_id);

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
                var response = (HttpWebResponse)ex.Response;
                http_status = (int)response.StatusCode;
                throw;
            }
            catch (Exception)
            {
                http_status = -2;
                throw;
            }
        }

        #endregion

        //静态函数（登陆接口）
        #region Static Functions

        //2016-11-01 16:50:30 test succeeded.
        /// <summary>
        /// login to Pixiv.net, using official api (captured by Fiddler)
        /// </summary>
        /// <param name="username">Your user name(possibly an e-mail address)</param>
        /// <param name="password">Your password</param>
        /// <remarks>[MTA] [no throw]</remarks>
        public static void Login(string username, string password)
        {
            string login_main_url = "https://accounts.pixiv.net/login?lang=zh&source=pc&view_type=page&ref=wwwtop_accounts_index";
            string login_request_url = "https://accounts.pixiv.net/api/login?lang=zh";

            var ns = new NetStream();
            try
            {
                Debug.Print("Initializing login variables.");

                ns.HttpGet(login_main_url);
                var http_str = ns.ReadResponseString();
                http_str = http_str.Replace("\r", "").Replace("\n", "");
                ns.Close();

                //get the json data
                string reg_ptr = "<input type=\"hidden\"\\sid=\"init-config\"\\sclass=\"json-data\"\\svalue='(?<json>.*?)'>";
                var match = Regex.Match(http_str, reg_ptr);
                if (match.Success)
                {
                    var json_str = match.Result("${json}");
                    var json = (JToken)JsonConvert.DeserializeObject(json_str);

                    //check if logged in
                    var loggedin = json.Value<bool>("pixiv.user.loggedIn");
                    if (loggedin) return;
                    var postKey = json.Value<string>("pixivAccount.postKey");

                    //building xhr header
                    var header_param = new Parameters();
                    header_param.Add("X-Request-With", "XMLHttpRequest");
                    header_param.Add("Origin", "https://accounts.pixiv.net");
                    header_param.Add("Referer", login_main_url);
                    //building form data
                    var post_param = new Parameters();
                    post_param.Add("pixiv_id", username);
                    post_param.Add("password", password);
                    post_param.Add("captcha", "");
                    post_param.Add("g_recaptcha_response", "");
                    post_param.Add("post_key", postKey);
                    post_param.Add("source", "pc");

                    Debug.Print("Posting login data.");
                    //if nothing wrong, sending login data
                    ns.HttpPost(login_request_url, post_param, "application/x-www-form-urlencoded", header_param);
                    var login_result_str = ns.ReadResponseString();
                    ns.Close();
                    var login_result_json = (JToken)JsonConvert.DeserializeObject(login_result_str);

                    bool error = login_result_json.Value<bool>("error");
                    if (error)
                    {
                        throw new Exception("Login error: " + login_result_json.ToString());
                    }
                }
                else
                {
                    throw new Exception("Failed to get the login json data from html!");
                }
                Debug.Print("Login succeeded.");
                LoginSucceeded?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.Print("Login failed: \n" + ex.ToString());
                LoginFailed?.Invoke(ex.ToString());
            }
        }
        #endregion


        #region Public Functions


        //获取投稿信息 [MTA] [throwable]
        public Illust GetIllustInfo(uint id)
        {
            var get_value_str = "SELECT ID, Author_ID, Title, Description, Tag, Tool, Click, Width, Height, Rate_Count, Score, Submit_Time, HTTP_Status, Last_Update FROM Illust WHERE ID=" + id;
            var ret = new Illust();
            if (id == 0) return ret;
            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
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
                ret.Size = new Size(dr.GetInt32(7), dr.GetInt32(8));
                ret.Rate_Count = dr.GetInt32(9);
                ret.Score = dr.GetInt32(10);
                ret.Submit_Time = (ulong)dr.GetInt64(11);
                ret.HTTP_Status = dr.GetInt32(12);
                ret.Last_Update = (ulong)dr.GetInt64(13);
                dr.Close();
            }
            catch (Exception)
            {

                throw;
            }
            m_sqlThreadLock.ReleaseWriterLock();
            return ret;
        }
        //获取用户信息 [MTA] [throwable] todo: fixed table section
        public User GetUserInfo(uint userID)
        {
            var get_user_str = "SELECT * FROM User WHERE ID=" + userID;
            var ret = new User();
            if (userID == 0) return ret;
            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
            try
            {
                m_dbCommand.CommandText = get_user_str;
                var dr = m_dbCommand.ExecuteReader();
                var suc = dr.Read();
                if (!suc)
                {
                    ret.ID = userID;
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
                ret.HTTP_Status = dr.GetInt32(5);
                ret.Last_Update = (ulong)dr.GetInt64(6);
                ret.User_Face_Url = dr.IsDBNull(4) ? "" : dr.GetString(4);

                dr.Close();
            }
            catch (Exception)
            {

                throw;
            }
            m_sqlThreadLock.ReleaseWriterLock();
            return ret;
        }
        //中止工作线程
        public void AbortWorkingThread()
        {
            m_abort_flag = true;
        }

        #endregion
    }
}
