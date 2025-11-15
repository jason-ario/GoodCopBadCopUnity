using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Pinwheel.Beam
{
    public sealed partial class VolumetricLightingRendererFeature
    {
        private class FilterPass : ScriptableRenderPass
        {
            private ShaderData m_shaderData;

            private const int PASS_BLUR_DOWNSAMPLE = 0;
            private const int PASS_COMBINE = 1;

            class PassData
            {
                public TextureHandle accumTexSrc;
                public TextureHandle accumTexFull;                                
                public TextureHandle accumTexHalf;
                public TextureHandle accumTexFourth;
                public TextureHandle accumTexEighth;
                public TextureHandle accumTexSixteenth;

                public Material material;
                public float blurRadius;
            }

            public class PassContextData : ContextItem
            {
                public TextureHandle accumFilteredTexture;

                public override void Reset()
                {
                    accumFilteredTexture = TextureHandle.nullHandle;
                }
            }

            public FilterPass(ShaderData data)
            {
                requiresIntermediateTexture = false;
                m_shaderData = data;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                AccumulationPass.PassContextData accumData = frameData.Get<AccumulationPass.PassContextData>();
                TextureHandle accumTexSrc = accumData.accumulationTexture;
                TextureDesc accumDesc = accumTexSrc.GetDescriptor(renderGraph);
                accumDesc.clearBuffer = false;

                int baseWidth = accumDesc.width;
                int baseHeight = accumDesc.height;

                TextureDesc accumTexFullDesc = new TextureDesc(accumDesc);
                accumTexFullDesc.width = baseWidth;
                accumTexFullDesc.height = baseHeight;
                accumTexFullDesc.name = "VL_Accum_Full";
                TextureHandle accumTexFull = renderGraph.CreateTexture(accumTexFullDesc);

                TextureDesc accumTexHalfDesc = new TextureDesc(accumDesc);
                accumTexHalfDesc.width = baseWidth / 2;
                accumTexHalfDesc.height = baseHeight / 2;
                accumTexHalfDesc.name = "VL_Accum_Half";
                TextureHandle accumTexHalf= renderGraph.CreateTexture(accumTexHalfDesc);

                TextureDesc accumTexFourthDesc = new TextureDesc(accumDesc);
                accumTexFourthDesc.width = baseWidth / 4;
                accumTexFourthDesc.height = baseHeight / 4;
                accumTexFourthDesc.name = "VL_Accum_Fourth";
                TextureHandle accumTexFourth = renderGraph.CreateTexture(accumTexFourthDesc);

                TextureDesc accumTexEighthDesc = new TextureDesc(accumDesc);
                accumTexEighthDesc.width = baseWidth / 8;
                accumTexEighthDesc.height = baseHeight / 8;
                accumTexEighthDesc.name = "VL_Accum_Eighth";
                TextureHandle accumTexEighth = renderGraph.CreateTexture(accumTexEighthDesc);

                TextureDesc accumTexSixteenthDesc = new TextureDesc(accumDesc);
                accumTexSixteenthDesc.width = baseWidth / 16;
                accumTexSixteenthDesc.height = baseHeight / 16;
                accumTexSixteenthDesc.name = "VL_Accum_Sixteenth";
                TextureHandle accumTexSixteenth = renderGraph.CreateTexture(accumTexSixteenthDesc);

                VolumetricLightingVolumeComponent volume = VolumeManager.instance.stack.GetComponent<VolumetricLightingVolumeComponent>();
                float blurRadius = volume.softness.value * 3f;

                using (var builder = renderGraph.AddUnsafePass<PassData>("VL Filter", out var passData, new ProfilingSampler(ProfilingId.FILTER_PASS)))
                {
                    passData.blurRadius = blurRadius;
                    passData.accumTexSrc = accumTexSrc;
                    passData.accumTexFull = accumTexFull;
                    passData.accumTexHalf = accumTexHalf;
                    passData.accumTexFourth = accumTexFourth;
                    passData.accumTexEighth = accumTexEighth;
                    passData.accumTexSixteenth = accumTexSixteenth;
                    passData.material = m_shaderData.filterCombineMaterial;

                    builder.UseTexture(accumTexSrc, AccessFlags.ReadWrite);
                    builder.UseTexture(accumTexFull, AccessFlags.ReadWrite);
                    builder.UseTexture(accumTexHalf, AccessFlags.ReadWrite);
                    builder.UseTexture(accumTexFourth, AccessFlags.ReadWrite);
                    builder.UseTexture(accumTexEighth, AccessFlags.ReadWrite);
                    builder.UseTexture(accumTexSixteenth, AccessFlags.ReadWrite);

                    builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                    {
                        CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        RenderBufferLoadAction loadAction = RenderBufferLoadAction.DontCare;
                        RenderBufferStoreAction storeAction = RenderBufferStoreAction.Store;

                        using (new ProfilingScope(cmd, new ProfilingSampler(ProfilingId.FILTER_DOWNSAMPLE)))
                        {
                            data.material.SetFloat(ShaderData.BLUR_RADIUS, data.blurRadius);
                            Blitter.BlitCameraTexture(cmd, data.accumTexSrc, data.accumTexFull, loadAction, storeAction, data.material, PASS_BLUR_DOWNSAMPLE);
                            Blitter.BlitCameraTexture(cmd, data.accumTexFull, data.accumTexHalf, loadAction, storeAction, data.material, PASS_BLUR_DOWNSAMPLE);
                            Blitter.BlitCameraTexture(cmd, data.accumTexHalf, data.accumTexFourth, loadAction, storeAction, data.material, PASS_BLUR_DOWNSAMPLE);
                            Blitter.BlitCameraTexture(cmd, data.accumTexFourth, data.accumTexEighth, loadAction, storeAction, data.material, PASS_BLUR_DOWNSAMPLE);
                            Blitter.BlitCameraTexture(cmd, data.accumTexEighth, data.accumTexSixteenth, loadAction, storeAction, data.material, 0);
                        } 
                        using (new ProfilingScope(cmd, new ProfilingSampler(ProfilingId.FILTER_COMBINE)))
                        {
                            data.material.SetTexture(ShaderData.ACCUM_TEX_FULL, data.accumTexFull);
                            data.material.SetTexture(ShaderData.ACCUM_TEX_HALF, data.accumTexHalf);
                            data.material.SetTexture(ShaderData.ACCUM_TEX_FOURTH, data.accumTexFourth);
                            data.material.SetTexture(ShaderData.ACCUM_TEX_EIGHTH, data.accumTexEighth);
                            data.material.SetTexture(ShaderData.ACCUM_TEX_SIXTEENTH, data.accumTexSixteenth);

                            Blitter.BlitCameraTexture(cmd, data.accumTexEighth, data.accumTexSrc, loadAction, storeAction, data.material, PASS_COMBINE);
                        }
                    }); 
                }

                FilterPass.PassContextData passContextData = frameData.Create<FilterPass.PassContextData>();
                passContextData.accumFilteredTexture = accumTexSrc;
            }
        }
    }
}