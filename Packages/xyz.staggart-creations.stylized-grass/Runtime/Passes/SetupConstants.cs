using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace sc.stylizedgrass.runtime
{
    public class SetupConstants : ScriptableRenderPass
    {
        private readonly GrassRenderFeature.Settings settings;
                    
        private static readonly int _CameraForwardVector = Shader.PropertyToID("_CameraForwardVector");
        private static Vector4 cameraForwardVector;
        private static readonly int _TerrainDetailDistance = Shader.PropertyToID("_TerrainDetailDistance");
        
        private readonly int _DitheringScaleOffset = Shader.PropertyToID("_DitheringScaleOffset");
        private static Vector4 ditheringScaleOffset;
        
        private readonly int _DitheringNoise = Shader.PropertyToID("_DitheringNoise");
        private RTHandle ditheringTextureHandle;
        
        public SetupConstants(ref GrassRenderFeature.Settings settings)
        {
            this.settings = settings;
        }

        private class PassData
        {
            public Vector4 cameraForwardVector;
            public Vector4 ditheringScaleOffset;
            public float detailRenderRange;
            public TextureHandle ditheringNoise;
            public RTHandle ditheringTextureHandle;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Setup Grass Constants", out var passData))
            {
                if (settings.forwardPerspectiveCorrection)
                {
                    //Pass the camera's forward vector for perspective correction
                    //This must be explicit, since during the shadow casting pass, the projection is that of the light (not the camera)
                    cameraForwardVector = cameraData.camera.transform.forward;
                    cameraForwardVector.w = 1f;
                }
                else
                {
                    cameraForwardVector.w = 0f;
                }

                var detailRenderRange = -1f;
                if (settings.fadeAtMaxRenderingRange)
                {
                    Terrain terrain = Terrain.activeTerrain;

                    if (terrain)
                    {
                        detailRenderRange = terrain.detailObjectDistance;
                    }
                }
                
                passData.detailRenderRange = detailRenderRange;
                passData.cameraForwardVector = cameraForwardVector;

                if (settings.ditheringNoise)
                {
                    RenderTextureDescriptor ditherNoiseDescriptor = new RenderTextureDescriptor(settings.ditheringNoise.width, settings.ditheringNoise.height, GraphicsFormat.R8_UNorm, 0, 0);
                    
                    if (ditheringTextureHandle?.rt != settings.ditheringNoise)
                    {
                        ditheringTextureHandle?.Release();
                        ditheringTextureHandle = RTHandles.Alloc(settings.ditheringNoise);
                    }
                    passData.ditheringNoise = renderGraph.ImportTexture(ditheringTextureHandle);
                    builder.UseTexture(passData.ditheringNoise, AccessFlags.None);

                    ditheringScaleOffset.x = 1f / settings.ditheringNoise.width;
                    ditheringScaleOffset.y = 1f / settings.ditheringNoise.height;

                    if (settings.animateDithering && cameraData.antialiasing == AntialiasingMode.TemporalAntiAliasing)
                    {
                        //Jitter the UV coordinates to perform stochastic sampling of the dithering pattern
                        ditheringScaleOffset.z = (Random.value * 2f - 1f) * ditheringScaleOffset.x;
                        ditheringScaleOffset.w = (Random.value * 2f - 1f) * ditheringScaleOffset.y;
                    }

                    passData.ditheringScaleOffset = ditheringScaleOffset;
                }

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    Execute(context.cmd, data);
                });
            }
        }

        void Execute(RasterCommandBuffer cmd, PassData data)
        {
            cmd.SetGlobalVector(_CameraForwardVector, data.cameraForwardVector);
            cmd.SetGlobalFloat(_TerrainDetailDistance, data.detailRenderRange);

            //if (data.ditheringNoise.IsValid())
            {
                cmd.SetGlobalTexture(_DitheringNoise, data.ditheringNoise);
                cmd.SetGlobalVector(_DitheringScaleOffset, data.ditheringScaleOffset);
            }
        }

        public void Dispose()
        {
            ditheringTextureHandle?.Release();
        }
        
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.SetGlobalFloat(_TerrainDetailDistance, -1f);
            cmd.SetGlobalVector(_CameraForwardVector, Vector4.zero);
            cmd.SetGlobalVector(_DitheringScaleOffset, Vector4.zero);
        }
        
#if !UNITY_6000_4_OR_NEWER
#pragma warning disable CS0672
#pragma warning disable CS0618
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { }
#pragma warning restore CS0672
#pragma warning restore CS0618
#endif
    }
}