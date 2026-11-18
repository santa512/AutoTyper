using AutoTyper;
using System.Diagnostics;

if (args.Length != 0)
{
    using var instance = new SingleInstance(args[1]);
    if (args[0] == "--race") { Console.WriteLine("READY"); Console.ReadLine(); }
    bool owns = instance.TryAcquire();
    Console.WriteLine(owns ? "OWNED" : "BLOCKED");
    if (owns && args[0] is "--race" or "--abandon") Console.ReadLine();
    if (args[0] == "--abandon") Environment.Exit(0);
    return;
}

string scope = "test-" + Guid.NewGuid().ToString("N");
using (var primary = new SingleInstance(scope))
{
    Require(primary.TryAcquire(), "First launch owns the instance lock");
    using var duplicate = Child("--probe",scope);
    Require(Line(duplicate)=="BLOCKED", "Another process cannot acquire the same user/session instance");
    Wait(duplicate);
}
using (var restarted = Child("--probe",scope))
{
    Require(Line(restarted)=="OWNED", "A new launch can acquire after normal exit");
    Wait(restarted);
}
using (var crashed = Child("--abandon",scope))
using (var recovery = new SingleInstance(scope))
{
    Require(Line(crashed)=="OWNED", "Crash fixture acquires its isolated instance");
    Require(!recovery.TryAcquire(), "Live primary remains exclusive");
    crashed.StandardInput.WriteLine("exit"); Wait(crashed);
    Require(recovery.TryAcquire(), "Abandoned mutex allows recovery after an unexpected exit");
}
using (var a = Child("--race",scope))
using (var b = Child("--race",scope))
{
    Require(Line(a)=="READY" && Line(b)=="READY", "Simultaneous launch fixtures ready");
    a.StandardInput.WriteLine("go"); b.StandardInput.WriteLine("go");
    string first=Line(a),second=Line(b);
    Require(new[]{first,second}.Count(x=>x=="OWNED")==1 && new[]{first,second}.Count(x=>x=="BLOCKED")==1,
        "Exactly one of two simultaneous launches becomes primary");
    if(first=="OWNED") a.StandardInput.WriteLine("exit");
    if(second=="OWNED") b.StandardInput.WriteLine("exit");
    Wait(a); Wait(b);
}
var bottom = TrayMenuPlacement.Calculate(1900,1060,new(0,0,1920,1032),352,293,8);
Require(bottom.Y+bottom.Height<=1024,"Tray menu stays above the bottom taskbar");
var top = TrayMenuPlacement.Calculate(1900,15,new(0,48,1920,1032),352,293,8);
Require(top.Y>=56,"Tray menu stays below the top taskbar");
var left = TrayMenuPlacement.Calculate(20,1000,new(60,0,1860,1080),352,293,8);
Require(left.X>=68,"Tray menu avoids a left-side taskbar");
var negative = TrayMenuPlacement.Calculate(-10,1060,new(-1920,0,1920,1032),704,586,16);
Require(negative.X>=-1904 && negative.X+negative.Width<=-16 && negative.Y+negative.Height<=1016,"Scaled menu fits a monitor with negative coordinates");
var small = TrayMenuPlacement.Calculate(319,239,new(0,0,320,240),704,586,8);
Require(small.X>=8 && small.Y>=8 && small.X+small.Width<=312 && small.Y+small.Height<=232,"Menu is constrained to small monitor work areas");
Console.WriteLine("All single-instance and tray placement checks passed.");

static Process Child(string mode,string scope)
{
    var start=new ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardInput=true,CreateNoWindow=true};
    start.ArgumentList.Add(mode);start.ArgumentList.Add(scope);
    return Process.Start(start)!;
}
static string Line(Process process)
{
    var line=process.StandardOutput.ReadLineAsync();
    if(!line.Wait(TimeSpan.FromSeconds(10))) {process.Kill();throw new Exception("Child output timed out");}
    return line.Result ?? "";
}
static void Wait(Process process)
{
    if(!process.WaitForExit(10000)) {process.Kill();throw new Exception("Child did not exit");}
    Require(process.ExitCode==0,"Child exited cleanly");
}
static void Require(bool value,string label) {if(!value) throw new Exception(label);Console.WriteLine("PASS: "+label);}
