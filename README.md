# PhysBone to DynamicBone
[![](https://img.shields.io/github/downloads/FACS01-01/PhysBone-to-DynamicBone/total.svg)](https://github.com/FACS01-01/PhysBone-to-DynamicBone/releases)
[![](https://img.shields.io/github/v/release/FACS01-01/PhysBone-to-DynamicBone)](https://github.com/FACS01-01/PhysBone-to-DynamicBone/releases/latest)

If you converted Dynamic Bones to VRChat PhysBones, this tool will help you revert it!


VRChat doesn't use all Dynamic Bone parameters, and in some cases combines 2 parameters into one, so a full 1-to-1 restoration isn't possible.
This is the closest it can get.


Lossless restoration of:

	- All colliders (sphere, capsule and plane)
	- Elasticity , Elasticity Distribution
	- Inert, Inert Distribution
	- Radius, Radius Distribution


Lossy restoration of:

	- Freeze Axis
	- Gravity, Force
	- Damping, Damping Distribution
	- Stiffness, Stiffness Distribution


Extras:

	- For Physbone colliders with custom rotations, an extra GameObject is added to be able to properly rotate the DynamicBone collider
	- For Physbones with custom Gravity Falloff: (new Gravity)^2 + (new Force)^2 = (old Gravity)^2

