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
from mathutils import Matrix, Vector

# name, length in metres, how many bananas, how fat relative to a natural one
VARIANTS = [
    # A normal banana, held like a revolver.
    ("BananaPistol",  0.30, 1, 1.00),

    # Two taped side by side. The Split.
    ("BananaShotgun", 0.55, 2, 1.00),

    # Longer, two handed.
    ("BananaRifle",   0.70, 1, 0.78),

    # Obnoxiously, unhelpfully long. Thin as well as long - scaling a banana up uniformly gives
    # a log, and a log this size fills most of the screen and takes the fight with it. A spear
    # is funnier anyway.
    ("BananaSniper",  1.18, 1, 0.45),

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


def flatten(obj):
    """Bake the object's full world transform into its mesh and unparent it.

    glTF nests the mesh under a chain of nodes - Sketchfab_model, root, GLTF_SceneRootNode -
    carrying the Y-up to Z-up conversion. transform_apply only touches the object it's called
    on, so with a parent above it the rotation goes nowhere and obj.dimensions keeps reporting
    the unrotated mesh. Writing straight to the vertex data sidesteps parents, selection state
    and operator context in one go, all of which are awkward in background mode.
    """
    world = obj.matrix_world.copy()
    obj.parent = None
    obj.matrix_world = Matrix.Identity(4)
    obj.data.transform(world)
    return obj


def extent(obj):
    """Size of the mesh on each axis, straight off the vertices."""
    verts = obj.data.vertices
    if not verts:
        raise SystemExit("[variants] mesh has no vertices")

    lo = Vector(verts[0].co)
    hi = Vector(verts[0].co)

    for v in verts:
        for axis in range(3):
            lo[axis] = min(lo[axis], v.co[axis])
            hi[axis] = max(hi[axis], v.co[axis])

    return lo, hi, Vector((hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2]))


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
    orientation, under any node hierarchy, and the variants still come out right.
    """
    flatten(obj)

    lo, hi, size = extent(obj)
    longest = max(range(3), key=lambda i: size[i])
    log(f"source dimensions {size[0]:.3f} x {size[1]:.3f} x {size[2]:.3f}, long axis is {'XYZ'[longest]}")

    # Put that axis on +Y. +90 about Z takes X to Y; -90 about X takes Z to Y.
    if longest == 0:
        obj.data.transform(Matrix.Rotation(math.radians(90), 4, "Z"))
    elif longest == 2:
        obj.data.transform(Matrix.Rotation(math.radians(-90), 4, "X"))

    lo, hi, size = extent(obj)

    # Checked rather than assumed, because getting this wrong is invisible here and only shows
    # up as a banana held broadside on somebody's screen.
    if max(range(3), key=lambda i: size[i]) != 1:
        raise SystemExit(
            f"[variants] after rotating, the long axis is still not Y "
            f"({size[0]:.3f} x {size[1]:.3f} x {size[2]:.3f})")

    if size[1] < 1e-6:
        raise SystemExit("[variants] source has no length along its long axis")

    # Roll it so the curve is vertical.
    #
    # A banana is not round: it bends, and which way that bend points is the difference between
    # a weapon and a piece of fruit lying on a table. With the curve horizontal it reads as
    # something resting sideways even when it is aimed correctly down +Z, because the shape the
    # eye reads is the arc, not the bounding box. Vertical is how you'd hold one as a gun.
    #
    # The arc spans the second largest extent, so this puts that on Blender Z - which the FBX
    # export turns into Unity Y, or up.
    if size[0] > size[2]:
        obj.data.transform(Matrix.Rotation(math.radians(90), 4, "Y"))
        lo, hi, size = extent(obj)
        log(f"rolled the curve upright: {size[0]:.3f} x {size[1]:.3f} x {size[2]:.3f}")

    # Point the black end forwards.
    #
    # A banana has a thick woody stem at one end and a small dried blossom tip - the black nub -
    # at the other. The stem is the part you'd wrap a fist around, so it belongs at the grip,
    # and the black tip is where the shot comes out. It was the wrong way round: the muzzle
    # flash was going off on the stem.
    #
    # Which end is which is measured rather than assumed. Both ends taper, but they taper
    # differently: the stem narrows to a stalk and then stays a stalk, while the blossom end
    # runs down to a point. So the last sliver of the fruit is meaningfully fatter at the stem.
    stem_at, blossom_at = stem_end(obj)

    if stem_at > blossom_at:
        # Stem is at +Y, which is the muzzle end. Turn it round about the curve axis, so the
        # ends swap and the curve carries on pointing the same way.
        obj.data.transform(Matrix.Rotation(math.radians(180), 4, "Z"))
        log("turned it round - the stem was at the muzzle")

    lo, hi, size = extent(obj)

    # Centre on the origin, then scale so one unit of length is one metre.
    centre = Vector(((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2))
    obj.data.transform(Matrix.Translation(-centre))

    scale = 1.0 / size[1]
    obj.data.transform(Matrix.Diagonal((scale, scale, scale, 1.0)))

    _, _, size = extent(obj)
    log(f"normalised to {size[0]:.3f} x {size[1]:.3f} x {size[2]:.3f}")
    return obj


def stem_end(obj):
    """Work out which end of the banana carries the stem.

    Returns (position of the stem end, position of the blossom end) along Y, as -1 or +1.

    The stem is the narrow woody stalk. The blossom end is the blunt rounded one with the
    little black nub on it. So at the very tip, the stem end is the *thinner* of the two - I
    had this backwards first time and the render caught it, because a stalk reads as "thin
    stick" and a blossom end reads as "end of a banana".
    """
    lo, hi, size = extent(obj)

    # The outermost fiftieth. A twentieth included too much of the body, which left the two
    # ends within a few percent of each other - a margin too thin to decide anything on.
    band = size[1] * 0.02
    axis_lo, axis_hi = lo[1] + band, hi[1] - band

    low = [v.co for v in obj.data.vertices if v.co[1] <= axis_lo]
    high = [v.co for v in obj.data.vertices if v.co[1] >= axis_hi]

    def girth(points):
        """Mean distance from the middle of this slice, in the plane across the fruit.

        Measured about the slice's own centre, not about the length axis. A banana is bent, so
        its ends sit well off that axis and distance from it is mostly a reading of the curve -
        which came out near identical at both ends and said nothing about either.
        """
        if not points:
            return 0.0

        mid_x = sum(p[0] for p in points) / len(points)
        mid_z = sum(p[2] for p in points) / len(points)

        return sum(math.hypot(p[0] - mid_x, p[2] - mid_z) for p in points) / len(points)

    low_girth, high_girth = girth(low), girth(high)

    ratio = max(low_girth, high_girth) / max(min(low_girth, high_girth), 1e-6)

    log(f"tip girth: -Y {low_girth:.4f} ({len(low)} verts), "
        f"+Y {high_girth:.4f} ({len(high)} verts), {ratio:.2f}x apart - the stem is the thinner")

    # Too close to call. Better to say so than to quietly pick one and have the muzzle flash
    # come out of the wrong end of the fruit.
    if ratio < 1.15:
        log("WARNING: the two ends are nearly the same thickness - check the render")

    return (1, -1) if high_girth < low_girth else (-1, 1)


def build_variant(source, name, length, count, fatness, width_ratio):
    bpy.ops.object.select_all(action="DESELECT")

    # Length along Y, thickness on the other two. A longer banana is proportionally thinner,
    # or the sniper comes out looking like a canoe.
    girth = length * fatness

    pieces = []
    for i in range(count):
        copy = source.copy()
        copy.data = source.data.copy()
        copy.name = f"{name}_{i}"
        bpy.context.collection.objects.link(copy)

        matrix = Matrix.Diagonal((girth, length, girth, 1.0))

        # Side by side for the shotgun, with a slight splay so it reads as two objects taped
        # together rather than one wide one.
        #
        # Spaced off the banana's actual width rather than off its length. Using the length
        # pushed them a whole banana apart, which came out wider than the weapon was long.
        # Real bananas in a bunch nest into each other, so a bit over half a width is right.
        if count > 1:
            offset = (i - (count - 1) / 2.0) * (width_ratio * girth) * 0.55
            matrix = (Matrix.Translation((offset, 0, 0))
                      @ Matrix.Rotation(math.radians(6 * (1 if i else -1)), 4, "Y")
                      @ matrix)

        copy.data.transform(matrix)
        copy.select_set(True)
        pieces.append(copy)

    bpy.context.view_layer.objects.active = pieces[0]

    if len(pieces) > 1:
        bpy.ops.object.join()

    obj = bpy.context.view_layer.objects.active
    obj.name = name

    # Centre it, so the exported file isn't offset for no reason. Where the grip actually sits
    # is decided at runtime by SingleShotGun.AnchorGrip.
    lo, hi, _ = extent(obj)
    centre = Vector(((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2))
    obj.data.transform(Matrix.Translation(-centre))

    return obj


def export(obj, out_dir):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    _, _, size = extent(obj)
    log(f"{obj.name}: {len(obj.data.vertices)} verts, "
        f"{size[0]:.3f} x {size[1]:.3f} x {size[2]:.3f} (length on Y)")

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

        # STRIP rather than COPY. Copying embedded every map into a .fbm folder beside each
        # weapon - five copies of the same 4MB spec/gloss and 1MB diffuse, about 25MB of
        # duplicate texture in the repo. All five weapons are the same banana, so one copy of
        # the diffuse sits in the folder and every material points at it.
        path_mode="STRIP",
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

    # How wide the fruit is across, per unit of length. With the curve rolled upright this is
    # the thickness rather than the span of the arc, which is what two bananas taped together
    # actually have to clear.
    _, _, source_size = extent(source)
    width_ratio = source_size[0]
    log(f"width is {width_ratio:.3f} per unit length")

    for name, length, count, fatness in VARIANTS:
        variant = build_variant(source, name, length, count, fatness, width_ratio)
        export(variant, args.out)

        bpy.ops.object.select_all(action="DESELECT")
        variant.select_set(True)
        bpy.ops.object.delete()

    log(f"wrote {len(VARIANTS)} weapons to {args.out}")


if __name__ == "__main__":
    main()
