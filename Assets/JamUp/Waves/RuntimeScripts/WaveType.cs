using System;

namespace JamUp.Waves.RuntimeScripts
{
    [Flags]
    public enum WaveType
    {
        Sine = 0x00000001,
        Square = 0x00000002,
        Triangle = 0x00000004, 
        Sawtooth = 0x00000008,
    }
}