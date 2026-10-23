using System;
using System.Drawing;
using System.Windows.Forms;
using NetPulse.Interop;

static class Diag
{
    [STAThread]
    static void Main()
    {
        Native.SetProcessDpiAwareness(1);
        Console.WriteLine("Primary   = " + Screen.PrimaryScreen.Bounds);
        Console.WriteLine("Virtual   = " + SystemInformation.VirtualScreen);
        Console.WriteLine("WorkArea  = " + Screen.PrimaryScreen.WorkingArea);
        Console.WriteLine("SM_CX/CY  = " + Native.GetSystemMetrics(0) + " x " + Native.GetSystemMetrics(1));

        var tb = TaskbarInfo.Query();
        if (tb == null) Console.WriteLine("taskbar   = NULL");
        else
        {
            Console.WriteLine("taskbar   = L{0} T{1} W{2} H{3}", tb.Left, tb.Top, tb.Width, tb.Height);
            Console.WriteLine("            right={0} bottom={1} trayLeft={2} btnRight={3} edge={4} vertical={5}",
                tb.Right, tb.Bottom, tb.TrayLeft, tb.ButtonsRight, tb.Edge, tb.Vertical);
            Console.WriteLine("            screenOfHandle=" + Screen.FromHandle(tb.Handle).Bounds);
            Console.WriteLine("            pos for h=40 => x={0} y={1}", (tb.TrayLeft > 0 ? tb.TrayLeft : tb.Right) - 120 - 4, tb.Edge == 1 ? tb.Bottom + 2 : tb.Top + (tb.Height - 40) / 2);
        }
        Console.WriteLine("foreground fullscreen = " + Native.IsFullscreenAppRunning());
        Console.WriteLine("taskbar light = " + Native.IsTaskbarLight());
    }
}

