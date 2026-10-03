#ifndef GOODCOPBADCOP_WORLDSPACE_UI_LIGHTING_INCLUDED
#define GOODCOPBADCOP_WORLDSPACE_UI_LIGHTING_INCLUDED

// Shared diffuse lighting for world-space canvas graphics (WorldSpaceUILit, WorldSpaceTMPLit).
// Main light (with shadows) + additional lights (Forward and Forward+) + ambient SH,
// clamped to a minimum and optionally blended toward unlit.
//
// Include AFTER URP Core.hlsl / Lighting.hlsl and after declaring these material properties
// in the shader's UnityPerMaterial CBUFFER:
//   half _Wrap, _AmbientStrength, _ShadowStrength, _AdditionalLightStrength, _Emission, _BackfaceFlip
//   half4 _MinLight
//
// Required multi_compiles in the including pass:
//   _MAIN_LIGHT_SHADOWS(_CASCADE), _ADDITIONAL_LIGHTS(_VERTEX), _CLUSTER_LIGHT_LOOP,
//   _ADDITIONAL_LIGHT_SHADOWS, _SHADOWS_SOFT*

half WorldSpaceUIDiffuse(half3 n, half3 l)
{
    return saturate((dot(n, l) + _Wrap) / (1.0h + _Wrap));
}

// Canvas quads face the viewer along canvas -Z.
float3 WorldSpaceUINormal()
{
    return TransformObjectToWorldNormal(float3(0, 0, -1));
}

// Flips the normal for back-facing pixels (Cull Off) when _BackfaceFlip is on.
half3 WorldSpaceUIFaceNormal(float3 normalWS, bool isFrontFace)
{
    half3 n = normalize(normalWS);
    if (_BackfaceFlip > 0.5h && !isFrontFace)
        n = -n;
    return n;
}

half3 WorldSpaceUILighting(float3 positionWS, half3 n, float4 positionCS)
{
    // Named 'inputData' on purpose: URP's Forward+ LIGHT_LOOP_BEGIN macro references it.
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalWS   = n;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    half4 shadowMask = half4(1, 1, 1, 1);

    // Ambient
    half3 lighting = max(SampleSH(n) * _AmbientStrength, 0);

    // Main light
    Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
    half mainShadow = lerp(1.0h, mainLight.shadowAttenuation, _ShadowStrength);
    lighting += mainLight.color * (WorldSpaceUIDiffuse(n, mainLight.direction) * mainLight.distanceAttenuation * mainShadow);

    // Additional lights
    #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
        half3 extra = 0;
        uint pixelLightCount = GetAdditionalLightsCount();

        #if USE_CLUSTER_LIGHT_LOOP
        for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
        {
            CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
            Light l = GetAdditionalLight(lightIndex, positionWS, shadowMask);
            half s = lerp(1.0h, l.shadowAttenuation, _ShadowStrength);
            extra += l.color * (WorldSpaceUIDiffuse(n, l.direction) * l.distanceAttenuation * s);
        }
        #endif

        LIGHT_LOOP_BEGIN(pixelLightCount)
            Light l = GetAdditionalLight(lightIndex, positionWS, shadowMask);
            half s = lerp(1.0h, l.shadowAttenuation, _ShadowStrength);
            extra += l.color * (WorldSpaceUIDiffuse(n, l.direction) * l.distanceAttenuation * s);
        LIGHT_LOOP_END

        lighting += extra * _AdditionalLightStrength;
    #endif

    lighting = max(lighting, _MinLight.rgb);
    return lerp(lighting, half3(1, 1, 1), _Emission);
}

#endif
