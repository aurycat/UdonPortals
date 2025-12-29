#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
#include "UnityInstancing.cginc"

struct appdata
{
	float4 vertex : POSITION;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct v2f
{
	float4 vertex : SV_POSITION;
	float4 screenPos : TEXCOORD0;
	UNITY_VERTEX_OUTPUT_STEREO
};

uniform float _VRChatCameraMode;
uniform float _VRChatMirrorMode;

sampler2D _ViewTexL;
sampler2D _ViewTexR;

// Only render portal for normal screen camera.
// See PortalBehaviour.cs OnWillRenderObject for explanation
bool CanRenderPortal()
{
	// Rendering normally or for a screenshot, and not rendering for a mirror
	// https://creators.vrchat.com/worlds/udon/vrc-graphics/vrchat-shader-globals/
	return (_VRChatCameraMode == 0 || _VRChatCameraMode == 2) && _VRChatMirrorMode == 0;
}

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
	// Output black when rendering in handheld camera, which would
	// otherwise look very broken. Same for rendering in mirror,
	// though mirrors are already handled by them not rendering the
	// Water layer. But add a check for mirrors for good measure.
	if (!CanRenderPortal()) {
		return half4(0,0,0,1);
	}

	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

	if (unity_StereoEyeIndex == 0) { // Left eye / desktop view
		return tex2Dproj(_ViewTexL, i.screenPos);
	}
	else { // Right eye
		return tex2Dproj(_ViewTexR, i.screenPos);
	}
}