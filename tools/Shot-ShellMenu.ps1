param(
  [string]$Out = 'G:\test-tool\BatchRename\build\menu.png',
  [ValidateSet('files','folder')][string]$Mode = 'files',
  [string]$FolderTitle = 'demo'
)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class UI {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte v, byte s, uint f, IntPtr e);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  public delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public const uint DOWN = 0x0002, UP = 0x0004, RIGHTDOWN = 0x0008, RIGHTUP = 0x0010;
  public static void Click(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(150); mouse_event(DOWN,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(60); mouse_event(UP,0,0,0,IntPtr.Zero); }
  public static void RightClick(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(150); mouse_event(RIGHTDOWN,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(60); mouse_event(RIGHTUP,0,0,0,IntPtr.Zero); }
  public static void Key(byte vk) { keybd_event(vk,0,0,IntPtr.Zero); keybd_event(vk,0,2,IntPtr.Zero); }

  public static List<string> Find(uint[] pids, string cls) {
    var res = new List<string>();
    EnumWindows((h, p) => {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (Array.IndexOf(pids, pid) < 0) return true;
      var cn = new StringBuilder(64); GetClassName(h, cn, 64);
      if (cn.ToString() != cls) return true;
      var tt = new StringBuilder(512); GetWindowTextW(h, tt, 512);
      res.Add(h.ToInt64() + "|" + pid + "|" + (IsWindowVisible(h) ? 1 : 0) + "|" + tt.ToString());
      return true;
    }, IntPtr.Zero);
    return res;
  }
  public static void ForceForeground(IntPtr h) {
    uint pid; uint tid = GetWindowThreadProcessId(h, out pid);
    uint cur = GetCurrentThreadId();
    AttachThreadInput(cur, tid, true);
    ShowWindow(h, 9); SetForegroundWindow(h); SetWindowPos(h, new IntPtr(-1), 20, 20, 1320, 880, 0x0040);
    AttachThreadInput(cur, tid, false);
  }
}
"@

$log = New-Object System.Collections.Generic.List[string]
$pids = @(Get-Process explorer | ForEach-Object { [uint32]$_.Id })
$wins = [UI]::Find($pids, 'CabinetWClass')
$log.Add("cabinet windows: " + ($wins -join ' ;; '))
$pick = $wins | Where-Object { $_ -like "*$FolderTitle*" } | Select-Object -First 1
if (-not $pick) { $pick = $wins | Select-Object -First 1 }
if (-not $pick) { $log.Add('ERROR: 没有 CabinetWClass 窗口'); $log -join "`n"; exit 2 }
$h = [IntPtr][int64]($pick.Split('|')[0])
$log.Add("picked: $pick")

[UI]::ForceForeground($h)
Start-Sleep -Milliseconds 900

try {
  $ae = [System.Windows.Automation.AutomationElement]::FromHandle($h)
  $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::ListItem)
  $items = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
  $log.Add("list items=$($items.Count)")
  if ($items.Count -eq 0) { throw '没有找到文件项' }

  if ($Mode -eq 'files') {
    [System.Windows.Forms.SendKeys]::SendWait('^a')
    Start-Sleep -Milliseconds 350
    $rc = $items[0].Current.BoundingRectangle
    $px = [int]($rc.X + $rc.Width/2); $py = [int]($rc.Y + $rc.Height/2)
    $log.Add("right-click item at $px,$py  name=$($items[0].Current.Name)")
    [UI]::RightClick($px, $py)
  } else {
    $r = New-Object UI+RECT
    [UI]::GetWindowRect($h, [ref]$r) | Out-Null
    $px = [int]($r.L + ($r.R - $r.L) * 0.62); $py = [int]($r.B - 70)
    $log.Add("right-click blank at $px,$py")
    [UI]::Click($px, $py)
    Start-Sleep -Milliseconds 300
    [UI]::RightClick($px, $py)
  }
  Start-Sleep -Milliseconds 1600

  $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
  $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  $log.Add("saved $Out " + (Get-Item $Out).Length)
} catch {
  $log.Add("ERROR: " + $_.Exception.Message)
} finally {
  [UI]::Key(0x1B)
  Start-Sleep -Milliseconds 200
  [UI]::SetWindowPos($h, [IntPtr]::new(-2), 0, 0, 0, 0, ((0x0001 -bor 0x0002) -bor 0x0040))
}
$log -join "`n"