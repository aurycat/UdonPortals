## PortalNoDepth

The most basic square portal; used in the "ExampleFramedPortal" prefab.
It is easy to use/setup, but it doesn't look as good without the depth
inset feature provided by the "Portal" prefab.

This prefab and the "Portal" prefab have been upgraded in v1.3 to no
longer be a flat Quad. They have physical thickness in the mesh to prevent
flickering when walking through it. The old versions of the prefab were
renamed to "VisualsOnlyPortal(NoDepth)"


## Portal

Improvement upon "PortalNoDepth" - you should use this when possible.
It is the same except it uses a different material which adds the
depth inset feature. This feature allows you stick your hand or pickups
'into' the portal without them clipping like when you stick your hand
through a wall.
Using this variant will require more careful setup of the portal's
surroundings: materials for the floor and walls behind/around the portal
need to be on a render queue of 1899 or below. Additionally, those floor
and wall materials ***CANNOT use Standard shader***.
See the main README file "Portal Depth" section for more info.


## VisualsOnlyPortal

This is the original portal prefab included with UdonPortals prior to v1.3.
While it *can* be used for walking through, the flat Quad mesh results in visual
flickers when walking through, especially in VR. The new Portal prefab that has
thickness doesn't have that issue. Hence the new name "VisualsOnlyPortal": you
should only use this prefab if the portal will never be walked through, only
looked through. For example, this should be used for a window, but not a door.


## VisualsOnlyPortalNoDepth

Same as "VisualsOnlyPortal", but uses the PortalNoDepth material.


## ExampleFramedPortal

Hopefully self explanatory :)