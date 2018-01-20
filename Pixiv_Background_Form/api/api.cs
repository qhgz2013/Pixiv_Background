using GlobalUtil;
using GlobalUtil.NetUtils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace Pixiv_Background_Form
{
    public class API
    {
        private PixivAuth _auth;
        public API(PixivAuth auth)
        {
            _auth = auth;
        }

        /// <summary>
        /// 从网页上获取投稿信息
        /// </summary>
        /// <param name="id">投稿的id</param>
        /// <param name="illust">[输出]投稿信息</param>
        public virtual void ParseIllustInfo(uint id, out Illust illust)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            //初始化
            illust = new Illust();
            illust.ID = id;
            illust.Origin = DataOrigin.Pixiv_App_API;
            illust.Last_Update = (ulong)util.ToUnixTimestamp(DateTime.Now);

            var ns = new NetStream();
            ns.TimeOut = 30000;
            ns.RetryTimes = 1;
            ns.ReadWriteTimeOut = 15000;

            //获取基本的信息
            try
            {
                var url = "https://app-api.pixiv.net/v1/illust/detail?illust_id=" + id;
                var header_param = new Parameters();
                if (_auth != null && !string.IsNullOrEmpty(_auth.AccessToken))
                {
                    header_param.Add("Authorization", "Bearer " + _auth.AccessToken);
                }

                ns.HttpGet(url, headerParam: header_param);
                if (ns.HTTP_Response == null)
                {
                    illust.HTTP_Status = -2;
                    return;
                }
                if (ns.HTTP_Response.StatusCode != HttpStatusCode.OK)
                {
                    illust.HTTP_Status = (int)ns.HTTP_Response.StatusCode;
                    return;
                }
                //解析开始~
                #region Parsing
                var str_json = ns.ReadResponseString();
                var json = JsonConvert.DeserializeObject(str_json) as JObject;
                json = json.Value<JObject>("illust");
                if (json == null)
                {
                    illust.HTTP_Status = (int)ns.HTTP_Response.StatusCode;
                }
                illust.Description = json.Value<string>("caption");
                var create_time = json.Value<DateTime>("create_date");
                illust.Submit_Time = (ulong)util.ToUnixTimestamp(create_time);
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
                    Tracer.GlobalTracer.TraceInfo("Pixiv server returned HTTP " + illust.HTTP_Status + " while accessing id #" + illust.ID);
                }
                throw;
            }
            catch (Exception ex)
            {
                illust.HTTP_Status = -2;
                Tracer.GlobalTracer.TraceError(ex);
                throw;
            }
            finally
            {
                ns.Close();
            }
        }
        public virtual void ParseUserInfo(uint id, out User user)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            user = new User();
            user.ID = id;
            user.Last_Update = (ulong)util.ToUnixTimestamp(DateTime.Now);

            var ns = new NetStream();
            ns.RetryTimes = 1;
            ns.ReadWriteTimeOut = 15000;
            ns.TimeOut = 30000;

            try
            {
                var url = "https://app-api.pixiv.net/v1/user/detail?user_id=" + id;
                var header_param = new Parameters();
                if (_auth != null && !string.IsNullOrEmpty(_auth.AccessToken))
                {
                    header_param.Add("Authorization", "Bearer " + _auth.AccessToken);
                }

                ns.HttpGet(url, headerParam: header_param);
                if (ns.HTTP_Response == null)
                {
                    user.HTTP_Status = -2;
                    return;
                }
                if (ns.HTTP_Response.StatusCode != HttpStatusCode.OK)
                {
                    user.HTTP_Status = (int)ns.HTTP_Response.StatusCode;
                    return;
                }

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
                    Tracer.GlobalTracer.TraceInfo("Pixiv server returned HTTP " + user.HTTP_Status + " while accessing uid #" + user.ID);
                }
            }
            catch (Exception ex)
            {
                user.HTTP_Status = -2;
                Tracer.GlobalTracer.TraceError(ex);
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
            ns.RetryTimes = 1;
            ns.ReadWriteTimeOut = 15000;
            ns.TimeOut = 30000;
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
                    nr = ns.ResponseStream.Read(buf, 0, 16384);
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
            catch (Exception)
            {
                http_status = -2;
            }
            return null; // new Bitmap(1, 1);
        }
    }
}
