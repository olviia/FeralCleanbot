Shader "Cleanbot/DustShell"
{
    Properties
    {
        _DustColor("Dust Color", Color) = (0.5, 0.5, 0.5, 1)
        _DustMask("Dust Mask", 2D) = "white"{}
        _MaxHeight("Max Height", Range(0, 0.5)) = 0.1
        _CellCount("Dust Density", Range (1,200)) = 50
    }
    
    SubShader
    {
        Tags{"RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }
        
        Pass
        {
            Name "DustShell"
            Tags {"LightMode" = "UniversalForward"}
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include  "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl" 

            #ifdef UNITY_INSTANCING_ENABLED
                #define DUST_SHELL_INDEX  (unity_InstanceID)
            #else
                #define DUST_SHELL_INDEX  (0u)
            #endif

            
           CBUFFER_START(UnityPerMaterial)
                float4 _DustColor;
                float _ShellCount;
                float _MaxHeight;
                float _CellCount; //dust dencity
                float4 _PaintRect;
            CBUFFER_END

            TEXTURE2D(_DustMask);
            SAMPLER(sampler_DustMask);

            struct Attributes
            {
                float4 positionOS: POSITION;
                float3 normalOS: NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS: SV_POSITION;
                  float2 maskUV : TEXCOORD0;
                  float2 worldXZ: TEXCOORD1;
                float3 normalWS: TEXCOORD2;
                float t : TEXCOORD3; //0 at the surface, 1 at the outermost shell
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);

                uint shellIndex = DUST_SHELL_INDEX;
                
                OUT.t = (float)shellIndex / max(_ShellCount - 1.0, 1.0);
                
                 float3 baseWS = TransformObjectToWorld(IN.positionOS.xyz);
                  OUT.worldXZ = baseWS.xz;
                  OUT.maskUV  = (baseWS.xz - _PaintRect.xy) / _PaintRect.zw;

                  float3 posOS = IN.positionOS.xyz + IN.normalOS * (OUT.t * _MaxHeight);
                  OUT.positionCS = TransformObjectToHClip(posOS);
                  OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                  return OUT;

            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }
            
            half4 frag(Varyings IN) : SV_Target
            {
                float2 uvCell = IN.worldXZ * _CellCount;
                float2 local = frac(uvCell) -0.5;
                float falloff = saturate (1.0 - length(local) * 2.0);
                float strand= Hash(floor(uvCell))*falloff;

                if (IN.t > strand) discard;

                float3 N = normalize(IN.normalWS);
                Light key = GetMainLight();
                float3 lit = _DustColor.rgb * (key.color * saturate(dot(N, key.direction)) + SampleSH(N));

                float ao = lerp(0.4, 1.0, IN.t);

                float coverage = SAMPLE_TEXTURE2D(_DustMask, sampler_DustMask, IN.maskUV).r;
                
                return half4(lit * ao, coverage);
            }

            ENDHLSL
        }
    }
    
}