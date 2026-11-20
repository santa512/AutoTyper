using AutoTyper;
using System.Runtime.InteropServices;

internal static class Program
{
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr pid);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from,uint to,bool attach);
    static void Foreground(Form form)
    {
        uint other=GetWindowThreadProcessId(NativeKeyboard.GetForegroundWindow(),IntPtr.Zero), mine=GetCurrentThreadId();
        AttachThreadInput(mine,other,true);
        try { form.BringToFront(); SetForegroundWindow(form.Handle); form.Activate(); }
        finally { AttachThreadInput(mine,other,false); }
        Pump();
        Check(NativeKeyboard.GetForegroundWindow()==form.Handle,"Test window owns foreground");
    }
    [STAThread] static void Main()
    {
        using var form = new Form { Text = "Auto Typer focus test", Width = 500, Height = 200 };
        var first = new TextBox { Top = 20, Width = 300 };
        var second = new TextBox { Top = 60, Width = 300 };
        form.Controls.AddRange([first, second]);
        form.Show(); Foreground(form); first.Focus(); Pump();
        var target = TypingFocusTarget.Capture(IntPtr.Zero);
        Check(target.IsFocused(), "Original input recognized");
        second.Focus(); Pump();
        Check(!target.IsFocused(), "Different input in same window pauses");
        first.Focus(); Pump();
        Check(target.IsFocused(), "Original input resumes");
        using var other = new Form { Text = "Other window" };
        other.Show(); Foreground(other); Pump();
        Check(!target.IsFocused(), "Another window pauses");
        Foreground(form); first.Focus(); Pump();
        Check(target.IsFocused(), "Returning to original window/input resumes");
        first.Dispose(); Pump();
        Check(!target.IsFocused(), "Destroyed input does not resume elsewhere");
    }
    static void Pump() { for(int i=0;i<5;i++) { Application.DoEvents(); Thread.Sleep(50); } }
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); Console.WriteLine("PASS: " + message); }
}

