#include "vendor/signalsmith-stretch.h"
#include <algorithm>
#include <cmath>
#include <vector>

#define API extern "C" __declspec(dllexport)
namespace {
constexpr double pi = 3.14159265358979323846;
struct Filter {
    double b0=1,b1=0,b2=0,a1=0,a2=0,z1=0,z2=0;
    float process(float x) {
        double y=b0*x+z1; z1=b1*x-a1*y+z2; z2=b2*x-a2*y;
        return static_cast<float>(y);
    }
    void highPass(double rate, double frequency) {
        double c=std::cos(2*pi*frequency/rate), alpha=std::sin(2*pi*frequency/rate)/(2*0.707);
        double d=1+alpha;
        b0=(1+c)/(2*d); b1=-(1+c)/d; b2=b0; a1=-2*c/d; a2=(1-alpha)/d;
    }
    void shelf(double rate, double frequency, double db) {
        double A=std::pow(10,db/40), c=std::cos(2*pi*frequency/rate), s=std::sin(2*pi*frequency/rate);
        double alpha=s*std::sqrt(2.0)/2, beta=2*std::sqrt(A)*alpha;
        double d=(A+1)+(A-1)*c+beta;
        b0=A*((A+1)-(A-1)*c+beta)/d; b1=2*A*((A-1)-(A+1)*c)/d;
        b2=A*((A+1)-(A-1)*c-beta)/d; a1=-2*((A-1)+(A+1)*c)/d;
        a2=((A+1)+(A-1)*c-beta)/d;
    }
    void peak(double rate, double frequency, double q, double db) {
        double A=std::pow(10,db/40), c=std::cos(2*pi*frequency/rate), alpha=std::sin(2*pi*frequency/rate)/(2*q);
        double d=1+alpha/A;
        b0=(1+alpha*A)/d; b1=-2*c/d; b2=(1-alpha*A)/d; a1=b1; a2=(1-alpha/A)/d;
    }
};
struct Voice {
    signalsmith::stretch::SignalsmithStretch<float> stretch{12345};
    Filter highPass, warmth, clarity;
    double rate, envelope=0, reduction=1, limiter=1;
    float targetWet=1, wet=1, targetVolume=0.35f, volume=0.35f;
    float body=-1, depth=-1;
    int latency=0, dryCursor=0;
    std::vector<float> input, shifted, dry;
    explicit Voice(int sampleRate): rate(sampleRate), input(4096), shifted(4096) {
        stretch.configure(1, sampleRate*96/1000, sampleRate*12/1000, true);
        stretch.setFormantFactor(1.0f, true);
        stretch.setFormantBase(0); // speech F0 is estimated locally by the processor
        latency=stretch.inputLatency()+stretch.outputLatency();
        dry.resize(std::max(1,latency));
        highPass.highPass(rate,65);
        clarity.peak(rate,450,0.8,-0.8);
        configure(1,0.45f,0.35f,false);
    }
    void configure(float semitones, float newBody, float newVolume, bool bypass) {
        semitones=std::clamp(semitones,0.0f,3.0f);
        newBody=std::clamp(newBody,0.0f,1.0f);
        if (depth!=semitones) { depth=semitones; stretch.setTransposeSemitones(-depth); }
        if (body!=newBody) { body=newBody; warmth.shelf(rate,180,body*4.5); }
        targetVolume=std::clamp(newVolume,0.0f,1.0f);
        targetWet=bypass?0.0f:1.0f;
    }
    int process(const float* source, float* destination, int frames) {
        if (!source || !destination || frames<0 || frames>4096) return -1;
        for(int i=0;i<frames;++i) input[i]=std::isfinite(source[i])?std::clamp(source[i],-4.0f,4.0f):0.0f;
        const float* inputs[]={input.data()}; float* outputs[]={shifted.data()};
        stretch.process(inputs,frames,outputs,frames);
        const double attack=std::exp(-1/(rate*0.005)), release=std::exp(-1/(rate*0.10));
        const float smoothing=static_cast<float>(1-std::exp(-1/(rate*0.015)));
        for(int i=0;i<frames;++i) {
            float original=dry[dryCursor]; dry[dryCursor]=input[i]; dryCursor=(dryCursor+1)%dry.size();
            float value=clarity.process(warmth.process(highPass.process(shifted[i])));
            double level=std::abs(value), coefficient=level>envelope?attack:release;
            envelope=coefficient*envelope+(1-coefficient)*level;
            double over=20*std::log10(std::max(envelope,1e-9)/0.18);
            double gain=over>0?std::pow(10,-over*(1-1/1.8)/20):1;
            coefficient=gain<reduction?attack:release;
            reduction=coefficient*reduction+(1-coefficient)*gain;
            value*=static_cast<float>(reduction*1.05);
            wet+=smoothing*(targetWet-wet); volume+=smoothing*(targetVolume-volume);
            value=(original+(value-original)*wet)*volume;
            double peak=std::abs(value), wanted=peak>0.9?0.9/peak:1;
            limiter=wanted<limiter?wanted:release*limiter+(1-release)*wanted;
            value*=static_cast<float>(limiter);
            destination[i]=std::isfinite(value)?std::clamp(value,-0.9f,0.9f):0.0f;
        }
        return frames;
    }
};
}
API void* at_voice_create(int sampleRate) {
    if(sampleRate<16000 || sampleRate>96000) return nullptr;
    try { return new Voice(sampleRate); } catch(...) { return nullptr; }
}
API void at_voice_destroy(void* voice) { delete static_cast<Voice*>(voice); }
API int at_voice_latency(void* voice) { return voice?static_cast<Voice*>(voice)->latency:0; }
API int at_voice_configure(void* voice,float depth,float body,float volume,int bypass) {
    if(!voice || !std::isfinite(depth) || !std::isfinite(body) || !std::isfinite(volume)) return -1;
    try { static_cast<Voice*>(voice)->configure(depth,body,volume,bypass!=0); return 0; } catch(...) { return -1; }
}
API int at_voice_process(void* voice,const float* input,float* output,int frames) {
    if(!voice) return -1;
    try { return static_cast<Voice*>(voice)->process(input,output,frames); } catch(...) { return -1; }
}
