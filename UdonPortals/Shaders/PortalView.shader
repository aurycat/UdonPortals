// This portal shader lets you stick your hand into the portal without it
// getting 'cut off' like it normally would if you stick your hand into a
// wall. It gives the portal a more seamless feeling.
//
// It has some limitations: surfaces nearby behind the portal (e.g. the wall
// the portal is on) need to be on a lower render queue than this, otherwise
// they will render over the portal just like your hand would, and avatars
// need to be on a higher render queue than this.
//
// Author: aurycat
// License: MIT
//
// ============================================================================
//  How it works
//  (Very verbose so I remember how it works when I come back to it later...)
// ============================================================================
//
// To create the illusion of being able to "reach through" the portal, we want
// certain things (e.g. avatars, pickups) to render over the portal, while
// other things (e.g. the wall behind the portal) to be occluded by the portal.
// However, we can't just always draw walls on behind and avatars on top:
//
//  - Walls should interact normally with the portal, as if it were a normal
//    object. Walls behind the portal get occluded, walls in front occlude.
//    Usual 'ZTest Less'.
//
//  - Avatars, etc, need to render on top of the portal, so that a hand stuck
//    through the portal will not get cut off. BUT! Things that are far behind
//    the portal, like a player in another room, shouldn't render on top of
//    the portal. We only want to render avatars on top when they are within a
//    certain distance behind the portal.
//
// Achieving both of these requirements at once is tricky.
// Here is the method I've used:
//
// 1. Walls and other things that should interact normally with the portal go
//    on a render queue below the portal (they render before this shader).
//    Avatars and other things that should be able to reach through the portal
//    go on a render queue above the portal (they render after this shader).
//    On its own this has no effect (for opaque things) because of z-testing.
//
// 2. This portal shader is set to `ZTest Always`. That means we will always
//    render on top of walls, even if they are physically in front of the
//    portal. However, avatars rendering after will z-test with this normally,
//    so they act like nothing changed.
//
// 3. We take the depth (distance from camera) that would have normally been
//    written by this fragment to the depth buffer and "move it back" a little
//    bit. As if the portal surface has become "inset" into the wall. Therefore,
//    avatars now render over the portal but only up to a certain distance
//    behind the portal.
//
//    This "inset" depth is why `ZTest Always` is needed. It's important to
//    note here that z-testing happens AFTER the fragment shader. So if we
//    write out a new depth value that's inset into the wall, and then normal
//    z-testing happens afterwards, then this fragment will just always fail
//    the z-test with the wall and get discarded. By using `ZTest Always`, we
//    ensure this fragment always gets drawn unless we explicitly discard it.
//
// 4. Avatars now work, but walls physically in front of the portal still get
//    rendered over because of the `ZTest Always`. To fix that, we do a manual
//    z-test! It's the exact same z-test that would have been done if we had
//    used `ZTest Less` and not written out a custom depth.
//
// ============================================================================

Shader "Aurycat/PortalView"
{
    Properties
    {
        [HideInInspector] _ViewTexL("_ViewTexL", 2D) = "black" {}
        [HideInInspector] _ViewTexR("_ViewTexR", 2D) = "black" {}
		[Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags
		{
            "RenderType"="Opaque"

			// Queue should be between background surfaces
			// (e.g. wall behind the portal) and avatar queues
			// which are normally Geometry (2000) and above.
            "Queue"="Geometry-100"
        }

		Cull [_Cull]

		// This causes the shader to render on top of everything
		// that was rendered on an earlier queue. We then manually
		// z-test in the fragment program below.
		ZTest Always

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			#include "UnityInstancing.cginc"

			UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
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
				float2 stereoScreenPos : TEXCOORD1;
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

				// We also need ComputeScreenPos for sampling the depth texture.
				// ComputScreenPos is just ComputeNonStereoScreenPos followed
				// by TransformStereoScreenSpaceTex. (Which, btw, is a no-op
				// when not running single-pass stereo.)
				// Also, the w coordinate will be the same for screenPos and
				// stereoScreenPos, so we only need the xy for this. Put the
				// inputs into temp variables because this function modifies
				// the inputs for some reason.
				float2 spxy = o.screenPos.xy;
				float vw = o.vertex.w;
				o.stereoScreenPos.xy = TransformStereoScreenSpaceTex(spxy, vw);

				return o;
			}

			// Inverse of the LinearEyeDepth function provided by UnityCG
			float InverseLinearEyeDepth( float d )
			{
				return (1.0 - d*_ZBufferParams.w)/(d * _ZBufferParams.z);
			}

			half4 frag(v2f i, out float out_depth : SV_Depth) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

				// Get the fragment depth (distance from camera). This value is
				// in view-space, and is nonlinear. The range is 0 to 1, where
				// 0 is the far plane and and 1 is the near plane. 0.5 is NOT
				// halfway between the planes. Larger values are closer.
				//
				// This is the value that would normally get written to the depth
				// buffer after this fragment, if we didn't do anything else.
				//
				// Also note this is not the same thing as o.vertex.z in the
				// vertex program above. Between vertex and fragment programs,
				// the pipeline performs the "perspective divide", dividing
				// the vertex coordinates by o.vertex.w.
				float frag_view_depth = i.vertex.z;

				// Get the "existing"/"previous" depth where this fragment
				// will be drawn. This value is in nonlinear view-space.
				float4 stereoScreenPos = float4(i.stereoScreenPos, i.screenPos.zw);
				float existing_view_depth = tex2Dproj(_CameraDepthTexture, UNITY_PROJ_COORD(stereoScreenPos)).r;

				// Do the manual z-test. Discard this fragment if the existing
				// depth is closer than the fragment's depth. Note that the
				// depth values are reversed (larger values are closer), so the
				// comparison is backwards from expected:
				//   Discard if `frag_view_depth < existing_view_depth`
				clip(frag_view_depth - existing_view_depth);

				// Get the fragment depth in eye-space (aka camera-space). This
				// is "real" distance from the camera, in meters. Larger values
				// are further away.
				float frag_linear_eye_depth = LinearEyeDepth(frag_view_depth);

				// Generate the inset depth. I have chosen 2 meters as the
				// maximum depth.
				float new_depth = frag_linear_eye_depth + 2;

				// Convert the new depth back to nonlinear view-space and write
				// it out to the depth buffer.
				out_depth = InverseLinearEyeDepth(new_depth);

				// Now just do the normal render, just like PortalViewNoDepth.
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