Shader "MarbleArena/PrismGlass"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.3,0.8,1,0.18)
        _Accent ("Accent", Color) = (0.6,0.9,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Glass"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            half4 _Accent;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; half fog:TEXCOORD2; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 frag(Varyings input):SV_Target
            {
                half3 normal = normalize(input.normalWS);
                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half fresnel = pow(1 - saturate(dot(normal, view)), 3.2);
                half3 reflection = reflect(-view, normal);
                half3 environment = GlossyEnvironmentReflection(reflection, input.positionWS, 0.08, 1, GetNormalizedScreenSpaceUV(input.positionCS));
                Light light = GetMainLight();
                half3 halfway = normalize(view + light.direction);
                half spec = pow(saturate(dot(normal, halfway)), 120) * 0.9;
                half ribbon = pow(saturate(dot(reflection, normalize(half3(-0.25,0.85,0.3)))), 30);
                half3 color = _Tint.rgb * 0.72 + environment * 0.32 + fresnel * _Tint.rgb * 0.16 + spec * light.color + ribbon * 0.22;
                color = MixFog(color,input.fog);
                half alpha = saturate(_Tint.a + fresnel * 0.45 + spec * 0.3 + ribbon * 0.12);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
