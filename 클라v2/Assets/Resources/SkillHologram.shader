Shader "Retry/VFX/Skill Hologram"
{
 Properties { _BaseColor("Tint", Color)=(1,.85,.4,1) _Opacity("Opacity", Range(0,1))=1 }
 SubShader {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
  Pass {
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   Cull Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct Attributes { float4 positionOS:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
   struct Varyings { float4 positionCS:SV_POSITION; half4 color:COLOR; float3 world:TEXCOORD0; float2 uv:TEXCOORD1; };
   CBUFFER_START(UnityPerMaterial)
   half4 _BaseColor;
   half _Opacity;
   CBUFFER_END
   Varyings Vert(Attributes v) {
    Varyings o; o.world=TransformObjectToWorld(v.positionOS.xyz);
    o.positionCS=TransformWorldToHClip(o.world);o.color=v.color;o.uv=v.uv;return o;
   }
   half4 Frag(Varyings i):SV_Target {
    half scan=.90h+.10h*sin((i.world.y+i.world.z*.35)*72-_Time.y*3);
    half edge=smoothstep(0,.08,i.uv.y)*smoothstep(0,.08,1-i.uv.y);
    half3 tint=lerp(_BaseColor.rgb,half3(1,1,1),i.color.r*.32h);
    return half4(tint*1.15h,i.color.a*_BaseColor.a*_Opacity*scan*edge);
   }
   ENDHLSL
  }
 }
}
