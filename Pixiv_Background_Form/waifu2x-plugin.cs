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
        public bool ExecutionSuccess { get { return _exec_success; } }
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
                    Tracer.GlobalTracer.TraceInfo("Waifu2x [I] " + e.Data);
                });
                exec.ErrorDataReceived += ((sender, e) =>
                {
                    sw_err.WriteLine(e.Data);
                    Tracer.GlobalTracer.TraceError("Waifu2x [E] " + e.Data);
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
        /// <param name="input_file">(required) path to input image file</param>
        /// <param name="output_file">path to output image file (when input_path is folder, output_path must be folder)</param>
        /// <param name="tta">8x slower and slightly high quality</param>
        /// <param name="gpu">gpu device no</param>
        /// <param name="batch_size">input batch size</param>
        /// <param name="crop_h">input image split size(height)</param>
        /// <param name="crop_w">input image split size(width)</param>
        /// <param name="crop_size">input image split size</param>
        /// <param name="output_depth">output image channel depth bit</param>
        /// <param name="output_quality">output image quality</param>
        /// <param name="process">process mode (cpu|gpu|cudnn)</param>
        /// <param name="model_dir">path to custom model directory (don't append last /)</param>
        /// <param name="scale_height">custom scale height</param>
        /// <param name="scale_width">custom scale width</param>
        /// <param name="scale_ratio">custom scale ratio</param>
        /// <param name="noise_level">noise reduction level (0|1|2|3)</param>
        /// <param name="mode">image processing mode (noise|scale|noise_scale|auto_scale)</param>
        /// <param name="output_extention">extention to output image file when output_path is (auto) or input_path is folder</param>
        /// <param name="input_extension_list">extention to input image file when input_path is folder</param>
        public void UpscaleImage(
            string input_file,
            string output_file,
            //available params
            int? tta = null,
            int? gpu = null,
            int? batch_size = null,
            int? crop_h = null,
            int? crop_w = null,
            int? crop_size = null,
            int? output_depth = null,
            int? output_quality = null,
            string process = null,
            string model_dir = null,
            double? scale_height = null,
            double? scale_width = null,
            double? scale_ratio = null,
            int? noise_level = null,
            string mode = null,
            string output_extention = null,
            string input_extension_list = null
            )
        {
            var str_arg = new StringBuilder();
            if (tta != null && tta > 0)
                str_arg.AppendFormat("--tta {0} ", (int)tta);
            if (gpu != null && gpu > 0)
                str_arg.AppendFormat("--gpu {0} ", (int)gpu);
            if (batch_size != null && batch_size > 0)
                str_arg.AppendFormat("--batch_size {0} ", (int)batch_size);
            if (crop_h != null && crop_h > 0)
                str_arg.AppendFormat("--crop_h {0} ", (int)crop_h);
            if (crop_w != null && crop_w > 0)
                str_arg.AppendFormat("--crop_w {0} ", (int)crop_w);
            if (crop_size != null && crop_size > 0)
                str_arg.AppendFormat("--crop_size {0} ", (int)crop_size);
            if (output_depth != null && output_depth > 0)
                str_arg.AppendFormat("--output_depth {0} ", (int)output_depth);
            if (output_quality != null && output_quality > 0)
                str_arg.AppendFormat("--output_quality {0}", (int)output_quality);
            if (!string.IsNullOrEmpty(process))
                str_arg.AppendFormat("--process {0} ", process);
            if (!string.IsNullOrEmpty(model_dir))
                str_arg.AppendFormat("--model_dir {0} ", model_dir);
            if (scale_height != null && scale_height > 0)
                str_arg.AppendFormat("--scale_height {0} ", (int)scale_height);
            if (scale_width != null && scale_width > 0)
                str_arg.AppendFormat("--scale_width {0} ", (int)scale_width);
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
            if (!string.IsNullOrEmpty(output_extention))
                str_arg.AppendFormat("--output_extention {0} ", output_extention);
            if (!string.IsNullOrEmpty(input_extension_list))
                str_arg.AppendFormat("--input_extention_list {0} ", input_extension_list);

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
