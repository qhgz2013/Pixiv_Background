using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Pixiv_Background_Form
{
    /// <summary>
    /// panel_item.xaml 的交互逻辑
    /// </summary>
    public partial class PanelItem : UserControl
    {
        public PanelItem(System.Drawing.Image show_image, string title = null, string desc = null, bool show_title = false, bool show_desc = false)
        {
            InitializeComponent();
            var ss = new MemoryStream();
            show_image.Save(ss, System.Drawing.Imaging.ImageFormat.Bmp);
            ss.Position = 0;

            iSourceImage.Cursor = Cursors.Hand;
            lMainTitle.Cursor = Cursors.Hand;
            lDescription.Cursor = Cursors.Hand;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ss;
            bmp.EndInit();
            WpfAnimatedGif.ImageBehavior.SetAnimatedSource(iSourceImage, bmp);

            var img_wh_ratio = 1.0 * show_image.Width / show_image.Height;
            Width = 200 * img_wh_ratio;

            if (!string.IsNullOrEmpty(title))
                lMainTitle.Content = title;
            if (!string.IsNullOrEmpty(desc))
                lDescription.Content = desc;

            int flag = (show_title ? 2 : 0) | (show_desc ? 1 : 0);
            switch (flag)
            {
                case 0:
                    Height = 200;
                    lMainTitle.Visibility = Visibility.Hidden;
                    lDescription.Visibility = Visibility.Hidden;
                    break;
                case 1:
                    Height = 225;
                    lMainTitle.Visibility = Visibility.Hidden;
                    mainLayout.RowDefinitions.RemoveAt(1);
                    break;
                case 2:
                    Height = 225;
                    lDescription.Visibility = Visibility.Hidden;
                    mainLayout.RowDefinitions.RemoveAt(2);
                    break;
                case 3:
                    Height = 250;
                    break;
                default:
                    break;
            }
        }

        private DateTime downTime;
        private object downSender;
        public event EventHandler<MouseEventArgs> SourceImageClick, TitleClick, DescriptionClick;
        private void iSourceImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                downSender = sender;
                downTime = DateTime.Now;
            }
        }

        private void iSourceImage_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Released && sender == downSender)
            {
                TimeSpan timeSinceDown = DateTime.Now - downTime;
                if (timeSinceDown.TotalMilliseconds < 500)
                {
                    SourceImageClick?.Invoke(sender, e);
                }
            }
        }

        private void lMainTitle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                downSender = sender;
                downTime = DateTime.Now;
            }
        }

        private void lMainTitle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Released && sender == downSender)
            {
                TimeSpan timeSinceDown = DateTime.Now - downTime;
                if (timeSinceDown.TotalMilliseconds < 500)
                {
                    TitleClick?.Invoke(sender, e);
                }
            }
        }

        private void lMainTitle_MouseEnter(object sender, MouseEventArgs e)
        {
            var brush = new SolidColorBrush(Colors.Black);
            var da = new ColorAnimation(Colors.Orange, TimeSpan.FromMilliseconds(300));
            lMainTitle.Foreground = brush;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, da);
        }

        private void lMainTitle_MouseLeave(object sender, MouseEventArgs e)
        {
            var brush = new SolidColorBrush(Colors.Orange);
            var da = new ColorAnimation(Colors.Black, TimeSpan.FromMilliseconds(300));
            lMainTitle.Foreground = brush;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, da);
        }

        private void lDescription_MouseEnter(object sender, MouseEventArgs e)
        {
            var brush = new SolidColorBrush(Colors.Gray);
            var da = new ColorAnimation(Colors.Orange, TimeSpan.FromMilliseconds(300));
            lDescription.Foreground = brush;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, da);
        }

        private void lDescription_MouseLeave(object sender, MouseEventArgs e)
        {
            var brush = new SolidColorBrush(Colors.Orange);
            var da = new ColorAnimation(Colors.Gray, TimeSpan.FromMilliseconds(300));
            lDescription.Foreground = brush;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, da);
        }

        private void lDescription_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                downSender = sender;
                downTime = DateTime.Now;
            }
        }

        private void lDescription_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Released && sender == downSender)
            {
                TimeSpan timeSinceDown = DateTime.Now - downTime;
                if (timeSinceDown.TotalMilliseconds < 500)
                {
                    DescriptionClick?.Invoke(sender, e);
                }
            }
        }
    }
}
