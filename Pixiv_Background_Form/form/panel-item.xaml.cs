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
        public PanelItem(System.Drawing.Image show_image, string title = null, string desc = null, bool show_title = false, bool show_desc = false, int default_height = 200)
        {
            InitializeComponent();

            var ss = new MemoryStream();
            try
            {
                var cvt = new System.Drawing.ImageConverter();
                var data = (byte[])cvt.ConvertTo(show_image, typeof(byte[]));
                ss.Write(data, 0, data.Length);
            }
            catch
            {
                show_image.Save(ss, System.Drawing.Imaging.ImageFormat.Bmp);
            }
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
            Width = default_height * img_wh_ratio;

            if (!string.IsNullOrEmpty(title))
            {
                var tb = new TextBlock();
                tb.TextWrapping = TextWrapping.NoWrap;
                tb.Inlines.Add(title);
                lMainTitle.Content = tb;
                lMainTitle.ToolTip = title;
            }
            if (!string.IsNullOrEmpty(desc))
            {
                var tb = new TextBlock();
                tb.TextWrapping = TextWrapping.NoWrap;
                tb.Inlines.Add(desc);
                lDescription.Content = tb;
                lDescription.ToolTip = desc;
            }

            int flag = (show_title ? 2 : 0) | (show_desc ? 1 : 0);
            switch (flag)
            {
                case 0:
                    Height = default_height;
                    lMainTitle.Visibility = Visibility.Hidden;
                    lDescription.Visibility = Visibility.Hidden;
                    break;
                case 1:
                    Height = default_height + 25;
                    lMainTitle.Visibility = Visibility.Hidden;
                    mainLayout.RowDefinitions.RemoveAt(1);
                    break;
                case 2:
                    Height = default_height + 25;
                    lDescription.Visibility = Visibility.Hidden;
                    mainLayout.RowDefinitions.RemoveAt(2);
                    break;
                case 3:
                    Height = default_height + 50;
                    break;
                default:
                    break;
            }

            mainLayout.RowDefinitions[0].Height = new GridLength(default_height);
            Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Arrange(new Rect(new Point(0, 0), DesiredSize));
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

        private void frm_Loaded(object sender, RoutedEventArgs e)
        {
            //todo: move effect when width is not enough
            if (lMainTitle.Content != null && ((TextBlock)lMainTitle.Content).ActualWidth + lMainTitle.Padding.Left + lMainTitle.Padding.Right >= frm.ActualWidth)
            {
                lMainTitle.HorizontalAlignment = HorizontalAlignment.Left;

                const double pixel_per_second = 15;
                double dx = ((TextBlock)lMainTitle.Content).ActualWidth - frm.ActualWidth;
                dx += lMainTitle.Padding.Left + lMainTitle.Padding.Right;
                var time = dx / pixel_per_second;

                var ani = new ThicknessAnimationUsingKeyFrames();
                ani.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(0, 0, 0, 0), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0))));
                ani.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(0, 0, 0, 0), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2))));
                ani.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(-dx, 0, 0, 0), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2 + time))));
                ani.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(-dx, 0, 0, 0), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4 + time))));
                var sb = new Storyboard();
                Timeline.SetDesiredFrameRate(sb, 15);
                Storyboard.SetTarget(ani, lMainTitle);
                Storyboard.SetTargetProperty(ani, new PropertyPath(MarginProperty));
                sb.Children.Add(ani);
                sb.RepeatBehavior = RepeatBehavior.Forever;
                sb.Begin();
            }
            if (lDescription.Content != null && ((TextBlock)lDescription.Content).ActualWidth + lDescription.Padding.Left + lDescription.Padding.Right >= frm.ActualWidth)
            {
                lDescription.HorizontalAlignment = HorizontalAlignment.Left;

                const double pixel_per_second = 15;
                double dx = ((TextBlock)lDescription.Content).ActualWidth - frm.ActualWidth;
                dx += lDescription.Padding.Left + lDescription.Padding.Right;
                var time = dx / pixel_per_second;

                var ani = new ThicknessAnimationUsingKeyFrames();
                ani.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(0, 0, 0, 0), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0))));
                ani.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(0, 0, 0, 0), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2))));
                ani.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(-dx, 0, 0, 0), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2 + time))));
                ani.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(-dx, 0, 0, 0), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4 + time))));
                var sb = new Storyboard();
                Timeline.SetDesiredFrameRate(sb, 15);
                Storyboard.SetTarget(ani, lDescription);
                Storyboard.SetTargetProperty(ani, new PropertyPath(MarginProperty));
                sb.Children.Add(ani);
                sb.RepeatBehavior = RepeatBehavior.Forever;
                sb.Begin();
            }
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
