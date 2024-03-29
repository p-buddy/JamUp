using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace JamUp.Waves.RuntimeScripts
{
    [BurstCompile]
    public static class CameraHelper
    {
        private static readonly NativeArray<float4x4> Matrices;
        
        static CameraHelper()
        {
            //Matrices = new NativeArray<float4x4>(1, Allocator.Persistent);
        }
        
        public static void Dispose()
        {
            Matrices.Dispose();
        }

        [BurstCompile]
        public static float4x4 Lerp(float4x4 from, float4x4 to, float lerpTime)
        {
            new MatrixLerpJob
            {
                Start = from,
                End = to,
                LerpTime = lerpTime,
                Result = Matrices
            }.Run();
            return Matrices[0];
        }

        public static void LerpProjection(UnityEngine.Camera camera,
                                          ProjectionType from,
                                          ProjectionType to,
                                          float lerpTime,
                                          in CameraSettings settings)
        {
            float4x4 start = Construct(from, in settings);
            float4x4 end = Construct(to, in settings);
            camera.projectionMatrix = Lerp(start, end, lerpTime);
        }

        [BurstCompile]
        public static float4x4 Construct(ProjectionType projectionType, in CameraSettings settings)
        {
            new InitMatrixJob
            {
                Width = Screen.width,
                Height = Screen.height,
                ProjectionType = projectionType,
                Settings = settings,
                Result = Matrices
            }.Run();
            return Matrices[0];
        }
    }
}