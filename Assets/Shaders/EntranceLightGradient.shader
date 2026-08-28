Shader "HauntedHouse/EntranceLightGradient"
{
    Properties
    {
        [HDR] _EmissionColor("Emission Color", Color) = (8, 8, 8, 1)
        _Intensity("Intensity", Range(0, 20)) = 8
        _FadePower("Fade Power", Range(0.25, 8)) = 2.5
        _EdgeSoftness("Edge Softness", Range(0.01, 0.5)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "Entrance Light Gradient"
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _EmissionColor;
                half _Intensity;
                half _FadePower;
                half _EdgeSoftness;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // UV.y가 정문(-X)에서 1, 실내(+X)에서 0이 되도록 메쉬를 구성한다.
                half depthFade = pow(saturate(input.uv.y), _FadePower);

                // 좌우 가장자리를 부드럽게 지워 직사각형 경계가 드러나지 않게 한다.
                half edgeDistance = min(input.uv.x, 1.0h - input.uv.x);
                half sideFade = smoothstep(0.0h, _EdgeSoftness, edgeDistance);
                half alpha = depthFade * sideFade;

                return half4(_EmissionColor.rgb * _Intensity * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
