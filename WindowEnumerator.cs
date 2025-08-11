using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;


/// <summary>
/// 一个用于查找并枚举指定进程的所有窗口的实用工具类。
/// </summary>
public static class WindowEnumerator
{
    // 导入所需的 Win32 API 函数

    /// <summary>
    /// 遍历屏幕上的所有顶级窗口，并为每个窗口调用一次回调函数。
    /// </summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary>
    /// 获取指定窗口的标题（文本）。
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    /// <summary>
    /// 获取指定窗口的创建者线程ID，以及进程ID。
    /// </summary>
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // 定义一个委托，用作 EnumWindows 的回调
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    /// <summary>
    /// 将指定窗口置于前台。如果窗口被最小化，则会先恢复它。
    /// </summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// 显示或隐藏指定窗口。
    /// </summary>
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    // ShowWindow 的参数
    private const int SW_RESTORE = 9; // 恢复窗口

    /// <summary>
    /// 查找指定名称的进程，并激活标题匹配的窗口。
    /// </summary>
    /// <param name="processName">进程名称（不含.exe）。</param>
    /// <param name="targetTitle">目标窗口的标题。</param>
    /// <returns>如果找到并成功激活窗口，则返回 true；否则返回 false。</returns>
    public static bool FindAndActivateWindow(string processName, string targetTitle)
    {
        Process[] processes = Process.GetProcessesByName(processName);
        if (processes.Length == 0)
        {
           
            return false;
        }

        var targetPids = processes.Select(p => (uint)p.Id).ToHashSet();
        bool windowFound = false;

        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint windowPid);
            if (targetPids.Contains(windowPid))
            {
                StringBuilder sb = new StringBuilder(256);
                GetWindowText(hWnd, sb, sb.Capacity);
                string windowTitle = sb.ToString();

                if (windowTitle.Equals(targetTitle, StringComparison.OrdinalIgnoreCase))
                {
                    // 激活窗口
                    ShowWindow(hWnd, SW_RESTORE); // 如果最小化，则恢复
                    SetForegroundWindow(hWnd);    // 置于前台
                    windowFound = true;
                    return false; // 停止枚举
                }
            }
            return true; // 继续枚举
        }, IntPtr.Zero);

        return windowFound;
    }
}

