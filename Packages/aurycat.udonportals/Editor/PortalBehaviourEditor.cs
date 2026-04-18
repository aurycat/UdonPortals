using UnityEngine;
using System.Collections;
using UnityEditor;
using System.Collections.Generic;
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
	SerializedProperty activatePartnerOnTeleportProp;
	SerializedProperty deactivateSelfOnTeleportProp;

	SerializedProperty useObliqueProjectionProp;
	SerializedProperty obliqueClipPlaneOffsetProp;
	SerializedProperty obliqueClipPlaneDisableDistProp;

	SerializedProperty teleportPlaneOffsetProp;

	SerializedProperty viewTexLProp;
	SerializedProperty viewTexRProp;

	SerializedProperty useHoloportFixProp;

	SerializedProperty portalCameraPrefabProp;
	SerializedProperty portalCameraRootProp;

	GUIContent renderTextureDocs;
	GUIContent useObliqueProjectionDocs;

	static bool showAdvanced;

	void OnEnable()
	{
		operatingModeProp = serializedObject.FindProperty("operatingMode");
		partnerProp = serializedObject.FindProperty("partner");
		layerMaskProp = serializedObject.FindProperty("_layerMask");
		textureResolutionProp = serializedObject.FindProperty("_textureResolution");
		callbackScriptProp = serializedObject.FindProperty("callbackScript");
		momentumSnappingProp = serializedObject.FindProperty("momentumSnapping");
		activatePartnerOnTeleportProp = serializedObject.FindProperty("activatePartnerOnTeleport");
		deactivateSelfOnTeleportProp = serializedObject.FindProperty("deactivateSelfOnTeleport");

		viewTexLProp = serializedObject.FindProperty("viewTexL");
		viewTexRProp = serializedObject.FindProperty("viewTexR");

		teleportPlaneOffsetProp = serializedObject.FindProperty("teleportPlaneOffset");
		useHoloportFixProp = serializedObject.FindProperty("useHoloportFix");

		useObliqueProjectionProp = serializedObject.FindProperty("_useObliqueProjection");
		obliqueClipPlaneOffsetProp = serializedObject.FindProperty("obliqueClipPlaneOffset");
		obliqueClipPlaneDisableDistProp = serializedObject.FindProperty("obliqueClipPlaneDisableDist");

		portalCameraPrefabProp = serializedObject.FindProperty("portalCameraPrefab");
		portalCameraRootProp = serializedObject.FindProperty("portalCameraRoot");

		renderTextureDocs = new GUIContent("View Tex Documentation");
		useObliqueProjectionDocs = new GUIContent("Oblique Projection Documentation");
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
					GUILayout.Space(10);
					GUILayout.BeginVertical();
					EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
					GUILayout.EndVertical();
					break;
				}
			}
		}

		bool rtwarning = false;
		foreach (PortalBehaviour p in targets) {
			if (p.hasPreV21RenderTextures) {
				if (p.viewTexL == null && p.viewTexR == null) {
					p.hasPreV21RenderTextures = false;
				}
				else if (!rtwarning) {
					rtwarning = true;
					GUILayout.Space(5);
					EditorGUILayout.HelpBox("Starting with UdonPortals v2.1, a portal's render textures are automatically generated at runtime. Unless you need a customized texture format, please clear the 'View Tex L' and 'View Tex R' fields in the Advanced section below. After clearing the fields, you may delete the RenderTexture assets from your project.", MessageType.Warning);
					if (GUILayout.Button("Automatically clear the fields for me")) {
						Undo.RegisterCompleteObjectUndo(targets, "Clear render textures on portal");
						foreach (PortalBehaviour p2 in targets) {
							p2.viewTexL = null;
							p2.viewTexR = null;
							p2.hasPreV21RenderTextures = false;
						}
					}
					if (GUILayout.Button("Automatically clear fields & delete the assets (no undo)")) {
						var paths = new List<string>();
						var outFailedPaths = new List<string>();
						foreach (PortalBehaviour p2 in targets) {
							if (p2.viewTexL != null) {
								paths.Add(AssetDatabase.GetAssetPath(p2.viewTexL));
							}
							if (p2.viewTexR != null) {
								paths.Add(AssetDatabase.GetAssetPath(p2.viewTexR));
							}
							p2.viewTexL = null;
							p2.viewTexR = null;
							p2.hasPreV21RenderTextures = false;
						}
						AssetDatabase.MoveAssetsToTrash(paths.ToArray(), outFailedPaths);
					}
					if (GUILayout.Button("Silence warning")) {
						Undo.RegisterCompleteObjectUndo(targets, "Silence render texture warning");
						foreach (PortalBehaviour p2 in targets) {
							p2.hasPreV21RenderTextures = false;
						}
					}
					GUILayout.Space(10);
					GUILayout.BeginVertical();
					EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
					GUILayout.EndVertical();
				}
			}
		}

		GUILayout.Space(5);

		EditorGUILayout.PropertyField(operatingModeProp);

		GUILayout.Space(10);

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
			GUILayout.Label("Physics / Teleporting", EditorStyles.boldLabel);
			GUILayout.Space(10);
			EditorGUILayout.PropertyField(callbackScriptProp);
			EditorGUILayout.PropertyField(momentumSnappingProp);
			EditorGUILayout.PropertyField(activatePartnerOnTeleportProp);
			EditorGUILayout.PropertyField(deactivateSelfOnTeleportProp);
			GUILayout.Space(5);
			GUILayout.EndVertical();
		}

		GUILayout.Space(10);
		showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced", true);
		if (showAdvanced) {
			EditorGUI.indentLevel++;

			if (serializedObject.isEditingMultipleObjects || operatingModeProp.enumValueIndex != (int)PortalBehaviourMode.PhysicsOnly) {
				GUILayout.Space(10);
				GUILayout.BeginVertical("box");
				GUILayout.Space(5);
				EditorGUILayout.HelpBox(
					"Leave unset to automatically generate render textures at runtime.",
					MessageType.Info);
				if (IndentedButton(renderTextureDocs)) {
					Application.OpenURL("https://github.com/aurycat/UdonPortals/wiki/Public-API-of-PortalBehaviour#viewtexl-viewtexr-rendertexture-field");
				}
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
					"Using oblique projection can interfere with some shader effects e.g. " +
					"water caustics. However, disabling oblique projection will break " +
					"portal rendering in many circumstances. Please read the docs for this " +
					"setting before changing:",
					MessageType.Info);
				if (IndentedButton(useObliqueProjectionDocs)) {
					Application.OpenURL("https://github.com/aurycat/UdonPortals/wiki/Public-API-of-PortalBehaviour#useobliqueprojection-bool-property");
				}
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
					"These are automatically set. You probably shouldn't change these.",
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

	bool IndentedButton(GUIContent content)
	{
		Rect r = EditorGUI.IndentedRect(GUILayoutUtility.GetRect(content, GUI.skin.button));
		return GUI.Button(r, content, GUI.skin.button);
	}
}