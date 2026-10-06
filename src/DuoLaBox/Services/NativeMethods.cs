using System.Runtime.InteropServices;
using System.Text;

namespace DuoLaBox.Services;

internal static class NativeMethods
{
    internal const int GwlExStyle = -20;
    internal const long WsExTopMost = 0x00000008L;
    internal const long WsExToolWindow = 0x00000080L;
    internal const long WsExAppWindow = 0x00040000L;
    internal const long WsExNoActivate = 0x08000000L;
    internal const uint GaRoot = 2;
    internal const uint GwOwner = 4;
    internal const uint DwmwaCloaked = 14;
    internal const uint DwmwaUseImmersiveDarkMode = 20;
    internal const uint DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    internal const uint DwmwaBorderColor = 34;
    internal const uint DwmwaCaptionColor = 35;
    internal const uint DwmwaTextColor = 36;
    internal const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);
    internal const int SwRestore = 9;
    internal const int WhMouseLl = 14;
    internal const uint WmLButtonDown = 0x0201;
    internal const uint WmLButtonUp = 0x0202;
    internal const uint WmMouseMove = 0x0200;
    internal const uint EsContinuous = 0x80000000;
    internal const uint EsSystemRequired = 0x00000001;
    internal const uint EsDisplayRequired = 0x00000002;
    internal const uint EventSystemMinimizeStart = 0x0016;
    internal const uint EventSystemMinimizeEnd = 0x0017;
    internal const uint EventObjectLocationChange = 0x800B;
    internal const uint WineventOutofcontext = 0x0000;
    internal const int ObjidWindow = 0;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint OcrNormal = 32512;
    internal const uint OcrIBeam = 32513;
    internal const uint OcrWait = 32514;
    internal const uint OcrCross = 32515;
    internal const uint OcrUp = 32516;
    internal const uint OcrSizeNwSe = 32642;
    internal const uint OcrSizeNeSw = 32643;
    internal const uint OcrSizeWe = 32644;
    internal const uint OcrSizeNs = 32645;
    internal const uint OcrSizeAll = 32646;
    internal const uint OcrNo = 32648;
    internal const uint OcrHand = 32649;
    internal const uint OcrAppStarting = 32650;
    internal const uint OcrHelp = 32651;
    internal const uint SpiSetCursors = 0x0057;
    internal const uint WmSettingChange = 0x001A;
    internal const uint WmGetIcon = 0x007F;
    internal const uint SmtoAbortIfHung = 0x0002;
    internal const nuint IconSmall = 0;
    internal const nuint IconBig = 1;
    internal const nuint IconSmall2 = 2;
    internal const int GclpHicon = -14;
    internal const int GclpHiconSmall = -34;
    internal const uint ShgfiSysiconindex = 0x000004000;
    internal const uint ShcneAssocChanged = 0x08000000;
    internal const uint ShcnfIdList = 0x0000;
    internal const int ShilJumbo = 4;
    internal const uint IldTransparent = 0x00000001;
    internal const int ErrorMoreData = 234;

    internal static readonly nint HwndTopMost = new(-1);
    internal static readonly nint HwndNoTopMost = new(-2);
    internal static readonly nint HwndBroadcast = new(0xFFFF);

    internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);
    internal delegate void WinEventProc(
        nint hook,
        uint eventType,
        nint hWnd,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);
    internal delegate nint LowLevelMouseProc(
        int code,
        nint message,
        nint hookData);

    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IImageList
    {
        [PreserveSig]
        int Add(nint image, nint mask, out int index);

        [PreserveSig]
        int ReplaceIcon(int index, nint icon, out int newIndex);

        [PreserveSig]
        int SetOverlayImage(int image, int overlay);

        [PreserveSig]
        int Replace(int index, nint image, nint mask);

        [PreserveSig]
        int AddMasked(nint image, int maskColor, out int index);

        [PreserveSig]
        int Draw(nint drawParameters);

        [PreserveSig]
        int Remove(int index);

        [PreserveSig]
        int GetIcon(int index, uint flags, out nint icon);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    internal static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint window, nint deviceContext);

    [DllImport("user32.dll")]
    internal static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    internal static extern nint GetAncestor(nint hWnd, uint flags);

    [DllImport("user32.dll")]
    internal static extern nint GetWindow(nint hWnd, uint command);

    [DllImport("user32.dll")]
    internal static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint hWnd, out NativeRect rectangle);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindowAsync(nint hWnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    internal static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowText(nint hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(nint hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(
        nint hWnd,
        uint attribute,
        out int value,
        int valueSize);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(
        nint window,
        uint attribute,
        ref int value,
        int valueSize);

    [DllImport(
        "shell32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    internal static extern nint SHGetFileInfo(
        string path,
        uint fileAttributes,
        out ShellFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport(
        "shell32.dll",
        EntryPoint = "SHGetImageList")]
    internal static extern int SHGetImageList(
        int imageList,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IImageList images);

    [DllImport("shell32.dll")]
    internal static extern void SHChangeNotify(
        uint eventId,
        uint flags,
        nint item1,
        nint item2);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    internal static extern int RmStartSession(
        out uint sessionHandle,
        int sessionFlags,
        StringBuilder sessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    internal static extern int RmRegisterResources(
        uint sessionHandle,
        uint fileCount,
        [MarshalAs(
            UnmanagedType.LPArray,
            ArraySubType = UnmanagedType.LPWStr)]
        string[] fileNames,
        uint applicationCount,
        [In] RestartManagerUniqueProcess[] applications,
        uint serviceCount,
        [MarshalAs(
            UnmanagedType.LPArray,
            ArraySubType = UnmanagedType.LPWStr)]
        string[] serviceNames);

    [DllImport("rstrtmgr.dll")]
    internal static extern int RmGetList(
        uint sessionHandle,
        out uint processInfoNeeded,
        ref uint processInfoCount,
        [In, Out] RestartManagerProcessInfo[]? affectedApps,
        ref uint rebootReasons);

    [DllImport("rstrtmgr.dll")]
    internal static extern int RmShutdown(
        uint sessionHandle,
        uint actionFlags,
        nint statusCallback);

    [DllImport("rstrtmgr.dll")]
    internal static extern int RmEndSession(uint sessionHandle);

    [DllImport("user32.dll")]
    internal static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint eventHookModule,
        WinEventProc callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
    internal static extern nint SetWindowsHookEx(
        int hookType,
        LowLevelMouseProc callback,
        nint module,
        uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(
        nint hook,
        int code,
        nint message,
        nint hookData);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetSystemCursor(
        nint cursor,
        uint cursorId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        nint data,
        uint flags);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    internal static extern nint SendMessageTimeout(
        nint window,
        uint message,
        nuint messageParameter,
        string settingName,
        uint flags,
        uint timeout,
        out nuint result);

    [DllImport(
        "user32.dll",
        EntryPoint = "SendMessageTimeoutW",
        SetLastError = true)]
    internal static extern nint SendMessageTimeoutHandle(
        nint window,
        uint message,
        nuint messageParameter,
        nint data,
        uint flags,
        uint timeout,
        out nuint result);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetIconInfo(
        nint icon,
        out IconInfo iconInfo);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint CreateIconIndirect(
        ref IconInfo iconInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyCursor(nint cursor);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint graphicsObject);

    [DllImport("gdi32.dll")]
    internal static extern uint GetPixel(
        nint deviceContext,
        int x,
        int y);

    [DllImport("kernel32.dll")]
    internal static extern uint SetThreadExecutionState(uint executionState);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr64(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    internal static extern int GetWindowLong32(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    internal static extern nint GetClassLongPtr64(
        nint window,
        int index);

    [DllImport("user32.dll", EntryPoint = "GetClassLongW")]
    internal static extern uint GetClassLong32(
        nint window,
        int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern nint SetWindowLongPtr64(nint hWnd, int index, nint newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    internal static extern int SetWindowLong32(nint hWnd, int index, int newValue);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    internal static nint GetWindowLongPtr(nint hWnd, int index) =>
        Environment.Is64BitProcess
            ? GetWindowLongPtr64(hWnd, index)
            : new nint(GetWindowLong32(hWnd, index));

    internal static nint SetWindowLongPtr(nint hWnd, int index, nint newValue) =>
        Environment.Is64BitProcess
            ? SetWindowLongPtr64(hWnd, index, newValue)
            : new nint(SetWindowLong32(hWnd, index, newValue.ToInt32()));

    internal static nint GetClassLongPtr(nint window, int index) =>
        Environment.Is64BitProcess
            ? GetClassLongPtr64(window, index)
            : new nint(unchecked((int)GetClassLong32(window, index)));

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal int Width => Right - Left;

        internal int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LowLevelMouseData
    {
        internal NativePoint Point;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        internal bool IsIcon;
        internal uint HotspotX;
        internal uint HotspotY;
        internal nint MaskBitmap;
        internal nint ColorBitmap;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ShellFileInfo
    {
        internal nint Icon;
        internal int IconIndex;
        internal uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        internal string TypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RestartManagerUniqueProcess
    {
        internal int ProcessId;
        internal System.Runtime.InteropServices.ComTypes.FILETIME
            ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct RestartManagerProcessInfo
    {
        internal RestartManagerUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        internal string ApplicationName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        internal string ServiceShortName;

        internal uint ApplicationType;
        internal uint ApplicationStatus;
        internal uint TerminalSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        internal bool Restartable;
    }
}
