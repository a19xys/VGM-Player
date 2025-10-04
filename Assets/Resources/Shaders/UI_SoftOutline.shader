Shader "UI/Soft Outline (Built-in)"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _Color   ("Tint",   Color) = (1,1,1,1)

        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        // Grosor en PÍXELES DE PANTALLA (no texeles)
        _OutlineWidth ("Outline Width (px)", Range(0,8)) = 2.0

        // ---- Propiedades estándar de UI (no tocar) ----
        _StencilComp       ("Stencil Comparison", Float) = 8
        _Stencil           ("Stencil ID", Float) = 0
        _StencilOp         ("Stencil Operation", Float) = 0
        _StencilWriteMask  ("Stencil Write Mask", Float) = 255
        _StencilReadMask   ("Stencil Read Mask", Float) = 255
        _ColorMask         ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "UI-SoftOutline"
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.0

            // Clipping de UI
            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 tangent  : TANGENT;   // UI usa este canal para clipping
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float4 color    : COLOR;
                float2 uv       : TEXCOORD0;
                float4 worldPos : TEXCOORD1; // pos objeto para clip rect
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            fixed4 _OutlineColor;
            float  _OutlineWidth;

            float4 _ClipRect;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.pos      = UnityObjectToClipPos(v.vertex);
                o.uv       = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color    = v.color * _Color;   // tinte UI * tinte vértice
                o.worldPos = v.vertex;           // espacio de objeto para clip rect de UI
                return o;
            }

            // Muestra color del sprite (RGBA) ya tintado
            fixed4 SampleBase(float2 uv, fixed4 tint)
            {
                return tex2D(_MainTex, uv) * tint;
            }

            // Auxiliar GLOBAL: muestrea alfa desplazando 'p' píxeles de pantalla
            fixed SampleAlphaScreen(float2 baseUV, float2 uv_dx, float2 uv_dy, float2 p)
            {
                float2 uv = baseUV + p.x * uv_dx + p.y * uv_dy;
                return tex2D(_MainTex, uv).a;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Base
                fixed4 baseCol   = SampleBase(i.uv, i.color);
                fixed  baseAlpha = baseCol.a;

                // ---- Clip Rect (compatible build) ----
                #ifdef UNITY_UI_CLIP_RECT
                if (i.worldPos.x < _ClipRect.x || i.worldPos.y < _ClipRect.y ||
                    i.worldPos.x > _ClipRect.z || i.worldPos.y > _ClipRect.w)
                {
                    discard;
                }
                #endif

                // ========== OUTLINE EN ESPACIO DE PANTALLA ==========
                // Ancho deseado en píxeles de pantalla
                float r = _OutlineWidth;

                // Derivadas: delta UV por píxel de pantalla en X/Y
                float2 uv_dx = ddx(i.uv);
                float2 uv_dy = ddy(i.uv);

                // 16 direcciones alrededor del círculo para uniformidad en curvas
                const float SQRT1_2 = 0.70710678; // 1/sqrt(2)
                float2 dirs[16] = {
                    float2( 1, 0), float2(-1, 0), float2(0,  1), float2(0, -1),
                    float2( SQRT1_2,  SQRT1_2), float2(-SQRT1_2,  SQRT1_2),
                    float2( SQRT1_2, -SQRT1_2), float2(-SQRT1_2, -SQRT1_2),
                    float2( 0.9239,  0.3827), float2(-0.9239,  0.3827),
                    float2( 0.9239, -0.3827), float2(-0.9239, -0.3827),
                    float2( 0.3827,  0.9239), float2(-0.3827,  0.9239),
                    float2( 0.3827, -0.9239), float2(-0.3827, -0.9239)
                };

                fixed neighborMax = 0;
                [unroll]
                for (int k = 0; k < 16; k++)
                {
                    float2 p = r * dirs[k];
                    neighborMax = max(neighborMax, SampleAlphaScreen(i.uv, uv_dx, uv_dy, p));
                }

                // Alfa del contorno sólo donde no hay relleno base
                fixed outlineAlpha = saturate(neighborMax - baseAlpha);

                // Composición: contorno detrás del relleno
                fixed4 outlineCol = fixed4(_OutlineColor.rgb, _OutlineColor.a);
                fixed4 outCol = baseCol;
                outCol.rgb = lerp(outlineCol.rgb, outCol.rgb, baseAlpha);
                outCol.a   = baseAlpha + (1 - baseAlpha) * outlineAlpha * outlineCol.a;

                // Alpha clip opcional del pipeline UI
                #ifdef UNITY_UI_ALPHACLIP
                if (outCol.a <= 0.001) discard;
                #endif

                return outCol;
            }
            ENDCG
        }
    }
}