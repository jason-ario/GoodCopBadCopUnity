using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Pinwheel.Beam
{
    public sealed partial class VolumetricLightingRendererFeature
    {
        private class IntegrationPass : ScriptableRenderPass
        {
            private ShaderData m_ShaderData;
            private static MaterialPropertyBlock s_SharedPropertyBlock = new MaterialPropertyBlock();

            class PassData
            {
                public Material material;
                public TextureHandle cameraColorTexture;
                public TextureHandle accumulationTexture;
                public float dark;
                public float bright;
            }

            public IntegrationPass(ShaderData shaderData)
            {
                requiresIntermediateTexture = false;
                m_ShaderData = shaderData;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourcesData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("VL Integration", out var passData, new ProfilingSampler(ProfilingId.INTEGRATION_PASS)))
                {
                    passData.material = m_ShaderData.integrationMaterial;

                    VolumetricLightingVolumeComponent volume = VolumeManager.instance.stack.GetComponent<VolumetricLightingVolumeComponent>();
                    if (volume != null)
                    {
                        passData.dark = volume.dark.value;
                        passData.bright = volume.bright.value;
                    }

                    FilterPass.PassContextData accumulationPassData = frameData.Get<FilterPass.PassContextData>();
                    TextureHandle accumulationTex = accumulationPassData.accumFilteredTexture;
                    passData.accumulationTexture = accumulationTex;
                    builder.UseTexture(accumulationTex, AccessFlags.Read);

                    var cameraColorDesc = renderGraph.GetTextureDesc(resourcesData.cameraColor);
                    cameraColorDesc.name = "_CameraColor_VolumetricIntegration";
                    cameraColorDesc.clearBuffer = false;
                    TextureHandle destination = renderGraph.CreateTexture(cameraColorDesc);
                    passData.cameraColorTexture = resourcesData.cameraColor;

                    builder.UseTexture(passData.cameraColorTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));

                    //Swap cameraColor to the new temp resource (destination) for the next pass
                    resourcesData.cameraColor = destination;
                }
            }

            private static void ExecutePass(PassData passData, RasterGraphContext context)
            {
                s_SharedPropertyBlock.Clear();
                if (passData.cameraColorTexture.IsValid())
                {
                    s_SharedPropertyBlock.SetTexture(ShaderData.BLIT_TEXTURE, passData.cameraColorTexture);
                    s_SharedPropertyBlock.SetVector(ShaderData.BLIT_SCALE_BIAS, new Vector4(1, 1, 0, 0));
                }
                if (passData.accumulationTexture.IsValid())
                {
                    s_SharedPropertyBlock.SetTexture(ShaderData.ACCUMULATION_TEXTURE, passData.accumulationTexture);
                }

                s_SharedPropertyBlock.SetFloat(ShaderData.DARK, passData.dark);
                s_SharedPropertyBlock.SetFloat(ShaderData.BRIGHT, passData.bright);

                context.cmd.DrawProcedural(Matrix4x4.identity, passData.material, 0, MeshTopology.Triangles, 3, 1, s_SharedPropertyBlock);
            }
        }
    }
}