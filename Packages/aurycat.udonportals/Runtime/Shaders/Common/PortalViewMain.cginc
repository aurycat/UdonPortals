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
uniform float4x4 _ScreenProjectionMatrix;
uniform float4x4 _ScreenViewMatrix;
uniform float _RenderOK;

v2f vert(appdata v)
{
	v2f o;

	UNITY_SETUP_INSTANCE_ID(v);
	UNITY_INITIALIZE_OUTPUT(v2f, o);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

	o.vertex = UnityObjectToClipPos(v.vertex);

	// When we don't have a real render of the portal, fake the render by
	// obtaining the same screenPos coords as the screen camera render.
	// It'll look weird and flat, but usually better the alternative of
	// solid black! To do that, we need the screen camera's view and projection
	// matrices.
	float4 clipVertex = o.vertex;
	if (!_RenderOK) {
		float4x4 proj = _ScreenProjectionMatrix;
		// This replicates the behavior of GL.GetGPUProjectionMatrix(p, true).
		// Needed because _ScreenProjectionMatrix comes
		// directly from Camera.projectionMatrix, which is always in OpenGL
		// projection matrix format.
		// This conversion is probably wrong at least somewhat, but
		// seems to be sufficient for VRChat needs! I don't know what
		// GetGPUProjectionMatrix does in all cases; I created this
		// conversion code by observing GetGPUProjectionMatrix's behavior.
		#if UNITY_REVERSED_Z
			proj[1] *= -1;
			proj[2] *= proj[2]*-0.5 + proj[3]*0.5;
		#endif

		clipVertex = mul(proj, mul(_ScreenViewMatrix, mul(UNITY_MATRIX_M, v.vertex)));
	}
	o.screenPos = ComputeNonStereoScreenPos(clipVertex);

	return o;
}

half4 frag(v2f i) : SV_Target
{
	if (!_RenderOK) {
		// Render black if out of range, or if rendering opposite direction (w < 0)
		// Square out-of-range would be `any(pos < 0 || pos > 1)` but I think
		// circular out-of-range looks nicer.
		float2 pos = i.screenPos.xy / i.screenPos.w;
		float2 d = abs(pos - 0.5);
		float rad = dot(d,d)*4;
		if (rad > 1 || i.screenPos.w < 0) {
			return half4(0,0,0,1);
		}
	}

	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

	if (unity_StereoEyeIndex == 0) { // Left eye / desktop view
		return tex2Dproj(_ViewTexL, i.screenPos);
	}
	else { // Right eye
		return tex2Dproj(_ViewTexR, i.screenPos);
	}
}