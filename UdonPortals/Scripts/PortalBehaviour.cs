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

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using System;
using UnityEditor;
using VRC.SDK3.Components;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PortalBehaviour : UdonSharpBehaviour
{
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
	 * The world reference camera is used to copy properties such as the
	 * clearflags to the portal cameras. Properties are only updated when
	 * the portal is enabled or when RefreshReferenceCamera() is called.
	 */
	[Tooltip("Set this to the world reference camera to copy its properties to the portal cameras.")]
	public Camera referenceCamera;

	/**
	 * The Unity layers to show through the portal. Default value is to show:
	 *  Default, TransparentFX, Ignore Raycast, Interactive, Player,
	 *  Environment, Pickup, PickupNoEnvironment, Walkthrough, MirrorReflection
	 */
	[Tooltip("Only the selected layers are shown in the portal. Water and PlayerLocal are never shown.")]
	[FieldChangeCallback(nameof(layerMask))]
	public LayerMask _layerMask = 0x66B07;
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
	 * The offset, relative to the portal surface, of the oblique near
	 * clipping plane of the portal camera.
	 *
	 * Due to inaccuracies in the stereo separation value, portals without
	 * any sort of opaque frame around their edge may have a slightly visible
	 * "gap" at the floor when viewed in VR at some angles. If that happens,
	 * you can set this to a slightly negative value, e.g. -0.02, which
	 * should help. However it has the downside that at very shallow viewing
	 * angles, you may see the wall behind the partner portal, if present.
	 */
	[Tooltip("The offset, relative to the portal surface, of the oblique near clipping plane of the portal camera. This should probably be left at 0 except for some special cases.")]
	public float clipPlaneOffset = 0f;

	/**
	 * The portal teleports the player when their head crosses the portal
	 * surface plane. It can be helpful to have a slight positive offset
	 * on the plane so that the player teleports slightly before their head
	 * reaches the portal surface, to prevent clipping through the portal
	 * for 1 frame. Unfortunately, if the player walks backwards through
	 * the portal, then a larger teleportPlaneOffset will make the clipping
	 * worse... but it helps for the common case of walking forward.
	 */
	[Tooltip("The distance from the portal surface where the player will teleport when their head crosses.")]
	public float teleportPlaneOffset = 0.03f;

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
	 * This should be set to the Portal Camera prefab in the RuntimePrefabs
	 * folder of the UdonPortals package. This prefab contains the virtual
	 * head camera. It gets instantiated when the portal is enabled, and is
	 * destroyed when disabled.
	 */
	[Tooltip("Leave this as the default PortalCamera prefab asset unless you know what you're doing :)")]
	public GameObject portalCameraPrefab;

	/**
	 * This should be set to the Tracking Scale prefab in the RuntimePrefabs
	 * folder of the UdonPortals package. The prefab contains an object that
	 * is used to determine the player's avatar's stereo separation (aka IPD
	 * aka distance between the eyes) in VR.
	 */
	[Tooltip("Leave this as the default TrackingScale prefab asset unless you know what you're doing :)")]
	public GameObject trackingScalePrefab;

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
	 * When true, the portal has the same teleporting behavior but does not
	 * show any image or render anything or even create a camera. If all you
	 * want is the physics/teleporting effect of the portal but not the
	 * visuals, use this to save performance by not calculating camera stuff.
	 *
	 * The PortalBehavior component must be disabled and reenabled for changes
	 * of this setting to take effect.
	 */
	[Tooltip("Disables all visual effects of the portal, only the physics & teleporting are applied. The object must be disabled and reenabled for changes to this setting to take effect.")]
	public bool noVisuals = false;

	/**
	 * Normally portals use an "oblique projection matrix" to solve a problem
	 * known as "Banana Juice". It's best described in this video by the team
	 * that made the Portal games: https://youtu.be/ivyseNMVt-4&t=1064
	 *
	 * tl;dw when rendering the virtual portal camera, the entire world behind
	 * the plane of the portal is clipped. Since this plane isn't necessarily
	 * parallel to the view plane of the camera, an oblique near clipping plane
	 * is used.
	 *
	 * The downside of using an oblique near clipping plane is it screws with
	 * depth-buffer based effects when viewed through the portal. For example,
	 * caustics in water shaders won't work (I believe it is resolvable by 
	 * modifying the water shader to account for the oblique projection, but
	 * I don't know how.)
	 *
	 * You can use this setting to disable the oblique projection matrix if
	 * you need to.
	 * IMPORTANT: Only enable this if there is nothing behind the partner
	 * portal -- e.g. the partner portal is floating in air, or placed on an
	 * external wall.
	 */
	[Tooltip("Read documentation for this setting in PortalBehaviour.cs")]
	public bool _useObliqueProjection = true;
	public bool useObliqueProjection {
		get => _useObliqueProjection;
		set { _UpdateUseObliqueProjection(value); }
	}

	// PortalCamera prefab instance.
	[Tooltip("If not set, automatically set to an instance of portalCameraPrefab.")]
	public Transform portalCameraRoot;

	/**
	 * These colliders will be disabled when the player is positioned
	 * to "walk" through the portal. This includes any HORZIONTAL
	 * approaches to the portal. You can use this to turn off walls
	 * behind the portal so the player can walk through the portal.
	 */
	// TODO
	//public Collider[] disableForWalkthrough;

	/**
	 * These colliders will be disabled when the player is positioned
	 * to "fall" (or jump up) through the portal. This includes any
	 * VERTICAL approaches to the portal. You can use this to turn off
	 * floors below the portal (or ceilings above the portal).
	 */
	// TODO
	//public Collider[] disableForFallthrough;


	// ---- PRIVATE PROPERTIES ----

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

	// Whether the local player was previously in front of the portal on the
	// most recent check
	private bool prevInFront = false;

	// Rigidbody that was in front of the portal on the most recent check.
	// If support is added for multiple rigidbodies moving through the
	// portal simultaneously, this will probably need to be an array.
	private Rigidbody prevBody;

	// Local player cache
	private VRCPlayerApi localPlayer;
	private bool inVR;

	// Cache of noVisuals that only changes at OnEnable
	private bool _noVisuals;


	void OnEnable()
	{
		_noVisuals = noVisuals;

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

		if (!_noVisuals) {
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
		trigger.isTrigger = true;

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

	void OnWillRenderObject()
	{
		if (_noVisuals || !Utilities.IsValid(localPlayer)) {
			return;
		}

		VRCPlayerApi.TrackingData playerHead = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);

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
		virtualHead.SetPositionAndRotation(playerHead.position, playerHead.rotation);
		virtualHead.RotateAround(transform.position, transform.up, 180);
		portalCameraRoot.SetPositionAndRotation(partner.position, partner.rotation);

		if (inVR) {
			// Set portal cameras to match dummy cameras' setings
			_ApplyDummyCameraProperties(portalCameraL, dummyCameraL, Camera.StereoscopicEye.Left);
			_ApplyDummyCameraProperties(portalCameraR, dummyCameraR, Camera.StereoscopicEye.Right);
		}

		// Update the projection matrix of the portal cameras to have an oblique
		// projection matrix. This allows everything "behind" the portal to be
		// culled, so that the portal can have stuff behind it without obscuring
		// the view of the cameras. Need to do this once for each eye.
		Vector3 pos = partner.position;
		Vector3 normal = -partner.forward;

		if (_useObliqueProjection) {
			Vector4 clipPlane = _CameraSpacePlane(portalCameraL.worldToCameraMatrix, pos, normal);
			Matrix4x4 projection = portalCameraL.CalculateObliqueMatrix(clipPlane);
			portalCameraL.projectionMatrix = projection;
		}
		portalCameraL.Render();

		if (inVR) {
			if (_useObliqueProjection) {
				Vector4 clipPlane = _CameraSpacePlane(portalCameraR.worldToCameraMatrix, pos, normal);
				Matrix4x4 projection = portalCameraR.CalculateObliqueMatrix(clipPlane);
				portalCameraR.projectionMatrix = projection;
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
	}

	public override void OnPlayerTriggerStay(VRCPlayerApi player)
	{
		if (!player.isLocal || !Utilities.IsValid(localPlayer)) {
			return;
		}

		VRCPlayerApi.TrackingData playerHead = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);

		// Check that the player's head is actually in the trigger, so we don't
		// teleport if the player is merely walking beside the portal. ClosestPoint
		// is the only way to manually check if a point is inside a collider.
		if (trigger.ClosestPoint(playerHead.position) == playerHead.position)
		{
			Plane p = new Plane(-transform.forward, transform.position - (transform.forward*teleportPlaneOffset));
			bool inFront = p.GetSide(playerHead.position);

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

	// Not public API. Only public for calling from SendCustomEventDelayedFrames.
	public void _TeleportPlayer()
	{
		if (callbackScript != null) {
			callbackScript.SetProgramVariable("sourcePortal", this);
			callbackScript.SendCustomEvent("_PortalWillTeleportPlayer");
		}

		VRCPlayerApi.TrackingData origin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);

		// This is calculating the new playspace origin position, not the new
		// location of the player exactly. Since we're using AlignRoomWithSpawnPoint
		// in VR (since that's necessary for correct rotation), calculating new
		// playspace origin makes things easier.
		Vector3 localPos = transform.InverseTransformPoint(origin.position);
		localPos = Quaternion.AngleAxis(180, Vector3.up) * localPos;
		Vector3 newPos = partner.TransformPoint(localPos);

		// Player rotation only cares about Y. In fact, when I tried to
		// do rotation for all axes, like with Rigidbodies below, it
		// didn't work quite right. Not sure why. *shrug*
		Quaternion partner180 = Quaternion.AngleAxis(180, partner.up);
		Quaternion partnerInvRot = partner180 * partner.rotation;
		float inputY = transform.rotation.eulerAngles.y;
		float outputY = partnerInvRot.eulerAngles.y;
		float diffY = outputY - inputY;
		float playerY = origin.rotation.eulerAngles.y;
		float newY = playerY + diffY;
		Quaternion newRot = Quaternion.Euler(0, newY, 0);

		Vector3 playerVel = localPlayer.GetVelocity();
		Vector3 localVel = transform.InverseTransformDirection(playerVel);
		Vector3 newVel = partner180 * partner.TransformDirection(localVel);

		#if UNITY_EDITOR
			// AlignRoomWithSpawnPoint doesn't work properly in ClientSim
			localPlayer.TeleportTo(
				newPos,
				newRot,
				VRC.SDKBase.VRC_SceneDescriptor.SpawnOrientation.AlignPlayerWithSpawnPoint,
				/*lerpOnRemote=*/false);
		#else
			localPlayer.TeleportTo(
				newPos,
				newRot,
				VRC.SDKBase.VRC_SceneDescriptor.SpawnOrientation.AlignRoomWithSpawnPoint,
				/*lerpOnRemote=*/false);
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
		if (!Utilities.IsValid(localPlayer)) {
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
			prevBody = null;
			return;
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
	}

	private void _ApplyReferenceCamera(Camera cam)
	{
		if (referenceCamera != null) {
			cam.allowHDR            = referenceCamera.allowHDR;
			cam.backgroundColor     = referenceCamera.backgroundColor;
			cam.clearFlags          = referenceCamera.clearFlags;
			cam.useOcclusionCulling = referenceCamera.useOcclusionCulling;
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
		Vector3 offsetPos = pos + normal * clipPlaneOffset;
		Vector3 cpos = m.MultiplyPoint(offsetPos);
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

	public void _UpdateUseObliqueProjection(bool val)
	{
		if (!inVR && !val && portalCameraL != null) {
			portalCameraL.farClipPlane     = dummyCameraL.farClipPlane;
			portalCameraL.nearClipPlane    = dummyCameraL.nearClipPlane;
			portalCameraL.fieldOfView      = dummyCameraL.fieldOfView;
			portalCameraL.aspect           = dummyCameraL.aspect;
			portalCameraL.ResetProjectionMatrix();
		}
		_useObliqueProjection = val;
	}
}
