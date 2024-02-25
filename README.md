# UdonPortals Prefab

This prefab lets you create visual portals in VRChat worlds that can be looked through, walked through, fallen through, and appear as high quality as a Mirror. Even pickups can be thrown through the portals!  

![Looping video of walking through portals](.github/resources/videodemo.gif)


## Download

### [Install through VRChat Creator Companion (VPM)](https://aurycat.github.io/vpm)

[Or view all releases & download .unitypackage](https://github.com/aurycat/UdonPortals/releases)


## Basic Usage

1. Ensure UdonSharp is installed. (If your VCC is up to date, it is installed automatically for all VRC Worlds projects.)
2. Install UdonPortals into your VRC World project through VCC or directly through a .unitypackage.
3. Drag two "ExampleFramedPortal" prefab instances from the Packages/UdonPortals/Prefabs folder into your scene.
4. In each framed portal instance, there is a child object named "Portal". It is a good idea to rename each of these to something different, e.g. "Portal A" and "Portal B".
5. Ctrl-select both of the portal child objects. With both selected, in the Inspector...
6. ...click "Generate Render Textures" in the PortalBehaviour component.
7. ...click "Pair These Two Selected Portals"
8. ...click "Autodetect Main Camera"
9. ...click "Setup FOVDetector".

That's it! The portals will look black in the editor, but will work in Play Mode and in-game. I suggest looking at the provided example scene for more details.


## Adjusting the portal trigger

Each portal must have a trigger collider on it which is used to detect when players or objects are able to pass through the portal. The trigger must extend a short distance (about 1-2m is normally fine) in front and behind the portal, however **it must not extend AT ALL off the sides of the portal, horizontally or vertically**. The portal teleports the player when crossing the portal surface while their *head* is in the trigger collider, so if the trigger extends off the sides, the portal may teleport the player when they're only walking nearby it.

If you expect players or objects to be travelling very fast through the portal, such as in an infinite-fall situation, you will need to make the trigger longer. This is because otherwise you may go fast enough to pass all the way through the trigger in a single frame, making the portal not detect you. For example, if you have an infinite fall, I suggest extending the trigger of the bottom portal to go all the way up to the top portal, and significantly below the portal too, so that the player never leaves the trigger while falling.


## Using Portal Depth

An optional, more advanced feature of this prefab is "portal depth". This adds an effect where you can appear to stick your hand (or objects) through the portal, instead of them being clipped off like when you normally stick your hand through a wall. It is only a rendering illusion; the object won't really stick out of the partner portal!

Enabling portal depth can be done by using the "PortalView" material instead of "PortalViewNoDepth". However, some changes to the world will be necessary to keep it from looking broken. Primarily, any "background" surface that appears nearby behind a portal (within 2 meters behind a portal), such as a wall, floor, or ceiling, needs to have a material with a Render Queue less than the PortalView material, which in turn must have a Render Queue less than 2000. The default Render Queue for the "PortalView" material is 1900. I suggest 1800 for the background surfaces.

**[!] IMPORTANT: You CANNOT use the Standard shader for background surfaces, because VRChat will force all materials with the Standard shader to Render Queue 2000.** If you use Standard set to 1800, it will look fine in the editor, but won't work in-game! That's why I have a "ExampleBackgroundSurfaceForPortalDepth" shader which you can use instead. Other shaders like Poiyomi World will work too.


## Colliders behind the portal

Players or objects can only pass through a portal if there is no collider immediately behind the portal, such as a wall or floor, which would impede travelling through the portal surface.

One potential way of fixing this is to have colliders behind the portal turn off when the player is inside the portal trigger, on the front-side of the portal. However, implementing this would require logic specific to your world and your use case, so I have not included that in the prefab. I may try to implement some "built-in" solution for this eventually, but for now, you're on your own!


## Credits

Created by aurycat. Thanks to Nestorboy, Merlin, kingBigfootia, Esska, and my patient friends who helped me test it over and over :)

Check out my "Portals Prefab Demo" world on VRChat to see some examples of what you can make with this prefab.

This prefab is released under the [MIT license](https://mit-license.org/). Attribution is not required, but much appreciated!