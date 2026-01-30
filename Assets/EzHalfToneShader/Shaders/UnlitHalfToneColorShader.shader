Shader "Unlit/BuiltinHalfToneColorShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _DotTex ("DotTexture", 2D) = "white" {}
		[HDR] _Color ("Dot Color", Color) = (0,0,0)
		_BgColor ("BG Color", Color) = (0,0,0,0.5)
        _DotRate ("DotRate", Range(0, 1000)) = 100
        _Rotation ("Rotation", Range(0, 360)) = 0
        _Contrast ("Contrast", Range(0, 5)) = 1
        _FillRate ("Fill Rate", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
			#pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : VPOS;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            sampler2D _DotTex;
            float _DotRate;
            float _Rotation;
            float _Contrast;
            float _FillRate;
            float4 _Color;
            float4 _BgColor;

            fixed4 frag (v2f i, UNITY_VPOS_TYPE screenPos : VPOS) : SV_Target
            {
                float rot = _Rotation/180*3.14159265358979323846;
                float2 sPos = float2(screenPos.x,_ScreenParams.y-screenPos.y);
                float rx = cos(rot)*sPos.x - sin(rot)*sPos.y;
                float ry = sin(rot)*sPos.x + cos(rot)*sPos.y;
                float2 rPos = float2(rx,ry);
                float rate = _ScreenParams.x/_DotRate;
        		float2 base_uv = rPos/rate;

                fixed4 col = tex2D(_MainTex, i.uv);
                float4 grayRate = saturate((col-_FillRate*2)*_Contrast+1);

                fixed4 dotCol = tex2D(_DotTex, base_uv)*_Color*col;
                float4 dotAlpha = saturate((dotCol.a - grayRate)*10);

                col = lerp(col, _BgColor, _BgColor.a);
                col = col*(1-dotAlpha)+dotCol*dotAlpha;
                col.a =1;
                return col;
            }
            ENDCG
        }
    }
}
