// This portal shader lets you stick your hand into the portal without it
// getting 'cut off' like it normally would if you stick your hand into a
// wall. It gives the portal a more seamless feeling.
//
// It has some limitations: surfaces nearby behind the portal (e.g. the wall
// the portal is on) need to be on a lower render queue than this, otherwise
// they will render over the portal just like your hand would, and avatars
// need to be on a higher render queue than this.
//
// Additionally, to avoid some undesireable visuals, I recommend to not have
// anything except a wall (on a lower render queue, as mentioned above) physically
// behind the portal when using this shader.
//
// Author: aurycat
// License: MIT
// History:
//  1.0 (2023-01-13)
//  1.3 (2023-10-29) Switched from a method using _CameraDepthTexture to a 
//                   method using the stencil buffer. Using _CDT has a number
//                   of drawbacks including: not working for non-shadowcasting
//                   objects, breaking MSAA, & requiring a real-time light in
//                   the scene to force Unity to render _CDT.
//  1.5 (2024-11-17) Don't render in handheld camera.
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
//    Usual 'ZTest LEqual'.
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
// 2. The portal mesh is drawn with normal z-testing, but a stencil buffer bit
//    is set everywhere the mesh is drawn. This creates a sort-of "depth mask"
//    where a next pass can use the stencil buffer instead of a real z-test.
//
// 3. A second shader pass is set to `ZTest Always` so it draws regardless of
//    actual depth, but then uses the "depth mask" from the first pass to only
//    draw where needed. This pass only writes out the inset depth.
//
// 4. Therefore, the portal gets drawn with a normal ztest, but writes out an
//    inset depth. Things that render afterwards will z-test against the inset
//    depth and so can appear to reach into the portal!
//
// ============================================================================
//
// NOTE: This shader uses stencil bit 5 (ref value 32). If your world needs
// that bit for something else at this draw-queue, you can change all the
// "32"s in the Stencil blocks below to some other power of two.
//
// ============================================================================

Shader "Aurycat/PortalView"
{
	Properties
	{
		[HideInInspector] _ViewTexL("_ViewTexL", 2D) = "black" {}
		[HideInInspector] _ViewTexR("_ViewTexR", 2D) = "black" {}
		[Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
		_DepthInset ("Depth Inset Distance (Meters)", Range(0,5)) = 2
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
		ZTest Less
		ZClip False

		// Pass #1:
		// Draw the portal surface completely normally. The only thing special
		// here is we set bit 5 (ref=32) of the stencil everywhere a fragment
		// gets drawn (i.e. everywhere the z-test passes!). Also, skip ZWrite
		// because it will get overwritten in the next pass.
		// Note bit 5 is arbitrary. Hopefully it's less likely to be used by
		// other stuff than bit 1.
		Pass
		{
			ZWrite Off

			Stencil {
				Ref 32
				WriteMask 32
				ReadMask 0
				Pass Replace
			}

			CGPROGRAM
			#include "Common/PortalViewMain.cginc"
			ENDCG
		}

		// Pass #2:
		// Everywhere the stencil bit is set (from the previous pass), write
		// out a depth value that is the standard depth plus 2 meters further
		// away in screen-space. Then, clear the stencil bit back to 0 to avoid
		// doing this again for another portal behind this one. ZTest Always
		// because the stencil is effectively our z-test.
		//
		// Note this sort-of depends on front-to-back draw order of the portal
		// meshes: If drawn back-to-front, and one portal obscures another with
		// nothing between, the back portal will write its custom depth and
		// clear the stencil bit, and then the front portal will just keep that
		// potentially much further away depth unchanged. However, even if that
		// happens (which it shouldn't because we're on a Geometry queue so
		// Unity will draw front-to-back), the effect of that incorrect depth
		// should rarely be visible. Plus it will never be an issue if there is
		// any z-writing below-1900-queue surface between them, which is expected.
		Pass
		{
			ZWrite On
			ZTest Always
			ColorMask 0

			Stencil {
				Ref 32
				ReadMask 32
				WriteMask 32
				Pass Zero
				Comp Equal
			}

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			float _DepthInset;

			// Inverse of the LinearEyeDepth function provided by UnityCG
			float InverseLinearEyeDepth(float d)
			{
				return (1.0 - d*_ZBufferParams.w)/(d * _ZBufferParams.z);
			}

			float4 vert(float3 vertex : POSITION) : SV_POSITION
			{
				return UnityObjectToClipPos(vertex);
			}

			half4 frag(float4 pos : POSITION, out float out_depth : SV_Depth) : SV_Target
			{
				// Get the fragment depth (distance from camera). This value is
				// in screen-space, and is nonlinear. The range is 0 to 1, where
				// 0 is the far plane and and 1 is the near plane. 0.5 is NOT
				// halfway between the planes. Larger values are closer.
				//
				// This is the value that would normally get written to the depth
				// buffer after this fragment, if we didn't do anything else.
				//
				// Also note this is not the same thing as the output `z` in the
				// vertex program above. Between vertex and fragment programs,
				// the pipeline performs the "perspective divide", dividing
				// the vertex coordinates by the output `w` coordinate.
				float frag_view_depth = pos.z;

				// Get the fragment depth in view-space (aka camera-space aka
				// eye-space). This is "real" distance from the camera, in meters.
				// Larger values are further away.
				float frag_linear_eye_depth = LinearEyeDepth(frag_view_depth);

				// Generate the inset depth (wow this is the actually important
				// bit of the whole shader!!)
				float new_depth = frag_linear_eye_depth + _DepthInset;

				// Convert the new depth back to nonlinear screen-space and write
				// it out to the depth buffer.
				out_depth = InverseLinearEyeDepth(new_depth);

				// This pass writes no color (ColorMask 0), return value is ignored.
				return 0;
			}
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