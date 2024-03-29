using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace JamUp.FFT
{
    [BurstCompile]
    internal struct BuildTwiddleFactorsJob : IJob
    {
        [WriteOnly]
        internal NativeArray<TwiddleFactor> TwiddleFactors;

        [ReadOnly] 
        internal int FFTLength;
        
        public void Execute()
        {
            var i = 0;
            for (var m = 4; m <= FFTLength; m <<= 1)
            {
                for (var k = 0; k < FFTLength; k += m)
                {
                    for (var j = 0; j < m / 2; j += 2)
                    {
                        TwiddleFactors[i++] = new TwiddleFactor(
                            math.int2((k + j) / 2, (k + j + m / 2) / 2), 
                            math.cos(-2 * math.PI / m * math.float2(j, j + 1)));
                    }
                }
            }
            
        }
    }
}