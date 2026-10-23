Add-Type @"
using System; using System.Runtime.InteropServices;
public static class SliderTest {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint dx,uint dy,uint d,IntPtr e);
  public static void Move(int x,int y){ SetCursorPos(x,y); System.Threading.Thread.Sleep(40); }
  public static void Down(){ mouse_event(2,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(40); }
  public static void Up(){ mouse_event(4,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(40); }
  public static void Drag(int x1,int y1,int x2,int y2){
    Move(x1,y1); Down();
    for(int i=1;i<=12;i++){ Move(x1+(x2-x1)*i/12, y1+(y2-y1)*i/12); }
    Up();
  }
}
"@
