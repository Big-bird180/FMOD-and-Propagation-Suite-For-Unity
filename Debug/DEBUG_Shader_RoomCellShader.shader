Shader "Unlit/DEBUG_Shader_RoomCellShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Tiling ("Tiling", Float) = 1.0
        _Color ("Color Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+200" "RenderType"="Overlay" }
        Blend SrcAlpha OneMinusSrcAlpha
          Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

          
            #include "UnityCG.cginc"
            
            struct appdata_t
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };
            
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 normal : TEXCOORD1;
            };
            
            sampler2D _MainTex;
            float _Tiling;
            float4 _Color;
            
            v2f vert (appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = mul((float3x3)unity_ObjectToWorld, v.normal);
                return o;
            }
            
            fixed4 TriplanarSampling(float3 worldPos, float3 normal)
            {
                float3 blending = abs(normal);
                blending = blending / max(dot(blending, 1.0), 1e-5);
                
                float2 uvX = worldPos.zy * _Tiling;
                float2 uvY = worldPos.xz * _Tiling;
                float2 uvZ = worldPos.xy * _Tiling;
                
                return tex2Dlod(_MainTex, float4(uvX, 0, 0)) * blending.x +
                       tex2Dlod(_MainTex, float4(uvY, 0, 0)) * blending.y +
                       tex2Dlod(_MainTex, float4(uvZ, 0, 0)) * blending.z;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                return TriplanarSampling(i.worldPos, normalize(i.normal)) * _Color;
            }
            
            ENDCG
        }
    }
}
