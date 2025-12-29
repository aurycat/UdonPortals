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
		ZClip False

		Pass
		{
			CGPROGRAM
			#include "Common/PortalViewMain.cginc"
			ENDCG
		}

		Pass
		{
			Tags { "LightMode"="ShadowCaster" }

			CGPROGRAM
			#include "Common/PortalViewShadow.cginc"
			ENDCG
		}
	}
}
