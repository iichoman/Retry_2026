Shader "Retry/E Flagstone" {
 Properties { _BaseColor("Stone",Color)=(.25,.27,.25,1) }
 SubShader { Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
 Pass { Tags {"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile _ _ADDITIONAL_LIGHTS
 #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;};
 struct V {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;};
 CBUFFER_START(UnityPerMaterial) half4 _BaseColor; CBUFFER_END
 V vert(A a){V o;o.world=TransformObjectToWorld(a.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(a.normalOS);return o;}
 float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 float noise(float2 p){float2 a=floor(p),b=frac(p);b=b*b*(3-2*b);return lerp(lerp(hash(a),hash(a+float2(1,0)),b.x),lerp(hash(a+float2(0,1)),hash(a+1),b.x),b.y);}
 half4 frag(V i):SV_Target {
 // Laid flagstones: staggered courses, mixed slab lengths and restrained chipped seams.
 float row=floor(i.world.z/1.35),slabWidth=2.0+hash(float2(row,37))*.85;
 float px=i.world.x/slabWidth+hash(float2(row,91))*.8;
 float2 cell=float2(floor(px),row),f=float2(frac(px),frac(i.world.z/1.35));
 float split=step(.72,hash(cell+17));float seamX=min(f.x,1-f.x)*slabWidth;
 seamX=min(seamX,lerp(10,abs(f.x-(.4+.2*hash(cell+31)))*slabWidth,split));
 float edge=min(seamX,min(f.y,1-f.y)*1.35);
 float chips=(noise(i.world.xz*9)-.5)*.018;
 float aa=max(fwidth(edge),.002);float grout=smoothstep(.012-aa,.027+aa,edge+chips);
 float variation=.89+hash(cell)*.17;float wear=(noise(i.world.xz*.38)-.5)*.11;
 float grain=(noise(i.world.xz*24)-.5)*.025/(1+length(fwidth(i.world.xz))*24);
 float bevel=smoothstep(.025,.065,edge);
 half3 albedo=_BaseColor.rgb*(variation+wear+grain)*lerp(.4,lerp(.88,1,bevel),grout);
 Light light=GetMainLight(TransformWorldToShadowCoord(i.world));half3 n=normalize(i.normal);
 half3 lit=SampleSH(n)+light.color*saturate(dot(n,light.direction))*light.shadowAttenuation;
 #ifdef _ADDITIONAL_LIGHTS
 InputData inputData=(InputData)0;inputData.positionWS=i.world;inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
 #if USE_CLUSTER_LIGHT_LOOP
 for(uint lightIndex=0;lightIndex<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS);lightIndex++){Light l=GetAdditionalLight(lightIndex,i.world);lit+=l.color*saturate(dot(n,l.direction))*l.distanceAttenuation;}
 #endif
 uint count=GetAdditionalLightsCount();
 LIGHT_LOOP_BEGIN(count)
 Light l=GetAdditionalLight(lightIndex,i.world);lit+=l.color*saturate(dot(n,l.direction))*l.distanceAttenuation;
 LIGHT_LOOP_END
 #endif
 return half4(albedo*lit,1);}
 ENDHLSL
 }
 UsePass "Universal Render Pipeline/Lit/ShadowCaster"
 }
}
