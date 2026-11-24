using AutoTyper;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class Program
{
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr pid);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from,uint to,bool attach);
    static void Foreground(IntPtr handle)
    {
        uint other=GetWindowThreadProcessId(NativeKeyboard.GetForegroundWindow(),IntPtr.Zero), mine=GetCurrentThreadId();
        AttachThreadInput(mine,other,true);
        try { SetForegroundWindow(handle); }
        finally { AttachThreadInput(mine,other,false); }
        Thread.Sleep(200);
        Check(NativeKeyboard.GetForegroundWindow()==handle,"Test window owns foreground");
    }
    [STAThread] static void Main(string[] args)
    {
        if(args.Contains("--target"))
        {
            using var form=new Form { Text="Focus test target",Width=400,Height=220 };
            var first=new TextBox { Top=20 }; var second=new TextBox { Top=60 };
            var button=new Button { Top=100,Text="Non-text control" };
            form.Controls.AddRange([first,second,button]);
            form.Shown+=(_,_)=> { Console.WriteLine(form.Handle.ToInt64()); Console.Out.Flush(); };
            _=Task.Run(()=> { string? line; while((line=Console.ReadLine()) is not null) { var command=line; form.BeginInvoke(()=> { if(command=="second") second.Focus(); else if(command=="button") button.Focus(); else if(command=="close") form.Close(); else if(command=="text") { Console.WriteLine(first.Text); Console.Out.Flush(); return; } Console.WriteLine("OK");Console.Out.Flush(); }); } });
            Application.Run(form);return;
        }
        using var child=Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--target") { RedirectStandardInput=true,RedirectStandardOutput=true })!;
        try
        {
            var handle=new IntPtr(long.Parse(child.StandardOutput.ReadLine()!));
            Foreground(handle);
            var target=TypingFocusTarget.Capture(IntPtr.Zero);
            Check(target.IsFocused(),"Initial application captured");
            const string sample = "Abc 123 !?";
            foreach(char c in sample) { NativeKeyboard.SendCharacter(c); Thread.Sleep(30); }
            Thread.Sleep(150);
            child.StandardInput.WriteLine("text"); child.StandardInput.Flush();
            Check(child.StandardOutput.ReadLine()==sample,"Physical keyboard events type letters, capitals, digits and punctuation");
            void Command(string command) { child.StandardInput.WriteLine(command);child.StandardInput.Flush();child.StandardOutput.ReadLine(); }
            Command("second");Check(target.IsFocused(),"Changing fields keeps typing");
            Command("button");Check(target.IsFocused(),"Non-text control keeps typing");
            Check(TypingFocusTarget.Capture(IntPtr.Zero).IsFocused(),"Can start on non-text control");
            using var other=new Form { Text="Auto Typer test window" };other.Show();Foreground(other.Handle);
            Check(!target.IsFocused(),"Leaving original application pauses");
            bool rejected=false;try { TypingFocusTarget.Capture(other.Handle); } catch(InvalidOperationException) { rejected=true; }
            Check(rejected,"Own application rejected as initial target");
            Foreground(handle);Check(target.IsFocused(),"Original application resumes");
            Command("close");child.WaitForExit(5000);Check(!target.IsFocused(),"Closed target cannot resume");
        }
        finally { if(!child.HasExited) child.Kill(); }
    }
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); Console.WriteLine("PASS: "+message); }
}
