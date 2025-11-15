using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Pinwheel.Beam
{
    public sealed partial class VolumetricLightingRendererFeature
    {
        private partial class AccumulationPass : ScriptableRenderPass
        {
            private ShaderData m_ShaderData;

            class PassData
            {
                public ComputeShader computeShader;
                public TextureHandle accumulationTexture;
                public TextureHandle scatteringTexture;
                public Vector3Int froxelCount;
                public Vector2Int accumulationTextureSize;
            }

            public class PassContextData : ContextItem
            {
                public TextureHandle accumulationTexture;
                public RenderTextureDescriptor accumulationRTDesc;

                public override void Reset()
                {
                    accumulationTexture = TextureHandle.nullHandle;
                    accumulationRTDesc = new RenderTextureDescriptor();
                }
            }

            public AccumulationPass(ShaderData shaderData)
            {
                requiresIntermediateTexture = false;
                m_ShaderData = shaderData;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                using (var builder = renderGraph.AddComputePass<PassData>("VL Accumulation", out var passData, new ProfilingSampler(ProfilingId.ACCUMULATION_PASS)))
                {
                    passData.computeShader = m_ShaderData.accumulationCS;

                    ScatteringPass.PassContextData scatteringData = frameData.Get<ScatteringPass.PassContextData>();
                    TextureHandle scatteringTex = scatteringData.scatteringTexture;
                    TextureDesc scatteringDesc = scatteringTex.GetDescriptor(renderGraph);
                    passData.scatteringTexture = scatteringTex;
                    builder.UseTexture(scatteringTex);

                    RenderTextureDescriptor accumRTDesc = new RenderTextureDescriptor();
                    accumRTDesc.dimension = TextureDimension.Tex2D;
                    accumRTDesc.width = scatteringDesc.width;
                    accumRTDesc.height = scatteringDesc.height;
                    accumRTDesc.volumeDepth = 1;
                    accumRTDesc.msaaSamples = 1;
                    accumRTDesc.enableRandomWrite = true;
                    accumRTDesc.depthBufferBits = 0;
                    accumRTDesc.colorFormat = RenderTextureFormat.ARGBHalf;
                    TextureDesc accumDesc = new TextureDesc(accumRTDesc);
                    accumDesc.name = "VolumetricAccumulation";
                    accumDesc.filterMode = FilterMode.Bilinear;
                    accumDesc.wrapMode = TextureWrapMode.Clamp;
                    accumDesc.useMipMap = false;
                    accumDesc.autoGenerateMips = false;
                    accumDesc.clearBuffer = false;
                    TextureHandle accumTex = renderGraph.CreateTexture(accumDesc);
                    passData.accumulationTexture = accumTex;
                    builder.UseTexture(accumTex, AccessFlags.Write);

                    Vector3Int froxelCount = new Vector3Int(scatteringDesc.width, scatteringDesc.height, scatteringDesc.slices);
                    passData.froxelCount = froxelCount;
                    passData.accumulationTextureSize = new Vector2Int(accumRTDesc.width, accumRTDesc.height);

                    builder.SetRenderFunc((PassData passData, ComputeGraphContext context) => ExecutePass(passData, context));

                    AccumulationPass.PassContextData passContextData = frameData.Create<AccumulationPass.PassContextData>();
                    passContextData.accumulationTexture = accumTex;
                    passContextData.accumulationRTDesc = accumRTDesc;
                }
            }

            private static void ExecutePass(PassData passData, ComputeGraphContext context)
            {
                context.cmd.SetComputeVectorParam(passData.computeShader, ShaderData.FROXEL_COUNT, new Vector4(passData.froxelCount.x, passData.froxelCount.y, passData.froxelCount.z));

                const int KERNEL_ACCUMULATE_NON_JITTERED = 0;
                context.cmd.SetComputeTextureParam(passData.computeShader, KERNEL_ACCUMULATE_NON_JITTERED, ShaderData.ACCUMULATION_TEXTURE, passData.accumulationTexture);
                context.cmd.SetComputeTextureParam(passData.computeShader, KERNEL_ACCUMULATE_NON_JITTERED, ShaderData.SCATTERING_TEXTURE, passData.scatteringTexture);

                context.cmd.DispatchCompute(passData.computeShader, KERNEL_ACCUMULATE_NON_JITTERED, (passData.accumulationTextureSize.x + 7) / 8, (passData.accumulationTextureSize.y + 7) / 8, 1);
            }
        }
    }
}