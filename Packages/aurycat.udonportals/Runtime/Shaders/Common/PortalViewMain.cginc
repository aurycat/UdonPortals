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

sampler2D _ViewTexL;
sampler2D _ViewTexR;

#if UDONPORTALS_EXPERIMENTAL_RENDERING_FOR_NON_SCREEN_CAMERA
	// Note: These _VRChat uniforms are not set by ClientSim,
	// so this doesn't work in the editor without extra steps.
	uniform float3 _VRChatScreenCameraPos;
	uniform float4 _VRChatScreenCameraRot;
	uniform float4x4 _Udon_UdonPortals_ScreenProjectionMatrix;
	uniform float _Udon_UdonPortals_RenderOK;

	// Only render portal for normal screen camera.
	// See PortalBehaviour.cs OnWillRenderObject for explanation
	bool CanRenderPortal()
	{
		return _Udon_UdonPortals_RenderOK == 1;
	}

	// Rotate quaternion 180 degrees on the Z axis.
	// I don't understand why this is needed.
	float4 RotZ180(float4 quat) {
		return float4(quat.y, -quat.x, quat.w, -quat.z);
	}

	// https://gist.github.com/mattatz/40a91588d5fb38240403f198a938a593
	float4x4 QuaternionToMatrix(float4 quat)
	{
		float4x4 m = float4x4(float4(0, 0, 0, 0), float4(0, 0, 0, 0), float4(0, 0, 0, 0), float4(0, 0, 0, 0));

		float x = quat.x, y = quat.y, z = quat.z, w = quat.w;
		float x2 = x + x, y2 = y + y, z2 = z + z;
		float xx = x * x2, xy = x * y2, xz = x * z2;
		float yy = y * y2, yz = y * z2, zz = z * z2;
		float wx = w * x2, wy = w * y2, wz = w * z2;

		m[0][0] = 1.0 - (yy + zz);
		m[0][1] = xy - wz;
		m[0][2] = xz + wy;

		m[1][0] = xy + wz;
		m[1][1] = 1.0 - (xx + zz);
		m[1][2] = yz - wx;

		m[2][0] = xz - wy;
		m[2][1] = yz + wx;
		m[2][2] = 1.0 - (xx + yy);

		m[3][3] = 1.0;

		return m;
	}

	float4 ViewTexScreenPos(float4 objVertex, float4 clipVertex)
	{
		// When we don't have a real render of the portal, fake the render by
		// obtaining the same screenPos coords as the screen camera render.
		// It'll look weird and flat, but usually better the alternative of
		// solid black! To do that, we need the screen camera's view and projection
		// matrices.
		if (!CanRenderPortal()) {
			// Compute view matrix of screen camera
			float4x4 invRot = transpose(QuaternionToMatrix(RotZ180(_VRChatScreenCameraRot)));
			float4x4 invPos = float4x4(
				1,  0,  0, -_VRChatScreenCameraPos.x,
				0,  1,  0, -_VRChatScreenCameraPos.y,
				0,  0,  1, -_VRChatScreenCameraPos.z,
				0,  0,  0, 1
			);
			float4x4 screenV = mul(invRot, invPos); // inverse of mul(pos,rot)

			// Get projection matrix of screen camera
			float4x4 screenP = _Udon_UdonPortals_ScreenProjectionMatrix;
			// This replicates the behavior of GL.GetGPUProjectionMatrix(p, true).
			// Needed because _Udon_UdonPortals_ScreenProjectionMatrix comes
			// directly from Camera.projectionMatrix, which is always in OpenGL
			// projection matrix format.
			// This conversion is probably wrong at least somewhat, but
			// seems to be sufficient for VRChat needs! I don't know what
			// GetGPUProjectionMatrix does in all cases; I created this
			// conversion code by observing GetGPUProjectionMatrix's behavior.
			#if UNITY_REVERSED_Z
				screenP[1] *= -1;
				screenP[2] *= screenP[2]*-0.5 + screenP[3]*0.5;
			#endif

			clipVertex = mul(screenP, mul(screenV, mul(UNITY_MATRIX_M, objVertex)));
		}

		return ComputeNonStereoScreenPos(clipVertex);
	}
#else // ! UDONPORTALS_EXPERIMENTAL_RENDERING_FOR_NON_SCREEN_CAMERA
	uniform float _VRChatCameraMode;
	uniform float _VRChatMirrorMode;

	// Only render portal for normal screen camera.
	// See PortalBehaviour.cs OnWillRenderObject for explanation
	bool CanRenderPortal()
	{
		// Rendering normally or for a screenshot, and not rendering for a mirror
		// https://creators.vrchat.com/worlds/udon/vrc-graphics/vrchat-shader-globals/
		return (_VRChatCameraMode == 0 || _VRChatCameraMode == 2) && _VRChatMirrorMode == 0;
	}

	float4 ViewTexScreenPos(float4 objVertex, float4 clipVertex)
	{
		return ComputeNonStereoScreenPos(clipVertex);
	}
#endif

v2f vert(appdata v)
{
	v2f o;

	UNITY_SETUP_INSTANCE_ID(v);
	UNITY_INITIALIZE_OUTPUT(v2f, o);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

	o.vertex = UnityObjectToClipPos(v.vertex);
	o.screenPos = ViewTexScreenPos(v.vertex, o.vertex);
	return o;
}

half4 frag(v2f i) : SV_Target
{
	// Output black when rendering in handheld camera, which would
	// otherwise look very broken. Same for rendering in mirror,
	// though mirrors are already handled by them not rendering the
	// Water layer. But add a check for mirrors for good measure.
	if (!CanRenderPortal()) {
		#if UDONPORTALS_EXPERIMENTAL_RENDERING_FOR_NON_SCREEN_CAMERA
			float2 pos = i.screenPos / i.screenPos.w;
			if (any(pos < 0 || pos > 1)) {
				return half4(0,0,0,1);
			}
			return tex2D(_ViewTexL, pos);
		#else // ! UDONPORTALS_EXPERIMENTAL_RENDERING_FOR_NON_SCREEN_CAMERA
			return half4(0,0,0,1);
		#endif
	}

	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

	if (unity_StereoEyeIndex == 0) { // Left eye / desktop view
		return tex2Dproj(_ViewTexL, i.screenPos);
	}
	else { // Right eye
		return tex2Dproj(_ViewTexR, i.screenPos);
	}
}