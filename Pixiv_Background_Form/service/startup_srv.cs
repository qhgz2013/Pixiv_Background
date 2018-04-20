using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;

namespace Pixiv_Background_Form
{
    partial class startup_srv : ServiceBase
    {
        public startup_srv()
        {
            InitializeComponent();
            App.Main();
        }

        protected override void OnStart(string[] args)
        {
            // TODO: 在此处添加代码以启动服务。
        }

        protected override void OnStop()
        {
            // TODO: 在此处添加代码以执行停止服务所需的关闭操作。
        }

        [STAThread]
        public static void Main()
        {
            var services = new ServiceBase[] { new startup_srv() };
            ServiceBase.Run(services);
        }
    }
}
