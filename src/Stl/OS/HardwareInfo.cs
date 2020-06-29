using System;
using System.Diagnostics;
using Stl.Mathematics;

namespace Stl.OS
{
    public class HardwareInfo
    {
        private const int RefreshIntervalTicks = 30_000; // Tick = millisecond
        private static readonly object Lock = new object();
        private static volatile int _processorCount; 
        private static volatile int _processorCountPo2;
        private static volatile int _lastRefreshTicks =
            // Environment.TickCount is negative in WebAssembly @ startup
            Environment.TickCount - (RefreshIntervalTicks << 1);

        public static int ProcessorCount {
            get {
                MaybeRefresh();
                return _processorCount;
            }
        }

        public static int ProcessorCountPo2 {
            get {
                MaybeRefresh();
                return _processorCountPo2;
            }
        }

        private static void MaybeRefresh()
        {
            var now = Environment.TickCount;
            if (now - _lastRefreshTicks < RefreshIntervalTicks)
                return;
            lock (Lock) {
                if (now - _lastRefreshTicks < RefreshIntervalTicks)
                    return;
                _processorCount = Math.Max(1, Environment.ProcessorCount);
                _processorCountPo2 = Math.Max(1, (int) Bits.GreaterOrEqualPowerOf2((uint) _processorCount));
                // This should be done at last, otherwise there is a chance
                // another thread sees _processorCount == 0
                _lastRefreshTicks = now;
            }
        }
    }
}
