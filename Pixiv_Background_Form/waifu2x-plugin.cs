using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Pixiv_Background_Form
{
    public class Waifu2xPlugin
    {
        private string _path;
        private bool _exec_success;
        public Waifu2xPlugin(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException("path");
            if (!File.Exists(path))
                throw new IOException("File not found");

            _path = path;
            _exec_success = false;
        }

        private string _exec_arg(string arg)
        {
            var exec = new Process();
            exec.StartInfo = new ProcessStartInfo(_path);
            exec.StartInfo.Arguments = arg;

            exec.StartInfo.RedirectStandardError = true;
            exec.StartInfo.RedirectStandardOutput = true;

            exec.StartInfo.UseShellExecute = false;
            exec.StartInfo.CreateNoWindow = true;

            try
            {
                var parentdir = new DirectoryInfo(_path).Parent.FullName;
                exec.StartInfo.WorkingDirectory = parentdir;

                exec.Start();

                var cout = new MemoryStream();
                var sw_out = new StreamWriter(cout);
                var cerr = new MemoryStream();
                var sw_err = new StreamWriter(cerr);
                exec.OutputDataReceived += ((sender, e) =>
                {
                    sw_out.WriteLine(e.Data);
                });
                exec.ErrorDataReceived += ((sender, e) =>
                {
                    sw_err.WriteLine(e.Data);
                });
                exec.BeginOutputReadLine();
                exec.BeginErrorReadLine();
                exec.WaitForExit();
                
                exec.Close();
                sw_out.Flush();
                sw_err.Flush();
                cout.Seek(0, SeekOrigin.Begin);
                cerr.Seek(0, SeekOrigin.Begin);
                var sr_out = new StreamReader(cout);
                var sr_err = new StreamReader(cerr);

                var errstr = sr_err.ReadToEnd();
                var outstr = sr_out.ReadToEnd();

                cerr.Close();
                cout.Close();
                _exec_success = true;
                if (!string.IsNullOrEmpty(errstr) && errstr != "\r\n") return errstr;
                if (!string.IsNullOrEmpty(outstr)) return outstr;
            }
            catch (Exception)
            {
                _exec_success = false;
                return string.Empty;
            }
            _exec_success = false;
            return null;
        }

        /// <summary>
        /// 使用waifu2x进行图片分辨率倍增
        /// </summary>
        /// <param name="input_file">path to input image file (you should input full path)</param>
        /// <param name="output_file">path to output image file (you should input full path)</param>
        /// <param name="block_size">block size</param>
        /// <param name="disable_gpu">disable GPU</param>
        /// <param name="force_opencl">force to use OpenCL on Intel Platform</param>
        /// <param name="processor">set target processor</param>
        /// <param name="jobs">number of threads launching at the same time</param>
        /// <param name="model_dir">path to custom model directory (don't append last / )</param>
        /// <param name="scale_ratio">custom scale ratio</param>
        /// <param name="noise_level">noise reduce level</param>
        /// <param name="mode">image processing mode</param>
        public void UpscaleImage(
            string input_file,
            string output_file,
            //available params
            int? block_size = null,
            bool? disable_gpu = null,
            bool? force_opencl = null,
            int? processor = null,
            int? jobs = null,
            string model_dir = null,
            double? scale_ratio = null,
            int? noise_level = null,
            string mode = null
            )
        {
            var str_arg = new StringBuilder();
            if (block_size != null && block_size > 0)
                str_arg.AppendFormat("--block_size {0} ", (int)block_size);
            if (disable_gpu != null && (bool)disable_gpu)
                str_arg.Append("--disable-gpu ");
            if (force_opencl != null && (bool)force_opencl)
                str_arg.Append("--force-OpenCL ");
            if (processor != null && processor > 0)
                str_arg.AppendFormat("--processor {0} ", (int)processor);
            if (jobs != null && jobs > 0)
                str_arg.AppendFormat("--jobs {0} ", (int)jobs);
            if (!string.IsNullOrEmpty(model_dir))
                str_arg.AppendFormat("--model_dir {0} ", model_dir);
            if (scale_ratio != null && scale_ratio > 0)
                str_arg.AppendFormat("--scale_ratio {0} ", scale_ratio);
            if (noise_level != null && noise_level >= 1 && noise_level <= 2)
                str_arg.AppendFormat("--noise_level {0} ", noise_level);

            if (string.IsNullOrEmpty(mode))
            {
                bool enable_scale = (scale_ratio != null && scale_ratio > 0);
                bool enable_nr = (noise_level != null && noise_level >= 1 && noise_level <= 3);

                if (enable_scale && enable_nr)
                    str_arg.Append("-m noise_scale ");
                else if (enable_nr)
                    str_arg.Append("-m noise ");
                else if (enable_scale)
                    str_arg.Append("-m scale ");
            }
            else
            {
                str_arg.AppendFormat("-m {0} ", mode);
            }

            str_arg.AppendFormat("-i {0} -o {1}", input_file, output_file);

            var result = _exec_arg(str_arg.ToString());
        }

        public string GetVersion()
        {
            return _exec_arg("--version");
        }
        public string ListProcessor()
        {
            return _exec_arg("--list-processor");
        }

    }

}
