using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace novideo_srgb
{

    public static class DisplayManager
    {
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VK_CONTROL = 0x11;
        private const byte VK_SHIFT = 0x10;
        private const byte VK_LWIN = 0x5B;
        private const byte VK_B = 0x42;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private static readonly object _lock = new object();

        public static bool RefreshEndFlag = true;
        private static bool FirstRunFlag = true;
        private static bool RefreshRequestedFlag = false;
        private static bool AllowDisplayRefreshFlag = true;

        public static event EventHandler RefreshEndEvent;
        private static Timer _refreshTimeoutTimer;
        private static Timer _refreshEndFlagLiftTimer;

        public static void RequestDisplayRefresh()
        {
            lock (_lock)
            {
                if (FirstRunFlag)
                {
                    FirstRunFlag = false;
                    SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
                }
                if (AllowDisplayRefreshFlag)
                {
                    DisplayRefresh();
                }
                else
                {
                    RefreshRequestedFlag = true;
                }
            }
        }

        public static void AllowDisplayRefresh(bool value)
        {
            lock (_lock)
            {
                AllowDisplayRefreshFlag = value;
            }
        }

        public static void AllowDisplayRefreshOnce()
        {
            lock (_lock)
            {
                if (RefreshRequestedFlag)
                {
                    DisplayRefresh();

                }
                else
                {
                    SetRefreshEndFlag(false, 100);
                }
            }
        }

        private static void SetRefreshEndFlag(bool value, int timeout = 10000)
        {
            lock (_lock)
            {             
                if (value)
                {
                    _refreshEndFlagLiftTimer?.Dispose();
                    _refreshEndFlagLiftTimer = new Timer(_ => {
                        lock (_lock)
                        {
                            RefreshEndFlag = true;
                        }
                        }, null, timeout / 2, Timeout.Infinite);

                    
                    _refreshTimeoutTimer?.Dispose();
                    RefreshEndEvent?.Invoke(null, EventArgs.Empty);
                }
                else
                {
                    _refreshEndFlagLiftTimer?.Dispose();
                    RefreshEndFlag = false;
                    _refreshTimeoutTimer?.Dispose();
                    _refreshTimeoutTimer = new Timer(_ =>
                    {
                        lock (_lock)
                        {
                            if (!RefreshEndFlag)
                            {
                                SetRefreshEndFlag(true, timeout);   
                            }
                        }
                    }, null, timeout, Timeout.Infinite);
                }
            }
        }

        public static void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            lock (_lock)
            {
                if (!RefreshEndFlag)
                {
                    SetRefreshEndFlag(true);
                }
            }
        }

        private static void DisplayRefresh()
        {
            RefreshRequestedFlag = false;
            SetRefreshEndFlag(false); 

            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
            keybd_event(VK_B, 0, 0, UIntPtr.Zero);

            keybd_event(VK_B, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }

}
