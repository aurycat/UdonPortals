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

	static bool showAdvanced;

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

		GUILayout.Space(10); 
		showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced", true);
		if (showAdvanced) {
			EditorGUI.indentLevel++;
			EditorGUILayout.PropertyField(clipPlaneOffsetProp);
			EditorGUILayout.PropertyField(teleportPlaneOffsetProp);
			EditorGUILayout.PropertyField(viewTexLProp);
			EditorGUILayout.PropertyField(viewTexRProp);
			EditorGUILayout.PropertyField(portalCameraPrefabProp);
			EditorGUILayout.PropertyField(trackingScalePrefabProp);

			GUILayout.Space(15);
			GUILayout.BeginVertical("box");
			EditorGUILayout.LabelField(
				"The following settings control stereo separation behaviour (aka IPD) in VR. " +
				"If the portal is looking weird in VR (e.g. warping when you rotate " +
				"your head), something may be wrong with stereo separation. If that " +
				"happens, read the comment above the <i>stereoSeparationMode</i> property " +
				"in PortalBehaviour.cs for more info. Otherwise, leave these settings alone!",
				style);
			GUILayout.Space(5);
			EditorGUILayout.PropertyField(stereoSeparationModeProp);
			EditorGUILayout.PropertyField(trackingScaleProp);
			EditorGUILayout.PropertyField(manualStereoSeparationProp);
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
