#ifndef MY_CUSTOM_FUNCTIONS
#define MY_CUSTOM_FUNCTIONS

void HalfToneMono_float(float2 uv, float2 screenPos,
    float4 _BufferCol,
    float4 _DotCol,
    float _Contrast,
    float _FillRate,
    float4 _Color,
    float4 _BgColor,
    SamplerState _State,
    out float4 col)
{
    //SamplerState sampler_point_clamp;
    col = _BufferCol; // SAMPLE_TEXTURE2D(_MainTex, _State, uv); //float4(0,1,1,1);
    float4 dotCol = _DotCol * _Color;
    float4 grayRate = saturate((col - _FillRate * 2) * _Contrast * 2 + 1);
    float4 dotAlpha = saturate((dotCol.a - grayRate) * _Contrast);

    col = lerp(col, _BgColor, _BgColor.a);
    col = col * (1 - dotAlpha) + dotCol * dotAlpha;
    col.a = 1;
}

void HalfToneColor_float(float2 uv, float2 screenPos,
    float4 _BufferCol,
    float4 _DotCol,
    float _Contrast,
    float _FillRate,
    float4 _Color,
    float4 _BgColor,
    SamplerState _State,
    out float4 col)
{
    //SamplerState sampler_point_clamp;
    col = _BufferCol; // SAMPLE_TEXTURE2D(_MainTex, _State, uv); //float4(0,1,1,1);
	float4 dotCol = _DotCol * _Color;
    float4 grayRate = saturate((col - _FillRate * 2) * _Contrast*2 + 1);
    float4 dotAlpha = saturate((dotCol.a - grayRate) * _Contrast);

    col = lerp(col, _BgColor, _BgColor.a);
    col = col * (1 - dotAlpha) + dotCol * dotAlpha;
    col.a = 1;
}

void DitherColor_float(
    float4 _BufferCol,
    float4 _DotCol,
    float _ToneNum,
    float _FillRate,
    float4 _Color,
    out float4 col)
{
    col = _BufferCol;
    int maskShift = 4;
    int ir = (int)(_BufferCol.r * _ToneNum) >> maskShift;
    int ig = (int)(_BufferCol.g * _ToneNum) >> maskShift;
    int ib = (int)(_BufferCol.b * _ToneNum) >> maskShift;
    float fr = (float)(ir << maskShift) / _ToneNum;
    float fg = (float)(ig << maskShift) / _ToneNum;
    float fb = (float)(ib << maskShift) / _ToneNum;

    // diff
    float dr = (_BufferCol.r - fr) * (1 << maskShift);
    float dg = (_BufferCol.g - fg) * (1 << maskShift);
    float db = (_BufferCol.b - fb) * (1 << maskShift);

    float3 dotCol = _DotCol * _BufferCol.rgb * (1 - float3(dr, dg, db)) * 0.5;
    dotCol = dotCol * _DotCol.a * _Color.rgb * _Color.a * 2;

    col.rgb = saturate(float3(fr, fg, fb) - dotCol.rgb);
}

#endif // MY_CUSTOM_FUNCTIONS
