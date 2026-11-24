Shader "ScrapTank/ProjectileBloom"
{
    Properties
    {
        [HDR] _EmissionColor ("Emission", Color) = (3, 3, 3, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _EmissionColor;
            CBUFFER_END

            float4 Vert(float4 position : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(position.xyz);
            }

            half4 Frag() : SV_Target
            {
                return _EmissionColor;
            }
            ENDHLSL
        }
    }
}
