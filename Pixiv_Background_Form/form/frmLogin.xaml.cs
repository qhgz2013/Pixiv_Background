using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Pixiv_Background_Form
{
    public class CenterConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values[0] == DependencyProperty.UnsetValue || values[1] == DependencyProperty.UnsetValue)
            {
                return DependencyProperty.UnsetValue;
            }

            double width = (double)values[0];
            double height = (double)values[1];

            return new Thickness(-width / 2, -height / 2, 0, 0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    /// <summary>
    /// frmLogin.xaml 的交互逻辑
    /// </summary>
    public partial class frmLogin : Window
    {
        public frmLogin()
        {
            InitializeComponent();
        }
        public bool canceled;
        public string user_name;
        public string pass_word;

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            canceled = false;
            user_name = UserName.Text;
            pass_word = PassWord.Password;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            canceled = true;
            user_name = "";
            pass_word = "";
            Close();
        }

        private void UserName_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                PassWord.Focus();
                e.Handled = true;
            }
        }

        private void PassWord_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Confirm.Focus();
                
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) this.DragMove();
        }

        private void PassWord_PasswordChanged(object sender, RoutedEventArgs e)
        {
            pass_word = PassWord.Password;
        }
    }
}
