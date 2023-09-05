using UnityEngine;
using System.Collections;
using UnityEditor;
//using UdonSharpEditor;

[CustomEditor(typeof(PortalBehaviour))]
[CanEditMultipleObjects]
public class PortalBehaviourEditor : Editor
{
	SerializedProperty partnerProp;
	SerializedProperty referenceCameraProp;
	SerializedProperty layerMaskProp;
	SerializedProperty textureResolutionProp;
	SerializedProperty callbackScriptProp;
	SerializedProperty viewTexLProp;
	SerializedProperty viewTexRProp;
	SerializedProperty clipPlaneOffsetProp;
	SerializedProperty teleportPlaneOffsetProp;
	SerializedProperty portalCameraPrefabProp;
	SerializedProperty trackingScalePrefabProp;
	SerializedProperty stereoSeparationModeProp;
	SerializedProperty trackingScaleProp;
	SerializedProperty manualStereoSeparationProp;
	SerializedProperty noVisualsProp;
	SerializedProperty useObliqueProjectionProp;
	SerializedProperty portalCameraRootProp;

	static bool showAdvanced;
	static bool showExtraAdvanced;

	void OnEnable()
	{
		partnerProp = serializedObject.FindProperty("partner");
		referenceCameraProp = serializedObject.FindProperty("referenceCamera");
		layerMaskProp = serializedObject.FindProperty("_layerMask");
		textureResolutionProp = serializedObject.FindProperty("_textureResolution");
		callbackScriptProp = serializedObject.FindProperty("callbackScript");
		viewTexLProp = serializedObject.FindProperty("viewTexL");
		viewTexRProp = serializedObject.FindProperty("viewTexR");
		clipPlaneOffsetProp = serializedObject.FindProperty("clipPlaneOffset");
		teleportPlaneOffsetProp = serializedObject.FindProperty("teleportPlaneOffset");
		portalCameraPrefabProp = serializedObject.FindProperty("portalCameraPrefab");
		trackingScalePrefabProp = serializedObject.FindProperty("trackingScalePrefab");
		stereoSeparationModeProp = serializedObject.FindProperty("stereoSeparationMode");
		trackingScaleProp = serializedObject.FindProperty("trackingScale");
		manualStereoSeparationProp = serializedObject.FindProperty("manualStereoSeparation");
		noVisualsProp = serializedObject.FindProperty("noVisuals");
		useObliqueProjectionProp = serializedObject.FindProperty("_useObliqueProjection");
		portalCameraRootProp = serializedObject.FindProperty("portalCameraRoot");
	}

	public override void OnInspectorGUI()
	{
		GUIStyle style = new GUIStyle(GUI.skin.label);
		style.wordWrap = true;
		style.richText = true;

		GameObject prefab = null;
		foreach (PortalBehaviour p in targets) {
			if (p.portalCameraPrefab == null) {
				if (prefab == null) {
					string path = AssetDatabase.GUIDToAssetPath("2a3f08df2e7a397438e5f2f7fd0505a2");
					prefab = (path != null) ? AssetDatabase.LoadMainAssetAtPath(path) as GameObject : null;
				}
				if (prefab != null) {
					p.portalCameraPrefab = prefab;
					EditorUtility.SetDirty(p);
				} else {
					GUILayout.Space(5);
					EditorGUILayout.HelpBox("Missing reference to prefab asset PortalCamera. The asset cannot be found. Try reimporting the UdonPortals package?", MessageType.Error);
					GUILayout.Space(15);
					break;
				}
			}
		}

		prefab = null;
		foreach (PortalBehaviour p in targets) {
			if (p.trackingScalePrefab == null) {
				if (prefab == null) {
					string path = AssetDatabase.GUIDToAssetPath("1b63f9fde77da584baa2de88791f392a");
					prefab = (path != null) ? AssetDatabase.LoadMainAssetAtPath(path) as GameObject : null;
				}
				if (prefab != null) {
					p.trackingScalePrefab = prefab;
					EditorUtility.SetDirty(p);
				} else {
					GUILayout.Space(5);
					EditorGUILayout.HelpBox("Missing reference to prefab asset TrackingScale. The asset cannot be found. Try reimporting the UdonPortals package?", MessageType.Error);
					GUILayout.Space(15);
					break;
				}
			}
		}

		foreach (PortalBehaviour p in targets) {
			if (p.viewTexL == null || p.viewTexR == null) {
				GUILayout.Space(5);
				EditorGUILayout.HelpBox("This portal is missing one or both of its Render Textures. Each portal must have two unique Render Texture assets assigned to it. Would you like to create them automatically? You can also set them manually in the 'Advanced' dropdown below.", MessageType.Error);
				if (GUILayout.Button("Generate Render Textures")) {
					GenerateRenderTextures();
				}
				GUILayout.Space(15);
				break;
			}
		}

		GUILayout.Space(5);

		EditorGUILayout.PropertyField(partnerProp,
			new GUIContent("Partner Portal"));
		if (!serializedObject.isEditingMultipleObjects && partnerProp.objectReferenceValue == null) {
			EditorGUILayout.HelpBox("A partner transform is required. Usually you should set this another portal object.", MessageType.Error);
			GUILayout.Space(10);
		}

		EditorGUILayout.PropertyField(referenceCameraProp);
		if (!serializedObject.isEditingMultipleObjects && referenceCameraProp.objectReferenceValue == null) {
			EditorGUILayout.HelpBox("Set this to your world's reference camera (Main Camera).", MessageType.Warning);
			GUILayout.Space(10);
		}

		EditorGUILayout.PropertyField(layerMaskProp);
		EditorGUILayout.PropertyField(textureResolutionProp);
		EditorGUILayout.PropertyField(callbackScriptProp);

		GUILayout.Space(5);
		showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced", true);
		if (showAdvanced) {
			EditorGUI.indentLevel++;
			EditorGUILayout.HelpBox("Hover your mouse over each property name to see information about its usage.", MessageType.Info);
			GUILayout.Space(10);
			EditorGUILayout.PropertyField(clipPlaneOffsetProp);
			EditorGUILayout.PropertyField(teleportPlaneOffsetProp);
			EditorGUILayout.PropertyField(noVisualsProp);
			EditorGUILayout.PropertyField(viewTexLProp);
			EditorGUILayout.PropertyField(viewTexRProp);
			EditorGUI.indentLevel--;
		}

		GUILayout.Space(5);
		showExtraAdvanced = EditorGUILayout.Foldout(showExtraAdvanced, "Extra Advanced", true);
		if (showExtraAdvanced) {
			EditorGUI.indentLevel++;

			GUILayout.Space(5);
			GUILayout.BeginVertical("box");
			GUILayout.Space(5);
			EditorGUILayout.HelpBox(
				"Using oblique projection can interfere with some shader effects such " +
				"as caustics in water. REGARDLESS, you should probably leave this enabled. " +
				"Read the pros & cons above the 'useObliqueProjection' property " +
				"in PortalBehavior.cs before changing this.",
				MessageType.Info);
			GUILayout.Space(5);
			EditorGUILayout.PropertyField(useObliqueProjectionProp);
			if (!serializedObject.isEditingMultipleObjects) {
				if (useObliqueProjectionProp.boolValue != true) {
					EditorGUILayout.HelpBox("This setting is not at its default value. The portal may not function as expected!", MessageType.Warning);
					GUILayout.Space(5);
				}
			}
			GUILayout.Space(5);
			GUILayout.EndVertical();

			GUILayout.Space(15);
			GUILayout.BeginVertical("box");
			GUILayout.Space(5);
			EditorGUILayout.HelpBox(
				"The following settings control stereo separation behaviour (aka IPD) in VR. " +
				"If the portal is looking weird in VR (e.g. warping when you rotate " +
				"your head), something may be wrong with stereo separation. If that " +
				"happens, read the comment above the 'stereoSeparationMode' property " +
				"in PortalBehaviour.cs for more info. Otherwise, don't touch these!",
				MessageType.Info);
			GUILayout.Space(5);
			EditorGUILayout.PropertyField(stereoSeparationModeProp);
			EditorGUILayout.PropertyField(trackingScaleProp);
			EditorGUILayout.PropertyField(manualStereoSeparationProp);
			if (!serializedObject.isEditingMultipleObjects) {
				if (stereoSeparationModeProp.intValue < 0 || stereoSeparationModeProp.intValue > 2) {
					EditorGUILayout.HelpBox("Stereo Separation Mode must be between 0 and 2.", MessageType.Error);
					GUILayout.Space(5);
				}
				else if (stereoSeparationModeProp.intValue != 0 || trackingScaleProp.objectReferenceValue != null) {
					EditorGUILayout.HelpBox("These settings are not at their default value. You should probably not change these. Make sure to read the documentation for 'stereoSeparationMode' in PortalBehavior.cs if you do.", MessageType.Warning);
					GUILayout.Space(5);
				}
			}
			GUILayout.Space(5);
			GUILayout.EndVertical();

			GUILayout.Space(15);
			GUILayout.BeginVertical("box");
			GUILayout.Space(5);
			EditorGUILayout.HelpBox(
				"These are automatically set. You shouldn't change these.",
				MessageType.Info);
			GUILayout.Space(5);
			EditorGUILayout.PropertyField(portalCameraPrefabProp);
			EditorGUILayout.PropertyField(trackingScalePrefabProp);
			EditorGUILayout.PropertyField(portalCameraRootProp);
			if (!serializedObject.isEditingMultipleObjects) {
				if (portalCameraRootProp.objectReferenceValue != null) {
					EditorGUILayout.HelpBox("Portal Camera Root is automatically set to an instance of Portal Camera Prefab at runtime. Leave this unset unless you have a manually-instantiated version of the Portal Camera Prefab that you want to use for some reason.", MessageType.Warning);
					GUILayout.Space(5);
				}
			}
			GUILayout.Space(5);
			GUILayout.EndVertical();

			EditorGUI.indentLevel--;
		}
		GUILayout.Space(5);

		serializedObject.ApplyModifiedProperties();
	}

	void GenerateRenderTextures()
	{
		foreach(PortalBehaviour p in targets) {
			if (p.viewTexL == null) {
				p.viewTexL = GenerateOneRenderTexture(p.name + "-L");
				EditorUtility.SetDirty(p);
			}
			if (p.viewTexR == null) {
				p.viewTexR = GenerateOneRenderTexture(p.name + "-R");
				EditorUtility.SetDirty(p);
			}
		}
	}

	RenderTexture GenerateOneRenderTexture(string name)
	{
		if (!AssetDatabase.IsValidFolder("Assets/PortalRenderTextures")) {
			AssetDatabase.CreateFolder("Assets", "PortalRenderTextures");
		}
		// Create a render texture the same as "Create > Render Texture" would do
		RenderTextureDescriptor desc = new RenderTextureDescriptor(256, 256, RenderTextureFormat.ARGB32, 24);
		desc.sRGB = false;
		RenderTexture tex = new RenderTexture(desc);
		AssetDatabase.CreateAsset(tex, "Assets/PortalRenderTextures/" + name + ".renderTexture");
		return tex;
	}
}
