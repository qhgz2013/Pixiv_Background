using GlobalUtil;
using GlobalUtil.NetUtils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Pixiv_Background_Form
{
    public class ApiNoAuth : API
    {
        private const string _MOBILE_UA = "Mozilla/5.0 (Linux; Android 4.0.4; Galaxy Nexus Build/IMM76B) AppleWebKit/535.19 (KHTML, like Gecko) Chrome/18.0.1025.133 Mobile Safari/535.19";

        public ApiNoAuth() : base(null)
        {
        }

        public override void ParseIllustInfo(uint id, out Illust illust)
        {
            illust = new Illust();
            illust.ID = id;
            illust.HTTP_Status = -1;
            illust.Last_Update = (ulong)util.ToUnixTimestamp(DateTime.Now);
            illust.Origin = DataOrigin.Pixiv_Html;

            var ns = new NetStream();
            ns.RetryTimes = 3;
            ns.UserAgent = _MOBILE_UA;
            var url = "https://www.pixiv.net/member_illust.php?mode=medium&illust_id=" + id;

            if (id == 0) throw new ArgumentOutOfRangeException("id");
            try
            {
                ns.HttpGet(url);

                var response_html = ns.ReadResponseString();
                if (ns.HTTP_Response == null)
                {
                    illust.HTTP_Status = -2;
                    return;
                }
                var response_code = (int)ns.HTTP_Response.StatusCode;
                illust.HTTP_Status = response_code;

                illust.Title = "";
                illust.Description = "";
                illust.Tag = "";
                illust.Tool = "";

                if (response_code == 200)
                {
                    #region html parsing for illust
                    //author id
                    var re_match = Regex.Match(response_html, "<a\\s+class=\"profile\\sauthor\"\\s*href=\"/member\\.php\\?id=(?<id>\\d+)\">");
                    if (re_match.Success)
                        illust.Author_ID = uint.Parse(re_match.Result("${id}"));
                    else
                    { illust.HTTP_Status = 404; return; }

                    //page count (maybe failed)
                    re_match = Regex.Match(response_html, "<div\\s+class=\"page-count\">(?<page>\\d+)</div><img\\s+src=");
                    if (re_match.Success)
                        illust.Page = uint.Parse(re_match.Result("${page}"));
                    else
                        illust.Page = 1;

                    //"like" count
                    re_match = Regex.Match(response_html, "<span\\s+class=\"rating-count\\s+likes\">(?<like>\\d+)</span>");
                    if (re_match.Success)
                        illust.Rate_Count = int.Parse(re_match.Result("${like}"));
                    else
                        Tracer.GlobalTracer.TraceWarning("Failed to parse Like from HTML code, UPDATE THE MATCHING RULES!");

                    //bookmark count
                    re_match = Regex.Match(response_html, "<span\\s+class=\"bookmark-count\">(?<bookmark>\\d+)</span>");
                    if (re_match.Success)
                        illust.Bookmark_Count = int.Parse(re_match.Result("${bookmark}"));
                    else
                        Tracer.GlobalTracer.TraceWarning("Failed to parse Bookmark from HTML code, UPDATE THE MATCHING RULES!");

                    //comment count
                    re_match = Regex.Match(response_html, "<span\\s+class=\"comment-count\">(?<comment>\\d+)</span>");
                    if (re_match.Success)
                        illust.Comment_Count = int.Parse(re_match.Result("${comment}"));
                    else
                        Tracer.GlobalTracer.TraceWarning("Failed to parse Comment from HTML code, UPDATE THE MATCHING RULES!");

                    //submit time
                    re_match = Regex.Match(response_html, "<div\\s+class=\"works-status\">(?<y>\\d+)年(?<m>\\d+)月(?<d>\\d+)日\\s(?<h>\\d+):(?<m2>\\d+)");
                    if (re_match.Success)
                        illust.Submit_Time = (ulong)util.ToUnixTimestamp(new DateTime(int.Parse(re_match.Result("${y}")), int.Parse(re_match.Result("${m}")), int.Parse(re_match.Result("${d}")), int.Parse(re_match.Result("${h}")), int.Parse(re_match.Result("${m2}")), 0, DateTimeKind.Local));
                    else
                        Tracer.GlobalTracer.TraceWarning("Failed to parse Submit Time from HTML code, UPDATE THE MATCHING RULES!");

                    //clicks
                    re_match = Regex.Match(response_html, "<span\\sclass=\"activity-views\">.*?<strong>(?<click>\\d+)</strong></span>");
                    if (re_match.Success)
                        illust.Click = int.Parse(re_match.Result("${click}"));
                    else
                        Tracer.GlobalTracer.TraceWarning("Failed to parse Click from HTML code, UPDATE THE MATCHING RULES!");

                    //title
                    re_match = Regex.Match(response_html, "<span\\sclass=\"title\">(?<title>.*?)</span>");
                    if (re_match.Success)
                        illust.Title = System.Net.WebUtility.HtmlDecode(re_match.Result("${title}"));
                    else
                        Tracer.GlobalTracer.TraceWarning("Failed to parse Title from HTML code, UPDATE THE MATCHING RULES!");

                    //tags
                    re_match = Regex.Match(response_html, "<div\\sclass=\"works-info-tags\">(?<data>.*?)</p>");
                    illust.Tag = "";
                    if (re_match.Success)
                    {
                        var result = re_match.Result("${data}");
                        var tag_match = Regex.Match(result, "<a[^>]*>(\\*&nbsp;)?(?<tag>.*?)</a>");
                        while (tag_match.Success)
                        {
                            illust.Tag += tag_match.Result("${tag}") + ",";
                            tag_match = tag_match.NextMatch();
                        }

                        if (illust.Tag.Length > 0)
                            illust.Tag = illust.Tag.Substring(0, illust.Tag.Length - 1);
                    }
                    else
                        Tracer.GlobalTracer.TraceWarning("Failed to parse Tag from HTML code, UPDATE THE MATCHING RULES!");

                    //description
                    re_match = Regex.Match(response_html, "<p\\sclass=\"caption\">(?<description>.*?)</p>");
                    if (re_match.Success)
                        illust.Description = re_match.Result("${description}");

                    //size
                    //User Agent Logging status    Result
                    //Mobile     Not Logged in     Failed (no info)
                    //PC         Not Logged in     Failed (no info)
                    //Mobile     Logged in         Failed (no info)
                    //PC         Logged in         Success (this id contains only 1 illust)
                    illust.Size = new System.Drawing.Size(0, 0);

                    //tools
                    //Same result as the size above
                    illust.Tool = "";

                    illust.Last_Success_Update = illust.Last_Update;
                    #endregion
                }
            }
            catch (Exception ex)
            {
                Tracer.GlobalTracer.TraceError(ex);
                illust.HTTP_Status = -2;
            }
            finally
            {
                ns.Close();
            }
        }

        public override void ParseUserInfo(uint id, out User user)
        {
            user = new User();
            user.ID = id;
            user.HTTP_Status = -1;
            user.Last_Update = (ulong)util.ToUnixTimestamp(DateTime.Now);

            var ns = new NetStream();
            ns.RetryTimes = 3;
            ns.UserAgent = _MOBILE_UA;
            var url = "https://www.pixiv.net/member.php?id=" + id;

            if (id == 0) throw new ArgumentOutOfRangeException("id");
            try
            {
                ns.HttpGet(url);

                var response_html = ns.ReadResponseString();
                if (ns.HTTP_Response == null)
                {
                    user.HTTP_Status = -2;
                    return;
                }
                var response_code = (int)ns.HTTP_Response.StatusCode;
                user.HTTP_Status = response_code;

                user.Address = "";
                user.Birthday = "";
                user.Description = "";
                user.Gender = "";
                user.Home_Page = "";
                user.Job = "";
                user.Name = "";
                user.Personal_Tag = "";
                user.Twitter = "";
                user.User_Face_Url = "";

                if (response_code == 200)
                {
                    //user name
                    var re_match = Regex.Match(response_html, "<p\\sclass=\"profile-user-name\"><a[^>]*>(?<name>.*?)</a></p>");
                    if (re_match.Success)
                        user.Name = re_match.Result("${name}");
                    else
                    { user.HTTP_Status = 404; return; }

                    //user image url
                    re_match = Regex.Match(response_html, "<a[^>]*class=\"profile-row-item\\sprofile-imgbox\"\\s*style=\"background-image:\\s*url\\((?<url>.*?)\\);\"");
                    if (re_match.Success)
                        user.User_Face_Url = re_match.Result("${url}");
                    else
                        Tracer.GlobalTracer.TraceWarning("Failed to parse User Face URL from HTML code, UPDATE THE MATCHING RULES!");

                    //user image
                    if (!string.IsNullOrEmpty(user.User_Face_Url))
                    {
                        var ns2 = new NetStream();
                        ns2.UserAgent = _MOBILE_UA;
                        ns2.RetryTimes = 3;
                        var header_param = new Parameters();
                        header_param.Add("Referer", url);
                        try
                        {
                            ns2.HttpGet(user.User_Face_Url, header_param);
                            var data = ns2.ReadResponseBinary();
                            var ms = new MemoryStream(data);
                            user.User_Face = System.Drawing.Image.FromStream(ms);
                        }
                        catch (Exception ex)
                        {
                            Tracer.GlobalTracer.TraceError(ex);
                        }
                        finally
                        {
                            ns2.Close();
                        }
                    }

                    //description
                    re_match = Regex.Match(response_html, "<p\\sid=\"profile-comment\">(?<description>.*?)</p>");
                    if (re_match.Success)
                        user.Description = re_match.Result("${description}");
                    else
                        Tracer.GlobalTracer.TraceWarning("Failed to parse Description from HTML code, UPDATE THE MATCHING RULES!");

                    //additional data:
                    re_match = Regex.Match(response_html, "<table\\sclass=\"collapsed-hidden\">(?<data>.*?)</table>", RegexOptions.Singleline);
                    if (re_match.Success)
                    {
                        var kv_match = Regex.Match(re_match.Result("${data}"), "<tr[^>]*><td>(<div>|<span>)?(?<key>.*?)(</div>|</span>)?</td><td>(<div>|<span>)?(?<value>.*?)(</div>|</span>)?</td></tr>");

                        while (kv_match.Success)
                        {
                            switch (kv_match.Result("${key}").ToLower())
                            {
                                case "昵称":
                                    //name, already parsed
                                    break;
                                case "性别":
                                    //gender
                                    user.Gender = kv_match.Result("${value}");
                                    break;
                                case "地址":
                                    //address
                                    user.Address = kv_match.Result("${value}");
                                    break;
                                case "年龄":
                                    //age, no corresponding storage value
                                    break;
                                case "生日":
                                    //birthday
                                    user.Birthday = kv_match.Result("${value}");
                                    break;
                                case "职业":
                                    //job
                                    user.Job = kv_match.Result("${value}");
                                    break;
                                case "twitter":
                                    //twitter
                                    var temp_match = Regex.Match(kv_match.Result("${value}"), "<a[^>]*>(?<twitter>.*?)</a>");
                                    if (temp_match.Success)
                                        user.Twitter = temp_match.Result("${twitter}");
                                    else
                                        user.Twitter = kv_match.Result("${value}");
                                    break;
                                case "google talk":
                                case "facebook":
                                case "tumblr":
                                    break;

                                case "主页":
                                    //home page
                                    temp_match = Regex.Match(kv_match.Result("${value}"), "<a[^>]*>(?<homepage>.*?)</a>");
                                    if (temp_match.Success)
                                        user.Home_Page = temp_match.Result("${homepage}");
                                    else
                                        user.Home_Page = kv_match.Result("${value}");
                                    break;
                                default:
                                    //missing: personal tags (PC page only)
                                    Tracer.GlobalTracer.TraceInfo("Unidentified user data key: " + kv_match.Result("${key}"));
                                    break;
                            }

                            kv_match = kv_match.NextMatch();
                        }
                    }
                    else
                        Tracer.GlobalTracer.TraceWarning("Failed to parse Additional User Data from HTML code, UPDATE THE MATCHING RULES!");

                    //total illusts
                    //HINT: R-18 illusts will not be listed without logging in
                    re_match = Regex.Match(response_html, "<span>作品<br\\s*/></span><span><a[^>]*>(?<count>\\d+)</a>");
                    if (re_match.Success)
                        user.Total_Illusts = int.Parse(re_match.Result("${count}"));
                    else
                        Tracer.GlobalTracer.TraceWarning("Failed to parse Total Illusts from HTML code, UPDATE THE MATCHING RUELS!");

                    //total novels
                    re_match = Regex.Match(response_html, "<section\\sid=\"member-novels\"[^>]*>.*?<a[^>]*>(?<count>\\d+)");
                    if (re_match.Success)
                        user.Total_Novels = int.Parse(re_match.Result("${count}"));

                    //total public bookmarks
                    re_match = Regex.Match(response_html, "<section\\sid=\"member-illust-bookmarks\"[^>]*>.*?<a[^>]*>(?<count>\\d+)");
                    if (re_match.Success)
                        user.Illust_Bookmark_Public = int.Parse(re_match.Result("${count}"));

                    //followers: not found
                    user.Follower = 0;

                    //follow users
                    re_match = Regex.Match(response_html, "<span>关注<br\\s*/></span><span>(?<count>\\d+)</span>");
                    if (re_match.Success)
                        user.Follow_Users = int.Parse(re_match.Result("${count}"));
                    else
                        Tracer.GlobalTracer.TraceWarning("Failed to parse Follow User from HTML code, UPDATE THE MATCHING RUELS!");

                    //mypixiv users
                    re_match = Regex.Match(response_html, "<span>好P友<br\\s*/></span><span>(?<count>\\d+)</span>");
                    if (re_match.Success)
                        user.Mypixiv_Users = int.Parse(re_match.Result("${count}"));
                    else
                        Tracer.GlobalTracer.TraceWarning("Failed to parse Mypixiv User from HTML code, UPDATE THE MATCHING RULES!");


                    user.Last_Success_Update = user.Last_Update;
                }
            }
            catch (Exception ex)
            {
                Tracer.GlobalTracer.TraceError(ex);
                user.HTTP_Status = -2;
            }
            finally
            {
                ns.Close();
            }
        }
    }
}
