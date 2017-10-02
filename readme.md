# Pixiv Background
给自己当背景应用的小程序

## 说明
- 应用数据调用需要API登陆，而且据了解p站会自带反爬虫的封号机制，原理不明
> 账号封禁时，登陆会提示 `104:無効なユーザーアカウントです。` (无效的用户账号)
### 一切由于使用本软件而造成的账号损失，开发者一概不承担任何责任

## 最近国内的dns污染解决方法
在 `C:\Windows\System32\drivers\etc\hosts` 中加入如下代码<br/>
```
210.129.120.43 www.pixiv.net
210.140.131.145 source.pixiv.net
210.129.120.43 accounts.pixiv.net
210.129.120.43 app-api.pixiv.net
210.129.120.41 oauth.secure.pixiv.net
```
即可解决Err_Connection_Timed_Out的问题

## 使用
- 登录到pixiv（因为api调用需要oauth）
- 点击设置→背景路径设置→添加路径，选择p站图片的文件夹（也就是满满的类似 ` 123456789_p0.jpg ` 的文件夹）
- 按确定就OK了

### 插件内容
- 支持Waifu2x放缩图片 [lltcggie/waifu2x-caffe](https://github.com/lltcggie/waifu2x-caffe) [(Release Download)](https://github.com/lltcggie/waifu2x-caffe/releases)
> 使用方法<br>
> A. 在release下下载最新的Windows预编译版本，解压之后在设置的路径里选择waifu2x-converter-cpp.exe即可<br>
> B. 或者下载源代码重新编译生成可执行文件，再在设置里选择文件路径即可

<p>
最新更新的图片：<br>
多屏壁纸<br>
<img src="https://raw.githubusercontent.com/qhgz2013/Pixiv_Background/master/history_screenshot/20171002214628.png"/><br>
单屏壁纸<br>
<img src="https://raw.githubusercontent.com/qhgz2013/Pixiv_Background/master/history_screenshot/20171002214659.png"/><br>
小型窗体<br>
<img src="https://raw.githubusercontent.com/qhgz2013/Pixiv_Background/master/history_screenshot/20171002214926.png"/><br>
</p>