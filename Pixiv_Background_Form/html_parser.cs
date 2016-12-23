using System;
using System.Collections.Generic;
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
using System.Net;
using System.Text.RegularExpressions;
using System.Collections;

//todo : 超链接不换行

namespace Pixiv_Background_Form
{
    public class html_parser
    {
        public html_parser() { }
        public static UserControl parseHTML(string html)
        {
            var uc = new UserControl();
            uc.Name = "user_control";
            var main_layout = new Grid();
            main_layout.Name = "main_layout";

            int index = 0;
            var htmltree = parse_html_tree(html, ref index);
            System.Diagnostics.Debug.Print("parsing html string:\n" + html);
            System.Diagnostics.Debug.Print("Deserialized string:\n" + htmltree.ToString());
            var html_visual = (TextBlock)convert_object(htmltree);
            html_visual.Foreground = new SolidColorBrush(Color.FromRgb(0x5b, 0x5b, 0x5b));
            main_layout.Children.Add(html_visual);

            uc.Content = main_layout;
            return uc;
        }
        private struct HtmlTreeNode
        {
            public string Name;
            public HtmlTreeAttributeCollection Attribute;
            public List<object> Document;
            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.Append("<");
                sb.Append(Name);
                if (Attribute != null)
                {
                    sb.Append(" ");
                    sb.Append(Attribute.ToString());
                }
                sb.Append(">");

                foreach (var item in Document)
                {
                    sb.Append(item.ToString());
                }
                sb.Append("</");
                sb.Append(Name);
                sb.Append(">");
                return sb.ToString();
            }
        }
        private class HtmlTreeAttributeCollection : IDictionary<string, string>
        {
            private Dictionary<string, string> _inner_dict;
            public HtmlTreeAttributeCollection() { _inner_dict = new Dictionary<string, string>(); }

            public string this[string key]
            {
                get
                {
                    return _inner_dict[key];
                }

                set
                {
                    _inner_dict[key] = value;
                }
            }

            public int Count
            {
                get
                {
                    return _inner_dict.Count;
                }
            }

            public bool IsReadOnly
            {
                get
                {
                    return false;
                }
            }

            public ICollection<string> Keys
            {
                get
                {
                    return _inner_dict.Keys;
                }
            }

            public ICollection<string> Values
            {
                get
                {
                    return _inner_dict.Values;
                }
            }

            public void Add(KeyValuePair<string, string> item)
            {
                _inner_dict.Add(item.Key, item.Value);
            }

            public void Add(string key, string value)
            {
                _inner_dict.Add(key, value);
            }

            public void Clear()
            {
                _inner_dict.Clear();
            }

            public bool Contains(KeyValuePair<string, string> item)
            {
                return _inner_dict.Contains(item);
            }

            public bool ContainsKey(string key)
            {
                return _inner_dict.ContainsKey(key);
            }

            public void CopyTo(KeyValuePair<string, string>[] array, int arrayIndex)
            {
                var arr = _inner_dict.ToArray();
                arr.CopyTo(array, arrayIndex);
            }

            public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
            {
                return _inner_dict.GetEnumerator();
            }

            public bool Remove(KeyValuePair<string, string> item)
            {
                return _inner_dict.Remove(item.Key);
            }

            public bool Remove(string key)
            {
                return _inner_dict.Remove(key);
            }

            public override string ToString()
            {
                var sb = new StringBuilder();
                if (_inner_dict != null)
                {
                    foreach (var item in _inner_dict)
                    {
                        sb.Append(item.Key);
                        sb.Append("=\"");
                        sb.Append(item.Value);
                        sb.Append("\" ");
                    }
                    if (sb.Length > 0) sb.Remove(sb.Length - 1, 1);
                }
                return sb.ToString();
            }

            public bool TryGetValue(string key, out string value)
            {
                return _inner_dict.TryGetValue(key, out value);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return _inner_dict.GetEnumerator();
            }
        }
        //parsing html [throwable]
        private static HtmlTreeNode parse_html_tree(string html, ref int index)
        {
            var htn = new HtmlTreeNode();
            htn.Name = "";
            htn.Document = new List<object>();

            bool skip_content = false;
            //name
            if (index < html.Length && html[index] == '<')
            {

                index++;
                skip_char(html, ref index);
                var name = parse_html_property(html, ref index);
                skip_char(html, ref index);
                //attr
                var attr = new HtmlTreeAttributeCollection();

                while (html[index] != '>')
                {
                    skip_char(html, ref index);
                    if (html[index] == '/')
                    {
                        skip_content = true;
                        index++;
                        skip_char(html, ref index);
                        break;
                    }

                    string attr_key = parse_html_property(html, ref index);
                    if (string.IsNullOrEmpty(attr_key)) throw new ArgumentNullException("HTML Attribute key null: Index = " + index);

                    skip_char(html, ref index);
                    char assign_char = html[index];
                    if (assign_char != '=') throw new ArgumentException("Invalid Assignment Character: Index = " + index);
                    index++;

                    skip_char(html, ref index);

                    char quote_type1 = html[index];
                    index++;
                    skip_char(html, ref index);
                    string attr_value = parse_html_attr_value(html, ref index);
                    skip_char(html, ref index);
                    char quote_type2 = html[index];
                    index++;
                    if ((quote_type1 == '\"' && quote_type2 != '\"') || (quote_type1 != '\'' && quote_type2 == '\'')) throw new ArgumentException("Different quote type: Index = " + index);

                    attr.Add(attr_key, attr_value);
                    skip_char(html, ref index);
                }

                index++;
                htn.Name = name;
                htn.Attribute = attr;
            }
            //inner text
            if (!skip_content)
                htn.Document = parse_html_innerText(html, ref index, htn.Name);
            else
                htn.Document = new List<object>();

            return htn;
        }
        private static string parse_html_property(string html, ref int index)
        {
            string ret = "";
            while (index < html.Length &&  !" !@#$%^&*()-=+[{]}\\|;:'\",<.>/?".Contains(html[index])) //escape special char
            {
                ret += html[index++];
            }
            return ret;
        }
        private static string parse_html_attr_value(string html, ref int index)
        {
            string ret = "";
            while (index < html.Length && !"\"'".Contains(html[index])) //escape special char
            {
                ret += html[index++];
            }
            return ret;
        }
        private static void skip_char(string html, ref int index)
        {
            while (index < html.Length && html[index] == ' ') index++;
        }
        private static List<object> parse_html_innerText(string html, ref int index, string keyName = "")
        {
            var ret = new List<object>();

            var str = "";
            while (index < html.Length)
            {
                if (html[index] == '<')
                {
                    if (!string.IsNullOrEmpty(str)) { ret.Add(WebUtility.HtmlDecode(str)); str = ""; }
                    skip_char(html, ref index);
                    if (html[index + 1] == '/')
                    {
                        index+=2;
                        skip_char(html, ref index);
                        var proper_name = parse_html_property(html, ref index);
                        if (!string.IsNullOrEmpty(keyName) && keyName != proper_name) throw new ArgumentException("Unexpected Node End Sign: " + proper_name + " (should be " + keyName + ") : Index = " + index);
                        //skipping ">"
                        index++;
                        return ret;
                    }
                    var node = parse_html_tree(html, ref index);
                    ret.Add(node);
                }
                else
                {
                    str += html[index];
                    index++;
                }
            }
            if (!string.IsNullOrEmpty(str)) ret.Add(WebUtility.HtmlDecode(str));

            return ret;
        }
        
        //creating visual tree
        private static object convert_object(object html_object)
        {
            if (html_object.GetType() == typeof(string))
            {
                return convert_string((string)html_object);
            }
            else if (html_object.GetType() == typeof(HtmlTreeNode))
            {
                var type_cast = (HtmlTreeNode)html_object;
                switch (type_cast.Name)
                {
                    case "strong":
                        return convert_strong(type_cast);
                    case "a":
                        return convert_a(type_cast);
                    case "span":
                        return convert_span(type_cast);
                    case "br":
                        return convert_br();
                    case "i":
                        return convert_i(type_cast);
                    case "":
                        return convert_document(type_cast.Document);
                    default:
                        break;
                }
            }
            return null;
        }
        private static TextBlock convert_span(HtmlTreeNode node)
        {
            var tb = new TextBlock();
            tb.MaxWidth = 550;
            tb.TextWrapping = TextWrapping.Wrap;
            tb.Name = "span_object";

            tb.Inlines.Add(convert_document(node.Document));
            var style_str = node.Attribute["style"];
            var color_str = Regex.Match(style_str, @"color:#(\w{6});").Result("$1");
            var hex = hex_str(color_str);
            var color = Color.FromRgb(hex[0], hex[1], hex[2]);

            tb.Foreground = new SolidColorBrush(color);
            return tb;
        }
        private static byte[] hex_str(string hexString)
        {
            string newString = hexString;
            if (newString.Length % 2 != 0)
                newString += "0";
            int byteLength = newString.Length / 2;
            byte[] bytes = new byte[byteLength];
            for (int i = 0; i < byteLength; i ++)
            {
                bytes[i] = Convert.ToByte(newString.Substring(i * 2, 2), 16);
            }
            return bytes;       
        }
        private static TextBlock convert_strong(HtmlTreeNode node)
        {
            var tb = new TextBlock();
            tb.MaxWidth = 550;
            tb.TextWrapping = TextWrapping.Wrap;
            tb.Name = "bold_object";
            tb.Inlines.Add(convert_document(node.Document));
            set_object_bold(tb);
            return tb;
        }
        private static TextBlock convert_i(HtmlTreeNode node)
        {
            var tb = new TextBlock();
            tb.MaxWidth = 550;
            tb.TextWrapping = TextWrapping.Wrap;
            tb.Name = "italic_object";
            tb.Inlines.Add(convert_document(node.Document));
            set_object_italic(tb);
            return tb;
        }
        private static TextBlock convert_string(string html_str)
        {
            var tb = new TextBlock();
            tb.MaxWidth = 550;
            tb.TextWrapping = TextWrapping.Wrap;
            tb.Name = "string_object";
            tb.Inlines.Add(html_str);
            return tb;
        }
        private static LineBreak convert_br()
        {
            return new LineBreak();
        }
        private static TextBlock convert_a(HtmlTreeNode node)
        {
            var tb = new TextBlock();
            tb.MaxWidth = 550;
            tb.TextWrapping = TextWrapping.Wrap;
            tb.Name = "hyperlink_object";

            var hl = new Hyperlink();
            var child = convert_document(node.Document);
            hl.RequestNavigate += Hl_RequestNavigate;
            hl.MouseEnter += Hl_MouseEnter;
            hl.MouseLeave += Hl_MouseLeave;
            hl.TextDecorations = null;
            hl.Inlines.Add(child);
            tb.Inlines.Add(hl);
            hl.Foreground = new SolidColorBrush(Color.FromRgb(0, 0x78, 0xd7));

            var request_url = node.Attribute["href"];
            var match = Regex.Match(request_url, @"/jump\.php\?(?<actual_url>.*)");
            if (match.Success) request_url = Uri.UnescapeDataString(match.Result("${actual_url}"));
            else
            {
                match = Regex.Match(request_url, @"http(s?)://.+");
                if (!match.Success)
                    request_url = "http://www.pixiv.net" + request_url;
            }

            var uri = new Uri(request_url);
            hl.NavigateUri = uri;
            System.Diagnostics.Debug.Print("Modifying URI: " + node.Attribute["href"] + " --> " + request_url);

            return tb;
        }

        private static void Hl_MouseLeave(object sender, MouseEventArgs e)
        {
            var sb = new Storyboard();
            var ca = new ColorAnimation(Color.FromRgb(0, 0x78, 0xd7), new Duration(new TimeSpan(0, 0, 0, 0, 300)));
            sb.Children.Add(ca);
            Storyboard.SetTargetProperty(ca, new PropertyPath("Foreground.(SolidColorBrush.Color)"));
            ((Hyperlink)sender).BeginStoryboard(sb);
        }

        private static void Hl_MouseEnter(object sender, MouseEventArgs e)
        {
            var sb = new Storyboard();
            var ca = new ColorAnimation(Colors.Orange, new Duration(new TimeSpan(0, 0, 0, 0, 300)));
            sb.Children.Add(ca);
            Storyboard.SetTargetProperty(ca, new PropertyPath("Foreground.(SolidColorBrush.Color)"));
            ((Hyperlink)sender).BeginStoryboard(sb);
        }
        
        private static void set_object_bold(object uiObject)
        {
            InlineCollection inlines = null;
            if (uiObject.GetType() == typeof(TextBlock))
            {
                var type_cast = (TextBlock)uiObject;
                inlines = type_cast.Inlines;
                type_cast.FontWeight = FontWeights.Bold;
            }
            else if (uiObject.GetType() == typeof(Hyperlink))
            {
                var type_cast = (Hyperlink)uiObject;
                inlines = type_cast.Inlines;
                type_cast.FontWeight = FontWeights.Bold;
            }

            if (inlines != null)
                for (int i = 0; i < inlines.Count; i++)
                {
                    var e = inlines.ElementAt(i);
                    if (e is Hyperlink)
                    {
                        set_object_bold((Hyperlink)e);
                    }
                    else if (e is InlineUIContainer)
                    {
                        set_object_bold(((InlineUIContainer)e).Child);
                    }
                    else //if(e is Run)
                    {
                        e.FontWeight = FontWeights.Bold;
                    }
                }
        }
        private static void set_object_italic(object uiObject)
        {
            InlineCollection inlines = null;
            if (uiObject.GetType() == typeof(TextBlock))
            {
                var type_cast = (TextBlock)uiObject;
                inlines = type_cast.Inlines;
                type_cast.FontStyle = FontStyles.Italic;
            }
            else if (uiObject.GetType() == typeof(Hyperlink))
            {
                var type_cast = (Hyperlink)uiObject;
                inlines = type_cast.Inlines;
                type_cast.FontStyle = FontStyles.Italic;
            }

            if (inlines != null)
                for (int i = 0; i < inlines.Count; i++)
                {
                    var e = inlines.ElementAt(i);
                    if (e is Hyperlink)
                    {
                        set_object_italic((Hyperlink)e);
                    }
                    else if (e is InlineUIContainer)
                    {
                        set_object_italic(((InlineUIContainer)e).Child);
                    }
                    else //if(e is Run)
                    {
                        e.FontStyle = FontStyles.Italic;
                    }
                }
        }
        private static TextBlock convert_document(List<object> document)
        {
            var tb = new TextBlock();
            tb.MaxWidth = 550;
            tb.TextWrapping = TextWrapping.Wrap;
            //tb.Foreground = new SolidColorBrush(Color.FromRgb(0x5b, 0x5b, 0x5b));
            tb.Name = "object_collection_object";
            foreach (var item in document)
            {
                var obj = convert_object(item);
                if (obj != null)
                {
                    if (obj.GetType() == typeof(LineBreak))
                    {
                        tb.Inlines.Add((LineBreak)obj);
                    }
                    else if (obj is UIElement)
                    {
                        tb.Inlines.Add((UIElement)obj);
                    }
                }
            }
            return tb;
        }
        private static void Hl_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(e.Uri.AbsoluteUri);
        }
    }
}
