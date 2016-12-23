using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VBUtil.Utils;
using VBUtil.Utils.NetUtils;
using System.IO;
using System.Net;
using System.Drawing;

namespace Pixiv_Background
{
    public class saucenaoAPI
    {
        public const string request_url = "http://saucenao.com/search.php";

        //this is my private api key, limit: 20 requests per 30 seconds, 300 requests per day
        public const string api_key = "d800c4204b9b13d9779a2b05fd38d6e67f6d8cf1";
        private const string boundryCharList = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private static string generate_random_boundary()
        {
            Random r = new Random();
            string ret = "";
            int length = r.Next(15, 20);
            for (int i = 0; i < length; i++)
            {
                ret += boundryCharList[r.Next(boundryCharList.Length)];
            }
            return ret;
        }

        private static byte[] get_str_byte(string str)
        {
            return System.Text.Encoding.UTF8.GetBytes(str);
        }
        private static void post_formdata_img(Stream ostream, Image iimg, string boundry)
        {
            byte[] buf;
            buf = get_str_byte("---" + boundry + "\r\n");
            ostream.Write(buf, 0, buf.Length);
            buf = get_str_byte("Content-Disposition: form-data; name=\"file\"; filename=\"tmpFile\"\r\n");
            ostream.Write(buf, 0, buf.Length);
            string mine_type = "";
            if (iimg.RawFormat == System.Drawing.Imaging.ImageFormat.Png) mine_type = "png";
            else if (iimg.RawFormat == System.Drawing.Imaging.ImageFormat.Jpeg) mine_type = "jpg";

            buf = get_str_byte("Content-Type: image/" + mine_type + "\r\n");
        }
    }
}
