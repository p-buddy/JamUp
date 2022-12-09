using System;
using JamUp.Waves.RuntimeScripts.API;
using JamUp.Waves.RuntimeScripts.Audio;
using JamUp.Waves.RuntimeScripts.BufferIndexing;
using pbuddy.TypeScriptingUtility.RuntimeScripts;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace JamUp.Waves.RuntimeScripts
{

    [BurstCompile]
    public struct CreateEntities : IJob
    {
        public const int MaxWaveCount = 10;
        private const float AttackTime = 2f;
        private const float ReleaseTime = 0.1f;

        // NOTE: Consider that a wave update could have happened, and thus the waves could look different
        public float DataRetrievalTime; 

        #region Data From Execution
        [ReadOnly] 
        public NativeArray<int> FrameOffsets;
        public NativeArray<PackedFrame> PackedFrames;

        [ReadOnly] 
        public NativeArray<int> WaveOffsets;
        public NativeArray<Animatable<WaveState>> Waves;
        
        [ReadOnly]
        public NativeArray<float> PreviousJobExecutionTime;

        [ReadOnly] 
        public NativeArray<float> RootFrequencies;
        #endregion

        //public float RootFrequency;

        //public float TimeNow;

        //public int Index;

        public EntityCommandBuffer ECB;

        public EntityArchetype EntityArchetype;
        
        #region Data From Existing Entities        
        [ReadOnly] 
        public NativeArray<Entity> ExistingEntities;
        [ReadOnly]
        public NativeArray<AudioGraphReference> GraphReferences;
        [ReadOnly]
        public NativeArray<PropertyBlockReference> PropertyBlocks;
        [ReadOnly]
        public NativeArray<CurrentTimeFrame> TimeFrames;
        [ReadOnly] 
        public NativeArray<CurrentWaveCount> WaveCounts;
        [ReadOnly] 
        public NativeArray<CurrentProjection> ProjectionForEntity;
        [ReadOnly] 
        public NativeArray<CurrentSignalLength> SignalLengthForEntity;
        [ReadOnly]
        public NativeArray<CurrentSampleRate> SampleRateForEntity;
        [ReadOnly] 
        public NativeArray<CurrentThickness> ThicknessForEntity;
        [ReadOnly] 
        public BufferFromEntity<CurrentWavesElement> WavesForEntity;
        [ReadOnly] 
        public BufferFromEntity<CurrentWaveAxes> AxesForEntity;
        #endregion
        public void Execute()
        {
            int waveIndex = 0;
            int frameIndex = 0;
            float timeNow = DataRetrievalTime + PreviousJobExecutionTime[0];

            for (int i = 0; i < FrameOffsets.Length; i++)
            {
                int frameCount = FrameOffsets[i];
                ConstructEntity(i, timeNow, frameCount, waveIndex, frameIndex);
                waveIndex += WaveOffsets[i];
                frameIndex += frameCount;
            }
        }
        
        private void ConstructEntity(int index, float timeNow, int frameCount, int startingFrameIndex, int startingWaveIndex)
        {
            EntityHandle entityHandle = new (index, ExistingEntities, ECB, EntityArchetype, timeNow, TimeFrames);
            InitializeEntity(in entityHandle);
            
            ECB.SetComponent(entityHandle.Entity, new SignalEntity(RootFrequencies[index]));
            ECB.SetComponent(entityHandle.Entity, CurrentIndex.Invalid());
            ECB.SetComponent<CurrentTimeFrame>(entityHandle.Entity, default);
            ECB.SetComponent<CurrentWaveIndex>(entityHandle.Entity, default);

            //int frameCount = PackedFrames.Length;
            int elementsToAdd = frameCount + 2; // all frames, plus 'sustain' and 'release'
            int capacity = 1 + elementsToAdd; // prepend one frame for 'attack'
            
            ECB.SetComponent(entityHandle.Entity, new LastIndex(capacity - 2));
            
            Init<DurationElement>(capacity, in entityHandle).Append(new DurationElement(AttackTime));
            Init<WaveCountElement>(capacity, in entityHandle).Append(new WaveCountElement
            {
                Value = entityHandle.UseExisting ? WaveCounts[index].Value : 0
            });

            SetBuffersWithInitialElements(capacity, in entityHandle);

            CommandBufferForEntity entityCommandBuffer = new()
            {
                CommandBuffer = ECB,
                Entity = entityHandle.Entity
            };
            
            int accumulatedWaveCounts = 0;
            int maxWaveCount = -1;
            for (int i = 0; i < elementsToAdd; i++)
            {
                bool isRelease = i >= frameCount;
                bool isFinal = i > frameCount;
                PackedFrame frame = !isRelease
                    ? PackedFrames[startingFrameIndex + i]
                    : isFinal
                        ? PackedFrames[startingFrameIndex + frameCount - 1] // nothing, modifications will be to waves
                        : PackedFrames[startingFrameIndex + frameCount - 1].DefaultAnimations(ReleaseTime);

                int waveCount = frame.WaveCount;
                entityCommandBuffer.Append(new WaveCountElement { Value = waveCount });
                entityCommandBuffer.Append(new DurationElement(frame.Duration));

                entityCommandBuffer.AppendAnimationElement<ProjectionElement>(frame.ProjectionType);
                entityCommandBuffer.AppendAnimationElement<SignalLengthElement>(frame.SignalLength);
                entityCommandBuffer.AppendAnimationElement<SampleRateElement>(frame.SampleRate);
                entityCommandBuffer.AppendAnimationElement<ThicknessElement>(frame.Thickness);

                entityCommandBuffer.AppendWaveElements(Waves, startingWaveIndex + accumulatedWaveCounts, waveCount);

                if (i < frameCount - 1)
                {
                    accumulatedWaveCounts += waveCount;
                }

                maxWaveCount = math.max(maxWaveCount, waveCount);
            }

            if (!entityHandle.UseExisting) Init<AudioKernelWrapper>(maxWaveCount, in entityHandle);
        }

        private void InitializeEntity(in EntityHandle handle)
        {
            var entity = handle.Entity;
            
            if (!handle.UseExisting)
            {
                Init<CurrentWavesElement>(MaxWaveCount, in handle);
                Init<CurrentWaveAxes>(MaxWaveCount, in handle);
                ECB.SetComponent(entity, PropertyBlocks[handle.Index - ExistingEntities.Length]);
                ECB.SetComponent(entity, GraphReferences[handle.Index - ExistingEntities.Length]);
                return;
            }
            
            ECB.RemoveComponent<UpdateRequired>(entity);
            NativeArray<CurrentWavesElement> currentWaves = WavesForEntity[entity].AsNativeArray();
            NativeArray<CurrentWaveAxes> currentWaveAxes = AxesForEntity[entity].AsNativeArray();

            ECB.SetBuffer<AllWavesElement>(entity);
            
            for (int i = 0; i < currentWaves.Length; i++)
            {
                AllWavesElement lerp = AllWavesElement.FromLerp(handle.Interpolant, currentWaves[i], currentWaveAxes[i]);
                ECB.AppendToBuffer(entity, lerp);
            }
        }

        private void SetBuffersWithInitialElements(int capacity, in EntityHandle entityHandle)
        {
            PackedFrame firstFrame = PackedFrames[0];
            ProjectionElement projection = ToBufferElement<ProjectionElement>(firstFrame.ProjectionType);
            ProjectionElement signalLength = ToBufferElement<ProjectionElement>(firstFrame.SignalLength);
            ProjectionElement sampleRate = ToBufferElement<ProjectionElement>(firstFrame.SampleRate);
            ProjectionElement thickness = ToBufferElement<ProjectionElement>(firstFrame.Thickness);
            
            InitWithFirstElement(capacity, ProjectionForEntity, in entityHandle, projection);
            InitWithFirstElement(capacity, SignalLengthForEntity, in entityHandle, signalLength);
            InitWithFirstElement(capacity, SampleRateForEntity, in entityHandle, sampleRate);
            InitWithFirstElement(capacity, ThicknessForEntity, in entityHandle, thickness);
        }

        private void InitWithFirstElement<TBufferElement, TCurrentElement>(int capacity,
                                                                           NativeArray<TCurrentElement> elements,
                                                                           in EntityHandle handle,
                                                                           TBufferElement initialElement)
            where TBufferElement : struct, IBufferElementData, IValueSettable<float>, IAnimatableSettable 
            where TCurrentElement : struct, IComponentData, IValuable<Animation<float>> 
            => Init<TBufferElement>(capacity, in handle).Append(First(elements, in handle, initialElement));
        
        private BufferHelper<TBufferElement> Init<TBufferElement>(int capacity, in EntityHandle handle)
            where TBufferElement : struct, IBufferElementData
        {
            var entity = handle.Entity;
            ECB.SetBuffer<TBufferElement>(entity).EnsureCapacity(capacity);
            return new BufferHelper<TBufferElement>
            {
                CB4E = new()
                {
                    Entity = entity,
                    CommandBuffer = ECB,
                }
            };
        }

        private TBufferElement First<TBufferElement, TComponent>(NativeArray<TComponent> dataFromEntity, in EntityHandle handle, TBufferElement _default)
            where TBufferElement : struct, IBufferElementData, IValueSettable<float>, IAnimatableSettable
            where TComponent : struct, IComponentData, IValuable<Animation<float>> 
            => handle.UseExisting
                ? new TBufferElement
                {
                    Value = dataFromEntity[handle.Index].Value.Lerp(handle.Interpolant),
                    AnimationCurve = AnimationCurve.Linear
                }
                : _default;

        [BurstCompile]
        public struct PackedFrame
        {
            public float Duration;
            public int WaveCount;
            
            public Animatable<float> ProjectionType;
            public Animatable<float> SampleRate;
            public Animatable<float> SignalLength;
            public Animatable<float> Thickness;

            public PackedFrame DefaultAnimations(float duration)
            {
                Duration = duration;
                ProjectionType = new Animatable<float>(ProjectionType.Value);
                SampleRate = new Animatable<float>(SampleRate.Value);
                SignalLength = new Animatable<float>(SignalLength.Value);
                Thickness = new Animatable<float>(Thickness.Value);
                return this;
            }
            
            public static PackedFrame Pack(in KeyFrame frame) => new()
            {
                Duration = frame.Duration,
                WaveCount = frame.Waves.Length,
                ProjectionType = new Animatable<float>((int)frame.ProjectionType.Value, frame.ProjectionType.AnimationCurve),
                SampleRate = new Animatable<float>(frame.SampleRate.Value, frame.SampleRate.AnimationCurve),
                SignalLength = frame.SignalLength,
                Thickness = frame.Thickness
            };
        };

        private readonly struct EntityHandle
        {
            public bool UseExisting { get; }
            public Entity Entity { get; }
            public float Interpolant { get; }
            public int Index { get; }

            
            public EntityHandle(int index,
                                NativeArray<Entity> existingEntities,
                                EntityCommandBuffer ecb,
                                EntityArchetype archetype,
                                float timeNow,
                                NativeArray<CurrentTimeFrame> timeFrames)
            {
                Index = index;
                UseExisting = Index < existingEntities.Length;
                Entity = UseExisting ? existingEntities[Index] : ecb.CreateEntity(archetype);
                Interpolant = UseExisting ? timeFrames[Index].Interpolate(timeNow) : 0f;
            }
        }

        private static TBufferElement ToBufferElement<TBufferElement>(Animatable<float> property)
            where TBufferElement : struct, IBufferElementData, IAnimatableSettable, IValueSettable<float>
            => new() { Value = property.Value, AnimationCurve = property.AnimationCurve };
        
        private struct CommandBufferForEntity
        {
            public EntityCommandBuffer CommandBuffer;
            public Entity Entity;
            public void Append<TBufferElement>(TBufferElement element) where TBufferElement : struct, IBufferElementData
                => CommandBuffer.AppendToBuffer(Entity, element);

            public void AppendAnimationElement<TBufferElement>(Animatable<float> property)
                where TBufferElement : struct, IBufferElementData, IAnimatableSettable, IValueSettable<float> 
                => Append(ToBufferElement<TBufferElement>(property));

            public void AppendWaveElements(NativeArray<Animatable<WaveState>> waves, int offset, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    Animatable<WaveState> waveState = waves[i + offset];
                    Append(waveState.Value.AsWaveElement(waveState.AnimationCurve));
                }
            }
        }
        
        private struct BufferHelper<TBufferElement> where TBufferElement : struct, IBufferElementData
        {
            public CommandBufferForEntity CB4E;
            public void Append(TBufferElement element) => CB4E.Append(element);
        }
    }
}
