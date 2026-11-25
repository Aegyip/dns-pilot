using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DnsDegistirici
{
    // "Kapanınca birlikte kapat" iş nesnesi (Job Object). Buraya eklenen süreçler (winws), DNS Pilot sona erdiğinde —
    // normal çıkış, çökme ya da Görev Yöneticisi'nden sonlandırma fark etmez — Windows tarafından kapatılır.
    public static class ChildJob
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimitInformation info, int length);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        const int JobObjectExtendedLimitInformation = 9;
        const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [StructLayout(LayoutKind.Sequential)]
        struct BasicLimitInformation
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IoCounters { public ulong a, b, c, d, e, f; }

        [StructLayout(LayoutKind.Sequential)]
        struct ExtendedLimitInformation
        {
            public BasicLimitInformation Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        static readonly object sync = new object();
        static IntPtr job; // uygulama ömrü boyunca açık kalır; süreç bitince Windows kapatır

        public static void Assign(Process p)
        {
            try
            {
                lock (sync)
                {
                    if (job == IntPtr.Zero)
                    {
                        IntPtr h = CreateJobObject(IntPtr.Zero, null);
                        if (h == IntPtr.Zero) return;
                        var info = new ExtendedLimitInformation();
                        info.Basic.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                        if (!SetInformationJobObject(h, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf(info)))
                        {
                            Diag.Log("iş nesnesi ayarlanamadı: " + Marshal.GetLastWin32Error());
                            return;
                        }
                        job = h;
                    }
                }
                if (!AssignProcessToJobObject(job, p.Handle))
                    Diag.Log("zapret iş nesnesine eklenemedi: " + Marshal.GetLastWin32Error());
            }
            catch (Exception ex) { Diag.Error("iş nesnesi", ex); }
        }
    }
}
