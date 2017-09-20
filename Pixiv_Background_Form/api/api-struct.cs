using System;
using System.Drawing;

/*
* 目前数据库的表格变量定义 [v1.0.7]
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

namespace Pixiv_Background_Form
{
    /// <summary>
    /// 画师信息
    /// </summary>
    [Serializable]
    public struct User
    {
        /// <summary>
        /// 画师ID
        /// </summary>
        public uint ID;
        /// <summary>
        /// 画师名称
        /// </summary>
        public string Name;
        /// <summary>
        /// 画师描述（html代码）
        /// </summary>
        public string Description;
        /// <summary>
        /// 画师头像
        /// </summary>
        public Image User_Face;
        /// <summary>
        /// 画师头像的url
        /// </summary>
        public string User_Face_Url;
        /// <summary>
        /// 主页
        /// </summary>
        public string Home_Page;
        /// <summary>
        /// 性别
        /// </summary>
        public string Gender;
        /// <summary>
        /// 个人标签
        /// </summary>
        public string Personal_Tag;
        /// <summary>
        /// 地址
        /// </summary>
        public string Address;
        /// <summary>
        /// 生日
        /// </summary>
        public string Birthday;
        /// <summary>
        /// 职业
        /// </summary>
        public string Job;
        /// <summary>
        /// 关注其他画师的人数
        /// </summary>
        public int Follow_Users;
        /// <summary>
        /// 被他人关注的人数
        /// </summary>
        public int Follower;
        /// <summary>
        /// 公开的收藏数
        /// </summary>
        public int Illust_Bookmark_Public;
        /// <summary>
        /// 好p友数
        /// </summary>
        public int Mypixiv_Users;
        /// <summary>
        /// 总插画投稿数
        /// </summary>
        public int Total_Illusts;
        /// <summary>
        /// 总漫画投稿数
        /// </summary>
        public int Total_Novels;
        /// <summary>
        /// Twitter
        /// </summary>
        public string Twitter;
        /// <summary>
        /// http状态
        /// </summary>
        public int HTTP_Status;
        /// <summary>
        /// 最后更新的时间
        /// </summary>
        public ulong Last_Update;
        /// <summary>
        /// 最后成功更新的时间
        /// </summary>
        public ulong Last_Success_Update;

        public override string ToString()
        {
            return Name + " (ID:" + ID + ")";
        }
    }
    /// <summary>
    /// 投稿信息
    /// </summary>
    [Serializable]
    public struct Illust
    {
        /// <summary>
        /// 作品ID
        /// </summary>
        public uint ID;
        /// <summary>
        /// 画师ID
        /// </summary>
        public uint Author_ID;
        /// <summary>
        /// 投稿分p
        /// </summary>
        public uint Page;
        /// <summary>
        /// 投稿标题
        /// </summary>
        public string Title;
        /// <summary>
        /// 投稿的描述
        /// </summary>
        public string Description;
        /// <summary>
        /// 投稿标签
        /// </summary>
        public string Tag;
        /// <summary>
        /// 工具
        /// </summary>
        public string Tool;
        /// <summary>
        /// 点击数
        /// </summary>
        public int Click;
        /// <summary>
        /// 收藏数
        /// </summary>
        public int Bookmark_Count;
        /// <summary>
        /// 评论数
        /// </summary>
        public int Comment_Count;
        /// <summary>
        /// 作品像素
        /// </summary>
        public Size Size;
        /// <summary>
        /// 评分次数
        /// </summary>
        public int Rate_Count;
        /// <summary>
        /// 总分
        /// </summary>
        public int Score;
        /// <summary>
        /// 投稿时间
        /// </summary>
        public ulong Submit_Time;
        /// <summary>
        /// 获取投稿信息时的http状态码
        /// </summary>
        public int HTTP_Status;
        /// <summary>
        /// 最后更新投稿信息的时间
        /// </summary>
        public ulong Last_Update;
        /// <summary>
        /// 最后成功更新的时间
        /// </summary>
        public ulong Last_Success_Update;
        /// <summary>
        /// 数据来源
        /// </summary>
        public DataOrigin Origin;

        public override string ToString()
        {
            return Title + " (" + ID + "p" + Page + ")";
        }
    }

    /// <summary>
    /// 数据源
    /// </summary>
    public enum DataOrigin
    {
        /// <summary>
        /// 网页解析
        /// </summary>
        Pixiv_Html,
        /// <summary>
        /// SauceNao API
        /// </summary>
        SauceNao_API,
        /// <summary>
        /// 安卓端API
        /// </summary>
        Pixiv_App_API
    }

    /// <summary>
    /// 数据更新模式
    /// </summary>
    public enum DataUpdateMode
    {
        //flg : 0 0(Force mode) 0(Sync mode) 0(Async mode)

        /// <summary>
        /// 不更新
        /// </summary>
        No_Update,
        /// <summary>
        /// 异步更新（事件回调）
        /// </summary>
        Async_Update,
        /// <summary>
        /// 同步更新（也会有事件回调）
        /// </summary>
        Sync_Update,
        /// <summary>
        /// 强制更新（单独使用无效果）
        /// </summary>
        Force_Update = 4,
        /// <summary>
        /// 强制异步更新（事件回调）
        /// </summary>
        Force_Async_Update,
        /// <summary>
        /// 强制同步更新（也会有事件回调）
        /// </summary>
        Force_Sync_Update
    }

}