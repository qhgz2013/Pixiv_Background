using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pixiv_Background_Form.NetUtils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Pixiv_Background_Form
{
    public class PixivAuth
    {
        public delegate void NoArgEventHandler();
        //登陆成功
        public event NoArgEventHandler LoginSucceeded;
        public delegate void StrArgEventHandler(string arg);
        //登陆失败，arg:登陆失败后的html代码，Todo：解析html代码
        public event StrArgEventHandler LoginFailed;

        //client id
        private const string M_CLIENT_ID = "MOBrBDS8blbauoSck0ZfDbtuzpyT";
        //client secret
        private const string M_CLIENT_SECRET = "lsACyCD94FhDUtGTXi3QzcFE2uU1hqtDaKeqrdwj";
        private const string M_APP_USER_AGENT = "PixivAndroidApp/5.0.54 (Android 6.0.1; MI 5s)";


        //更新token的线程锁
        private ReaderWriterLock _tokenThreadLock = new ReaderWriterLock();
        private string _access_token;
        private string _device_token;
        private string _refresh_token;
        private DateTime _expire_time;
        private bool _token_loaded;
        private string _last_error_message;

        public string AccessToken { get { RefreshAccessToken(); return _access_token; } }
        public string DeviceToken { get { return _device_token; } }
        public string RefreshToken { get { return _refresh_token; } }
        public DateTime ExpireTime { get { return _expire_time; } }

        public bool IsLogined { get { return (!string.IsNullOrEmpty(_access_token)); } }
        public string LastErrorMessage { get { return _last_error_message; } }
        public PixivAuth(string path = "token.dat")
        {
            if (!string.IsNullOrEmpty(path))
                _token_loaded = LoadToken(path);
        }
        private bool LoadToken(string path = "token.dat")
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            if (_token_loaded) return true;
            if (!File.Exists(path)) return false;

            var stream = new StreamReader(path, Encoding.UTF8);
            var str_json = stream.ReadLine();
            stream.Close();

            var json = JsonConvert.DeserializeObject(str_json) as JObject;
            _access_token = json.Value<string>("access_token");
            _device_token = json.Value<string>("device_token");
            _refresh_token = json.Value<string>("refresh_token");
            _expire_time = util.FromUnixTimestamp(json.Value<double>("expire_time"));
            return true;
        }

        public bool SaveToken(string path = "token.dat")
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            if (string.IsNullOrEmpty(path)) return false;

            var json = new JObject();
            json.Add("access_token", _access_token);
            json.Add("device_token", _device_token);
            json.Add("refresh_token", _refresh_token);
            json.Add("expire_time", util.ToUnixTimestamp(_expire_time));

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
        /// <remarks>[no throw]</remarks>
        public void Login(string username, string password)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            string login_request_url = "https://oauth.secure.pixiv.net/auth/token";

            var ns = new NetStream();
            ns.RetryTimes = 1;
            ns.ReadWriteTimeOut = 15000;
            ns.TimeOut = 30000;
            try
            {
                Tracer.GlobalTracer.TraceInfo("Initializing login variables.");
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

                Tracer.GlobalTracer.TraceInfo("Posting login data");
                ns.HttpPost(login_request_url, post_param, headerParam: header_param);

                var response_str = ns.ReadResponseString();

                var response_json = JsonConvert.DeserializeObject(response_str) as JObject;

                ns.Close();

                if (response_json.Value<bool>("has_error"))
                {
                    _last_error_message = response_json["errors"]["system"].Value<string>("message");
                    Tracer.GlobalTracer.TraceError("Login failed: " + _last_error_message);
                    LoginFailed?.Invoke(_last_error_message);
                }
                else
                {
                    _last_error_message = string.Empty;
                    _access_token = response_json["response"].Value<string>("access_token");
                    _device_token = response_json["response"].Value<string>("device_token");
                    _refresh_token = response_json["response"].Value<string>("refresh_token");
                    _expire_time = DateTime.Now.AddSeconds(response_json["response"].Value<int>("expires_in"));

                    SaveToken();
                    Tracer.GlobalTracer.TraceInfo("Login succeeded.");
                    LoginSucceeded?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Tracer.GlobalTracer.TraceError("Login failed: \n" + ex.ToString());
                LoginFailed?.Invoke(ex.ToString());
            }
        }
        public void RefreshAccessToken()
        {
            //Tracer.GlobalTracer.TraceFunctionEntry();
            try
            {
                _tokenThreadLock.AcquireWriterLock(Timeout.Infinite);
                if (DateTime.Now < _expire_time) return;
                if (string.IsNullOrEmpty(_refresh_token) || string.IsNullOrEmpty(_device_token)) return;

                string login_request_url = "https://oauth.secure.pixiv.net/auth/token";
                Tracer.GlobalTracer.TraceFunctionEntry();
                var ns = new NetStream();
                ns.RetryTimes = 1;
                ns.ReadWriteTimeOut = 15000;
                ns.TimeOut = 30000;

                Tracer.GlobalTracer.TraceInfo("Initializing login variables.");
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

                Tracer.GlobalTracer.TraceInfo("Posting login data");
                ns.HttpPost(login_request_url, post_param, headerParam: header_param);

                if (ns.ResponseStream == null)
                {
                    ns.Close();
                    return;
                }
                var response_str = ns.ReadResponseString();

                var response_json = JsonConvert.DeserializeObject(response_str) as JObject;

                ns.Close();
                _access_token = response_json["response"].Value<string>("access_token");
                _device_token = response_json["response"].Value<string>("device_token");
                _refresh_token = response_json["response"].Value<string>("refresh_token");
                _expire_time = DateTime.Now.AddSeconds(response_json["response"].Value<int>("expires_in"));

                SaveToken();
                Tracer.GlobalTracer.TraceInfo("Refresh token succeeded.");
            }
            catch (Exception ex)
            {
                Tracer.GlobalTracer.TraceInfo("Refresh token failed: \n" + ex.ToString());
            }
            finally
            {
                _tokenThreadLock.ReleaseWriterLock();
            }
        }
    }
}
