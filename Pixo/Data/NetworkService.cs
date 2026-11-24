using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;

namespace Pixo.Data
{
    /// <summary>Velocidade da rede (download/upload) e histórico para o gráfico.</summary>
    public static class NetworkService
    {
        private static Timer _timer;
        private static long _rx, _tx;
        private static DateTime _t;
        private static readonly object Lock = new object();
        private static readonly Queue<double> History = new Queue<double>();

        public static volatile float DownBps;
        public static volatile float UpBps;

        public static double[] Spark
        {
            get { lock (Lock) return History.ToArray(); }
        }

        public static void Start()
        {
            Read(out _rx, out _tx);
            _t = DateTime.UtcNow;
            _timer = new Timer(_ => Tick(), null, 1000, 1000);
        }

        public static void Stop() => _timer?.Dispose();

        private static void Read(out long rx, out long tx)
        {
            rx = 0; tx = 0;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    var s = ni.GetIPStatistics();
                    rx += s.BytesReceived;
                    tx += s.BytesSent;
                }
            }
            catch { }
        }

        private static void Tick()
        {
            Read(out var rx, out var tx);
            var now = DateTime.UtcNow;
            double dt = (now - _t).TotalSeconds;
            if (dt <= 0) return;
            DownBps = (float)Math.Max(0, (rx - _rx) / dt);
            UpBps = (float)Math.Max(0, (tx - _tx) / dt);
            _rx = rx; _tx = tx; _t = now;
            lock (Lock)
            {
                History.Enqueue(DownBps);
                while (History.Count > 16) History.Dequeue();
            }
        }
    }
}
