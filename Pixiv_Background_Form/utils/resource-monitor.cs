using GlobalUtil;
using OpenHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace Pixiv_Background_Form
{
    public class ResourceMonitor
    {
        private static Thread _background_thd;

        public static EventHandler ResourceUpdated;
        private static void _on_thd_callback()
        {
            bool isadmin = false;
            try
            {
                isadmin = WinAPI.IsRunAsAdmin();
                _init_monitor();
            }
            catch (Exception)
            {
                return;
            }
            var update_time = DateTime.Now.AddSeconds(1);
            while (true)
            {
                var ts = (update_time - DateTime.Now);
                if (ts.TotalMilliseconds > 0)
                    Thread.Sleep((int)ts.TotalMilliseconds);
                update_time = update_time.AddSeconds(1);

                try
                {
                    CPU_Usage = _pc_cpu.NextValue() / 100;
                    DISK_Read = _pc_disk_r.NextValue();
                    DISK_Write = _pc_disk_w.NextValue();
                    float net_sent = 0, net_recv = 0;
                    foreach (var item in _pc_net_sent)
                        net_sent += item.NextValue();
                    foreach (var item in _pc_net_recv)
                        net_recv += item.NextValue();
                    NET_Sent = net_sent;
                    NET_Recv = net_recv;

                    var mem = new WinAPI.MemoryStatusEx();
                    if (WinAPI.GlobalMemoryStatusEx(mem) == false)
                        throw new InvalidOperationException("WINAPI GlobalMemoryStatusEx failed");
                    RAM_Usage = 1.0f - mem.ullAvailPhys * 1.0f / mem.ullTotalPhys;

                    if (isadmin)
                    {
                        var visitor = new UpdateVisitor();
                        var computer = new Computer();
                        computer.CPUEnabled = true;
                        computer.GPUEnabled = true;
                        computer.Open();
                        computer.Accept(visitor);

                        var temp_cpu_cores = new List<float>();

                        for (int i = 0; i < computer.Hardware.Length; i++)
                        {
                            for (int j = 0; j < computer.Hardware[i].Sensors.Length; j++)
                            {
                                if (computer.Hardware[i].Sensors[j].SensorType == SensorType.Temperature)
                                {
                                    if (computer.Hardware[i].HardwareType == HardwareType.CPU && computer.Hardware[i].Sensors[j].Name.Contains("CPU Core"))
                                    {
                                        if (computer.Hardware[i].Sensors[j].Value.HasValue)
                                            temp_cpu_cores.Add(computer.Hardware[i].Sensors[j].Value.Value);
                                    }
                                    else if ((computer.Hardware[i].HardwareType == HardwareType.GpuAti || computer.Hardware[i].HardwareType == HardwareType.GpuNvidia) && computer.Hardware[i].Sensors[j].Name.Contains("GPU Core"))
                                    {
                                        GPU_Temp = computer.Hardware[i].Sensors[j].Value.HasValue ?
                                            computer.Hardware[i].Sensors[j].Value.Value : float.NaN;
                                    }
                                }
                                else if (computer.Hardware[i].Sensors[j].SensorType == SensorType.Load)
                                {
                                    if ((computer.Hardware[i].HardwareType == HardwareType.GpuAti || computer.Hardware[i].HardwareType == HardwareType.GpuNvidia) && computer.Hardware[i].Sensors[j].Name.Contains("GPU Core"))
                                    {
                                        GPU_Usage = computer.Hardware[i].Sensors[j].Value.HasValue ?
                                            computer.Hardware[i].Sensors[j].Value.Value / 100 : float.NaN;
                                    }
                                }
                            }

                        }
                        computer.Close();

                        if (temp_cpu_cores.Count == 0)
                            CPU_Temp = float.NaN;
                        else
                        {
                            double sum = 0;
                            foreach (var item in temp_cpu_cores)
                                sum += item;
                            CPU_Temp = (float)(sum / temp_cpu_cores.Count);
                        }

                    } //endif (admin)

                    ResourceUpdated?.Invoke(null, new EventArgs());
                }
                catch (Exception ex)
                {
                    Tracer.GlobalTracer.TraceError(ex);
                    try
                    {
                        _init_monitor();
                    }
                    catch (Exception)
                    {
                        return;
                    }
                }
            }
        }

        //monitors
        private static PerformanceCounter _pc_cpu;
        private static PerformanceCounter _pc_disk_r;
        private static PerformanceCounter _pc_disk_w;
        private static PerformanceCounter[] _pc_net_sent;
        private static PerformanceCounter[] _pc_net_recv;
        private static void _init_monitor()
        {
            CPU_Usage = float.NaN;
            RAM_Usage = float.NaN;
            GPU_Usage = float.NaN;
            NET_Sent = float.NaN;
            NET_Recv = float.NaN;
            DISK_Read = float.NaN;
            DISK_Write = float.NaN;
            CPU_Temp = float.NaN;
            GPU_Temp = float.NaN;

            _pc_cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _pc_disk_r = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
            _pc_disk_w = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");

            var net_cat = new PerformanceCounterCategory("Network Interface");
            var net_ins = net_cat.GetInstanceNames();
            var net_pcs = new List<PerformanceCounter[]>(from x in net_ins select net_cat.GetCounters(x));
            var net_flatten = new List<PerformanceCounter>();
            var net_pc_sent = new List<PerformanceCounter>();
            var net_pc_recv = new List<PerformanceCounter>();
            foreach (var item in net_pcs)
            {
                var pc_sent = from x in item where x.CounterName == "Bytes Sent/sec" select x;
                var pc_recv = from x in item where x.CounterName == "Bytes Received/sec" select x;

                net_pc_sent.AddRange(pc_sent);
                net_pc_recv.AddRange(pc_recv);
            }
            _pc_net_sent = net_pc_sent.ToArray();
            _pc_net_recv = net_pc_recv.ToArray();

            _pc_cpu.NextValue();
            _pc_disk_r.NextValue();
            _pc_disk_w.NextValue();
            foreach (var item in _pc_net_sent)
                item.NextValue();
            foreach (var item in _pc_net_recv)
                item.NextValue();
        }
        static ResourceMonitor()
        {
            _background_thd = new Thread(_on_thd_callback);
            _background_thd.IsBackground = true;
            _background_thd.Name = "Resource Monitor Thread";
            _background_thd.Start();
        }
        //open hardware monitor wrapper

        public class UpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer)
            {
                computer.Traverse(this);
            }
            public void VisitHardware(IHardware hardware)
            {
                hardware.Update();
                foreach (IHardware subHardware in hardware.SubHardware)
                    subHardware.Accept(this);
            }
            public void VisitSensor(ISensor sensor) { }
            public void VisitParameter(IParameter parameter) { }
        }

        public static float CPU_Usage;
        public static float RAM_Usage;
        public static float GPU_Usage;
        public static float NET_Sent;
        public static float NET_Recv;
        public static float DISK_Read;
        public static float DISK_Write;
        public static float CPU_Temp;
        public static float GPU_Temp;
    }
}
