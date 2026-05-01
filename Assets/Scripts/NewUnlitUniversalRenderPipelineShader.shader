Shader "Hidden/StereoStitcher180" {
    Properties {
        _LeftTex ("Left Eye", 2D) = "white" {}
        _RightTex ("Right Eye", 2D) = "white" {}
    }
    SubShader {
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _LeftTex;
            sampler2D _RightTex;

            v2f vert (appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                // Lógica Side-by-Side: 
                // Si x < 0.5, muestrear ojo izquierdo (escalando UV.x * 2)
                // Si x > 0.5, muestrear ojo derecho (escalando (UV.x - 0.5) * 2)
                if (i.uv.x < 0.5) {
                    float2 uvL = float2(i.uv.x * 2.0, i.uv.y);
                    return tex2D(_LeftTex, uvL);
                } else {
                    float2 uvR = float2((i.uv.x - 0.5) * 2.0, i.uv.y);
                    return tex2D(_RightTex, uvR);
                }
            }
            ENDCG
        }
    }
}