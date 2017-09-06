using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Drawing;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Diagnostics;
using Pixiv_Background_Form.NetUtils;

namespace Pixiv_Background_Form
{
    public class saucenaoAPI
    {
        #region Constants
        private const string request_url = "http://saucenao.com/search.php";
        
        private const string boundryCharList = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        //图片缩小设置
        private const int max_image_width = 1000;
        private const int max_image_height = 1000;
        //api设置
        private const int output_type = 2; //json
        private const string DEFAULT_API_KEY_CHECK = "d800c4204b9b13d9779a2b05fd38d6e67f6d8cf1";
        private const string api_key = DEFAULT_API_KEY_CHECK; //this is my private api key, limit: 20 requests per 30 seconds, 300 requests per day
        private const int testmode = 1;
        private const int db = (int)DataBase_Name.Pixiv_Images; //pixiv only
        private const int numres = 5; //max response number
        #endregion //Constants


        #region Enums
        //database name (last update: 17/1/15)
        private enum DataBase_Name
        {
            H_Magazines = 0,
            H_Game_CG = 2,
            DoujinshiDB = 3,
            Pixiv_Images = 5,
            Nico_Nico_Seiga = 8,
            Danbooru = 9,
            drawr_Images = 10,
            Nijie_Images = 11,
            Yande_re = 12,
            Openings_moe = 13,
            Shutterstock = 15,
            FAKKU = 16,
            H_Misc = 18,
            _2D_Market = 19,
            MediBang = 20,
            Anime = 21,
            H_Anime = 22,
            Movies = 23,
            Shows = 24,
            Gelbooru = 25,
            Konachan = 26,
            Sankaku_Complex = 27,
            Anime_Pictures_net = 28,
            e621_net = 29,
            TBA = 999
        }
        #endregion //Enums


        #region Private Static Functions
        //string转byte[]
        private static byte[] get_str_byte(string str)
        {
            return System.Text.Encoding.UTF8.GetBytes(str);
        }
        //将图像数据按表单格式写入ostream里
        private static void post_formdata_img(Stream ostream,string name, Image iimg, string boundary)
        {
            byte[] buf;
            buf = get_str_byte("--" + boundary + "\r\n");
            ostream.Write(buf, 0, buf.Length);
            buf = get_str_byte("Content-Disposition: form-data; name=\"" + name + "\"; filename=\"tmpFile\"\r\n");
            ostream.Write(buf, 0, buf.Length);

            iimg = downscaling_image(iimg);

            buf = get_str_byte("Content-Type: image/jpeg\r\n\r\n");
            ostream.Write(buf, 0, buf.Length);

            var ms = new MemoryStream();
            iimg.Save(ms, iimg.RawFormat);
            ms.Seek(0, SeekOrigin.Begin);
            buf = new byte[ms.Length];
            ms.Read(buf, 0, buf.Length);
            ostream.Write(buf, 0, buf.Length);

            buf = get_str_byte("\r\n");
            ostream.Write(buf, 0, buf.Length);
        }
        //将字符串数据按表单格式写入ostream里
        private static void post_formdata_string(Stream ostream, string name, string data, string boundary)
        {
            byte[] buf;
            buf = get_str_byte("--" + boundary + "\r\n");
            ostream.Write(buf, 0, buf.Length);
            buf = get_str_byte("Content-Disposition: form-data; name=\"" + name + "\"\r\n\r\n");
            ostream.Write(buf, 0, buf.Length);
            buf = get_str_byte(data + "\r\n");
            ostream.Write(buf, 0, buf.Length);
        }
        //写入表单的结束标识
        private static void post_formdata_end(Stream ostream, string boundary)
        {
            byte[] buf;
            buf = get_str_byte("--" + boundary + "--");
            ostream.Write(buf, 0, buf.Length);
        }
        //缩小图片，避免图片过大
        private static Image downscaling_image(Image origin)
        {
            double width_multiplier = (double)origin.Width / max_image_width;
            double height_multiplier = (double)origin.Height / max_image_height;

            double multiplier = width_multiplier > height_multiplier ? width_multiplier : height_multiplier;

            
            int new_width = multiplier > 1 ? (int)(origin.Width / multiplier) : origin.Width;
            int new_height = multiplier > 1 ? (int)(origin.Height / multiplier) : origin.Height;

            Image img = new Bitmap(new_width, new_height);

            var gr = Graphics.FromImage(img);
            gr.DrawImage(origin, 0, 0, new_width, new_height);
            gr.Dispose();

            //regenerate a image from jpg format
            var ms = new MemoryStream();
            img.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
            //img.Dispose();

            ms.Seek(0, SeekOrigin.Begin);
            return Image.FromStream(ms);
        }
        //HTTP Post函数
        private static JObject post_formdata(Image img)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            var ns = new NetStream();
            ns.RetryTimes = 0;

            var ms = new MemoryStream();
            var boundary = util.GenerateFormDataBoundary();


            post_formdata_img(ms, "file", img, boundary);
            post_formdata_end(ms, boundary);
            ms.Seek(0, SeekOrigin.Begin);
            byte[] buf = new byte[ms.Length];
            ms.Read(buf, 0, buf.Length);

            var url_param = new Parameters();
            url_param.Add("api_key", api_key);
            url_param.Add("output_type", output_type);
            url_param.Add("testmode", testmode);
            url_param.Add("db", db);
            url_param.Add("numres", numres);

            try
            {
                var connection_stream = ns.HttpPost(request_url, ms.Length, contentType: "multipart/form-data; boundary=" + boundary, urlParam: url_param);
                connection_stream.Write(buf, 0, buf.Length);
                connection_stream.Close();
                ns.HttpPostClose();

                var response_string = ns.ReadResponseString();
                ns.Close();

                return (JObject)JsonConvert.DeserializeObject(response_string);
            }
            catch (Exception)
            {
                return null;
            }
        }
        //返回JSON解析函数
        private static float parse_json_object(JObject obj, out Illust illust, out User user)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            illust = new Illust();
            user = new User();
            illust.Origin = DataOrigin.SauceNao_API;
            illust.Last_Update = (ulong)util.ToUnixTimestamp(DateTime.Now);
            user.Last_Update = illust.Last_Update;

            try
            {
                //example returning
                //{ "header":{ "user_id":"11792","account_type":"1","short_limit":"20","long_limit":"300","long_remaining":297,"short_remaining":19,"status":0,"results_requested":5,"index":{ "5":{ "status":0,"parent_id":5,"id":5,"results":1},"51":{ "status":0,"parent_id":5,"id":51,"results":1},"52":{ "status":0,"parent_id":5,"id":52,"results":1},"6":{ "status":0,"parent_id":6,"id":6,"results":1} },"search_depth":"250","minimum_similarity":55,"query_image_display":"userdata\/ZqYEX1XuF.jpg.png","query_image":"ZqYEX1XuF.jpg","results_returned":4},"results":[{"header":{"similarity":"95.25","thumbnail":"http:\/\/img1.saucenao.com\/res\/pixiv\/1132\/11325767_s.jpg?auth=bd2WMmPKJyby0J-v8XzMHg\u0026exp=1484475495","index_id":5,"index_name":"Index #5: Pixiv Images - 11325767_s.jpg"},"data":{"title":"\u66fc\u73e0\u6c99\u83ef","pixiv_id":11325767,"member_name":"\u305c\u308d\u304d\u3061","member_id":225421}},{"header":{"similarity":"40.84","thumbnail":"http:\/\/img1.saucenao.com\/res\/pixiv_historical\/743\/7434254_s.jpg?auth=fOagzzgWm5DedEb5dfZFxQ\u0026exp=1484475495","index_id":6,"index_name":"Index #6: Pixiv Historical - 7434254_s.jpg"},"data":{"title":"\u79cb","pixiv_id":7434254,"member_name":"\u9b3c\u706f","member_id":938194}},{"header":{"similarity":"40.00","thumbnail":"http:\/\/img1.saucenao.com\/res\/pixiv\/4799\/manga\/47990533_p3.jpg?auth=28pVyGmYN4seFSU_wQXdEg\u0026exp=1484475495","index_id":5,"index_name":"Index #5: Pixiv Images - 47990533_p3.jpg"},"data":{"title":"\u5b9f\u6cc1\u8005LOG","pixiv_id":47990533,"member_name":"K8","member_id":5857954}}]}

                var result_status = obj["header"].Value<int>("status");
                var result_count = obj["header"].Value<int>("results_returned");
                var short_remaining = obj["header"].Value<int>("short_remaining");
                var long_remaining = obj["header"].Value<int>("long_remaining");

                var result_list = obj.Value<JArray>("results");

                float max_similarity = 0;
                JObject max_similarity_obj = null;
                foreach (JObject item in result_list)
                {
                    var similarity = item["header"].Value<float>("similarity");
                    if (similarity > max_similarity)
                    {
                        max_similarity = similarity;
                        max_similarity_obj = item;
                    }
                }

                if (max_similarity_obj != null)
                {
                    var user_id = max_similarity_obj["data"].Value<uint>("member_id");
                    var user_name = max_similarity_obj["data"].Value<string>("member_name");
                    var illust_id = max_similarity_obj["data"].Value<uint>("pixiv_id");
                    var illust_title = max_similarity_obj["data"].Value<string>("title");

                    illust.Author_ID = user_id;
                    illust.ID = illust_id;
                    illust.Title = illust_title;
                    illust.HTTP_Status = (int)HttpStatusCode.OK;
                    illust.Last_Success_Update = illust.Last_Update;
                    
                    user.ID = user_id;
                    user.Name = user_name;
                    user.HTTP_Status = (int)HttpStatusCode.OK;
                    user.Last_Success_Update = user.Last_Update;

                    return max_similarity / 100; //转为 [0, 1]范围
                }
                else
                {
                    illust.HTTP_Status = -2;
                    user.HTTP_Status = -2;
                }
            }
            catch (WebException ex)
            {
                if (ex.Response != null)
                {
                    var http_response = (HttpWebResponse)ex.Response;
                    illust.HTTP_Status = (int)http_response.StatusCode;
                    user.HTTP_Status = illust.HTTP_Status;
                }
                else
                {
                    illust.HTTP_Status = -2;
                    user.HTTP_Status = -2;
                }
            }
            catch (Exception)
            {
                illust.HTTP_Status = -2;
                user.HTTP_Status = -2;
            }
            return 0f;
        }
        #endregion //Private Static Functions


        #region Public Static Functions
        /// <summary>
        /// 在SauceNao查询指定的图片
        /// </summary>
        /// <param name="img">图片</param>
        /// <param name="illust">[输出]投稿信息（仅包含ID和名称）</param>
        /// <param name="user">[输出]用户信息（仅包含ID和名称）</param>
        /// <returns>相似程度（最大值1）</returns>
        public static float QueryImage(Image img, out Illust illust, out User user)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            var json = post_formdata(img);
            return parse_json_object(json, out illust, out user);
        }
        /// <summary>
        /// 在SauceNao查询指定的图片
        /// </summary>
        /// <param name="path">图片的路径</param>
        /// <param name="illust">[输出]投稿信息（仅包含ID和名称）</param>
        /// <param name="user">[输出]用户信息（仅包含ID和名称）</param>
        /// <returns>相似程度（最大值1）</returns>
        /// <returns></returns>
        public static float QueryImage(string path, out Illust illust, out User user)
        {
            Tracer.GlobalTracer.TraceFunctionEntry();
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException("Path");
            if (!File.Exists(path)) throw new IOException("File not exists");
            var img = Image.FromFile(path);
            var ret = QueryImage(img, out illust, out user);
            img.Dispose();
            return ret;
        }
        #endregion //Public Static Functions


        #region Disabling Constructor
        protected saucenaoAPI() { }
        #endregion //Disabling Constructor
    }
}
