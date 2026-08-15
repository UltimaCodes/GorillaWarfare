"""Turn one real banana into the five weapons.

Run headless:

    blender --background --python tools/banana_variants.py -- --source "D:/downloades/banana.fbx"

Supersedes banana_generator.py, which built the bananas from scratch out of a swept circle.
Those were fine as placeholders and always looked like what they were. This takes a proper
modelled banana and derives the whole set from it, so every weapon is recognisably the same
fruit at different sizes - which is the joke, and it only works if they match.

Conventions, inherited from the generator this replaces and asserted by WeaponCheck:

  * the weapon runs along Unity +Z, because the muzzle flash and the grip anchoring both
    assume it. Blender is Z-up, so the model is laid along Blender +Y and the FBX exporter
    is told to bake the axis conversion.
  * the origin does not matter. SingleShotGun.AnchorGrip re-seats every model at runtime so
    its blunt end sits on the grip, which is what stopped the sniper hanging behind the camera.
  * lengths match what WeaponCheck expects: between 0.15m and 0.9m for everything except the
    sniper, which is deliberately absurd.
"""

import argparse
import math
import os
import sys

import bpy
from mathutils import Vector

# name, length in metres, how many bananas, how fat relative to a natural one
VARIANTS = [
    # A normal banana, held like a revolver.
    ("BananaPistol",  0.30, 1, 1.00),

    # Two taped side by side. The Split.
    ("BananaShotgun", 0.50, 2, 1.00),

    # Longer, two handed.
    ("BananaRifle",   0.70, 1, 0.92),

    # Obnoxiously, unhelpfully long.
    ("BananaSniper",  1.45, 1, 0.70),

    # What's left after you eat one. Short and hooked.
    ("BananaPeel",    0.22, 1, 0.85),
]

OUT_DIR = "Assets/Resources/Models/Weapons"


def log(message):
    print(f"[variants] {message}")


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)

    for block in (bpy.data.meshes, bpy.data.objects, bpy.data.materials):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def import_source(path):
    ext = os.path.splitext(path)[1].lower()

    if ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=path)
    elif ext == ".obj":
        # Blender 4.x renamed this; try the new name first.
        if hasattr(bpy.ops.wm, "obj_import"):
            bpy.ops.wm.obj_import(filepath=path)
        else:
            bpy.ops.import_scene.obj(filepath=path)
    elif ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=path)
    elif ext == ".dae":
        bpy.ops.wm.collada_import(filepath=path)
    elif ext == ".blend":
        with bpy.data.libraries.load(path) as (src, dst):
            dst.objects = src.objects
        for obj in dst.objects:
            if obj is not None:
                bpy.context.collection.objects.link(obj)
    else:
        raise SystemExit(f"[variants] don't know how to import '{ext}'")

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise SystemExit("[variants] the source file has no mesh in it")

    log(f"imported {len(meshes)} mesh object(s) from {os.path.basename(path)}")
    return meshes


def join(meshes, name):
    for obj in bpy.context.scene.objects:
        obj.select_set(False)

    for obj in meshes:
        obj.select_set(True)

    bpy.context.view_layer.objects.active = meshes[0]

    if len(meshes) > 1:
        bpy.ops.object.join()

    obj = bpy.context.view_layer.objects.active
    obj.name = name
    return obj


def normalise(obj):
    """Lay the banana along +Y, centre it, and scale it to exactly one metre long.

    Everything after this is a straight scale, so the source can arrive at any size, in any
    orientation, and the variants still come out right.
    """
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    # Longest dimension is the length of the fruit, whichever axis it happens to be on.
    dims = list(obj.dimensions)
    longest = dims.index(max(dims))
    log(f"source dimensions {dims[0]:.3f} x {dims[1]:.3f} x {dims[2]:.3f}, long axis is {'XYZ'[longest]}")

    # Rotate that axis onto +Y.
    if longest == 0:
        obj.rotation_euler = (0, 0, math.radians(90))
    elif longest == 2:
        obj.rotation_euler = (math.radians(-90), 0, 0)

    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    # Centre on the origin.
    bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
    obj.location = (0, 0, 0)

    # One metre long, so a variant's length is just its scale.
    length = obj.dimensions.y
    if length < 1e-6:
        raise SystemExit("[variants] source has no length along its long axis")

    obj.scale = (1.0 / length, 1.0 / length, 1.0 / length)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    log(f"normalised to {obj.dimensions.x:.3f} x {obj.dimensions.y:.3f} x {obj.dimensions.z:.3f}")
    return obj


def build_variant(source, name, length, count, fatness):
    bpy.ops.object.select_all(action="DESELECT")

    pieces = []
    for i in range(count):
        copy = source.copy()
        copy.data = source.data.copy()
        copy.name = f"{name}_{i}"
        bpy.context.collection.objects.link(copy)

        # Length along Y, thickness on the other two. A longer banana is proportionally
        # thinner, or the sniper comes out looking like a canoe.
        girth = length * fatness
        copy.scale = (girth, length, girth)

        # Side by side for the shotgun, with a slight splay so it reads as two objects taped
        # together rather than one wide one.
        if count > 1:
            offset = (i - (count - 1) / 2.0) * girth * 0.85
            copy.location = (offset, 0, 0)
            copy.rotation_euler = (0, math.radians(6 * (1 if i else -1)), 0)

        pieces.append(copy)

    for piece in pieces:
        piece.select_set(True)

    bpy.context.view_layer.objects.active = pieces[0]

    if len(pieces) > 1:
        bpy.ops.object.join()

    obj = bpy.context.view_layer.objects.active
    obj.name = name

    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
    obj.location = (0, 0, 0)

    return obj


def export(obj, out_dir):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    d = obj.dimensions
    log(f"{obj.name}: {len(obj.data.vertices)} verts, {d.x:.3f} x {d.y:.3f} x {d.z:.3f}")

    # Blender +Y becomes Unity +Z with this conversion baked in, which is what puts the length
    # of the banana down the barrel.
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(out_dir, f"{obj.name}.fbx"),
        use_selection=True,
        add_leaf_bones=False,
        bake_anim=False,
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        path_mode="COPY",
    )


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True, help="the modelled banana to derive from")
    parser.add_argument("--out", default=OUT_DIR)
    args = parser.parse_args(argv)

    if not os.path.exists(args.source):
        raise SystemExit(f"[variants] no file at {args.source}")

    os.makedirs(args.out, exist_ok=True)

    clear_scene()
    source = normalise(join(import_source(args.source), "SourceBanana"))

    # Kept out of the way so the copies below are the only things exported.
    source.hide_set(True)

    for name, length, count, fatness in VARIANTS:
        variant = build_variant(source, name, length, count, fatness)
        export(variant, args.out)

        bpy.ops.object.select_all(action="DESELECT")
        variant.select_set(True)
        bpy.ops.object.delete()

    log(f"wrote {len(VARIANTS)} weapons to {args.out}")


if __name__ == "__main__":
    main()
