// This FOV detection script was originally created by Esska and was
// modified for use in UdonPortals and redistributed with permission.
// The original script is here: https://github.com/Ess-Ka/EsskaFOVDetector
//
// ==== WARNING ====
// This GameObject must be on a layer that ONLY the main player camera
// can see. That is done by setting the layer to PlayerLocal. If you
// have any Cameras in your world that render the PlayerLocal layer,
// **THIS SCRIPT WILL BREAK** (while that Camera looks at the player).
//
// ==== ABOUT ====
// In Udon, the player's FOV *in VR* can be easily detected by reading
// the fieldOfView property of a Camera that has its "Target Eye" set
// to non-None. Unfortunately, there is no similar way to detect FOV
// in Desktop play.
// This script implements FOV detection for desktop players by placing
// a small cube in front of the head with a specific distance and angle.
// The cube is moved one degree each frame until it has either entered
// or exited the view frustum (has determined by OnWillRenderObject),
// which tells us the FOV.
// If you think this is silly, updoot this canny :)
// https://feedback.vrchat.com/udon/p/give-us-an-interface-to-the-main-camera-via-the-vrcgraphics-class
//
// ==== USAGE ====
// The detection will automatically run every <detectInterval> seconds
// as long as the FOVDetector GameObject is active. Other scripts can
// register for an OnFOVChanged event by calling the function <Register>.
// Other scripts can do a manual one-off detection by calling <StartOneDetection>.
//
// ==== OPTIMIZATION ====
// To avoid unnecessary CPU usage, I recommend activating the FOVDetector
// GameObject only when the player is near a portal. Ideally, turn the detector
// on a few seconds before the player is able to see a portal (e.g. activate
// it with a trigger collider outside the door to the room with the portal).
// You can leave the detector on all the time if that is appropriate for the
// world (e.g. lots of portals).
//
// Author: Esska & aurycat
// License: MIT
// History:
//  1.3 (2023-11-12): Introduced in Udon Portals v1.3


/// <summary>
/// Uncomment to enable debug output.
/// </summary>
// #define FOVD_DEBUG

/// <summary>
/// Comment this #define to make the FOVDetector work in ClientSim
/// (playmode) instead of just instantly returning 60°.
/// -------
/// WARNING: If the Scene View camera looks at the FOV Detector object
/// while it is detecting, it will *NOT WORK*. Either close the Scene View
/// tab while in play mode, or set the FOVDetector object to "hidden"
/// in the hierarchy so the Scene View camera doesn't render it
/// (click the eye icon to the left of the name in Hierarchy tab)
/// </summary>
#define DISABLE_IN_CLIENTSIM

using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class UdonPortalsFOVDetector : UdonSharpBehaviour {

    #region Constants

    private const int FOV_DEFAULT = 60; // Default value VRC uses
    private const int FOV_MIN = 50;     // Minimum value VRC lets you choose in desktop
    private const int FOV_MAX = 100;    // Maximum value VRC lets you choose in desktop
    private const float DETECT_DISTANCE = 0.2f;

    #endregion

    #region Properies

    /// <summary>
    /// Returns true as long FOV detection runs.
    /// </summary>
    public bool Detecting { get; private set; }

    /// <summary>
    /// Contains the detected FOV as soon the detection has finished.
    /// </summary>
    public int DetectedFOV { get; private set; } = FOV_DEFAULT;

    #endregion

    #region Fields

    [Header("FOV Detection")]
    [Tooltip("Interval in seconds at which FOV detection runs when the game object is enabled.")]
    [Range(2f, 30f)]
    public float detectInterval = 4f;

    [Header("UI")]
    [Tooltip("Optional text on which status and detectecd FOV will be shown.'")]
#if !FOVD_DEBUG
    [HideInInspector]
#endif
    public Text FOVText;

    #endregion

    #region Runtime Variables

    private VRCPlayerApi playerApi;
    private MeshRenderer meshRenderer;
    private UdonSharpBehaviour[] onFOVChangedReceivers = new UdonSharpBehaviour[0];
    private bool doingManualDetect = false;
    private int detectFOV = FOV_DEFAULT;
    private bool firstDetect = true;
    private int state = 0;
    private bool rendered = false;
    private bool doubleCheck = false;

    #endregion

    #region Event Methods

    void Start() {
        // FOVDetector needs to be on PlayerLocal
        // See "==== WARNING ====" at top of file for explanation.
        gameObject.layer = 10;

        playerApi = Networking.LocalPlayer;
        meshRenderer = GetComponent<MeshRenderer>();
        StartDetection(false);
    }

    public override void PostLateUpdate() {

        if (!Detecting) {
            return;
        }

        if ( state == 0 ) { // First frame of detection
            if (detectFOV < FOV_MIN || detectFOV > FOV_MAX) {
                detectFOV = FOV_DEFAULT;
            }
            state = 1;
        }
        else if ( state == 1 ) { // Second frame of detection
            if ( rendered ) {
                // OnWillRenderObject was called since last PostLastUpdate.
                // transform is in view. Increase the FOV.
                detectFOV++;
                state = 3;
            }
            else {
                // OnWillRenderObject was not called since last PostLastUpdate
                // transform is presumably not in view. Decrease the FOV.
                detectFOV--;
                state = 2;
            }
        }
        else if ( state == 2 ) { // Decrement until in-view
            if ( rendered ) {
                // In view, FOV found!
                TerminateDetection(true);
                return;
            }
            else {
                detectFOV--;
            }
        }
        else if ( state == 3 ) { // Increment until out-of-view
            if ( rendered ) {
                detectFOV++;
            }
            else {
                // Out of view, FOV found!
                detectFOV--; // Decrement one to bring it back into view
                TerminateDetection(true);
                return;
            }
        }
        else {
            // Invalid state somehow
            TerminateDetection(false);
            return;
        }

        if (detectFOV < FOV_MIN || detectFOV > FOV_MAX+1) {
            TerminateDetection(false);
            return;
        }

        #if FOVD_DEBUG
            Debug.Log($"state:{state}, rendered:{rendered}, new detectFOV:{detectFOV}");
        #endif

        VRCPlayerApi.TrackingData head = playerApi.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        transform.SetPositionAndRotation(head.position, head.rotation);
        transform.Rotate(detectFOV / 2f, 0f, 0f, Space.Self);
        transform.Translate(0f, 0f, DETECT_DISTANCE, Space.Self);
        rendered = false;
    }

    private void OnWillRenderObject() {
        rendered = true;
    }

#endregion

#region FOV Detection

    /// <summary>
    /// Manually starts one FOV detection cycle.
    /// The automatic interval detection is not affected.
    /// </summary>
    public void StartOneDetection() {
        StartDetection(true);
    }

    /// <summary>
    /// Registers any <see cref="UdonSharpBehaviour"/> on which an OnFOVChanged method will be called when FOV changes.
    /// </summary>
    /// <param name="behavior"></param>
    public void Register(UdonSharpBehaviour behavior) {
        UdonSharpBehaviour[] behaviors = new UdonSharpBehaviour[onFOVChangedReceivers.Length + 1];

        behaviors[behaviors.Length - 1] = behavior;

        for (int i = 0; i < onFOVChangedReceivers.Length; i++) {
            behaviors[i] = onFOVChangedReceivers[i];
        }

        onFOVChangedReceivers = behaviors;
    }

    private void TriggerFOVChangedEvent() {
        #if FOVD_DEBUG
            Debug.Log("TriggerFOVChangedEvent");
        #endif
        foreach (var receiver in onFOVChangedReceivers) {
            receiver.SendCustomEvent("OnFOVChanged");
        }
    }

    private void StartDetection(bool manual) {
        if (Detecting || !Utilities.IsValid(playerApi) ||
            // This script is only intended for use in Desktop. It theoretically
            // works in VR, but the FOV_MAX setting is too low for many HMDs.
            playerApi.IsUserInVR())
        {
            return;
        }
        #if DISABLE_IN_CLIENTSIM && UNITY_EDITOR
            detectFOV = FOV_DEFAULT;
            TerminateDetection(true);
        #else
            Detecting = true;
            doingManualDetect = manual;
            meshRenderer.enabled = true;
            enabled = true;
            rendered = false;
            state = 0;
        #endif
    }

    // Not public API; public only for SendCustomEventDelayedSeconds
    public void _TimedStartDetection() {
        if (Detecting) {
            // Auto/timed-start detect triggered during a manual detect.
            // Unset the manual detect flag so the timer will start again
            // once manual detection is finished.
            doingManualDetect = false;
        } else {
            StartDetection(false);
        }
    }

    private void TerminateDetection(bool success) {
        // In rare cases (e.g. walk into or out of frustum of a camera
        // rendering on PlayerLocal for some reason) a detection can be
        // successful but wrong. So on a successful check where FOV changed,
        // reset and try again. Since detectFOV remains where it was,
        // re-checking on a legit success should only take 3 extra frames.
        if (success && !doubleCheck && (detectFOV != DetectedFOV || firstDetect)) {
            #if !DISABLE_IN_CLIENTSIM || !UNITY_EDITOR
                #if FOVD_DEBUG
                    Debug.Log($"state:{state}, rendered:{rendered}, new detectFOV:{detectFOV}  (terminated; doing double check)");
                #endif
                doubleCheck = true;
                rendered = false;
                state = 0;
                return;
            #endif
        }

        #if FOVD_DEBUG
            Debug.Log($"state:{state}, rendered:{rendered}, new detectFOV:{detectFOV}  (terminated)");
        #endif
        Detecting = false;
        meshRenderer.enabled = false;
        enabled = false;
        rendered = false;
        doubleCheck = false;
        state = 0;
        if (success) {
            #if FOVD_DEBUG
                if (detectFOV != DetectedFOV || firstDetect) {
                    Debug.Log($"DETECTED FOV IS {detectFOV}°");
                }
                if (FOVText != null) {
                    if (detectFOV != DetectedFOV || firstDetect || FOVText.text == "Failed") {
                        FOVText.text = detectFOV.ToString("0°");
                    }
                }
            #endif

            if (detectFOV != DetectedFOV || firstDetect) {
                firstDetect = false;
                DetectedFOV = detectFOV;
                TriggerFOVChangedEvent();
            }
        }
        else {
            #if FOVD_DEBUG
                if (FOVText != null) {
                    FOVText.text = "Failed";
                }
                Debug.Log($"DETECTION OF FOV FAILED");
            #endif
        }

        if (!doingManualDetect) {
            #if !DISABLE_IN_CLIENTSIM || !UNITY_EDITOR
                SendCustomEventDelayedSeconds(nameof(_TimedStartDetection), detectInterval);
            #endif
        }
        doingManualDetect = false;
    }

#endregion
}
