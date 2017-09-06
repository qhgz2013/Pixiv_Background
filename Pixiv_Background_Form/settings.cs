using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Pixiv_Background_Form
{

    public struct IllustKey { public uint id, page;  public override string ToString() { return id.ToString() + "p" + page.ToString(); } }

    /// <summary>
    /// 路径设置
    /// </summary>
    public struct PathSetting
    {
        /// <summary>
        /// 路径名称
        /// </summary>
        public string Directory;
        /// <summary>
        /// 是否包含子文件夹
        /// </summary>
        public bool IncludingSubDir;
        public override string ToString()
        {
            return Directory + (IncludingSubDir ? " (+subdir)" : "");
        }
    }

    public class Settings
    {
        //设置文件的保存和加载路径
        private const string M_SETTING_NAME = "settings.json";
        //图片路径
        private static List<PathSetting> m_paths;
        private const string M_PATHS_KEY = "paths";
        //是否开启不同屏幕不同的背景图片（需要开启缓存渲染）
        private static bool m_enable_multi_monitor_different_wallpaper;
        private const string M_ENABLE_MULTI_MONITOR_DIFFERENT_WALLPAPER_KEY = "enable_multi_monitor_different_wallpaper";
        //是否开启缓存渲染
        private static bool m_enable_buffering;
        private const string M_ENABLE_BUFFERING_KEY = "enable_buffering";
        //是否开启waifu2x的图片放大（需要waifu2x的exe文件路径）
        private static bool m_enable_waifu2x_upscaling;
        private const string M_ENABLE_WAIFU2X_UPSCALING_KEY = "enable_waifu2x_upscaling";
        //执行waifu2x缩放时所需要达到的最小缩放比
        private static double m_waifu2x_upscale_threshold;
        private const double M_DEFAULT_WAIFU2X_UPSCALE_THRESHOLD = 1.3;
        private const string M_WAIFU2X_UPSCALE_THRESHOLD_KEY = "waifu2x_upscale_threshold";
        //waifu2x的文件路径
        private static string m_waifu2x_path;
        private const string M_WAIFU2X_PATH_KEY = "waifu2x_path";
        //是否开启背景的切换效果
        private static bool m_enable_slide_animation;
        private const string M_ENABLE_SLIDE_ANIMATION_KEY = "enable_slide_animation";
        //壁纸的更换时间,s（会触发WallPaper Change Event）
        private static int m_wallpaper_change_time;
        private const int M_DEFAULT_WALLPAPER_CHANGE_TIME = 600;
        private const string M_WALLPAPER_CHANGE_TIME_KEY = "wallpaper_change_time";
        //是否将图片全部轮放，开启后在同一图片再次出现之前，所有图片都会出现一遍
        private static bool m_enable_illust_queue;
        private const string M_ENABLE_ILLUST_QUEUE_KEY = "enable_illust_queue";
        //图片轮放过的队列
        private static List<IllustKey> m_illust_queue;
        private const string M_ILLUST_QUEUE_KEY = "illust_queue";

        //触发背景切换的线程
        private static Thread m_background_thread;
        private static double m_constructor_execution_time;

        //触发壁纸更新的事件
        public static event EventHandler WallPaperChangeEvent;
        //更变数值触发的事件
        public static event EventHandler PathsChanged, EnableMultiMonitorDifferentWallpaperChanged, EnableBufferingChanged, EnableWaifu2xUpscalingChanged,
            Waifu2xPathChanged, EnableSlideAnimationChanged, WallpaperChangeTimeChanged, EnableIllustQueueChanged, IllustQueueChanged, Waifu2xUpscaleThresholdChanged;
        #region Properties
        /// <summary>
        /// 图片路径
        /// </summary>
        public static List<PathSetting> Paths { get { return m_paths; } set { m_paths = value; _verifySetting(); SaveSetting(); PathsChanged?.Invoke(null, new EventArgs()); } }
        /// <summary>
        /// 是否开启不同屏幕不同的背景图片（需要开启缓存渲染）
        /// </summary>
        public static bool EnableMultiMonitorDifferentWallpaper { get { return m_enable_multi_monitor_different_wallpaper; } set { m_enable_multi_monitor_different_wallpaper = value; _verifySetting(); SaveSetting(); EnableMultiMonitorDifferentWallpaperChanged?.Invoke(null, new EventArgs()); } }
        /// <summary>
        /// 是否开启缓存渲染
        /// </summary>
        public static bool EnableBuffering { get { return m_enable_buffering; } set { m_enable_buffering = value; _verifySetting(); SaveSetting(); EnableBufferingChanged?.Invoke(null, new EventArgs()); } }
        /// <summary>
        /// 是否开启waifu2x的图片放大（需要waifu2x的exe文件路径）
        /// </summary>
        public static bool EnableWaifu2xUpscaling { get { return m_enable_waifu2x_upscaling; } set { m_enable_waifu2x_upscaling = value; _verifySetting(); SaveSetting(); EnableWaifu2xUpscalingChanged?.Invoke(null, new EventArgs()); } }
        /// <summary>
        /// waifu2x的文件路径
        /// </summary>
        public static string Waifu2xPath { get { return m_waifu2x_path; } set { m_waifu2x_path = value; _verifySetting(); SaveSetting(); Waifu2xPathChanged?.Invoke(null, new EventArgs()); } }
        /// <summary>
        /// 是否开启背景的切换效果
        /// </summary>
        public static bool EnableSlideAnimation { get { return m_enable_slide_animation; } set { m_enable_slide_animation = value; _verifySetting(); SaveSetting(); EnableSlideAnimationChanged?.Invoke(null, new EventArgs()); } }
        /// <summary>
        /// 壁纸的更换时间,s（会触发WallPaper Change Event）
        /// </summary>
        public static int WallpaperChangeTime { get { return m_wallpaper_change_time; } set { m_wallpaper_change_time = value; _verifySetting(); SaveSetting(); WallpaperChangeTimeChanged?.Invoke(null, new EventArgs()); } }
        /// <summary>
        /// 是否将图片全部轮放，开启后在同一图片再次出现之前，所有图片都会出现一遍
        /// </summary>
        public static bool EnableIllustQueue { get { return m_enable_illust_queue; } set { m_enable_illust_queue = value; _verifySetting(); SaveSetting(); EnableIllustQueueChanged?.Invoke(null, new EventArgs()); } }
        /// <summary>
        /// 图片轮放过的队列
        /// </summary>
        public static List<IllustKey> IllustQueue { get { return m_illust_queue; } set { m_illust_queue = value; _verifySetting(); SaveSetting(); IllustQueueChanged?.Invoke(null, new EventArgs()); } }
        /// <summary>
        /// 下一次壁纸更新的时间戳
        /// </summary>
        public static double NextUpdateTimestamp { get { return m_constructor_execution_time; } set { m_constructor_execution_time = value; if ((m_background_thread.ThreadState & ThreadState.WaitSleepJoin) != 0) m_background_thread.Interrupt(); } }
        /// <summary>
        /// 执行waifu2x缩放时所需要达到的最小缩放比
        /// </summary>
        public static double Waifu2xUpscaleThreshold { get { return m_waifu2x_upscale_threshold; } set { m_waifu2x_upscale_threshold = value; _verifySetting(); SaveSetting(); Waifu2xUpscaleThresholdChanged?.Invoke(null, new EventArgs()); } }
        #endregion

        static Settings()
        {
            m_paths = new List<PathSetting>();
            m_illust_queue = new List<IllustKey>();
            m_wallpaper_change_time = M_DEFAULT_WALLPAPER_CHANGE_TIME;
            m_constructor_execution_time = util.ToUnixTimestamp(DateTime.Now);
            LoadSetting();
            m_background_thread = new Thread(_background_thread_cb);
            m_background_thread.SetApartmentState(ApartmentState.STA);
            m_background_thread.IsBackground = true;
            m_background_thread.Name = "背景线程";
            m_background_thread.Start();
        }
        private static void _background_thread_cb()
        {
            try
            {
                do
                {
                    m_constructor_execution_time += m_wallpaper_change_time;
                    var ts = util.FromUnixTimestamp(m_constructor_execution_time) - DateTime.Now;
                    if (ts.TotalMilliseconds > 0)
                    {
                        //捕获ThreadInterruptedException不抛出，并且跳过执行ChangeEvent
                        try
                        {
                            Thread.Sleep((int)ts.TotalMilliseconds);
                            WallPaperChangeEvent?.Invoke(null, new EventArgs());

                        }
                        catch (ThreadInterruptedException) { }
                        catch (Exception)
                        {
                            throw;
                        }
                    }
                    else
                    {
                        Tracer.GlobalTracer.TraceWarning("Next update time is less than now");
                        m_constructor_execution_time = util.ToUnixTimestamp(DateTime.Now) + m_wallpaper_change_time;
                    }
                } while (true);

            }
            catch (Exception ex)
            {
                Tracer.GlobalTracer.TraceError(ex.ToString());
            }
        }
        //验证设置的合理性
        private static void _verifySetting()
        {
            if (m_enable_multi_monitor_different_wallpaper)
                m_enable_buffering = true;
            if (string.IsNullOrEmpty(m_waifu2x_path) || !File.Exists(m_waifu2x_path))
                m_enable_waifu2x_upscaling = false;
            if (m_wallpaper_change_time <= 0)
                m_wallpaper_change_time = M_DEFAULT_WALLPAPER_CHANGE_TIME;
            if (m_waifu2x_upscale_threshold <= 0)
                m_waifu2x_upscale_threshold = M_DEFAULT_WAIFU2X_UPSCALE_THRESHOLD;
        }

        //加载设置
        public static void LoadSetting()
        {
            if (File.Exists(M_SETTING_NAME))
            {
                var str = File.ReadAllText(M_SETTING_NAME);
                try
                {
                    var json = JsonConvert.DeserializeObject(str) as JObject;
                    m_paths = json[M_PATHS_KEY].ToObject<List<PathSetting>>();
                    m_enable_multi_monitor_different_wallpaper = json.Value<bool>(M_ENABLE_MULTI_MONITOR_DIFFERENT_WALLPAPER_KEY);
                    m_enable_buffering = json.Value<bool>(M_ENABLE_BUFFERING_KEY);
                    m_enable_waifu2x_upscaling = json.Value<bool>(M_ENABLE_WAIFU2X_UPSCALING_KEY);
                    m_waifu2x_path = json.Value<string>(M_WAIFU2X_PATH_KEY);
                    m_enable_slide_animation = json.Value<bool>(M_ENABLE_SLIDE_ANIMATION_KEY);
                    m_wallpaper_change_time = json.Value<int>(M_WALLPAPER_CHANGE_TIME_KEY);
                    m_enable_illust_queue = json.Value<bool>(M_ENABLE_ILLUST_QUEUE_KEY);
                    m_illust_queue = json[M_ILLUST_QUEUE_KEY].ToObject<List<IllustKey>>();
                    m_waifu2x_upscale_threshold = json.Value<double>(M_WAIFU2X_UPSCALE_THRESHOLD_KEY);
                }
                catch (Exception)
                {
                    Tracer.GlobalTracer.TraceWarning("Exception raised while loading settings.");
                }
                finally
                {
                    _verifySetting();
                }
            }
        }

        //保存设置
        public static void SaveSetting()
        {
            try
            {
                var json = new JObject();
                json.Add(M_PATHS_KEY, JToken.FromObject(m_paths));
                json.Add(M_ENABLE_MULTI_MONITOR_DIFFERENT_WALLPAPER_KEY, m_enable_multi_monitor_different_wallpaper);
                json.Add(M_ENABLE_BUFFERING_KEY, m_enable_buffering);
                json.Add(M_ENABLE_WAIFU2X_UPSCALING_KEY, m_enable_waifu2x_upscaling);
                json.Add(M_WAIFU2X_PATH_KEY, m_waifu2x_path);
                json.Add(M_ENABLE_SLIDE_ANIMATION_KEY, m_enable_slide_animation);
                json.Add(M_WALLPAPER_CHANGE_TIME_KEY, m_wallpaper_change_time);
                json.Add(M_ENABLE_ILLUST_QUEUE_KEY, m_enable_illust_queue);
                json.Add(M_ILLUST_QUEUE_KEY, JToken.FromObject(m_illust_queue));
                json.Add(M_WAIFU2X_UPSCALE_THRESHOLD_KEY, m_waifu2x_upscale_threshold);

                var str = JsonConvert.SerializeObject(json);
                File.WriteAllText(M_SETTING_NAME, str);
            }
            catch (Exception)
            {
                Tracer.GlobalTracer.TraceWarning("Exception raised while saving settings.");
            }
        }

    }
}
