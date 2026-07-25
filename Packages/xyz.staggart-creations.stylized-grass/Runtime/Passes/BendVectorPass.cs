using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace sc.stylizedgrass.runtime
{
    public class BendVectorPass : ScriptableRenderPass
    {
        private const string profilerTag = "Render Grass Bending Vectors";
        private static ProfilingSampler profilerSampler = new ProfilingSampler(profilerTag);
        private const string profilerTagPass = "Geometry to vectors";
        private static ProfilingSampler profilerSamplerRendering = new ProfilingSampler(profilerTagPass);

        private readonly GrassRenderFeature.Settings settings;

        public const int TexelsPerMeter = 8;
        private const float FRUSTUM_MULTIPLIER = 2f;

        //Rather than culling based on layers, only render shaders with this pass tag
        private const string LightModeTag = "GrassBender";

        private static readonly int vectorMapID = Shader.PropertyToID("_GrassOffsetVectors");
        private static readonly int vectorUVID = Shader.PropertyToID("_GrassBendCoords");

        private static Vector4 rendererCoords;

        private static Matrix4x4 projection { set; get; }
        private static Matrix4x4 view { set; get; }

        private static Vector3 centerPosition;
        private static int resolution;
        public static int CurrentResolution;
        private static float orthoSize;
        private static Bounds bounds;

        private static readonly Quaternion viewRotation = Quaternion.Euler(new Vector3(-90f, 0f, 0f));
        private static readonly Vector3 viewScale = new Vector3(1, 1, -1);
        private static readonly Color neutralVector = new Color(0.5f, 0f, 0.5f, 0f);
        private static Rect viewportRect;

        //Render pass
        FilteringSettings m_FilteringSettings;
        RenderStateBlock m_RenderStateBlock;

        private readonly List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>()
        {
            new ShaderTagId(LightModeTag)
        };

        private static readonly Plane[] frustrumPlanes = new Plane[6];

        public BendVectorPass(ref GrassRenderFeature.Settings settings)
        {
            this.settings = settings;
            m_FilteringSettings = new FilteringSettings(RenderQueueRange.all, -1);
            m_RenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }

        private static int CalculateResolution(float size)
        {
            int res = Mathf.RoundToInt(size * TexelsPerMeter);
            res = Mathf.NextPowerOfTwo(res);
            res = Mathf.Clamp(res, 256, 4096);

            return res;
        }

        private void SetupProjection(RasterGraphContext context, Camera camera, int renderResolution)
        {
            RasterCommandBuffer cmd = context.cmd;

            centerPosition = camera.transform.position + (camera.transform.forward * orthoSize);

            centerPosition = StabilizeProjection(centerPosition, (orthoSize * 2f) / renderResolution);
            bounds = new Bounds(centerPosition, Vector3.one * orthoSize);

            centerPosition -= (Vector3.up * orthoSize * FRUSTUM_MULTIPLIER);

            projection = Matrix4x4.Ortho(-orthoSize, orthoSize, -orthoSize, orthoSize, 0.03f,
                orthoSize * FRUSTUM_MULTIPLIER * 2f);

            view = Matrix4x4.TRS(centerPosition, viewRotation, viewScale).inverse;

            cmd.SetViewProjectionMatrices(view, projection);

            viewportRect.width = renderResolution;
            viewportRect.height = renderResolution;
            cmd.SetViewport(new Rect(0, 0, renderResolution, renderResolution));

            GeometryUtility.CalculateFrustumPlanes(projection * view, frustrumPlanes);

            //Position/scale of projection. Converted to a UV in the shader
            rendererCoords.x = 1f - bounds.center.x - 1f + orthoSize;
            rendererCoords.y = 1f - bounds.center.z - 1f + orthoSize;
            rendererCoords.z = orthoSize * 2f;
            rendererCoords.w = 1f; //Enable in shader

            cmd.SetGlobalVector(vectorUVID, rendererCoords);
        }

        //Important to snap the projection to the nearest texel. Otherwise pixel swimming is introduced when moving, due to bilinear filtering
        private static Vector3 StabilizeProjection(Vector3 pos, float texelSize)
        {
            float Snap(float coord, float cellSize) =>
                Mathf.FloorToInt(coord / cellSize) * (cellSize) + (cellSize * 0.5f);

            return new Vector3(Snap(pos.x, texelSize), Snap(pos.y, texelSize), Snap(pos.z, texelSize));
        }

        private class PassData
        {
            public TextureHandle renderTarget;
            public RendererListHandle rendererList;
            public Camera camera;
            public int resolution;
        }

        private class FrameData : ContextItem
        {
            public TextureHandle grassVectors;
            
            public override void Reset()
            {
                grassVectors = TextureHandle.nullHandle;
            }
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            orthoSize = Mathf.Max(5, settings.bendingRenderRange) * 0.5f;
            resolution = CalculateResolution(orthoSize);
            CurrentResolution = resolution;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(profilerTag, out var passData, profilerSampler))
            {
                //Create render texture descriptor
                RenderTextureDescriptor desc = new RenderTextureDescriptor(resolution, resolution,
                    UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat, 0);
                desc.msaaSamples = 1;

                //Import or create the texture handle
                TextureHandle renderTarget = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc,
                    "GrassOffsetVectors", false, FilterMode.Bilinear, TextureWrapMode.Clamp);

                FrameData passFrameData = frameData.GetOrCreate<FrameData>();
                passFrameData.grassVectors = renderTarget;
                
                passData.renderTarget = renderTarget;
                passData.camera = cameraData.camera;
                passData.resolution = resolution;

                //Setup renderer list
                DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(m_ShaderTagIdList,
                    universalRenderingData, cameraData, lightData,
                    SortingCriteria.RenderQueue | SortingCriteria.SortingLayer | SortingCriteria.QuantizedFrontToBack);
                drawingSettings.enableInstancing = !UniversalRenderPipeline.asset.useSRPBatcher;
                drawingSettings.perObjectData = PerObjectData.None;

                var renderListParams = new RendererListParams(universalRenderingData.cullResults, drawingSettings,
                    m_FilteringSettings);
                passData.rendererList = renderGraph.CreateRendererList(renderListParams);

                builder.UseRendererList(passData.rendererList);
                builder.SetRenderAttachment(renderTarget, 0);
                builder.AllowGlobalStateModification(true);
                builder.SetGlobalTextureAfterPass(renderTarget, vectorMapID);

                //TODO: Count benders active
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    using (new ProfilingScope(context.cmd, profilerSamplerRendering))
                    {
                        SetupProjection(context, data.camera, data.resolution);
                        context.cmd.DrawRendererList(data.rendererList);
                    }
                });
            }
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.SetGlobalVector(vectorUVID, Vector4.zero);
        }

        public void Dispose()
        {
            DisableBendingShaderPath();
        }

        public static void DisableBendingShaderPath()
        {
            Shader.SetGlobalVector(vectorUVID, Vector4.zero);
        }

        //Using data only from the matrices, to ensure what you're seeing closely represents them
        public static void DrawOrthographicViewGizmo()
        {
            Gizmos.matrix = Matrix4x4.identity;

            float near = frustrumPlanes[4].distance;
            float far = frustrumPlanes[5].distance;
            float height = near + far;

            Vector3 position = new Vector3(view.inverse.m03, view.inverse.m13 + (height * 0.5f), view.inverse.m23);
            Vector3 scale = new Vector3((frustrumPlanes[0].distance + frustrumPlanes[1].distance), height,
                frustrumPlanes[2].distance + frustrumPlanes[3].distance);

            //Gizmos.DrawSphere(new Vector3(view.inverse.m03, view.inverse.m13 + height, view.inverse.m23), 1f);
            Gizmos.DrawWireCube(position, scale);
            Gizmos.color = Color.white * 0.25f;
            Gizmos.DrawCube(position, scale);
        }
        
#if !UNITY_6000_4_OR_NEWER
#pragma warning disable CS0672
#pragma warning disable CS0618
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { }
#pragma warning restore CS0672
#pragma warning restore CS0618
#endif

        internal class DebugPass : ScriptableRenderPass
        {
            private class DebugData
            {
                public TextureHandle renderTarget;
            }
            
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                using (var builder = renderGraph.AddRasterRenderPass<DebugData>("Debug Grass Bending", out var passData, new ProfilingSampler("Debug Grass Bending")))
                {
                    FrameData passFrameData = frameData.Get<FrameData>();
                    
                    passData.renderTarget = passFrameData.grassVectors;
                    builder.UseTexture(passData.renderTarget, AccessFlags.Read);
                        
                    //Drawing to the backbuffer/active color attachment
                    builder.SetRenderAttachment(frameData.Get<UniversalResourceData>().activeColorTexture, 0);

                    builder.SetRenderFunc((DebugData data, RasterGraphContext context) =>
                    {
                        float size = Screen.height * 0.33f;
                        Vector2 debugSize = new Vector2(size, size);
                        
                        //Top-left corner
                        Rect viewport = new Rect(10, Screen.height - debugSize.y - 10f, debugSize.x, debugSize.y);
                            
                        context.cmd.SetViewport(viewport);
                        Blitter.BlitTexture(context.cmd, data.renderTarget, new Vector4(1, 1, 0, 0), 0, true);
                    });
                }
            }
        }
    }
}