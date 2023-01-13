// This shader is basically identical to the default VRChat mirror
// shader (FX/MirrorReflection) except with different texture names.
//
// Author: aurycat
// License: MIT

Shader "Aurycat/PortalViewNoDepth"
{
    Properties
    {
        [HideInInspector] _ViewTexL("_ViewTexL", 2D) = "black" {}
        [HideInInspector] _ViewTexR("_ViewTexR", 2D) = "black" {}
		[Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags {
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }
		Cull [_Cull]
		ZTest Less

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityInstancing.cginc"

            sampler2D _ViewTexL;
            sampler2D _ViewTexR;

            struct appdata 
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
				float4 screenPos : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.screenPos = ComputeNonStereoScreenPos(o.vertex);

                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

				if ( unity_StereoEyeIndex == 0 ) { // Left eye / desktop view
					return tex2Dproj(_ViewTexL, UNITY_PROJ_COORD(i.screenPos));
				}
				else { // Right eye
					return tex2Dproj(_ViewTexR, UNITY_PROJ_COORD(i.screenPos));
				}
            }
            ENDCG
        }
    }
}
