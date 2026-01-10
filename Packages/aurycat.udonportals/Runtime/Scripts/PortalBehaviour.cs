// This is the main script that controls the portals.
//
// If you make improvements to this project that you think would be useful for
// everyone, please make a pull request on this project's GitHub page! Thanks!
// https://github.com/aurycat/UdonPortals
//
// Documentation:
// https://github.com/aurycat/UdonPortals/wiki
//
// Author: aurycat
// License: MIT
// History:
//  1.0 (2023-01-12)
//  1.1 (2023-08-20) Code formatting fixes.
//  1.2 (2023-09-04) Fixed rendering issues in desktop mode introduced by Unity 2022
//                   in the VRC open beta; fixed teleporting issues introduced by a
//                   change in the behavior of VRCPlayerApi.GetRotation; improved
//                   inspector editor UI; added noVisuals and useObliqueProjection
//                   options; black out portals in VR as local player leaves instance
//                   to prevent nauseating visuals.
//  1.3 (2023-11-12) Some fixes to the oblique near clipping plane, and better support
//                   for using a portal mesh that isn't a flad quad to avoid visual
//                   flashes when walking through the portals. Improve the layout of
//                   properties in the inspector again. Add 'behaviour mode' for
//                   physics only or visuals only portals. Add momentum snapping option.
//                   Support user-changed FOVs in Desktop play.
//  1.5 (2024-11-17) Support Holoport locomotion.
//  2.0 (2025-07-11) Upgrade to using VRCCameraSettings API for getting camera info.
//                   Some fields and properties that are no longer necessary have been
//                   removed from PortalBehavior, and FOVDetector has been removed.

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using System;
using VRC.SDK3.Components;
using VRC.SDK3.Rendering;
using JetBrains.Annotations;

// Documentation at:
// https://github.com/aurycat/UdonPortals/wiki/Public-API-of-PortalBehaviour#operatingmode-portalbehaviourmode-field
public enum PortalBehaviourMode
{
	VisualsAndPhysics,
	VisualsOnly,
	PhysicsOnly,
}

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
[HelpURL("https://github.com/aurycat/UdonPortals/wiki")]
public class PortalBehaviour : UdonSharpBehaviour
{
	// ========================================================================
	// PUBLIC PROPERTIES
	//
	// Documentation at:
	// https://github.com/aurycat/UdonPortals/wiki/Public-API-of-PortalBehaviour
	//
	// ========================================================================

	[Tooltip("Whether the portal can be looked through (VisualsOnly), telported through (PhysicsOnly), or both (VisualsAndPhysics).")]
	[PublicAPI]
	public PortalBehaviourMode operatingMode = PortalBehaviourMode.VisualsAndPhysics;

	[Tooltip("The transform of the partner portal. Normally this is set to another PortalBehaviour object, but it can be anything.")]
	[PublicAPI]
	public Transform partner;

	[Tooltip("Only the selected layers are shown in the portal. Water and PlayerLocal are never shown.")]
	[FieldChangeCallback(nameof(layerMask))]
	[PublicAPI]
	public LayerMask _layerMask = unchecked((int)0xFFC66B07);
	public LayerMask layerMask {
		get => _layerMask;
		set { _UpdateLayerMask(value); }
	}

	[Tooltip("Higher resolution looks better but costs more performance, just like a Mirror.")]
	[Range(0.2f, 1.0f)]
	[FieldChangeCallback(nameof(textureResolution))]
	[PublicAPI]
	public float _textureResolution = 1.0f;
	public float textureResolution {
		get => _textureResolution;
		set { _UpdateTextureResolution(value); }
	}

	[Tooltip("Receives events about things the portal has done. See 'Callback Script' wiki page on GitHub for more info.")]
	[PublicAPI]
	public UdonBehaviour callbackScript;

	[Tooltip("When enabled, the portal attempts to align the player's momentum to the portal orientation when traveling through it. Additionally it has some extra snapping behavior for flat portals (i.e. on the floor or ceiling) to make infinite falls or infinite bouncing easier on the player. If a portal is on a wall, like a door, this setting can be turned off because it will have nearly no effect.")]
	[PublicAPI]
	public bool momentumSnapping = false;

	[Tooltip("When enabled, the partner portal/GameObject is automatically activated when the player is teleported to it. See 'Performance and Optimization' wiki page on GitHub for more info.")]
	[PublicAPI]
	public bool activatePartnerOnTeleport = true;

	[Tooltip("When enabled, this portal will be deactivated after the player passes through it. See 'Performance and Optimization' wiki page on GitHub for more info.")]
	[PublicAPI]
	public bool deactivateSelfOnTeleport = false;

	// ========================================================================
	// ADVANCED PUBLIC PROPERTIES
	//
	// Documentation at:
	// https://github.com/aurycat/UdonPortals/wiki/Public-API-of-PortalBehaviour
	//
	// ========================================================================

	[Tooltip("The render texture used for the left eye in VR, or the entire view in Desktop.")]
	[PublicAPI]
	public RenderTexture viewTexL;
	[Tooltip("The render texture used for the right eye in VR.")]
	[PublicAPI]
	public RenderTexture viewTexR;

	[Tooltip("The distance from the portal surface where the player will teleport when their head crosses. See 'Public API of PortalBehaviour' wiki page on GitHub for more info.")]
	[PublicAPI]
	public float teleportPlaneOffset = 0f;

	[Tooltip("See 'Public API of PortalBehaviour' wiki page on GitHub for info on this setting.")]
	[PublicAPI]
	public bool useHoloportFix = true;

	[Tooltip("See 'Public API of PortalBehaviour' wiki page on GitHub for info on this setting.")]
	[PublicAPI]
	public bool _useObliqueProjection = true;
	// This getter/setter used to do extra stuff, now just here for backwards compatability
	public bool useObliqueProjection {
		get => _useObliqueProjection;
		set { _useObliqueProjection = value; }
	}

	[Tooltip("See 'Public API of PortalBehaviour' wiki page on GitHub for info on this setting.")]
	[PublicAPI]
	public float obliqueClipPlaneOffset = 0f;

	[Tooltip("See 'Public API of PortalBehaviour' wiki page on GitHub for info on this setting.")]
	[PublicAPI]
	public float obliqueClipPlaneDisableDist = 0.05f;

	[HideInInspector]
	[PublicAPI]
	public float manualStereoSeparation = 0f;

	[Tooltip("See 'Public API of PortalBehaviour' wiki page on GitHub for info on this setting.")]
	[PublicAPI]
	public GameObject portalCameraPrefab;

	[Tooltip("If left unset, gets automatically set to a generated instance of portalCameraPrefab. See 'Public API of PortalBehaviour' wiki page on GitHub for more info.")]
	[PublicAPI]
	public Transform portalCameraRoot;


	// ========================================================================
	// PUBLIC RUNTIME PROPERTIES
	//
	// Documentation at:
	// https://github.com/aurycat/UdonPortals/wiki/Public-API-of-PortalBehaviour
	//
	// ========================================================================

	[HideInInspector] [PublicAPI] public Vector3 destPosition;
	[HideInInspector] [PublicAPI] public Quaternion destRotation;
	[HideInInspector] [PublicAPI] public Vector3 destVelocity;


	// ========================================================================
	// PRIVATE PROPERTIES
	// ========================================================================

	private bool savePortalCameraRoot;

	// PortalBehaviour component attached to the same GameObject as the partner
	// transform, if such a component exists. Otherwise, null.
	private PortalBehaviour partnerPortalBehaviour;

// Don't wrap this in #if !UDONPORTALS_DISABLE_PARTNER_PRELOAD so the U# asset
// file doesn't change when the macro is set. But disable "assigned but its value
// is never used" warning, emitted when UDONPORTALS_DISABLE_PARTNER_PRELOAD is set. 
#pragma warning disable 0414
	// Initially true, set false after preloading once. Avoids unnecessary
	// calls out to the partner portal. Reset to true if `partner` is changed.
	private bool shouldPreloadPartner = true;
#pragma warning restore 0414

	// Set true if OnEnable has ever been called. Indicates preloading is no
	// longer needed.
	private bool everEnabled;

	// This is the Transform of the "virtual" head, i.e. the player's head
	// relative to the current portal's front-face, transformed to be relative
	// to the opposite portal's back-face.
	private Transform virtualHead;

	// These cameras capture the scene at the opposite portal.
	private Camera portalCameraL;
	private Camera portalCameraR;

	// Cached reference to the camera transforms
	private Transform portalCameraLTransform;
	private Transform portalCameraRTransform;

	// The dummy camera is used to obtain the stereo projection matrix used by
	// the main screen camera. The dummy camera isn't ever used for rendering;
	// it's just configured to have the same rendering properties as the real
	// camera, and then GetStereoProjectionMatrix is called on it. Since the
	// dummy camera is set to ender in stereo (unlike the portalCameras), Unity
	// will automatically configure the dummy camera's projection matrices for
	// the current HMD, even though it never renders.
	private Camera dummyStereoCamera;

	// Cached reference to the renderer and trigger collider on this object
	private Renderer _renderer; // Use '_renderer' name to avoid warning about conflict with 'Component.renderer'.
	                            // The warning says to use the 'new' keyword, but adding it causes a different
	                            // warning saying that 'new' isn't necessary. So uh. Just give it a different name.
	private Material material;
	private Collider trigger;

	// Texture size at the previous frame
	private int widthCache = 0;
	private int heightCache = 0;
	private bool texturesNeedRefresh;

	// Properties to keep track of the player while they're within the
	// portal trigger:
	// Player was in front of the portal on the last check
	private bool prevInFront;
	// Player's viewpoint head pos was inside the trigger on the last check
	private bool trackingHeadInTrigger;
	// Player was using Holoport locomotion on the last check
	private bool isHoloport;

	// Rigidbody that was in front of the portal on the most recent check.
	// If support is added for multiple rigidbodies moving through the
	// portal simultaneously, this will probably need to be an array.
	private Rigidbody prevBody;

	// Local player cache
	private VRCPlayerApi localPlayer;
	private bool inVR;

	// Cache of operatingMode that only changes at OnEnable
	private bool _noVisuals;
	private bool _noPhysics;

	// Shader property IDs from VRCShader.PropertyToID
	private bool propIDsInited;
	private int propID_ViewTexL;
	private int propID_ViewTexR;
#if UDONPORTALS_EXPERIMENTAL_RENDERING_FOR_NON_SCREEN_CAMERA
	private int propID_ScreenProjectionMatrix;
	private int propID_ScreenViewMatrix;
	private int propID_RenderOK;
#endif


	void OnEnable()
	{
		everEnabled = true;

		// Udon bug: The first time a GameObject is activated, there's a few
		// frames of delay between the SetActive(true) and OnEnable being called.
		// That's already annoying, but even worse, if SetActive(false) is then
		// called before OnEnable is run, OnEnable will still run but OnDisable
		// is not run! That would be bad for the portal because the stuff init'd
		// in OnEnable won't be cleaned up in OnDisable. Luckily there's a work-
		// -around, checking !gameObject.activeInHierarchy, which will be false
		// in this situaton. (It should never be false in OnEnable in normal Unity.)
		// The Preload() function specifically causes this scenario, so we need
		// to account for it.
		if (!gameObject.activeInHierarchy) {
			return;
		}

		_noVisuals = (operatingMode == PortalBehaviourMode.PhysicsOnly);
		_noPhysics = (operatingMode == PortalBehaviourMode.VisualsOnly);

		/*
		 * Validate properties.
		 */

		if (!_noVisuals && portalCameraPrefab == null) {
			Debug.LogError($"Portal '{name}' does not have the Portal Camera Prefab property set. Please set it to the 'PortalCamera' prefab asset provided in the UdonPortals package.");
			return;
		} else if (partner == null) {
			Debug.LogError($"Portal '{name}' does not have a partner transform set.");
			return;
		} else if (!_noVisuals && (viewTexL == null || viewTexR == null)) {
			Debug.LogError($"Portal '{name}' does not have one or both of its View Tex properties set. Please set them to two separate RenderTextures, unique to this portal.");
			return;
		} else if (!_noVisuals && (viewTexL == viewTexR)) {
			Debug.LogError($"Portal '{name}' has the same texture set for both View Tex L and View Tex R. Please set them to two separate RenderTextures, unique to this portal.");
			return;
		} else if (_textureResolution <= 0 || _textureResolution > 1) {
			Debug.LogError($"Portal '{name}' has texture resolution set to an illegal value ({_textureResolution}).");
			return;
		}

		partnerPortalBehaviour = partner.GetComponent<PortalBehaviour>();

		if (!_noVisuals) {
			_InitShaderPropertyIDs();
			_renderer = GetComponent<Renderer>();
			if (_renderer != null) {
				material = _renderer.material;
				if (material != null) {
					if(!material.HasProperty(propID_ViewTexL) || !material.HasProperty(propID_ViewTexR)) {
						Debug.LogError($"Portal '{name}' is set to material '{material.name}' which does not have the _ViewTexL or _ViewTexR properties.");
						return;
					}
				} else {
					Debug.LogError($"Portal '{name}' does not have a material set on its Renderer.");
					return;
				}
			} else {
				Debug.LogError($"Portal '{name}' does not have a Renderer.");
				return;
			}
		}

		trigger = GetComponent<Collider>();
		if (trigger == null) {
			Debug.LogError($"Portal '{name}' does not have a trigger collider. See README or example world for how to configure the trigger collider.");
			return;
		}
		trigger.isTrigger = true;

		if (!_noVisuals) {
			// Portal must always be on the layer Water so it's not rendered
			// recursively, or by other portals, or by mirrors. Recursive
			// rendering is not supported, and critically, it can cause the
			// render texture to be overwritten, causing visual distortion
			// when looking through the initial portal.
			if (gameObject.layer != 4) {
				Debug.LogWarning($"Changing layer of portal '{name}' to Water (4)");
				gameObject.layer = 4;
			}
		}


		/*
		 * Initialize player after all error conditions, to prevent
		 * running OnWillRenderObject if there was an init error.
		 */

		localPlayer = Networking.LocalPlayer;
		if (!Utilities.IsValid(localPlayer)) {
			return;
		}
		inVR = localPlayer.IsUserInVR();

		savePortalCameraRoot = false;
		if (!_noVisuals) {
			/*
			 * Instantiate portal camera prefab.
			 */

			if (portalCameraRoot == null) {
				GameObject root = Instantiate(portalCameraPrefab);
				root.name = name + "_Camera";
				portalCameraRoot = root.transform;
			}
			else {
				savePortalCameraRoot = true;
				portalCameraRoot.SetParent(null);
			}
			portalCameraRoot.gameObject.SetActive(true);

			virtualHead = portalCameraRoot.Find("VirtualHead");
			dummyStereoCamera = virtualHead.Find("DummyStereoCamera").GetComponent<Camera>();
			portalCameraL = virtualHead.Find("PortalCameraL").GetComponent<Camera>();
			portalCameraR = virtualHead.Find("PortalCameraR").GetComponent<Camera>();

			portalCameraLTransform = portalCameraL.transform;
			portalCameraRTransform = portalCameraR.transform;

			portalCameraR.gameObject.SetActive(inVR);

			/*
			 * Configure portal cameras.
			 */

			_ResetTransform(virtualHead);

			_ResetCamera(portalCameraL);
			_ResetCamera(portalCameraR);
			_ResetCamera(dummyStereoCamera);

			_UpdateLayerMask(_layerMask);

			texturesNeedRefresh = true;

			#if UDONPORTALS_EXPERIMENTAL_RENDERING_FOR_NON_SCREEN_CAMERA
				material.SetFloat(propID_RenderOK, 0);
			#endif
			material.SetTexture(propID_ViewTexL, viewTexL);

			if (inVR) {
				material.SetTexture(propID_ViewTexR, viewTexR);
				dummyStereoCamera.stereoTargetEye = StereoTargetEyeMask.Both;
			}
		}


		/*
		 * Etc
		 */

		prevInFront = false;
		trackingHeadInTrigger = false;
		isHoloport = false;

		#if !UDONPORTALS_DISABLE_PARTNER_PRELOAD
			// Nesting if statements compiles to fewer instructions than using &&
			if (activatePartnerOnTeleport) {
				if (shouldPreloadPartner) {
					shouldPreloadPartner = false;
					if (partnerPortalBehaviour != null) {
						partnerPortalBehaviour.Preload();
					}
				}
			}
		#endif

	} /* OnEnable */

	void OnDisable()
	{
		if (portalCameraRoot != null) {
			if (savePortalCameraRoot) {
				portalCameraRoot.gameObject.SetActive(false);
			} else {
				Destroy(portalCameraRoot.gameObject);
				portalCameraRoot = null;
			}
		}

		if (viewTexL != null) {
			viewTexL.Release();
		}
		if (viewTexR != null) {
			viewTexR.Release();
		}

		// In the editor, reset the texture size when the portal turns off.
		// (Note OnDisable gets called when exiting play mode.)
		// The portal asset file is affected by changing texture size, which
		// means every time you run play mode with a different Game window
		// size, the texture file changes, which makes Git think there are
		// changes to be committed. Reset the files to always be 256x256
		// to avoid Git changes after every time running play mode!
		#if UNITY_EDITOR
			if (viewTexL != null) {
				viewTexL.width = 256;
				viewTexL.height = 256;
			}
			if (viewTexR != null) {
				viewTexR.width = 256;
				viewTexR.height = 256;
			}
		#endif

		virtualHead = null;
		portalCameraL = null;
		portalCameraR = null;
		dummyStereoCamera = null;
		_renderer = null;
		trigger = null;
		widthCache = 0;
		heightCache = 0;
		prevInFront = false;
		trackingHeadInTrigger = false;
		isHoloport = false;
		prevBody = null;
		localPlayer = null;
		inVR = false;
	}

	/**
	 * Call this to change the material being used by the portal. If the
	 * material is changed on the renderer directly without calling this
	 * function, the textures won't update properly until the Portal is
	 * turned off and back on.
	 */
	[PublicAPI]
	public void SetMaterial(Material mat)
	{
		if (_noVisuals) {
			return;
		} else if (mat == null) {
			Debug.LogError($"Attempt to change portal '{name}' material to null.");
			return;
		}

		_InitShaderPropertyIDs();

		if (!mat.HasProperty(propID_ViewTexL) || !mat.HasProperty(propID_ViewTexR)) {
			Debug.LogError($"Attempt to change portal '{name}' material to '{mat.name}' which does not have the _ViewTexL or _ViewTexR properties.");
			return;
		} else if (_renderer == null) {
			// The portal might not have been enabled yet
			_renderer = GetComponent<Renderer>();
			if (_renderer == null) {
				Debug.LogError($"Attempt to change portal '{name}' material to '{mat.name}', but portal has no Renderer.");
				return;
			}
		}

		_renderer.material = mat;
		material = _renderer.material;

		material.SetTexture(propID_ViewTexL, viewTexL);
		if (inVR) {
			material.SetTexture(propID_ViewTexR, viewTexR);
		}
	}

	/**
	 * This should be called if the viewTexL or viewTexR properties are
	 * changed externally while the portal is active.
	 */
	[PublicAPI]
	public void RefreshTextures()
	{
		texturesNeedRefresh = true;
	}

	// Local vars within OnWillRenderObject, just placed here to avoid
	// initializing them every frame when not doing oblique projection
	private Vector3 ocpNormal = Vector3.zero;
	private Vector3 ocpPos = Vector3.zero;
	private Plane ocpDisablePlane = new Plane(Vector3.zero, Vector3.zero);

	void OnWillRenderObject()
	{
		if (_noVisuals) {
			return;
		}

		// Although GetCurrentCamera lets us know what camera is rendering,
		// there are multiple problems with rendering anything but the screen
		// camera.
		//  1. Due to a bug, the provided data for PhotoCamera is incorrect.
		//     Things will look wrong rendering in the handheld photo camera
		//     or drone, even if we exactly copy the camera settings it gives.
		//     https://feedback.vrchat.com/bug-reports/p/vrccamerasettings-fov-wrong-for-photo-camera
		//     The Canny claims FOV is wrong, but it seems like the issue is
		//     more than just FOV... I'm not sure exactly what is wrong though.
		//  2. GetCurrentCamera returns (null, null) when rendering the smoothed
		//     VR camera. So, that one will always look wrong.
		//  3. Even if all those issues were fixed, rendering different cameras
		//     presents an issue with image resolution. Currently portals render
		//     at the same resolution as the screen (times _textureResolution).
		//     If we render with multiple cameras, we'd need to update the size
		//     of the render textures multiple times per frame in order to
		//     render each camera at the right resolution. Alternatively, there
		//     could be a single "extra" render texture, at some fixed
		//     resolution e.g. 1024x1024, which is used for all non-screen
		//     cameras. That still means swapping out the target texture of the
		//     portalCameraL, and the texture used by the portal material,
		//     multiple times per frame. I haven't done much performance testing
		//     of those options, but I assume neither of them are great for
		//     performance!
		//     A solution could be to use camera stacking & stencil-based portal
		//     rendering, but that presents a number of other challenges in
		//     VRChat and I don't know if it's feasible.
		// For now, save some performance by not rendering portals for anything
		// except the screen camera.
		VRCCameraSettings screenCamera = VRCCameraSettings.ScreenCamera;
		VRCCameraSettings.GetCurrentCamera(out VRCCameraSettings internalCamera, out Camera externalCamera);
		if (internalCamera != screenCamera) {
			#if UDONPORTALS_EXPERIMENTAL_RENDERING_FOR_NON_SCREEN_CAMERA
				material.SetFloat(propID_RenderOK, 0);
			#endif
			return;
		}

		int w = screenCamera.PixelWidth;
		int h = screenCamera.PixelHeight;
		if (w != widthCache || h != heightCache || texturesNeedRefresh) {
			widthCache = w;
			heightCache = h;
			_SetupTexture(viewTexL, portalCameraL);
			if (inVR) {
				_SetupTexture(viewTexR, portalCameraR);
			}
			texturesNeedRefresh = false;
		}

		// Move the virtual head to its appropriate position relative to the opposite portal.
		// 1. Move the camera root to the position of the current portal.
		portalCameraRoot.SetPositionAndRotation(transform.position, transform.rotation);
		// 2. Move the virtual head (child object of the root) to the position of the camera.
		//    The virtual head is now in the "center" of the player's eyes.
		virtualHead.SetPositionAndRotation(screenCamera.Position, screenCamera.Rotation);
		// 3. In VR, shift the cameras to the position of each eye
		if (inVR) {
			Vector3 leftEyePos = VRCCameraSettings.GetEyePosition(Camera.StereoscopicEye.Left);
			Vector3 rightEyePos = VRCCameraSettings.GetEyePosition(Camera.StereoscopicEye.Right);

			if (manualStereoSeparation > 0.0f) {
				Vector3 centerPos = leftEyePos*0.5f + rightEyePos*0.5f;
				Vector3 dir = (leftEyePos - centerPos).normalized;
				float ssHalf = manualStereoSeparation * 0.5f;
				leftEyePos = centerPos + dir * ssHalf;
				rightEyePos = centerPos - dir * ssHalf;
			}

			portalCameraLTransform.SetPositionAndRotation(
				leftEyePos,
				VRCCameraSettings.GetEyeRotation(Camera.StereoscopicEye.Left));
			portalCameraRTransform.SetPositionAndRotation(
				rightEyePos,
				VRCCameraSettings.GetEyeRotation(Camera.StereoscopicEye.Right));
		}
		#if UDONPORTALS_EXPERIMENTAL_RENDERING_FOR_NON_SCREEN_CAMERA
			// 3b. portalCameraL is now in the same position as, and so has the same
			//     view matrix as, the (left eye of the) main screen camera. Capture
			//     that view matrix and pass it to the shader. We only render the
			//     portal once, for the main screen camera, so if any other cameras
			//     (e.g. handheld photo camera, drone camera, stabilized camera) view
			//     the portal, the shader can use this view matrix from the time of
			//     rendering to approximate the portal surface for those other cameras.
			//     Also, only the left eye view is needed since we assume all the other
			//     cameras are not VR.
			material.SetMatrix(propID_ScreenViewMatrix, portalCameraL.worldToCameraMatrix);
		#endif
		// 4. Rotate the virtual head 180 degrees around the current portal,
		//    which accounts for the fact that the virtual head looks "out of"
		//    the partner portal. So we're looking from behind the portal now.
		virtualHead.RotateAround(transform.position, transform.up, 180);
		// 5. Finally shift the root, and thus the virtual head, to the position
		//    and rotation of the partner portal. The virtual head camera is now
		//    in its final position for rendering the portal view.
		portalCameraRoot.SetPositionAndRotation(partner.position, partner.rotation);

		// Copy properties from the rendering camera
		portalCameraL.aspect              = screenCamera.Aspect;
		portalCameraL.nearClipPlane       = screenCamera.NearClipPlane;
		portalCameraL.farClipPlane        = screenCamera.FarClipPlane;
		portalCameraL.fieldOfView         = screenCamera.FieldOfView;
		portalCameraL.allowHDR            = screenCamera.AllowHDR;
		portalCameraL.backgroundColor     = screenCamera.BackgroundColor;
		portalCameraL.clearFlags          = screenCamera.ClearFlags;
		portalCameraL.useOcclusionCulling = screenCamera.UseOcclusionCulling;

		if (inVR) {
			portalCameraR.aspect              = screenCamera.Aspect;
			portalCameraR.nearClipPlane       = screenCamera.NearClipPlane;
			portalCameraR.farClipPlane        = screenCamera.FarClipPlane;
			portalCameraR.fieldOfView         = screenCamera.FieldOfView;
			portalCameraR.allowHDR            = screenCamera.AllowHDR;
			portalCameraR.backgroundColor     = screenCamera.BackgroundColor;
			portalCameraR.clearFlags          = screenCamera.ClearFlags;
			portalCameraR.useOcclusionCulling = screenCamera.UseOcclusionCulling;

			// Use the dummy camera to get the per-eye stereo projection matrices.
			// Copy the properties from the rendering camera to the dummy camera so
			// that the matrices are computed correctly.
			dummyStereoCamera.aspect          = screenCamera.Aspect;
			dummyStereoCamera.nearClipPlane   = screenCamera.NearClipPlane;
			dummyStereoCamera.farClipPlane    = screenCamera.FarClipPlane;
			dummyStereoCamera.fieldOfView     = screenCamera.FieldOfView;
			portalCameraL.projectionMatrix    = dummyStereoCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
			portalCameraR.projectionMatrix    = dummyStereoCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);
		}
		else {
			portalCameraL.ResetProjectionMatrix();
		}

		#if UDONPORTALS_EXPERIMENTAL_RENDERING_FOR_NON_SCREEN_CAMERA
			// Capture projection matrix used to render the portal and pass it to
			// the shader. See above where the view matrix is passed to the shader
			// for more info.
			material.SetMatrix(propID_ScreenProjectionMatrix, portalCameraL.projectionMatrix);
		#endif

		if (_useObliqueProjection) {
			// Setup an oblique projection matrix for the portal cameras.
			// In VR, do this separately for each eye.
			// Read the documentation for 'useObliqueProjection' for more info.
			ocpNormal = -partner.forward;
			ocpPos = partner.position - (ocpNormal * obliqueClipPlaneOffset);
			ocpDisablePlane = new Plane(ocpNormal, ocpPos - (ocpNormal * obliqueClipPlaneDisableDist));

			// In some cases, we need to avoid oblique projection. Read the
			// documentation for 'obliqueClipPlaneDisableDist' to explain why.
			//
			// The GetSide check is detecting whether the portal camera is in
			// front of the partner portal - effectively in front of its own
			// near plane. We skip oblique projection if that happens.
			if (!trackingHeadInTrigger || !ocpDisablePlane.GetSide(portalCameraLTransform.position)) {
				Vector4 clipPlane = _CameraSpacePlane(portalCameraL.worldToCameraMatrix, ocpPos, ocpNormal);
				Matrix4x4 projection = portalCameraL.CalculateObliqueMatrix(clipPlane);
				portalCameraL.projectionMatrix = projection;
			}
		}
		portalCameraL.Render();

		if (inVR) {
			if (_useObliqueProjection) {
				if (!trackingHeadInTrigger || !ocpDisablePlane.GetSide(portalCameraRTransform.position)) {
					Vector4 clipPlane = _CameraSpacePlane(portalCameraR.worldToCameraMatrix, ocpPos, ocpNormal);
					Matrix4x4 projection = portalCameraR.CalculateObliqueMatrix(clipPlane);
					portalCameraR.projectionMatrix = projection;
				}
			}
			portalCameraR.Render();
		}

		#if UDONPORTALS_EXPERIMENTAL_RENDERING_FOR_NON_SCREEN_CAMERA
			material.SetFloat(propID_RenderOK, 1);
		#endif

	} /* OnWillRenderObject */

	// This attempts to detect when a VR player leaves the world. VRC has about
	// one second of fadeout when leaving the world where Udon stops working,
	// so the cameras stop updating, but shaders still run so you can still see
	// the portal view moving. It feels like when SteamVR freezes and your headset
	// doesn't update; it's very nauseating. So try to black out the portals
	// (set texture to null makes them render black) since it's a little nicer
	// to see than the render texutre not updating.
	public override void OnPlayerLeft(VRCPlayerApi player)
	{
		if (_noVisuals) {
			return;
		} else if (inVR) {
			if (!Utilities.IsValid(player) || player.isLocal) {
				if (material != null) {
					#if UDONPORTALS_EXPERIMENTAL_RENDERING_FOR_NON_SCREEN_CAMERA
						material.SetFloat(propID_RenderOK, 0);
					#else
						material.SetTexture(propID_ViewTexL, null);
						material.SetTexture(propID_ViewTexR, null);
					#endif
				}
			}
		}
	}

	public override void OnPlayerTriggerExit(VRCPlayerApi player)
	{
		prevInFront = false;
		trackingHeadInTrigger = false;
		isHoloport = false;
	}

	public override void OnPlayerTriggerStay(VRCPlayerApi player)
	{
		if (!Utilities.IsValid(player)) {
			return;
		}
		if (!player.isLocal) {
			return;
		}

		// 'trackingHeadInTrigger' is used for rendering when _useObliqueProjection is on
		if (_noPhysics) {
			if (!_useObliqueProjection) {
				return;
			}
		}

		VRCPlayerApi.TrackingData trackingHead = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);

		// ClosestPoint is the only way to manually check if a point is inside a collider.
		trackingHeadInTrigger = (trigger.ClosestPoint(trackingHead.position) == trackingHead.position);

		if (_noPhysics) {
			return;
		}

		Vector3 playerHeadPos;
		bool playerHeadInTrigger = trackingHeadInTrigger;
		if (inVR && useHoloportFix) {
			// Detect if the player is moving with Holoport locomotion. In Holoport
			// locomotion, the avatar head (head bone pos) will be distant from the
			// viewpoint head (tracking data head). Teleporting behavior needs to
			// be adjusted in that case. Thanks to Superbstingray for coming up with
			// this Holoport detection method and the teleporting fixes for it!
			Vector3 holoportHeadPos = player.GetBonePosition(HumanBodyBones.Head);
			if (holoportHeadPos != Vector3.zero) { // Zero if no head bone
				isHoloport = Vector3.Distance(trackingHead.position, holoportHeadPos) > 1;
			}
			else {
				isHoloport = false;
			}

			if (isHoloport) {
				playerHeadPos = holoportHeadPos;

				// When using Holoport, there's a difference between the player's
				// head being in the trigger for the purposes of deciding when to
				// teleport, and the player's head being in the trigger for the
				// purposes of rendering the visuals. Visuals need tracking head
				// pos (since it's where you're actually looking from), but
				// teleporting needs avatar head pos.
				// The difference is extremely subtle and only causes a problem
				// when looking at the portal from an extremely oblique angle
				// while the player's avatar is Holoported in front of the portal.
				// A rare condition, but I have obsessions okay let me be like this
				playerHeadInTrigger = (trigger.ClosestPoint(playerHeadPos) == playerHeadPos);
			}
			else {
				playerHeadPos = trackingHead.position;
			}
		}
		else {
			isHoloport = false;
			playerHeadPos = trackingHead.position;
		}

		// Check that the player's head is actually in the trigger, so we don't
		// teleport if the player is merely walking beside the portal.
		if (playerHeadInTrigger)
		{
			Plane p = new Plane(-transform.forward, transform.position - (transform.forward*teleportPlaneOffset));
			bool inFront = p.GetSide(playerHeadPos);

			if (prevInFront) {
				if (!inFront) {
					// Player crossed from front to back, teleport them

					// Calling TeleportTo from a FixedUpdate event like OnPlayerTriggerStay
					// causes some "ghosting" -- it appears like you can see yourself
					// through the portal one frame in advance, or something like that.
					// Delaying until Update makes it go away.
					SendCustomEventDelayedFrames(nameof(_TeleportPlayer), 0, VRC.Udon.Common.Enums.EventTiming.Update);
				}
			}
			prevInFront = inFront;
		}
		else {
			prevInFront = false;
		}
	}

	private Quaternion local180 = Quaternion.AngleAxis(180, Vector3.up);

	// Not public API. Only public for calling from SendCustomEventDelayedFrames.
	public void _TeleportPlayer()
	{
		VRCPlayerApi.TrackingData trackingHead = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);

		// Use AvatarRoot in VR due to a few bugs with using Origin-based teleporting.
		// Using AvatarRoot also means the TeleportTo needs to use AlignPlayerWithSpawnPoint
		// instead of AlignRoomWithSpawnPoint. 
		VRCPlayerApi.TrackingData trackingRoot =
			inVR
			? localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.AvatarRoot)
			: localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);

		// When a player is currently moving with Holoport locomotion, their
		// avatar will be distant from their viewpoint. Teleport based on
		// their avatar location so that their avatar ends up in the correct
		// spot after teleporting. Thankfully, when calling TeleportTo, VRChat
		// automatically resets the viewpoint to the teleport destination.
		//
		// Note we can't use avatar & head bone position with normal locomotion
		// because it won't be perfectly aligned with the VR viewpoint, meaning
		// the teleport won't be visually seamless. Unlike normal locomotion,
		// Holoport's whole purpose is to snap to a new viewpoint, so it's okay
		// that the teleport isn't visually seamless.
		Vector3 playerHeadPos =
			isHoloport
			? localPlayer.GetBonePosition(HumanBodyBones.Head)
			: trackingHead.position;

		// Don't use transform/partner.TransformPoint because it accounts for
		// scale, which we don't want -- otherwise a portal pair that have
		// different world scales (even if the surfaces are identical size
		// due to different meshes) won't work correctly together.
		Matrix4x4 selfWorldToLocalMat = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one).inverse;
		Matrix4x4 partnerLocalToWorldMat = Matrix4x4.TRS(partner.position, partner.rotation, Vector3.one);

		// Calculate the new position of the player's *head*, not their origin.
		// The player teleports when their head passes through the surface, so
		// the relative position of the head is what needs to remain constant.
		Vector3 localHeadPos = selfWorldToLocalMat.MultiplyPoint3x4(playerHeadPos);
		localHeadPos = local180 * localHeadPos;
		Vector3 newHeadPos = partnerLocalToWorldMat.MultiplyPoint3x4(localHeadPos);

		// Player rotation only cares about Y. In fact, when I tried to
		// do rotation for all axes, like with Rigidbodies below, it
		// didn't work quite right. Not sure why. *shrug*
		Quaternion partner180 = Quaternion.AngleAxis(180, partner.up);
		Quaternion partnerInvRot = partner180 * partner.rotation;
		float inputY = transform.rotation.eulerAngles.y;
		float outputY = partnerInvRot.eulerAngles.y;
		float diffY = outputY - inputY;
		float playerY = trackingRoot.rotation.eulerAngles.y;
		float newY = playerY + diffY;
		destRotation = Quaternion.Euler(0, newY, 0);

		// From the newHeadPos and rotation delta, calculate the new origin
		// position which is what we need for teleporting.
		Vector3 headToRoot = trackingRoot.position - playerHeadPos;
		headToRoot = Quaternion.AngleAxis(diffY, Vector3.up) * headToRoot;
		destPosition = newHeadPos + headToRoot;

		// Find new velocity

		// Use cosine of angles (aka output of dot product) instead of angles
		// since it's cheaper to compute Vector3.Dot than Vector3.Angle, and
		// with Dot its easier to compare directionless - just use Abs.
		const float LOOKING_AT_PORTAL = 0.70710678f; // cos(45 deg)
		const float SNAP_KINDA_CLOSE  = 0.96592582f; // cos(15 deg)
		const float SNAP_CLOSE        = 0.99254615f; // cos(7 deg)
		const float SNAP_NEARLY_EXACT = 0.99984769f; // cos(1 deg)

		Vector3 playerVel = localPlayer.GetVelocity();
		Vector3 localVel;

		// Transform player's momentum into portal-local space, where 'forward'
		// is directly into this portal.
		if (momentumSnapping) {
			if (Mathf.Abs(transform.forward.y) > SNAP_CLOSE) {
				// Player is moving approximately vertically towards a portal approximately
				// parallel to the ground.
				// We want to snap them to alignment, but there is an issue: in an infinite
				// fall situation, it can be very hard to get out. Any horizontal momentum
				// the player obtains from pressing on the thumbstick/WASD gets erased each
				// time they pass through the portal, which is happening frequently. It makes
				// moving out very slow.
				// So instead, take a hint from Portal's (the game) funneling system and
				// require the player to be looking at the portal to do the snapping. If they
				// look away, require a nearly perfect vertical momentum. That way they can
				// keep infinite-falling once they start, but it's still easy to get out just
				// by looking away.
				float playerVertAlignment = Mathf.Abs(playerVel.normalized.y);
				if ( playerVertAlignment > SNAP_NEARLY_EXACT ||
				     (playerVertAlignment > SNAP_CLOSE &&
				      Vector3.Dot((trackingHead.rotation * Vector3.forward), transform.forward) > LOOKING_AT_PORTAL) )
				{
					// Snap! Player must be moving towards the portal,
					// so we can ignore the direction of their velocity.
					localVel = Vector3.forward * Mathf.Abs(playerVel.y);
				}
				else {
					localVel = transform.InverseTransformDirection(playerVel);
				}
			}
			else if (Mathf.Abs(Vector3.Dot(playerVel.normalized, transform.forward)) > SNAP_KINDA_CLOSE) {
				// Portal is not flat on the ground, but player is still
				// approaching portal head-on - snap to perfectly aligned.
				// Output magnitude is reduced to the component of the
				// player's velocity that is aligned to the portal.
				localVel = Vector3.forward * Vector3.Dot(playerVel, transform.forward);
			}
			else {
				localVel = transform.InverseTransformDirection(playerVel);
			}
		}
		else {
			localVel = transform.InverseTransformDirection(playerVel);
		}

		// Flip momentum around, so it's going out instead of in
		localVel = local180 * localVel;

		// Transform local momentum back into world-space, from the partner
		// portal's rotation.
		Quaternion localToPartnerRot = partner.rotation;
		if (momentumSnapping) {
			// Instead of like above where we snap the player's incoming
			// velocity vector, here we're snapping the partner portal's
			// outwards direction (-forward) to vertical if it's nearly there.
			// The partner outwards direction is NOT necessarily the same
			// as the player's new final velocity direction.
			Vector3 partnerOutwards = -partner.forward;
			float partnerVertAlignment = Mathf.Abs(Vector3.Dot(partnerOutwards, Vector3.up));
			if (Mathf.Approximately(partnerVertAlignment, 1)) {
				// Partner is already perfectly parallel to ground, no need to snap it
			}
			else if (partnerVertAlignment > SNAP_CLOSE) {
				// targetDir is Vector3.up if the portal is "on the ground"
				// -Vector3.up if the portal is "on the ceiling".
				Vector3 targetDir = Vector3.up * Mathf.Sign(partnerOutwards.y);
				// Get a quaternion which would rotate the partner's outwards
				// vector to be vertical, and apply it to the local-to-world
				// transformation. In other words, this pretends the yaw/pitch
				// of the portal were adjusted to become vertical, but roll is kept.
				Quaternion toVertical = Quaternion.FromToRotation(partnerOutwards, targetDir);
				localToPartnerRot = toVertical * localToPartnerRot;
			}
		}
		destVelocity = localToPartnerRot * localVel;

		// Inform others
		if (callbackScript != null) {
			callbackScript.SetProgramVariable("sourcePortal", this);
			callbackScript.SendCustomEvent("_PortalWillTeleportPlayer");
		}
		if (activatePartnerOnTeleport) {
			partner.gameObject.SetActive(true);
		}
		if (partnerPortalBehaviour != null) {
			partnerPortalBehaviour.OnWillReceivePlayer(this);
		}

		// Do teleport. In VR, positioning is all relative to AvatarRoot,
		// which means we can use AlignPlayerWithSpawnPoint in all cases.
		localPlayer.TeleportTo(
			destPosition,
			destRotation,
			VRC.SDKBase.VRC_SceneDescriptor.SpawnOrientation.AlignPlayerWithSpawnPoint,
			/*lerpOnRemote=*/false);

		localPlayer.SetVelocity(destVelocity);

		if (deactivateSelfOnTeleport) {
			gameObject.SetActive(false);
		}
	}

	/*
	 * Basic support for physics objects travelling through the portal.
	 * Currently, this only handles one object at a time. However multiple
	 * objects can move through the portal in quick sucession; as soon as
	 * the first object is teleported or exits the trigger, the next object
	 * will start being detect.
	 */

	public void OnTriggerExit(Collider collider)
	{
		if (prevBody != null) {
			if (prevBody == collider.attachedRigidbody) {
				prevBody = null;
			}
		}
	}

	public void OnTriggerStay(Collider collider)
	{
		if (_noPhysics) {
			return;
		}

		Rigidbody body = collider.attachedRigidbody;
		if (body == null) {
			return;
		}
		if (body.isKinematic) {
			return;
		}

		if (prevBody != null) {
			if (prevBody != body) {
				return;
			}
		}

		// Only the object owner should update the object transform. If we're
		// not the owner, don't do anything. Also, clear prevBody in case we
		// *lose* ownership while it's in the trigger.
		if (!Networking.IsOwner(collider.gameObject)) {
			// Only if it has an object sync. If there's no object sync,
			// assume it's a local object and ownership doesn't matter.
			// This assumption may not always be accurate.
			if (collider.GetComponent<VRCObjectSync>() != null) {
				prevBody = null;
				return;
			}
		}

		Plane p = new Plane(-transform.forward, transform.position);
		bool inFront = p.GetSide(body.position);

		if (inFront) {
			// Object is in front, save it to check it next frame
			prevBody = body;
		}
		else if (prevBody != null) {
			// Object crossed from front to back, teleport it

			VRCPickup pickup = body.GetComponent<VRCPickup>();
			if (pickup != null && pickup.IsHeld) {
				prevBody = null;
				return;
			}

			VRCObjectSync sync = body.GetComponent<VRCObjectSync>();
			if (sync != null) {
				sync.FlagDiscontinuity();
			}

			// Compute new transform
			Vector3 localPos = transform.InverseTransformPoint(body.position);
			localPos = Quaternion.AngleAxis(180, Vector3.up) * localPos;
			destPosition = partner.TransformPoint(localPos);

			Quaternion partner180 = Quaternion.AngleAxis(180, partner.up);
			Quaternion rotDiff = partner.rotation * Quaternion.Inverse(transform.rotation);
			destRotation = partner180 * rotDiff * body.rotation;

			Vector3 localVel = transform.InverseTransformDirection(body.velocity);
			destVelocity = partner180 * partner.TransformDirection(localVel);

			// Inform others
			if (callbackScript != null) {
				callbackScript.SetProgramVariable("sourcePortal", this);
				callbackScript.SetProgramVariable("teleportedObject", body);
				callbackScript.SendCustomEvent("_PortalWillTeleportObject");
			}
			if (partnerPortalBehaviour != null) {
				partnerPortalBehaviour.OnWillReceiveObject(this, body);
			}

			// Do teleport
			body.position = destPosition;
			body.rotation = destRotation;
			body.velocity = destVelocity;

			// Object is now somewhere else, treat it as if it has exited the
			// trigger and clear prevBody to make room for other objects. If
			// it got teleported to in front of the portal, that will get
			// detected on the next Stay event.
			prevBody = null;
		}
		// 'else' means the object is behind the portal, but has never
		// been in front of the portal, so it hasn't crossed from front
		// to back. So we ignore it.

	} /* OnTriggerStay */

	private void _ResetTransform(Transform t)
	{
		t.localPosition = Vector3.zero;
		t.localRotation = Quaternion.identity;
		t.localScale    = Vector3.one;
	}

	private void _ResetCamera(Camera cam)
	{
		_ResetTransform(cam.transform);

		cam.enabled         = false;
		cam.cullingMask     = 0;
		cam.stereoTargetEye = StereoTargetEyeMask.None;
		cam.targetTexture   = null;
		cam.depth           = -10;
	}

	private void _SetupTexture(RenderTexture tex, Camera cam)
	{
		tex.Release();
		tex.vrUsage = VRTextureUsage.None;
		tex.width = (int)(widthCache * _textureResolution);
		tex.height = (int)(heightCache * _textureResolution);

		// Need to set to null first for the change to apply correctly, dunno why
		cam.targetTexture = null;
		cam.targetTexture = tex;
	}

	private void _InitShaderPropertyIDs()
	{
		if (propIDsInited) {
			return;
		}
		propID_ViewTexL = VRCShader.PropertyToID("_ViewTexL");
		propID_ViewTexR = VRCShader.PropertyToID("_ViewTexR");
		#if UDONPORTALS_EXPERIMENTAL_RENDERING_FOR_NON_SCREEN_CAMERA
			propID_ScreenProjectionMatrix = VRCShader.PropertyToID("_ScreenProjectionMatrix");
			propID_ScreenViewMatrix = VRCShader.PropertyToID("_ScreenViewMatrix");
			propID_RenderOK = VRCShader.PropertyToID("_RenderOK");
		#endif
		propIDsInited = true;
	}

	// Given position/normal of the plane, calculates plane in camera space.
	private Vector4 _CameraSpacePlane(Matrix4x4 m, Vector3 pos, Vector3 normal)
	{
		Vector3 cpos = m.MultiplyPoint(pos);
		Vector3 cnormal = m.MultiplyVector(normal).normalized;
		return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
	}

	private void _UpdateLayerMask(LayerMask val)
	{
		// Never show Water, containing other mirrors/portals which
		// will look very broken, or PlayerLocal, which shows the
		// player without their head.
		_layerMask = val & (~0x410);
		if (portalCameraL != null) {
			portalCameraL.cullingMask = _layerMask;
			portalCameraR.cullingMask = _layerMask;
		}
	}

	private void _UpdateTextureResolution(float val)
	{
		if (val <= 0 || val > 1) {
			Debug.LogError($"Attempt to set texture resolution on portal '{name}' to an illegal value ({val}).");
			return;
		}
		_textureResolution = val;
		texturesNeedRefresh = true;
	}

	// `partner` can't be changed to a property with a FieldChangeCallback without
	// breaking backwards compatability for either graphs or U#. So use this hack
	// to check for changes to the partner variable. The on-change event is really
	// just a public event called `_onVarChange_<variable name>`!
	public void _onVarChange_partner()
	{
		if (enabled) {
			if (gameObject.activeInHierarchy) {
				if (partner == null) {
					Debug.LogError($"Partner of active portal '{name}' was changed to null! Deactivating self.");
					gameObject.SetActive(false);
					return;
				}
				partnerPortalBehaviour = partner.GetComponent<PortalBehaviour>();
			}
		}
		#if !UDONPORTALS_DISABLE_PARTNER_PRELOAD
			// Reset this flag so that the new partner will be preloaded next time
			// this portal is enabled.
			shouldPreloadPartner = true;
		#endif
	}

	// Called by the partner portal (if the partner is a PortalBehaviour)
	// when it is about to teleport the player here.
	public void OnWillReceivePlayer(PortalBehaviour source)
	{
		// After being teleported here, there may be a few frames of
		// rendering the portal before OnPlayerTriggerStay is called
		// to update `trackingHeadInTrigger`. That can lead to flicker
		// due to wrongly using an oblique near plane while an eye
		// is still in the portal. Set this true assuming that the
		// head will always be in the trigger after teleporting.
		trackingHeadInTrigger = true;

		if (callbackScript) {
			callbackScript.SetProgramVariable("sourcePortal", source);
			callbackScript.SetProgramVariable("targetPortal", this);
			callbackScript.SendCustomEvent("_PortalWillReceivePlayer");
		}
	}

	// Called by the partner portal (if the partner is a PortalBehaviour)
	// when it is about to teleport an object here.
	public void OnWillReceiveObject(PortalBehaviour source, Rigidbody body)
	{
		if (callbackScript) {
			callbackScript.SetProgramVariable("sourcePortal", source);
			callbackScript.SetProgramVariable("targetPortal", this);
			callbackScript.SetProgramVariable("teleportedObject", body);
			callbackScript.SendCustomEvent("_PortalWillReceiveObject");
		}
	}

	// Called by the partner portal when it becomes active, if the partner uses
	// activatePartnerOnTeleport. The first time an object becomes active in the
	// scene, it can take a few frames to start rendering. With activatePartner-
	// -OnTeleport, it might be that the first time the portal becomes active is
	// right when the player is teleported to it. That means for a few frames
	// the portal won't be rendered -- i.e., ugly flicker!
	//
	// Preloading activates and then immediately deactivates the portal, which
	// is enough to get Unity to do whatever processing it needs to do, and then
	// the next time the portal is activated it starts rendering the same frame.
	//
	// Unfortunately this only works if the parent object is active too. I assume
	// that usually if the partner portal is active and using activatePartnerOn-
	// -Teleport, this portal will be ready to activate, hence its parent should
	// be active already.
	public void Preload()
	{
		#if !UDONPORTALS_DISABLE_PARTNER_PRELOAD
			// The partner is who called this function, so it's already active and
			// doesn't need to be preloaded.
			shouldPreloadPartner = false;
		#endif

		// Nesting if statements compiles to fewer instructions than using &&
		// Plus with &&, the unary negations get compiled as EXTERNs! D:
		if (!everEnabled) {
			if (!gameObject.activeSelf) {
				// Preloading is to avoid flicker on teleport, so it's only
				// necessary when using VisualsAndPhysics
				if (operatingMode == PortalBehaviourMode.VisualsAndPhysics) {
					gameObject.SetActive(true);
					gameObject.SetActive(false);
				}
			}
		}
	}
}