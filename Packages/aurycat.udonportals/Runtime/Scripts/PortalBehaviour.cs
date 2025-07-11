// This is the main script that controls the portals. All the public properties
// and functions have document comments above them. Please read them for more
// detail.
//
// If you make improvements to this project that you think would be useful for
// everyone, please make a pull request on this project's GitLab page! Thanks!
// https://gitlab.com/aurycat/UdonPortals
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

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using System;
using UnityEditor;
using VRC.SDK3.Components;
using VRC.SDK3.Rendering;

// See 'operatingMode' property documentation below for info
public enum PortalBehaviourMode
{
	VisualsAndPhysics,
	VisualsOnly,
	PhysicsOnly,
}

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PortalBehaviour : UdonSharpBehaviour
{
	// ========================================================================
	// PUBLIC PROPERTIES
	// ========================================================================

	/**
	 * VisualsAndPhysics:
	 *     Default behavior. You can look through and walk through the portal.
	 * VisualsOnly:
	 *     The portal looks the same, but does not do any physics/teleporting
	 *     to players or objects that pass through it. Use this if your portal
	 *     is only for looks and cannot be passed through.
	 * PhysicsOnly:
	 *     The portal has the same teleporting behavior but does not show any
	 *     image or render anything or even create a camera. If all you want
	 *     is the physics/teleporting effect of the portal but not the visuals,
	 *     use this to save performance by not calculating camera stuff.
	 *
	 * The PortalBehaviour component must be disabled and reenabled for changes
	 * of this setting to take effect.
	 */
	[Tooltip("Whether the portal does visuals at all, does physics/teleporting at all, or does both. Doing both is the standard behavior.")]
	public PortalBehaviourMode operatingMode = PortalBehaviourMode.VisualsAndPhysics;

	/**
	 * The transform of the partner portal. Normally this should be set to
	 * another PortalBehaviour object, but it could be anything. The "partner"
	 * doesn't actually have to be a working portal, it's just an arbitrary
	 * position in the world.
	 *
	 * This transform can be changed at any time. Don't set it to null.
	 */
	[Tooltip("The transform of the partner portal. Normally this should be set to another PortalBehaviour object, but it can be anything.")]
	public Transform partner;

	/**
	 * Auto-detected from `partner`.
	 */
	[HideInInspector]
	public PortalBehaviour partnerPortalBehaviour;

	/**
	 * The Unity layers to show through the portal. Default value is to show:
	 *  Default, TransparentFX, Ignore Raycast, Interactive, Player,
	 *  Environment, Pickup, PickupNoEnvironment, Walkthrough, MirrorReflection,
	 *  and all non-VRC custom layers (22 and above).
	 */
	[Tooltip("Only the selected layers are shown in the portal. Water and PlayerLocal are never shown.")]
	[FieldChangeCallback(nameof(layerMask))]
	public LayerMask _layerMask = unchecked((int)0xFFC66B07);
	public LayerMask layerMask {
		get => _layerMask;
		set { _UpdateLayerMask(value); }
	}

	/**
	 * The size of the render textures as a proportion of the screen size.
	 * 1 means maximum quality, smaller is lower quality.
	 */
	[Tooltip("Higher resolution looks better but costs more performance, just like a Mirror.")]
	[Range(0.2f, 1.0f)]
	[FieldChangeCallback(nameof(textureResolution))]
	public float _textureResolution = 1.0f;
	public float textureResolution {
		get => _textureResolution;
		set { _UpdateTextureResolution(value); }
	}

	/**
	 * If non-null, this script will get sent events by the portal. Before
	 * sending an event, variables will get set on the callback script as
	 * parameters. The variable "sourcePortal" (type PortalBehaviour) will
	 * always get set to <this> portal.
	 *
	 *  - "_PortalWillTeleportPlayer": The portal is about to teleport the
	 *     local player.
	 *  - "_PortalWillTeleportObject": The portal is about to teleport a
	 *     non-player Rigidbody that **is owned by the local player**. The
	 *     variable "teleportedObject" will be set to the Rigidbody.
	 */
	[Tooltip("Receives events about things the portal has done. Read the documentation in PortalBehaviour.cs for more info.")]
	public UdonBehaviour callbackScript;

	/**
	 * When enabled, the portal will attempt to align the player's
	 * momentum/velocity to the orientation of the portal when the
	 * player travels through it. For portals that point close to
	 * vertically (i.e. almost flat on the ground or on the ceiling),
	 * using this setting will pretend the portal is perfectly axis-
	 * aligned.
	 *
	 * This setting is useful for portals that can be arbitrarily
	 * moved by the user (i.e. pickups), because placing the portal
	 * perfectly flat with your hand in VR is nearly impossible, so
	 * this makes it pretend like they did.
	 *
	 * This setting is also good for infinite falling loops or infinite
	 * bouncing between two portals on the floor, because it will
	 * cancel out horizontal momentum, making it easier for the player
	 * to stay in the portal.
	 *
	 * The vertical snapping takes effect when the player is looking
	 * at the portal. That way, once the player wants to leave the
	 * infinite fall/bounce and they look away and start to move,
	 * the portal won't cancel out their horizontal momentum and make
	 * it hard to leave.
	 *
	 * If a portal is on a wall, like a door, this setting can be
	 * turned off because it will have nearly no effect.
	 */
	[Tooltip("When enabled, the portal attempts to align the player's momentum to the portal orientation when traveling through it. Additionally it has some extra snapping behavior for flat portals (i.e. on the floor or ceiling) to make infinite falls or infinite bouncing easier on the player. If a portal is on a wall, like a door, this setting can be turned off because it will have nearly no effect.")]
	public bool momentumSnapping = false;


	// ========================================================================
	// ADVANCED PUBLIC PROPERTIES
	// ========================================================================

	/**
	 * Portal surface textures. In VR, both Left and Right textures are used.
	 * In Desktop, only the Left texture is used. These should be set to a
	 * dummy RenderTexture asset, unique for each portal. (Only needed
	 * because Udon can't instantiate/construct RenderTextures at runtime!)
	 *
	 * You should not change these at runtime. If you need to for some reason,
	 * call RefreshTextures() after changing them. Don't set them to null.
	 */
	[Tooltip("The render texture used for the left eye in VR, or the entire view in Desktop.")]
	public RenderTexture viewTexL;
	[Tooltip("The render texture used for the right eye in VR.")]
	public RenderTexture viewTexR;

	/**
	 * The portal teleports the player when their head tracking point crosses
	 * the portal surface plane. This value shifts that plane (for the purposes
	 * of teleporting). Positive values move the plane "out" of the portal, so
	 * you teleport earlier; negative values move the plane "into" the portal,
	 * so you teleport later.
	 *
	 * If your portal mesh is a flat plane, it is helpful to have a slight
	 * positive offset (about 0.03 is good) for this value so the player
	 * teleports slightly before their head crosses the portal surface. This
	 * reduces the chance of part of their field-of-view from clipping through
	 * the portal surface for 1 frame before getting teleported, causing an
	 * annoying "flash". The downside is that if the player walks through the
	 * portal backwards, the larger teleportPlaneOffset will mean they'll
	 * teleport too early and they'll see more of a flash! There isn't a
	 * good way to fix this, which is why I recommend not using a flat mesh.
	 *
	 * If your portal mesh is not flat but an inverted cube shape (recommended)
	 * then this should be left at 0.
	 */
	[Tooltip("The distance from the portal surface where the player will teleport when their head crosses. Read this property's documentation in PortalBehaviour.cs before changing!")]
	public float teleportPlaneOffset = 0f;

	/**
	 * Holoport locomotion breaks the normal method for detecting walking
	 * through the portal, and the method for teleporting. There is a fix,
	 * but the detection method is a bit of a hack and relies on comparing
	 * avatar bone positions to tracking positions. I'm worried that it could
	 * possibly cause problems in certain weird use cases (situations where
	 * an avatar is moved separately from the viewpoint -- e.g. MMD worlds)
	 * so this option lets you disable the Holoport fix if needed.
	 */
	[Tooltip("Read this property's documentation in PortalBehaviour.cs before changing!")]
	public bool useHoloportFix = true;

	/**
	 * Normally portals use an "oblique projection matrix" to solve a problem
	 * known as "Banana Juice". It's best described in this video by the team
	 * that made the Portal games: https://youtu.be/ivyseNMVt-4&t=1064
	 *
	 * tl;dw when rendering the virtual portal camera, the entire world behind
	 * the plane of the portal is clipped. Since this plane isn't necessarily
	 * parallel to the view plane of the camera, an "oblique near clipping plane"
	 * is used.
	 *
	 * One downside of using an oblique near clipping plane is it screws with
	 * depth-buffer based effects when viewed through the portal. For example,
	 * caustics in water shaders won't work (I believe it is resolvable by
	 * modifying the water shader to account for the oblique projection, but
	 * I don't know how.)
	 *
	 * If your scene uses depth-buffer-based effects, **and you avoid the
	 * Banana Juice issue by making sure there's nothing behind your portal**
	 * you may try disabling this setting.
	 */
	[Tooltip("Read this property's documentation in PortalBehaviour.cs before changing!")]
	public bool _useObliqueProjection = true;
	// This getter/setter used to do extra stuff, now just here for backwards compatability
	public bool useObliqueProjection {
		get => _useObliqueProjection;
		set { _useObliqueProjection = value; }
	}

	/**
	 * The offset, relative to the portal surface, of the oblique near
	 * clipping plane of the portal camera. Probably leave this at 0.
	 *
	 * Positive values move the plane "out" of the portal; negative
	 * values move the plane "into" the portal.
	 *
	 * Due to inaccuracies in the stereo separation value, portals without
	 * any sort of opaque frame around their edge may have a slightly visible
	 * "gap" at the floor when viewed in VR at some angles. If that happens,
	 * you can set this to a slightly positive value, e.g. 0.02, which
	 * should help. However it has the downside that at very shallow viewing
	 * angles, you may see the wall behind the partner portal, if present.
	 */
	[Tooltip("Read this property's documentation in PortalBehaviour.cs before changing!")]
	public float obliqueClipPlaneOffset = 0f;

	/**
	 * The oblique near clipping plane is (surprise) a near clipping plane, and
	 * as with any normal near clipping plane, the camera cannot be positioned
	 * at or in front of it, otherwise all the careful math involved in rendering
	 * breaks down. And for reasons I don't understand, it seems to break the
	 * the rendering permanently until the camera is reset, even if the camera
	 * moves back behind the clipping plane. Also for reasons I don't fully
	 * understand, the camera needs to be at least a few cm behind the plane to
	 * prevent those issues.
	 *
	 * This value is the distance of the portal camera from the clipping plane
	 * at which oblique clipping is disabled and the camera renders normally.
	 *
	 * If this value is too large, stuff behind the partner portal will pop
	 * into view when you get close to the portal. If this value is too small,
	 * you'll see weird zfighting artifacts through the portal after you walk
	 * through it a few times. 5cm has seemed to work as a good default, but
	 * you may need to adjust. This value should never be negative.
	 */
	[Tooltip("Read this property's documentation in PortalBehaviour.cs before changing!")]
	public float obliqueClipPlaneDisableDist = 0.05f;

	/**
	 * If set to a value > 0, this overrides the stereo separation of the VR
	 * portal camera with a custom value. This is essentially only useful for
	 * demonstrating the effect of stereo separation on VR rendering.
	 */
	[HideInInspector]
	public float manualStereoSeparation = 0f;

	/**
	 * This should be set to the Portal Camera prefab in the RuntimePrefabs
	 * folder of the UdonPortals package. This prefab contains the virtual
	 * head camera. It gets instantiated when the portal is enabled, and is
	 * destroyed when disabled.
	 */
	[Tooltip("Leave this as the default PortalCamera prefab asset unless you know what you're doing!")]
	public GameObject portalCameraPrefab;

	/**
	 * PortalCamera prefab instance. You can set this manually if you need to
	 * make some special customization to the portal camera object for only
	 * one portal.
	 */
	[Tooltip("If left unset, gets automatically set to a generated instance of portalCameraPrefab.")]
	public Transform portalCameraRoot;


	// ========================================================================
	// PRIVATE PROPERTIES
	// ========================================================================

	private bool savePortalCameraRoot;

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
	private new Renderer renderer;
	private Collider trigger;

	// Texture size at the previous frame
	private int widthCache = 0;
	private int heightCache = 0;

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

	private bool texturesNeedRefresh;


	void OnEnable()
	{
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
		} else if (_textureResolution <= 0) {
			Debug.LogError($"Portal '{name}' has texture resolution set to an illegal value ({_textureResolution}).");
			return;
		}

		partnerPortalBehaviour = partner.GetComponent<PortalBehaviour>();

		if (!_noVisuals) {
			renderer = GetComponent<Renderer>();
			if (renderer != null) {
				Material mat = renderer.material;
				if (mat != null) {
					if(!mat.HasProperty("_ViewTexL") || !mat.HasProperty("_ViewTexR")) {
						Debug.LogError($"Portal '{name}' is set to material '{mat.name}' which does not have the _ViewTexL or _ViewTexR properties.");
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

			renderer.material.SetTexture("_ViewTexL", viewTexL);

			if (inVR) {
				renderer.material.SetTexture("_ViewTexR", viewTexR);
				dummyStereoCamera.stereoTargetEye = StereoTargetEyeMask.Both;
			}
		}


		/*
		 * Etc
		 */

		prevInFront = false;
		trackingHeadInTrigger = false;
		isHoloport = false;

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
		renderer = null;
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
	public void SetMaterial(Material mat)
	{
		if (_noVisuals) {
			return;
		} else if (renderer == null) {
			renderer = GetComponent<Renderer>();
			if (renderer != null) {
				renderer.material = mat;
			}
			return;
		} else if (mat == null) {
			renderer.material = null;
			return;
		} else if (!mat.HasProperty("_ViewTexL") || !mat.HasProperty("_ViewTexR")) {
			Debug.LogError($"Attempt to change portal '{name}' material {mat.name} which does not have the _ViewTexL or _ViewTexR properties.");
			return;
		}

		renderer.material = mat;
		mat = renderer.material;

		if(mat.HasProperty("_ViewTexL") && mat.HasProperty("_ViewTexR")) {
			mat.SetTexture("_ViewTexL", viewTexL);
			if (inVR) {
				mat.SetTexture("_ViewTexR", viewTexR);
			}
		} else {
			Debug.LogError($"Portal '{name}' material changed to one that does not have the _ViewTexL or _ViewTexR properties.");
		}
	}

	/**
	 * This should be called if the viewTexL or viewTexR properties are
	 * changed externally while the portal is active.
	 */
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
		if (_noVisuals || !Utilities.IsValid(localPlayer)) {
			return;
		}

		// Note this is not correct: OnWillRenderObject could be called for
		// other cameras in the scene, e.g. the PhotoCamera. But there is
		// no API yet to tell which camera is rendering.
		//   https://feedback.vrchat.com/sdk-bug-reports/p/vrccamerasettings-property-to-tell-which-camera-is-currently-rendering
		VRCCameraSettings renderingCamera = VRCCameraSettings.ScreenCamera;

		int w = renderingCamera.PixelWidth;
		int h = renderingCamera.PixelHeight;
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
		virtualHead.SetPositionAndRotation(renderingCamera.Position, renderingCamera.Rotation);
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
		// 4. Rotate the virtual head 180 degrees around the current portal,
		//    which accounts for the fact that the virtual head looks "out of"
		//    the partner portal. So we're looking from behind the portal now.
		virtualHead.RotateAround(transform.position, transform.up, 180);
		// 5. Finally shift the root, and thus the virtual head, to the position
		//    and rotation of the partner portal. The virtual head camera is now
		//    in its final position for rendering the portal view.
		portalCameraRoot.SetPositionAndRotation(partner.position, partner.rotation);

		// Copy properties from the rendering camera
		portalCameraL.aspect              = renderingCamera.Aspect;
		portalCameraL.nearClipPlane       = renderingCamera.NearClipPlane;
		portalCameraL.farClipPlane        = renderingCamera.FarClipPlane;
		portalCameraL.fieldOfView         = renderingCamera.FieldOfView;
		portalCameraL.allowHDR            = renderingCamera.AllowHDR;
		portalCameraL.backgroundColor     = renderingCamera.BackgroundColor;
		portalCameraL.clearFlags          = renderingCamera.ClearFlags;
		portalCameraL.useOcclusionCulling = renderingCamera.UseOcclusionCulling;

		if (inVR) {
			portalCameraR.aspect              = renderingCamera.Aspect;
			portalCameraR.nearClipPlane       = renderingCamera.NearClipPlane;
			portalCameraR.farClipPlane        = renderingCamera.FarClipPlane;
			portalCameraR.fieldOfView         = renderingCamera.FieldOfView;
			portalCameraR.allowHDR            = renderingCamera.AllowHDR;
			portalCameraR.backgroundColor     = renderingCamera.BackgroundColor;
			portalCameraR.clearFlags          = renderingCamera.ClearFlags;
			portalCameraR.useOcclusionCulling = renderingCamera.UseOcclusionCulling;

			// Use the dummy camera to get the per-eye stereo projection matrices.
			// Copy the properties from the rendering camera to the dummy camera so
			// that the matrices are computed correctly.
			dummyStereoCamera.aspect          = renderingCamera.Aspect;
			dummyStereoCamera.nearClipPlane   = renderingCamera.NearClipPlane;
			dummyStereoCamera.farClipPlane    = renderingCamera.FarClipPlane;
			dummyStereoCamera.fieldOfView     = renderingCamera.FieldOfView;
			portalCameraL.projectionMatrix    = dummyStereoCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
			portalCameraR.projectionMatrix    = dummyStereoCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);
		}
		else {
			portalCameraL.ResetProjectionMatrix();
		}

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
				if (renderer != null && renderer.material != null) {
					renderer.material.SetTexture("_ViewTexL", null);
					renderer.material.SetTexture("_ViewTexR", null);
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
		if (!player.isLocal || !Utilities.IsValid(localPlayer)) {
			return;
		}
		// 'trackingHeadInTrigger' is used for rendering when _useObliqueProjection is on
		if (_noPhysics && !_useObliqueProjection) {
			return;
		}

		VRCPlayerApi.TrackingData trackingHead = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);

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
			Vector3 holoportHeadPos = localPlayer.GetBonePosition(HumanBodyBones.Head);
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

			if (prevInFront && !inFront) {
				// Player crossed from front to back, teleport them

				// Calling TeleportTo from a FixedUpdate event like OnPlayerTriggerStay
				// causes some "ghosting" -- it appears like you can see yourself
				// through the portal one frame in advance, or something like that.
				// Delaying until Update makes it go away.
				SendCustomEventDelayedFrames("_TeleportPlayer", 0, VRC.Udon.Common.Enums.EventTiming.Update);
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
		if (callbackScript != null) {
			callbackScript.SetProgramVariable("sourcePortal", this);
			callbackScript.SendCustomEvent("_PortalWillTeleportPlayer");
		}

		if (partnerPortalBehaviour != null) {
			partnerPortalBehaviour.OnWillReceivePlayer();
		}

		VRCPlayerApi.TrackingData trackingHead = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);

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
		Vector3 playerOriginPos;
		Quaternion playerOriginRot;
		Vector3 playerHeadPos;
		if (isHoloport) {
			VRCPlayerApi.TrackingData avatarRoot = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.AvatarRoot);
			playerOriginPos = avatarRoot.position;
			playerOriginRot = avatarRoot.rotation;
			playerHeadPos = localPlayer.GetBonePosition(HumanBodyBones.Head);
		}
		else {
			VRCPlayerApi.TrackingData trackingOrigin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
			playerOriginPos = trackingOrigin.position;
			playerOriginRot = trackingOrigin.rotation;
			playerHeadPos = trackingHead.position;
		}

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
		float playerY = playerOriginRot.eulerAngles.y;
		float newY = playerY + diffY;
		Quaternion newRot = Quaternion.Euler(0, newY, 0);

		// From the newHeadPos and rotation delta, calculate the new origin
		// position which is what we need for teleporting.
		Vector3 headToOrigin = playerOriginPos - playerHeadPos;
		headToOrigin = Quaternion.AngleAxis(diffY, Vector3.up) * headToOrigin;
		Vector3 newPos = newHeadPos + headToOrigin;

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
		Vector3 newVel = localToPartnerRot * localVel;

		// Do teleport
		#if UNITY_EDITOR
			// AlignRoomWithSpawnPoint doesn't work properly in ClientSim
			localPlayer.TeleportTo(
				newPos,
				newRot,
				VRC.SDKBase.VRC_SceneDescriptor.SpawnOrientation.AlignPlayerWithSpawnPoint,
				/*lerpOnRemote=*/false);
		#else
			if (!isHoloport) {
				localPlayer.TeleportTo(
					newPos,
					newRot,
					VRC.SDKBase.VRC_SceneDescriptor.SpawnOrientation.AlignRoomWithSpawnPoint,
					/*lerpOnRemote=*/false);
			}
			else {
				localPlayer.TeleportTo(
					newPos,
					newRot,
					VRC.SDKBase.VRC_SceneDescriptor.SpawnOrientation.AlignPlayerWithSpawnPoint,
					/*lerpOnRemote=*/false);
			}
		#endif

		localPlayer.SetVelocity(newVel);
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
		if (prevBody != null && prevBody == collider.attachedRigidbody) {
			prevBody = null;
		}
	}

	public void OnTriggerStay(Collider collider)
	{
		if (_noPhysics || !Utilities.IsValid(localPlayer)) {
			return;
		}

		Rigidbody body = collider.attachedRigidbody;
		if (body == null || body.isKinematic) {
			return;
		}

		if (prevBody != null && prevBody != body) {
			return;
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

			if (callbackScript != null) {
				callbackScript.SetProgramVariable("sourcePortal", this);
				callbackScript.SetProgramVariable("teleportedObject", body);
				callbackScript.SendCustomEvent("_PortalWillTeleportObject");
			}

			Vector3 localPos = transform.InverseTransformPoint(body.position);
			localPos = Quaternion.AngleAxis(180, Vector3.up) * localPos;
			Vector3 newPos = partner.TransformPoint(localPos);

			Quaternion partner180 = Quaternion.AngleAxis(180, partner.up);
			Quaternion rotDiff = partner.rotation * Quaternion.Inverse(transform.rotation);
			Quaternion newRot = partner180 * rotDiff * body.rotation;

			Vector3 localVel = transform.InverseTransformDirection(body.velocity);
			Vector3 newVel = partner180 * partner.TransformDirection(localVel);

			body.position = newPos;
			body.rotation = newRot;
			body.velocity = newVel;

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
		if (val <= 0) {
			Debug.LogError($"Attempt to set texture resolution on portal '{name}' to an illegal value ({val}).");
			return;
		}
		_textureResolution = val;
		texturesNeedRefresh = true;
	}

	// Called by the partner portal (if the partner is a PortalBehaviour)
	// when it is about to teleport the player here.
	public void OnWillReceivePlayer() {
		// After being teleported here, there may be a few frames of
		// rendering the portal before OnPlayerTriggerStay is called
		// to update `trackingHeadInTrigger`. That can lead to flicker
		// due to wrongly using an oblique near plane while an eye
		// is still in the portal. Set this true assuming that the
		// head will always be in the trigger after teleporting.
		trackingHeadInTrigger = true;
	}
}
