using System.IO;
using System.Runtime.InteropServices;

namespace AutoTyper;

internal static class CaptionSpeechRuntime
{
    private static readonly Lazy<bool> Loaded=new(()=>
    {
        string folder=Path.GetDirectoryName(typeof(Vosk.Model).Assembly.Location)??AppContext.BaseDirectory;
        // Preload the pinned runtime dependencies from beside the managed engine, including
        // the portable EXE's extraction directory. Keep them loaded for the process lifetime.
        foreach(string name in new[]{"libwinpthread-1.dll","libgcc_s_seh-1.dll","libstdc++-6.dll","libvosk.dll"})
            NativeLibrary.Load(Path.Combine(folder,name));
        Vosk.Vosk.SetLogLevel(-1);
        return true;
    });
    internal static void EnsureAvailable()=>_=Loaded.Value;
}
