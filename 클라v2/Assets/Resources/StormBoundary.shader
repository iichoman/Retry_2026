Shader "Retry/StormBoundary"
{
    Properties { _Color("Color",Color)=(0.12,0.52,1,0.3) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            CBUFFER_END
            Varyings vert(Attributes input) { Varyings o; o.positionCS=TransformObjectToHClip(input.positionOS.xyz);o.uv=input.uv;return o; }
            half4 frag(Varyings i):SV_Target
            {
                float streak=pow(saturate(sin(i.uv.x*500+sin(i.uv.y*30+_Time.y*2))),18);
                half4 c=_Color;c.rgb+=streak*.5;c.a*=.75+streak*.5;return c;
            }
            ENDHLSL
        }
    }
}
