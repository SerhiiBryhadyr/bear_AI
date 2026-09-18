# Bear AI

Unity AI module for the bear enemy: navigation, patrol, vision, player pursuit,
last-known-position search, and the `BearAI` additive scene.

## Tracked scope

- `Assets/Scripts/Bear/` — scripts, prefab, AI scene, and Unity `.meta` files.
- `Assets/BaseFolder/Bears/Bear_2/Bear_aimator2.controller` — animator state
  controller used by the bear prefab.
- `Packages/manifest.json`, `Packages/packages-lock.json`, and
  `ProjectSettings/TagManager.asset` — required Unity configuration.

## Required base-project assets

This repository intentionally excludes terrain, map content, textures, and the
41 MB bear FBX model. To run it inside the main project, keep the original model
and its `.meta` file at:

`Assets/BaseFolder/Bears/Bear_2/source/Bear Animated.fbx`

Open `SampleScene` and `Assets/Scripts/Bear/Scenes/BearAI.unity` additively when
testing. The main scene provides the Terrain, NavMesh, and the active object
tagged `Player`; `BearAI` provides the bear and its patrol points.
