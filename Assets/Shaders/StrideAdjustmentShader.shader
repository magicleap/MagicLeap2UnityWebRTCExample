// It corrects the width of the image by adjusting for any padding (stride) that might be present in the raw image data.
// This ensures that only the visible part of the image is rendered without any visual artifacts.
// It flips the image vertically to correct the orientation, as camera images are often captured upside down.
Shader "Unlit/StrideAdjustmentShader"
{
    Properties
    {
        //main texture input
        _MainTex ("Texture", 2D) = "white" {}
        _EffectiveWidthNormalized("Effective width(Normalized)",Float)=1.0
    }
    SubShader
    {
        Tags {"RenderType"="Opaque"}
        LOD 100
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float _EffectiveWidthNormalized; 

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv=v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Flip and crop the image
                float2 flippedUV=float2(i.uv.x*_EffectiveWidthNormalized,1.0-i.uv.y);
                return tex2D(_MainTex,flippedUV);
            }
            ENDCG
        }
    }
}
