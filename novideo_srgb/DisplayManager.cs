using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace novideo_srgb
{
    using System;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;

    public static class DisplayManager
    {
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VK_CONTROL = 0x11;
        private const byte VK_SHIFT = 0x10;
        private const byte VK_LWIN = 0x5B;
        private const byte VK_B = 0x42;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private static DateTime _lastRequest = DateTime.MinValue;
        private static readonly object _lock = new object();
        private static Task _pendingTask;

        public static bool UsedFlag = false;

        public static void RequestDisplayRefresh()
        {
            UsedFlag = true;
            lock (_lock)
            {
                if (_pendingTask != null && !_pendingTask.IsCompleted)
                {
                    Console.WriteLine("Refresh already scheduled, skipping duplicate.");
                    return;
                }

                _lastRequest = DateTime.Now;

                _pendingTask = Task.Run(async () =>
                {
                    Console.WriteLine("Refresh scheduled, waiting 1 second...");
                    await Task.Delay(500);

                    DisplayRefresh();
                });
            }
        }

        public static bool IsRefreshPending()
        {
            lock (_lock)
            {
                return _pendingTask != null && !_pendingTask.IsCompleted;
            }
        }

        private static void DisplayRefresh()
        {
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
            keybd_event(VK_B, 0, 0, UIntPtr.Zero);

            keybd_event(VK_B, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            Console.WriteLine("Display refresh triggered.");
        }
    }



}
