Shader "Retry/VFX/Combat Unlit"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _Intensity ("Intensity", Range(0.2,3)) = 1.1
        _ClipRoom ("Clip to server room", Float) = 0
        _RoomMinMax ("Room minX minZ maxX maxZ", Vector) = (-100000,-100000,100000,100000)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Intensity;
                half _ClipRoom;
                float4 _RoomMinMax;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionHCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; float2 worldXZ:TEXCOORD1; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(world);
                output.worldXZ = world.xz;
                output.uv = TRANSFORM_TEX(input.uv,_BaseMap);
                output.color = input.color;
                return output;
            }
            half4 frag(Varyings input):SV_Target
            {
                if (_ClipRoom > 0.5)
                {
                    clip(input.worldXZ - _RoomMinMax.xy);
                    clip(_RoomMinMax.zw - input.worldXZ);
                }
                half4 result = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv) * input.color * _BaseColor;
                result.rgb *= _Intensity;
                return result;
            }
            ENDHLSL
        }
    }
}
