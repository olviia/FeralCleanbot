Shader "Hidden/Cleanbot/PaintingShader"
  {
      Properties {
          _FootprintTex ("Footprint", 2D) = "white" {}
          
      }
      SubShader
      {
          Tags { "RenderPipeline" = "UniversalPipeline" }

          Pass
          {
              Name "PaintStamp"
              Cull    Off
              ZWrite  Off
              ZTest   Always
              Blend   SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

              HLSLPROGRAM
              #pragma vertex   vert
              #pragma fragment frag
              #pragma target   3.0
              #include  "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"      

              float4 _BrushColor;
              float4 _PaintRect;    // world XZ at uv (0,0)
              float2 _BrushCenter;      // world XZ
              float  _BrushYaw;         // radians, about Y
              float  _BrushHalfSize;    // metres — half-extent of the footprint in world  
              TEXTURE2D(_FootprintTex);
              SAMPLER(sampler_FootprintTex);


              struct Attributes { float4 positionOS : POSITION; float2 uv :      TEXCOORD0; };
              struct Varyings
              {
                  float4 positionCS : SV_POSITION;
                  float2 uv :   TEXCOORD0;
              };

              Varyings vert (Attributes IN)
              {
                  Varyings OUT;
                  OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);  
                  OUT.uv = IN.uv;
                  return OUT;
              }

              half4 frag (Varyings IN) : SV_Target
              {
                  float2 world = _PaintRect.xy + IN.uv * _PaintRect.zw;;   // texel -> world XZ
                  float2 p     = world - _BrushCenter;                     // relative to brush centre
              
                  float s, c; sincos(_BrushYaw, s, c);                     // rotate world into brush frame (−yaw)
                  float2 local = float2( c * p.x + s * p.y,-s * p.x + c * p.y );
              
                  float2 buv = local / (2.0 * _BrushHalfSize) + 0.5;       // [−half,+half]-> [0,1]
                  float2 inb = step(0.0, buv) * step(buv, 1.0);            // killeverything outside the footprint
                  float  cov = SAMPLE_TEXTURE2D(_FootprintTex, sampler_FootprintTex, buv).a * inb.x * inb.y;
              
                  return half4(_BrushColor.rgb, cov * _BrushColor.a);
              }


              ENDHLSL
          }
      }
  }
