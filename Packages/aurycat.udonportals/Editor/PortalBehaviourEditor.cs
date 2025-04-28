using UnityEngine;
using System.Collections;
using UnityEditor;
//using UdonSharpEditor;

[CustomEditor(typeof(PortalBehaviour))]
[CanEditMultipleObjects]
public class PortalBehaviourEditor : Editor
{
	SerializedProperty operatingModeProp;
	SerializedProperty partnerProp;
	SerializedProperty layerMaskProp;
	SerializedProperty textureResolutionProp;
	SerializedProperty callbackScriptProp;
	SerializedProperty momentumSnappingProp;

	SerializedProperty useObliqueProjectionProp;
	SerializedProperty obliqueClipPlaneOffsetProp;
	SerializedProperty obliqueClipPlaneDisableDistProp;

	SerializedProperty teleportPlaneOffsetProp;

	SerializedProperty viewTexLProp;
	SerializedProperty viewTexRProp;

	SerializedProperty stereoSeparationModeProp;
	SerializedProperty manualStereoSeparationProp;
	SerializedProperty useHoloportFixProp;

	SerializedProperty portalCameraPrefabProp;
	SerializedProperty portalCameraRootProp;

	static bool showAdvanced;

	void OnEnable()
	{
		operatingModeProp = serializedObject.FindProperty("operatingMode");
		partnerProp = serializedObject.FindProperty("partner");
		layerMaskProp = serializedObject.FindProperty("_layerMask");
		textureResolutionProp = serializedObject.FindProperty("_textureResolution");
		callbackScriptProp = serializedObject.FindProperty("callbackScript");
		momentumSnappingProp = serializedObject.FindProperty("momentumSnapping");

		viewTexLProp = serializedObject.FindProperty("viewTexL");
		viewTexRProp = serializedObject.FindProperty("viewTexR");

		teleportPlaneOffsetProp = serializedObject.FindProperty("teleportPlaneOffset");
		useHoloportFixProp = serializedObject.FindProperty("useHoloportFix");

		useObliqueProjectionProp = serializedObject.FindProperty("_useObliqueProjection");
		obliqueClipPlaneOffsetProp = serializedObject.FindProperty("obliqueClipPlaneOffset");
		obliqueClipPlaneDisableDistProp = serializedObject.FindProperty("obliqueClipPlaneDisableDist");

		stereoSeparationModeProp = serializedObject.FindProperty("stereoSeparationMode");
		manualStereoSeparationProp = serializedObject.FindProperty("manualStereoSeparation");

		portalCameraPrefabProp = serializedObject.FindProperty("portalCameraPrefab");
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

		GUILayout.Space(5);

		EditorGUILayout.PropertyField(operatingModeProp);

		GUILayout.Space(10);

		if (serializedObject.isEditingMultipleObjects || operatingModeProp.enumValueIndex != (int)PortalBehaviourMode.PhysicsOnly) {
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
		}

		EditorGUILayout.PropertyField(partnerProp,
			new GUIContent("Partner Portal"));
		if (!serializedObject.isEditingMultipleObjects && partnerProp.objectReferenceValue == null) {
			EditorGUILayout.HelpBox("A partner transform is required. Usually you should set this to another portal object.", MessageType.Error);
		}
		else if (serializedObject.isEditingMultipleObjects) {
			foreach (PortalBehaviour p in targets) {
				if (p.partner == null) {
					EditorGUILayout.HelpBox("One or more selected portals do not have a partner transform set. Usually you should set these to another portal object.", MessageType.Error);
					break;
				}
			}
		}
		if (serializedObject.isEditingMultipleObjects && targets.Length == 2) {
			PortalBehaviour a = targets[0] as PortalBehaviour;
			PortalBehaviour b = targets[1] as PortalBehaviour;
			if (a != null && b != null) {
				if (a.partner == b.transform && b.partner == a.transform) {
					GUILayout.Label("The two selected portals are paired!", EditorStyles.boldLabel);
				}
				else if (GUILayout.Button("Pair These Two Selected Portals")) {
					Undo.RegisterCompleteObjectUndo(targets, "Pair two portals");
					a.partner = b.transform;
					b.partner = a.transform;
					PrefabUtility.RecordPrefabInstancePropertyModifications(a);
					PrefabUtility.RecordPrefabInstancePropertyModifications(b);
				}
			}
		}

		if (serializedObject.isEditingMultipleObjects || operatingModeProp.enumValueIndex != (int)PortalBehaviourMode.PhysicsOnly) {
			GUILayout.Space(15);
			GUILayout.BeginVertical("box");
			GUILayout.Label("Visuals", EditorStyles.boldLabel);
			GUILayout.Space(10);
			EditorGUILayout.PropertyField(layerMaskProp);
			EditorGUILayout.PropertyField(textureResolutionProp);
			GUILayout.Space(5);
			GUILayout.EndVertical();
		}

		if (serializedObject.isEditingMultipleObjects || operatingModeProp.enumValueIndex != (int)PortalBehaviourMode.VisualsOnly) {
			GUILayout.Space(15);
			GUILayout.BeginVertical("box");
			GUILayout.Label("Physics", EditorStyles.boldLabel);
			GUILayout.Space(10);
			EditorGUILayout.PropertyField(callbackScriptProp);
			EditorGUILayout.PropertyField(momentumSnappingProp);
			GUILayout.Space(5);
			GUILayout.EndVertical();
		}

		GUILayout.Space(10);
		showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced", true);
		if (showAdvanced) {
			EditorGUI.indentLevel++;
			EditorGUILayout.HelpBox("Hover your mouse over each property name to see information about its usage.", MessageType.Info);
			GUILayout.Space(5);

			if (serializedObject.isEditingMultipleObjects || operatingModeProp.enumValueIndex != (int)PortalBehaviourMode.PhysicsOnly) {
				GUILayout.Space(10);
				GUILayout.BeginVertical("box");
				GUILayout.Space(5);
				EditorGUILayout.PropertyField(viewTexLProp);
				EditorGUILayout.PropertyField(viewTexRProp);
				GUILayout.Space(5);
				GUILayout.EndVertical();
				GUILayout.Space(5);
			}

			if (serializedObject.isEditingMultipleObjects || operatingModeProp.enumValueIndex != (int)PortalBehaviourMode.VisualsOnly) {
				GUILayout.Space(10);
				GUILayout.BeginVertical("box");
				GUILayout.Space(5);
				EditorGUILayout.PropertyField(teleportPlaneOffsetProp);
				EditorGUILayout.PropertyField(useHoloportFixProp);
				GUILayout.Space(5);
				GUILayout.EndVertical();
				GUILayout.Space(5);
			}

			if (serializedObject.isEditingMultipleObjects || operatingModeProp.enumValueIndex != (int)PortalBehaviourMode.PhysicsOnly) {
				GUILayout.Space(10);
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
					if (!useObliqueProjectionProp.boolValue) {
						EditorGUILayout.HelpBox("Oblique projection is disabled. Make sure you understand the consequences of that! The portal may not function as expected!", MessageType.Warning);
						GUILayout.Space(5);
					}
				}
				if (serializedObject.isEditingMultipleObjects || useObliqueProjectionProp.boolValue) {
					EditorGUILayout.PropertyField(obliqueClipPlaneOffsetProp);
					EditorGUILayout.PropertyField(obliqueClipPlaneDisableDistProp);
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
				if (serializedObject.isEditingMultipleObjects || stereoSeparationModeProp.intValue == 2) {
					EditorGUILayout.PropertyField(manualStereoSeparationProp);
				}
				if (!serializedObject.isEditingMultipleObjects) {
					if (stereoSeparationModeProp.intValue < 0 || stereoSeparationModeProp.intValue > 2) {
						EditorGUILayout.HelpBox("Stereo Separation Mode must be between 0 and 2.", MessageType.Error);
						GUILayout.Space(5);
					}
					else if (!EditorApplication.isPlaying && stereoSeparationModeProp.intValue != 0) {
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
				EditorGUILayout.PropertyField(portalCameraRootProp);
				if (!serializedObject.isEditingMultipleObjects) {
					if (!EditorApplication.isPlaying && portalCameraRootProp.objectReferenceValue != null) {
						EditorGUILayout.HelpBox("Portal Camera Root is automatically set to an instance of Portal Camera Prefab at runtime. Leave this unset unless you have a manually-instantiated version of the Portal Camera Prefab that you want to use for some reason.", MessageType.Warning);
						GUILayout.Space(5);
					}
				}
				GUILayout.Space(5);
				GUILayout.EndVertical();
			}

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
				PrefabUtility.RecordPrefabInstancePropertyModifications(p);
			}
			if (p.viewTexR == null) {
				p.viewTexR = GenerateOneRenderTexture(p.name + "-R");
				EditorUtility.SetDirty(p);
				PrefabUtility.RecordPrefabInstancePropertyModifications(p);
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
		string path = "Assets/PortalRenderTextures/" + name + ".renderTexture";
		path = AssetDatabase.GenerateUniqueAssetPath(path);
		AssetDatabase.CreateAsset(tex, path);
		return tex;
	}
}
