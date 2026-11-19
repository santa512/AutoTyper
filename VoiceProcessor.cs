using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AutoTyper;

internal sealed class VoiceProcessor : IDisposable
{
    private readonly VoiceHandle _handle;
    internal int LatencySamples => Native.Latency(_handle);
    internal VoiceProcessor(int sampleRate=48000)
    {
        _handle=Native.Create(sampleRate);
        if(_handle.IsInvalid) { _handle.Dispose(); throw new InvalidOperationException("Could not initialize the local voice processor."); }
    }
    internal void Configure(AudioSettings settings,bool original)
    {
        settings.Validate();
        if(Native.Configure(_handle,(float)settings.Depth,(float)settings.Body,(float)settings.Volume,original?1:0)!=0)
            throw new InvalidOperationException("Could not update the local voice settings.");
    }
    internal void Process(float[] input,float[] output,int count)
    {
        if(count<0 || count>4096 || count>input.Length || count>output.Length) throw new ArgumentOutOfRangeException(nameof(count));
        if(Native.Process(_handle,input,output,count)!=count) throw new InvalidOperationException("The local voice processor stopped.");
    }
    public void Dispose()=>_handle.Dispose();
    private sealed class VoiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public VoiceHandle():base(true){}
        protected override bool ReleaseHandle() { Native.Destroy(handle); return true; }
    }
    private static class Native
    {
        private const string Library="AutoTyperAudio.dll";
        [DllImport(Library,EntryPoint="at_voice_create",CallingConvention=CallingConvention.Cdecl)] internal static extern VoiceHandle Create(int sampleRate);
        [DllImport(Library,EntryPoint="at_voice_destroy",CallingConvention=CallingConvention.Cdecl)] internal static extern void Destroy(IntPtr handle);
        [DllImport(Library,EntryPoint="at_voice_latency",CallingConvention=CallingConvention.Cdecl)] internal static extern int Latency(VoiceHandle handle);
        [DllImport(Library,EntryPoint="at_voice_configure",CallingConvention=CallingConvention.Cdecl)] internal static extern int Configure(VoiceHandle handle,float depth,float body,float volume,int original);
        [DllImport(Library,EntryPoint="at_voice_process",CallingConvention=CallingConvention.Cdecl)] internal static extern int Process(VoiceHandle handle,[In]float[] input,[Out]float[] output,int count);
    }
}
