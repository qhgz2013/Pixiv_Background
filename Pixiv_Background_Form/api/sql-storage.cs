using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.IO;
using System.Text.RegularExpressions;
using System.Data.SQLite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Drawing;
using System.Data;
using System.Net;
using Pixiv_Background_Form.NetUtils;

namespace Pixiv_Background_Form
{

    public class DataStorage
    {
        //类成员定义
        #region Member Definations
        private PixivAuth m_auth;
        private API m_api;
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
        /// <remarks></remarks>
        public DataStorage(API api, bool ignore_non_200 = false, PixivAuth auth = null)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();

            //初始化成员变量
            m_api = api;
            m_auth = auth;
            m_ignore_non_200_status = ignore_non_200;
            m_sqlThreadLock = new ReaderWriterLock();
            m_dataThreadLock = new ReaderWriterLock();

            m_illust_thd = new Thread[M_MAX_ILLUST_SYNC_THREAD];
            m_user_thd = new Thread[M_MAX_USER_SYNC_THREAD];

            _create_monitor_thread();

            m_illust_query_list = new List<uint>();
            m_user_query_list = new List<uint>();

            Tracer.GlobalTracer.TraceInfo("Started SQL Database Initialize");
            //InitializeStarted?.Invoke();
            //初始化数据库
            _init_database();
            Tracer.GlobalTracer.TraceInfo("Ended SQL Database Initialize");
            //InitializeEnded?.Invoke();

            //开始监控线程
            m_monitor_thd.Start();
        }

        //初始化数据库
        private void _init_database()
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            string create_db = "Data Source=appdata.db; Version=3;"; //declare using SQL v3
            bool file_exist = File.Exists("appdata.db");
            if (!file_exist)
            {
                Tracer.GlobalTracer.TraceInfo("System found the database does not exist, the program will create a new one.");
                File.Create("appdata.db").Close();
            }

            Tracer.GlobalTracer.TraceInfo("Connecting to SQL database.");
            m_dbConnection = new SQLiteConnection(create_db);
            m_dbConnection.Open();

            m_dbCommand = new SQLiteCommand(m_dbConnection);

            m_illust_list = new Dictionary<uint, int>();
            m_user_list = new Dictionary<uint, int>();

            //not exist: creating basic structure
            if (!file_exist)
            {
                Tracer.GlobalTracer.TraceInfo("Creating basic table and inserting variables into database.");

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
                Tracer.GlobalTracer.TraceInfo("Loading SQL database config.");
                //检查是否存在数据表
                var check_table_count = "SELECT COUNT(*) FROM sqlite_master WHERE type='table'";
                m_dbCommand.CommandText = check_table_count;
                int count = Convert.ToInt32(m_dbCommand.ExecuteScalar());
                if (count == 0)
                {
                    Tracer.GlobalTracer.TraceInfo("We found that the database contains no table, it should not be the normal state, deleting and re-initing it");
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

                Tracer.GlobalTracer.TraceInfo("Database version:" + db_version + " (Current:" + M_CURRENT_DBVERSION + ")");
                //更新数据库
                if (db_version != M_CURRENT_DBVERSION)
                {
                    SQLPatch.Patch(db_version, M_CURRENT_DBVERSION, m_dbConnection);
                }

                Tracer.GlobalTracer.TraceInfo("Initialize data to RAM.");

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
        //开始获取投稿数据（当前的id,当前任务在任务队列的位置，总任务队列的数量）
        public event EventHandler<FetchDataEventArgs> FetchIllustStarted;
        //成功获取单个投稿数据（参数同上，多了个投稿的数据）
        public event EventHandler<FetchIllustEventArgs> FetchIllustSucceeded;
        //获取单个投稿数据失败
        public event EventHandler<FetchIllustEventArgs> FetchIllustFailed;
        public event EventHandler<FetchDataEventArgs> FetchUserStarted;
        public event EventHandler<FetchUserEventArgs> FetchUserSucceeded;
        public event EventHandler<FetchUserEventArgs> FetchUserFailed;

        //需要登陆后才能操作
        //public event NoArgEventHandler LoginRequired;

        #endregion //Event Definations

        //数据导入
        #region Data Import

        #region From Path
        //从path的本地目录中跟新文件列表 [STA]
        private void _update_file_list(string path)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();

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
                        Tracer.GlobalTracer.TraceInfo("Fetching: Illust " + id);
                    }
                }
            }
            //递归调用，获取子目录
            //if (m_include_subdir)
            //{
            //    foreach (var item in dir_info.GetDirectories())
            //    {
            //        _update_file_list(item.FullName);
            //    }
            //}
        }

        //update local database from the current directory.
        /// <summary>
        /// 从文件夹中读取所有图片（文件名为***_p*）的相关信息，并保存到数据库中
        /// </summary>
        /// <param name="path"></param>
        public void UpdateFileList(string path)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            ThreadPool.QueueUserWorkItem(
                (object obj) =>
                {

                    //UpdateLocalFileListStarted?.Invoke();

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

                        Tracer.GlobalTracer.TraceInfo("Error occurred while updating local file list: " + ex.ToString());
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

                    //UpdateLocalFileListEnded?.Invoke();

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
            Tracer.GlobalTracer.TraceFunctionEntry();
            if (m_illust_working_thd_count >= M_MAX_ILLUST_SYNC_THREAD) return;
            //Tracer.GlobalTracer.TraceInfo("Sync Illust Thread Created");
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
                Tracer.GlobalTracer.TraceInfo("Started fetching illust id=" + id);
                FetchIllustStarted?.Invoke(this, new FetchDataEventArgs(id, m_query_finished, m_query_count));

                m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                Illust illust = __get_illust(id);
                m_sqlThreadLock.ReleaseWriterLock();
                //Illust illust = new Illust();
                try
                {
                    //解析
                    m_api.ParseIllustInfo(id, out illust);

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

                    FetchIllustSucceeded?.Invoke(this, new FetchIllustEventArgs(id, m_query_finished + 1, m_query_count, illust));
                }
                catch (Exception ex) //获取投稿时出错
                {
                    Tracer.GlobalTracer.TraceInfo("Exception occured while fetching illust info: \n" + ex.ToString());

                    //理论上在获取投稿信息时会自动把http code给补上，所以不需要再检查response了
                    m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                    __auto_insert_illust(illust);
                    m_sqlThreadLock.ReleaseWriterLock();
                    //throw;
                    FetchIllustFailed?.Invoke(this, new FetchIllustEventArgs(id, m_query_finished + 1, m_query_count, illust));
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
            //Tracer.GlobalTracer.TraceInfo("Sync Illust Thread Exited");
        }
        private void _sync_user_callback()
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            if (m_user_working_thd_count >= M_MAX_USER_SYNC_THREAD) return;
            //Tracer.GlobalTracer.TraceInfo("Sync User Thread Created");
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
                Tracer.GlobalTracer.TraceInfo("Started fetching user id=" + id);
                FetchUserStarted?.Invoke(this, new FetchDataEventArgs(id, m_query_finished, m_query_count));

                m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                User user = __get_user(id);
                m_sqlThreadLock.ReleaseWriterLock();
                //User user = new User();
                try
                {
                    //解析
                    m_api.ParseUserInfo(id, out user);

                    m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                    __auto_insert_user(user);
                    m_sqlThreadLock.ReleaseWriterLock();

                    FetchUserSucceeded?.Invoke(this, new FetchUserEventArgs(id, m_query_finished + 1, m_query_count, user));
                }
                catch (Exception ex) //获取投稿时出错
                {
                    Tracer.GlobalTracer.TraceInfo("Exception occured while fetching illust info: \n" + ex.ToString());

                    //理论上在获取投稿信息时会自动把http code给补上，所以不需要再检查response了
                    m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
                    __auto_insert_user(user);
                    m_sqlThreadLock.ReleaseWriterLock();
                    //throw;
                    FetchUserFailed?.Invoke(this, new FetchUserEventArgs(id, m_query_finished + 1, m_query_count, user));
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
            //Tracer.GlobalTracer.TraceInfo("Sync User Thread Exited");
        }
        private void _monitor_callback()
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            do
            {

                try
                {
                    m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);
                    int illust_count = m_illust_query_list.Count;
                    m_dataThreadLock.ReleaseWriterLock();

                    if (m_illust_working_thd_count < M_MAX_ILLUST_SYNC_THREAD && illust_count != 0)
                    {

                        //Tracer.GlobalTracer.TraceInfo("Multi-thread access for illust started!");
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
                        //Tracer.GlobalTracer.TraceInfo("Multi-thread access for user started!");
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
                    Tracer.GlobalTracer.TraceInfo("Unexpected exception happened in monitor thread:");
                    Tracer.GlobalTracer.TraceInfo(ex.ToString());
                }

                _join_all_thread(1000);

                //线程为空，提交数据
                if (m_illust_working_thd_count == 0 && m_user_working_thd_count == 0)
                {
                    if (m_dbTransaction != null)
                    {
                        Tracer.GlobalTracer.TraceInfo("Thread All Exited, Commiting data");
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
                    Tracer.GlobalTracer.TraceInfo("Query completed, resetting query list size");
                    m_query_count = 0;
                    m_query_finished = 0;
                }

                //Tracer.GlobalTracer.TraceInfo("[Debug] - m_illust_working_thd_count = " + m_illust_working_thd_count + ", m_user_working_thd_count = " + m_user_working_thd_count + ", m_illust_query_list = " + ic + ", m_user_query_list = " + uc);

                Thread.Sleep(1000);

            } while (!m_abort_flag);

            Tracer.GlobalTracer.TraceInfo("Abort signal received, waiting all thread to be aborted...");
            _join_all_thread();

            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
            if (m_dbTransaction != null)
            {
                Tracer.GlobalTracer.TraceInfo("Commiting data");
                m_dbTransaction.Commit();
                m_dbTransaction = null;
            }
            m_sqlThreadLock.ReleaseWriterLock();
            //线程安全退出，取消终止标识
            Tracer.GlobalTracer.TraceInfo("Monitor Thread exited");
            m_monitor_thd = null;
            m_abort_flag = false;
        }

        private void _create_monitor_thread()
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            if (m_monitor_thd == null)
            {
                m_monitor_thd = new Thread(_monitor_callback);
                m_monitor_thd.IsBackground = false;
                m_monitor_thd.Name = "Pixiv Data Monitor Thread";
            }
        }
        //从list中更新数据到query list
        private void _update_query_list()
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
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
            Tracer.GlobalTracer.TraceInfo("Illust queue: " + m_illust_query_list.Count + ", User queue: " + m_user_query_list.Count + ", Querying: " + m_querying_count);
        }

        private void _join_all_thread(int timeout = int.MaxValue)
        {
            //Tracer.GlobalTracer.TraceFunctionEntry();
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
            Tracer.GlobalTracer.TraceFunctionEntry();
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
                Tracer.GlobalTracer.TraceInfo("Error occured when updating illust data!:" + ex.ToString());
                return false;
            }
            finally
            {
                m_dbCommand.Parameters.Clear();
            }
        }
        private bool __update_illust(Illust illust, bool force_mode = false)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            var update_str = "UPDATE Illust SET HTTP_Status=@HTTP_Status, Last_Update=@Last_Update";
            m_dbCommand.Parameters.Add("@HTTP_Status", DbType.Int32);
            m_dbCommand.Parameters["@HTTP_Status"].Value = illust.HTTP_Status;
            m_dbCommand.Parameters.Add("@Last_Update", DbType.Int32);
            m_dbCommand.Parameters["@Last_Update"].Value = illust.Last_Update;

            //数据保护：在AuthorID=0时拒绝写入所有数据
            if (!force_mode && illust.Author_ID == 0)
            {
                m_dbCommand.Parameters.Clear();
                return false;
            }

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
                Tracer.GlobalTracer.TraceInfo("Error occured when updating illust data!:" + ex.ToString());
                return false;
            }
            finally
            {
                m_dbCommand.Parameters.Clear();
            }
        }
        private bool __auto_insert_illust(Illust illust)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            if (m_illust_list.ContainsKey(illust.ID))
                return __update_illust(illust);
            else
                return __insert_illust(illust);
        }
        private Illust __get_illust(uint id)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            var data = __query_illust_by_specified_constraint("ID = " + id);
            if (data.Length == 0)
            {
                var ret = new Illust();
                ret.ID = id;
                return ret;
            }
            else
                return data[0];
        }
        //根据特定的sql where约束来获取数据
        private Illust[] __query_illust_by_specified_constraint(string constraint)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            var get_value_str = "SELECT ID, Author_ID, Title, Description, Tag, Tool, Click, Bookmark_Count, Comment_Count, Width, Height, Rate_Count, Score, Submit_Time, HTTP_Status, Last_Update, Last_Success_Update, Page, Origin FROM Illust WHERE " + constraint;
            var list = new List<Illust>();
            m_dbCommand.CommandText = get_value_str;
            var dr = m_dbCommand.ExecuteReader();
            while (dr.Read())
            {
                var data = new Illust();
                data.ID = (uint)dr.GetInt32(0);
                data.Author_ID = (uint)dr.GetInt32(1);
                data.Title = dr.IsDBNull(2) ? "" : dr.GetString(2);
                data.Description = dr.IsDBNull(3) ? "" : dr.GetString(3);
                data.Tag = dr.IsDBNull(4) ? "" : dr.GetString(4);
                data.Tool = dr.IsDBNull(5) ? "" : dr.GetString(5);
                data.Click = dr.GetInt32(6);
                data.Bookmark_Count = dr.GetInt32(7);
                data.Comment_Count = dr.GetInt32(8);
                data.Size = new Size(dr.GetInt32(9), dr.GetInt32(10));
                data.Rate_Count = dr.GetInt32(11);
                data.Score = dr.GetInt32(12);
                data.Submit_Time = (ulong)dr.GetInt64(13);
                data.HTTP_Status = dr.GetInt32(14);
                data.Last_Update = (ulong)dr.GetInt64(15);
                data.Last_Success_Update = (ulong)dr.GetInt64(16);
                data.Page = (uint)dr.GetInt32(17);
                data.Origin = (DataOrigin)dr.GetByte(18);
                list.Add(data);
            }
            dr.Close();
            return list.ToArray();
        }
        #endregion //SQL Operations For Illust


        #region SQL Operations For User

        //插入用户信息到sql和内存列表中（自动跳过null参数） [STA]
        private bool __insert_user(User user)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
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
                Tracer.GlobalTracer.TraceInfo("Error occured when inserting user data!:" + ex.ToString());
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
            Tracer.GlobalTracer.TraceFunctionEntry();
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
                Tracer.GlobalTracer.TraceInfo("Error occured when updating user data!:" + ex.ToString());
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
            Tracer.GlobalTracer.TraceFunctionEntry();
            if (m_user_list.ContainsKey(user.ID))
                return __update_user(user);
            else
                return __insert_user(user);
        }
        private User __get_user(uint id)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            var data = __query_user_by_specified_constraint("ID = " + id);
            if (data.Length == 0)
            {
                var ret = new User();
                ret.ID = id;
                return ret;
            }
            else
                return data[0];
        }
        private User[] __query_user_by_specified_constraint(string constraint)
        {

            Tracer.GlobalTracer.TraceFunctionEntry();

            var get_user_str = "SELECT ID, Name, Description, User_Face, User_Face_Url, Home_Page, Gender, Personal_Tag, Address, Birthday, Job, Follow_Users, Follower, Illust_Bookmark_Public, Mypixiv_Users, Total_Illusts, Total_Novels, Twitter, HTTP_Status, Last_Update, Last_Success_Update FROM User WHERE " + constraint;
            var ret = new List<User>();
            try
            {
                m_dbCommand.CommandText = get_user_str;
                var dr = m_dbCommand.ExecuteReader();
                while (dr.Read())
                {
                    var data = new User();
                    data.ID = (uint)dr.GetInt32(0);
                    data.Name = dr.IsDBNull(1) ? "" : dr.GetString(1);
                    data.Description = dr.IsDBNull(2) ? "" : dr.GetString(2);
                    if (!dr.IsDBNull(3))
                    {
                        byte[] img_buf = (byte[])dr[3];
                        var mm = new MemoryStream();
                        mm.Write(img_buf, 0, img_buf.Length);
                        mm.Position = 0;
                        data.User_Face = Image.FromStream(mm);
                    }
                    data.User_Face_Url = dr.IsDBNull(4) ? "" : dr.GetString(4);
                    data.Home_Page = dr.IsDBNull(5) ? "" : dr.GetString(5);
                    data.Gender = dr.IsDBNull(6) ? "" : dr.GetString(6);
                    data.Personal_Tag = dr.IsDBNull(7) ? "" : dr.GetString(7);
                    data.Address = dr.IsDBNull(8) ? "" : dr.GetString(8);
                    data.Birthday = dr.IsDBNull(9) ? "" : dr.GetString(9);
                    data.Job = dr.IsDBNull(10) ? "" : dr.GetString(10);
                    data.Follow_Users = dr.IsDBNull(11) ? 0 : dr.GetInt32(11);
                    data.Follower = dr.IsDBNull(12) ? 0 : dr.GetInt32(12);
                    data.Illust_Bookmark_Public = dr.IsDBNull(13) ? 0 : dr.GetInt32(13);
                    data.Mypixiv_Users = dr.IsDBNull(14) ? 0 : dr.GetInt32(14);
                    data.Total_Illusts = dr.IsDBNull(15) ? 0 : dr.GetInt32(15);
                    data.Total_Novels = dr.IsDBNull(16) ? 0 : dr.GetInt32(16);
                    data.Twitter = dr.IsDBNull(17) ? "" : dr.GetString(17);
                    data.HTTP_Status = dr.IsDBNull(18) ? 0 : dr.GetInt32(18);
                    data.Last_Update = dr.IsDBNull(19) ? 0 : (ulong)dr.GetInt64(19);
                    data.Last_Success_Update = dr.IsDBNull(20) ? 0 : (ulong)dr.GetInt64(20);
                    ret.Add(data);
                }
                dr.Close();
            }
            catch (Exception)
            {

            }
            return ret.ToArray();
        }
        #endregion //SQL Operations For User

        #endregion //SQL Operations

        //公有函数
        #region Public Functions

        /// <summary>
        /// 获取投稿信息
        /// </summary>
        /// <param name="id">投稿ID</param>
        /// <param name="mode">数据更新模式</param>
        /// <returns></returns>
        public Illust GetIllustInfo(uint id, DataUpdateMode mode = DataUpdateMode.Async_Update)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            if (id == 0) return new Illust();

            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
            var info = __get_illust(id);
            m_sqlThreadLock.ReleaseWriterLock();

            if (mode != DataUpdateMode.No_Update)
            {
                var last_update = util.FromUnixTimestamp(info.Last_Update);
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
        /// <summary>
        /// 获取用户信息
        /// </summary>
        /// <param name="id">用户ID</param>
        /// <param name="mode">数据更新模式</param>
        /// <returns></returns>
        public User GetUserInfo(uint id, DataUpdateMode mode = DataUpdateMode.Async_Update)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            if (id == 0) return new User();

            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
            var info = __get_user(id);
            m_sqlThreadLock.ReleaseWriterLock();

            if (mode != DataUpdateMode.No_Update)
            {
                var last_update = util.FromUnixTimestamp(info.Last_Update);
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
        /// <summary>
        /// 中止工作线程
        /// </summary>
        /// <param name="wait"></param>
        public void AbortWorkingThread(bool wait = false)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            if (m_monitor_thd == null) return;
            m_abort_flag = true;
            if (wait) m_monitor_thd.Join();
        }
        /// <summary>
        /// 强制重新更新数据（包括成功更新的和未成功的，之前的数据不会删除）
        /// </summary>
        public void ForceUpdateAllData()
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            ThreadPool.QueueUserWorkItem((object obj) =>
            {
                Tracer.GlobalTracer.TraceFunctionEntry();
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
        /// <summary>
        /// 设置投稿信息
        /// </summary>
        /// <param name="illust">投稿信息</param>
        public void SetIllustInfo(Illust illust)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            if (illust.ID == 0) return;
            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
            m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);

            __auto_insert_illust(illust);
            _update_query_list();

            m_dataThreadLock.ReleaseWriterLock();
            m_sqlThreadLock.ReleaseWriterLock();
        }
        /// <summary>
        /// 设置用户信息
        /// </summary>
        /// <param name="user">用户信息</param>
        public void SetUserInfo(User user)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            if (user.ID == 0) return;
            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
            m_dataThreadLock.AcquireWriterLock(Timeout.Infinite);

            __auto_insert_user(user);
            _update_query_list();

            m_dataThreadLock.ReleaseWriterLock();
            m_sqlThreadLock.ReleaseWriterLock();
        }

        //查询功能
        #region fuzzy query
        /// <summary>
        /// 根据Tag进行查询
        /// </summary>
        /// <param name="tag">Tag名称</param>
        /// <returns></returns>
        public Illust[] GetIllustByTag(string tag)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            if (string.IsNullOrEmpty(tag)) return null;

            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
            var data = __query_illust_by_specified_constraint("Tag LIKE '%" + tag + "%'");
            m_sqlThreadLock.ReleaseWriterLock();
            return data.ToArray();
        }
        /// <summary>
        /// 根据作者id进行查询
        /// </summary>
        /// <param name="id">作者ID</param>
        /// <returns></returns>
        public Illust[] GetIllustByAuthorID(uint id)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            if (id == 0) return null;

            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
            var data = __query_illust_by_specified_constraint("Author_ID = " + id);
            m_sqlThreadLock.ReleaseWriterLock();
            return data.ToArray();
        }
        /// <summary>
        /// 根据作者名进行模糊查询
        /// </summary>
        /// <param name="name">作者名</param>
        /// <returns></returns>
        public Illust[] GetIllustByAuthorName(string name)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            if (string.IsNullOrEmpty(name)) return null;

            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
            var user_query_sql = "SELECT ID From User WHERE Name LIKE '%" + name + "%'";
            m_dbCommand.CommandText = user_query_sql;
            var dr = m_dbCommand.ExecuteReader();
            uint userID = 0;
            if (dr.Read())
            {
                userID = (uint)dr.GetInt32(0);
            }
            dr.Close();
            m_sqlThreadLock.ReleaseWriterLock();

            if (userID != 0)
                return GetIllustByAuthorID(userID);
            else return null;
        }
        /// <summary>
        /// 根据投稿ID进行模糊查询
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public Illust[] GetIllustByFuzzyID(string id)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            if (string.IsNullOrEmpty(id)) return null;

            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
            var data = __query_illust_by_specified_constraint("CAST(ID AS TEXT) LIKE '%" + id + "%'");
            m_sqlThreadLock.ReleaseWriterLock();
            return data.ToArray();
        }
        /// <summary>
        /// 根据标题进行模糊查询
        /// </summary>
        /// <param name="title">标题</param>
        /// <returns></returns>
        public Illust[] GetIllustByTitle(string title)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            if (string.IsNullOrEmpty(title)) return null;

            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
            var data = __query_illust_by_specified_constraint("Title LIKE '%" + title + "%'");
            m_sqlThreadLock.ReleaseWriterLock();
            return data.ToArray();
        }
        /// <summary>
        /// 根据画师名称进行模糊查询
        /// </summary>
        /// <param name="name">画师名称</param>
        /// <returns></returns>
        public User[] GetUserByName(string name)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            if (string.IsNullOrEmpty(name)) return null;

            m_sqlThreadLock.AcquireWriterLock(Timeout.Infinite);
            var data = __query_user_by_specified_constraint("Name LIKE '%" + name + "%'");
            m_sqlThreadLock.ReleaseWriterLock();
            return data.ToArray();
        }
        
        #endregion //fuzzy query

        #endregion //Public Functions


        //公有属性
        #region Public Properties
        //数据库中的投稿数量
        public int Illust_Count { get { return m_illust_list.Count; } }
        //数据库中的用户数量
        public int User_Count { get { return m_user_list.Count; } }
        #endregion //Public Properties
    }


    //event args
    public class FetchDataEventArgs : EventArgs
    {
        private uint _id;
        private int _currentTasks, _totalTasks;
        /// <summary>
        /// 投稿ID
        /// </summary>
        public uint ID { get { return _id; } set { _id = value; } }
        /// <summary>
        /// 该任务在队列中的序号
        /// </summary>
        public int CurrentTasks { get { return _currentTasks; } set { _currentTasks = value; } }
        /// <summary>
        /// 任务队列的大小
        /// </summary>
        public int TotalTasks { get { return _totalTasks; } set { _totalTasks = value; } }
        public FetchDataEventArgs(uint id, int currentTasks, int totalTasks)
        {
            _id = id;
            _currentTasks = currentTasks;
            _totalTasks = totalTasks;
        }
        public FetchDataEventArgs()
        {

        }
    }
    public class FetchIllustEventArgs : FetchDataEventArgs
    {
        private Illust _data;
        /// <summary>
        /// 投稿信息
        /// </summary>
        public Illust Data { get { return _data; } }
        public FetchIllustEventArgs(uint id, int currentTasks, int totalTasks, Illust data) : base(id, currentTasks, totalTasks)
        {
            _data = data;
        }
        public FetchIllustEventArgs() : base()
        {
            _data = new Illust();
        }
    }
    public class FetchUserEventArgs : FetchDataEventArgs
    {
        private User _data;
        /// <summary>
        /// 用户信息
        /// </summary>
        public User Data { get { return _data; } }
        public FetchUserEventArgs(uint id, int currentTasks, int totalTasks, User data) : base(id, currentTasks, totalTasks)
        {
            _data = data;
        }
        public FetchUserEventArgs() : base()
        {
            _data = new User();
        }
    }
}
