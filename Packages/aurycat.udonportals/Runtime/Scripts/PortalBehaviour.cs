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
	 * The world reference camera is used to copy properties such as the
	 * clearflags to the portal cameras. Properties are only updated when
	 * the portal is enabled or when RefreshReferenceCamera() is called.
	 */
	[Tooltip("Set this to the world reference camera to copy its properties to the portal cameras.")]
	public Camera referenceCamera;


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

	/**
	 * To support FOV changes in desktop (via the slider in the Graphics settings
	 * menu), you must create an instance of the FOVDetector prefab included with
	 * UdonPortals and put it in this property. This property must be set before
	 * the portal GameObject is first activated (before Start() is called).
	 *
	 * You only need one FOVDetector instance in the world; multiple portals can
	 * share a single instance.
	 *
	 * For more information on the FOVDetector, read the comment at the top of
	 * UdonPortalsFOVDetector.cs.
	 */
	[Tooltip("Set this to an instance of the FOVDetector prefab so the portals can support FOV changes for Desktop players.")]
	public UdonPortalsFOVDetector desktopFOVDetector;


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
	 * In order for the portal to look correct in VR, the player's avatar's
	 * stereo separation (aka IPD, aka distance between eyes) is needed. Note
	 * it's NOT the headset's IPD! The value changes with avatar height.
	 *
	 * The stereo separation value is not provided natively to Udon, but there
	 * are some ways of getting it indirectly. Notably, by finding "tracking
	 * scale", which is the proportion of how much the avatar's head moves in
	 * game compared to how much the user's head moves in their real playspace.
	 * Multiplying the tracking scale by the default stereo separation, which
	 * can be read from the dummy VR cameras, gives us the avatar's IPD.
	 *
	 * There are two ways (that I'm aware of as of this writing) to accurately
	 * get the tracking scale:
	 *
	 *  A. Active GameObjects that have an Audio Listener component have their
	 *     scale set to 1/trackingScale. Don't ask me why.
	 *
	 *  B. Using Merlin's PlayspaceTracking prefab:
	 *     https://github.com/MerlinVR/PlayspaceTracking
	 *     The scale of the HeadRoot object is set to the tracking scale using
	 *     some clever Camera tricks. However this method suffers from floating
	 *     point precision error in large worlds, and also jitters a bit when
	 *     switching avatars.
	 *
	 * Both of these methods are a hack and could easily break at any time with
	 * a VRChat update, but method A is the fastest and most accurate way right
	 * now. Also the Audio Listener is easy to instantiate from a prefab at
	 * runtime. Therefore, that is the default method for UdonPortals.
	 *
	 * But to account for the fact that the Audio Listener mode may break
	 * eventually, this property lets you select a different way to determine
	 * stero separation. The possible values are:
	 *
	 *  0: Default; uses Method A. Stereo separation is computed as:
	 *        (1.0/trackingScale.localScale.x) * default_stereo_separation
	 *     Also, if the 'trackingScale' property is set to null, the TrackingScale
	 *     prefab is automatically instantiated at /PortalTrackingScale if it
	 *     doesn't already exist. 'trackingScale' is then set to that object.
	 *
	 *  1: Stereo separation is computed as:
	 *        trackingScale.localScale.x * default_stereo_separation
	 *     If the default method breaks, use this mode along with Method B,
	 *     Merlin's PlayspaceTracking. Set the 'trackingScale' object to the
	 *     HeadRoot object in Merlin's prefab.
	 *
	 *  2: Stereo separation is taken directly from the 'manualStereoSeparation'
	 *     property.
	 */
	public int stereoSeparationMode = 0;
	public Transform trackingScale = null;
	public float manualStereoSeparation = 0f; // in meters

	/**
	 * The most recent stereo separation value used by the portal. Only updates
	 * when the portal is rendering. (Read only)
	 */
	private float _stereoSeparation = 0f; // in meters
	public float stereoSeparation {
		get => _stereoSeparation;
	}

	/**
	 * This should be set to the Portal Camera prefab in the RuntimePrefabs
	 * folder of the UdonPortals package. This prefab contains the virtual
	 * head camera. It gets instantiated when the portal is enabled, and is
	 * destroyed when disabled.
	 */
	[Tooltip("Leave this as the default PortalCamera prefab asset unless you know what you're doing!")]
	public GameObject portalCameraPrefab;

	/**
	 * This should be set to the Tracking Scale prefab in the RuntimePrefabs
	 * folder of the UdonPortals package. The prefab contains an object that
	 * is used to determine the player's avatar's stereo separation (aka IPD
	 * aka distance between the eyes) in VR.
	 */
	[Tooltip("Leave this as the default TrackingScale prefab asset unless you know what you're doing!")]
	public GameObject trackingScalePrefab;

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

	// These are translated along the X axis to set the stereo separation
	// of the eye cameras.
	private Transform offsetL;
	private Transform offsetR;

	// When there is a Camera that would render directly to the HMD (i.e. the
	// (player is in VR, targetTexture is null, and targetEye is set to Left,
	// Right, or Both), Unity tries to be "helpful" and automatically sets
	// the localPosition and localRotation of the Camera to match the HMD's
	// reported position relative to the playspace origin.
	//
	// That applies to the dummyCameras above, but since we are positioning
	// the dummyCameras manually, we don't want that. Unfortunately Unity
	// doesn't let you turn that off (as far as I know) so the easiest way
	// to set the position is to first "undo" the playspace position by
	// inverting it.
	//
	// A single Transform is evaluated by applying scale, then rotation, then
	// translation. The easiest way to invert that without doing fun Matrix
	// stuff is to just to apply each transformation in reverse order using
	// a hierarchy of GameObjects -- first invert position, then invert
	// rotation. Scale should always be 1 so it doesn't need to be inverted.
	private Transform invertRotationL;
	private Transform invertPositionL;
	private Transform invertRotationR;
	private Transform invertPositionR;

	// These cameras capture the scene at the opposite portal.
	private Camera portalCameraL;
	private Camera portalCameraR;

	// These cameras are used for obtaining VR rendering information, namely
	// the projection matrix necessary for the HMD. They are positioned at the
	// same place as the portal cameras, but aren't enabled and don't render.
	//
	// They are also used in VR and Desktop for determining the size of the
	// screen, which is used to set the size of the RenderTextures.
	//
	// The dummyCameras are needed because the portalCameras, which do the
	// actual rendering, are set to render to a RenderTexture. Unity adjusts
	// certain properties of the camera when the target is a RenderTexture
	// instead of the HMD or main display, which is undesirable. Therefore,
	// properties from the dummy cameras are copied to the rendering cameras.
	private Camera dummyCameraL;
	private Camera dummyCameraR;

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

	// Vertical FOV cached from desktopFOVDetector
	private int desktopFOV = 60;
	private bool registeredFOVDetector;


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

			if (trackingScale == null) {
				if (stereoSeparationMode == 0) {
					trackingScale = InstantiateTrackingScale();
					if (trackingScale == null) {
						Debug.LogError($"Portal '{name}' does not have the Tracking Scale Prefab property set and the GameObject /PortalTrackingScale doesn't exist in the scene. Please set Tracking Scale Prefab to the 'TrackingScale' prefab asset provided in the package.");
						return;
					}
				} else if (stereoSeparationMode == 1) {
					Debug.LogError($"Portal '{name}' does not have the 'trackingScale' property set on stereoSeparationMode=1.");
					return;
				}
			}

			if (referenceCamera == null) {
				Debug.LogWarning($"Portal '{name}' does not have a reference camera set.");
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

			offsetL = virtualHead.Find("OffsetL");
			invertRotationL = offsetL.Find("InvertRotationL");
			invertPositionL = invertRotationL.Find("InvertPositionL");
			dummyCameraL = invertPositionL.Find("DummyCameraL").GetComponent<Camera>();
			portalCameraL = offsetL.Find("PortalCameraL").GetComponent<Camera>();

			offsetR = virtualHead.Find("OffsetR");
			invertRotationR = offsetR.Find("InvertRotationR");
			invertPositionR = invertRotationR.Find("InvertPositionR");
			dummyCameraR = invertPositionR.Find("DummyCameraR").GetComponent<Camera>();
			portalCameraR = offsetR.Find("PortalCameraR").GetComponent<Camera>();

			offsetR.gameObject.SetActive(inVR);

			/*
			 * Get FOV in desktop
			 */
			if (!inVR && !registeredFOVDetector) {
				if (desktopFOVDetector != null) {
					desktopFOVDetector.Register(this);
					registeredFOVDetector = true;
					desktopFOV = desktopFOVDetector.DetectedFOV;
				}
				else {
					Debug.LogWarning($"Portal '{name}' does not have its desktop FOV detector set. The portal will not look correct if the player changes their FOV on desktop.");
				}
			}

			/*
			 * Configure portal cameras.
			 */

			_ResetTransform(offsetL);
			_ResetTransform(offsetR);
			_ResetTransform(virtualHead);

			_ResetCamera(portalCameraL);
			_ResetCamera(portalCameraR);
			_ResetCamera(dummyCameraL);
			_ResetCamera(dummyCameraR);

			_UpdateLayerMask(_layerMask);

			RefreshTextures();

			renderer.material.SetTexture("_ViewTexL", viewTexL);

			if (inVR) {
				renderer.material.SetTexture("_ViewTexR", viewTexR);

				dummyCameraL.stereoTargetEye = StereoTargetEyeMask.Left;
				dummyCameraR.stereoTargetEye = StereoTargetEyeMask.Right;
				_ApplyDummyCameraProperties(portalCameraL, dummyCameraL, Camera.StereoscopicEye.Left);
				_ApplyDummyCameraProperties(portalCameraR, dummyCameraR, Camera.StereoscopicEye.Right);
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

		virtualHead = null;
		offsetL = null;
		offsetR = null;
		invertRotationL = null;
		invertPositionL = null;
		invertRotationR = null;
		invertPositionR = null;
		portalCameraL = null;
		portalCameraR = null;
		dummyCameraL = null;
		dummyCameraR = null;
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
	 * This should be called if the reference camera changes, e.g. if the skybox
	 * changes, while the portal is enabled.
	 */
	public void RefreshReferenceCamera()
	{
		if (portalCameraL != null) {
			_ApplyReferenceCamera(portalCameraL);
			_ApplyReferenceCamera(portalCameraR);
		}
	}

	/**
	 * This should be called when the render textures need to be recreated,
	 * e.g. when their size changes, while the portal is enabled. This should
	 * not need to be called manually, because it is automatically called
	 * when the screen size or textureResolution changes.
	 */
	public void RefreshTextures()
	{
		if (portalCameraL != null) {
			widthCache = (int)dummyCameraL.pixelRect.width;
			heightCache = (int)dummyCameraL.pixelRect.height;

			_SetupTexture(viewTexL, portalCameraL);
			if (inVR) {
				_SetupTexture(viewTexR, portalCameraR);
			}
		}
	}

	/**
	 * Instantiates (or returns it, if already created) the trackingScale
	 * prefab. This object is used to determine the local player's avatar
	 * scale using the "Method A" (Audio Listener) described in the
	 * `stereoSeparationMode` property description above.
	 *
	 * This is called automatically by the portal on enable if
	 * `stereoSeparationMode` is 0, so you shouldn't need to call this
	 * except maybe to use the scale for debug output.
	 */
	public Transform InstantiateTrackingScale()
	{
		GameObject trackingScaleObj = GameObject.Find("/PortalTrackingScale");
		if (trackingScaleObj == null) {
			if (trackingScalePrefab != null) {
				trackingScaleObj = Instantiate(trackingScalePrefab);
				trackingScaleObj.name = "PortalTrackingScale";
			} else {
				return null;
			}
		}
		return trackingScaleObj.transform;
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

		VRCPlayerApi.TrackingData trackingHead = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);

		int w = (int)dummyCameraL.pixelRect.width;
		int h = (int)dummyCameraL.pixelRect.height;
		if (w != widthCache || h != heightCache) {
			RefreshTextures();
		}

		if (inVR) {
			// "Undo" the forced transformation applied to the dummy cameras
			// by Unity. This puts them into the same position/rotation as
			// the portal cameras.
			invertRotationL.localRotation = Quaternion.Inverse(dummyCameraL.transform.localRotation);
			invertPositionL.localPosition = -dummyCameraL.transform.localPosition;
			invertRotationR.localRotation = Quaternion.Inverse(dummyCameraR.transform.localRotation);
			invertPositionR.localPosition = -dummyCameraR.transform.localPosition;

			// Calculate stereo separation. This changes based on avatar size.
			if (stereoSeparationMode == 0) {
				_stereoSeparation = (1f/trackingScale.localScale.x) * dummyCameraL.stereoSeparation;
			} else if (stereoSeparationMode == 1) {
				_stereoSeparation = trackingScale.localScale.x * dummyCameraL.stereoSeparation;
			} else {
				_stereoSeparation = manualStereoSeparation;
			}
			float centerToEyeDist = _stereoSeparation * 0.5f;
			offsetL.localPosition = new Vector3(-centerToEyeDist, 0f, 0f);
			offsetR.localPosition = new Vector3(centerToEyeDist, 0f, 0f);
		}

		// Move the virtual head to its appropriate position relative to the opposite portal.
		portalCameraRoot.SetPositionAndRotation(transform.position, transform.rotation);
		virtualHead.SetPositionAndRotation(trackingHead.position, trackingHead.rotation);
		virtualHead.RotateAround(transform.position, transform.up, 180);
		portalCameraRoot.SetPositionAndRotation(partner.position, partner.rotation);

		if (inVR) {
			// Set portal cameras to match dummy cameras' setings
			_ApplyDummyCameraProperties(portalCameraL, dummyCameraL, Camera.StereoscopicEye.Left);
			_ApplyDummyCameraProperties(portalCameraR, dummyCameraR, Camera.StereoscopicEye.Right);
		}
		else {
			portalCameraL.fieldOfView = desktopFOV;
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
			if (!trackingHeadInTrigger || !ocpDisablePlane.GetSide(offsetL.position)) {
				Vector4 clipPlane = _CameraSpacePlane(portalCameraL.worldToCameraMatrix, ocpPos, ocpNormal);
				Matrix4x4 projection = portalCameraL.CalculateObliqueMatrix(clipPlane);
				portalCameraL.projectionMatrix = projection;
			}
		}
		portalCameraL.Render();

		if (inVR) {
			if (_useObliqueProjection) {
				if (!trackingHeadInTrigger || !ocpDisablePlane.GetSide(offsetR.position)) {
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
		_ApplyReferenceCamera(cam);

		cam.enabled         = false;
		cam.cullingMask     = 0;
		cam.stereoTargetEye = StereoTargetEyeMask.None;
		cam.targetTexture   = null;
		cam.depth           = -10;

		if (!inVR) {
			cam.fieldOfView = desktopFOV;
			cam.ResetProjectionMatrix();
		}
	}

	private void _ApplyReferenceCamera(Camera cam)
	{
		if (referenceCamera != null) {
			cam.allowHDR            = referenceCamera.allowHDR;
			cam.backgroundColor     = referenceCamera.backgroundColor;
			cam.clearFlags          = referenceCamera.clearFlags;
			cam.useOcclusionCulling = referenceCamera.useOcclusionCulling;
			cam.farClipPlane        = referenceCamera.farClipPlane;
			// Clamp reference cam near-plane like VRC does. See ClientSimSceneManager.cs
			cam.nearClipPlane       = Mathf.Clamp(referenceCamera.nearClipPlane, 0.01f, 0.05f);
		}
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

	// Only used in VR
	private void _ApplyDummyCameraProperties(Camera to, Camera frm, Camera.StereoscopicEye eye)
	{
		to.farClipPlane     = frm.farClipPlane;
		to.nearClipPlane    = frm.nearClipPlane;
		to.fieldOfView      = frm.fieldOfView;
		to.aspect           = frm.aspect;
		to.projectionMatrix = frm.GetStereoProjectionMatrix(eye);
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
			dummyCameraL.cullingMask = _layerMask;
			dummyCameraR.cullingMask = _layerMask;
		}
	}

	private void _UpdateTextureResolution(float val)
	{
		if (val <= 0) {
			Debug.LogError($"Attempt to set texture resolution on portal '{name}' to an illegal value ({val}).");
			return;
		}
		_textureResolution = val;
		RefreshTextures();
	}

	// Called from the FOVDetector
	public void OnFOVChanged()
	{
		desktopFOV = desktopFOVDetector.DetectedFOV;
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
