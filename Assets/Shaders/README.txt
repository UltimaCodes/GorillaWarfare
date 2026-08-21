Custom shaders for this project, first ones written 2026-08-22. Nothing borrowed - Built-in
Render Pipeline (no URP/HDRP asset anywhere in Assets/Settings), so these are plain CGPROGRAM
shaders rather than Shader Graph.

JungleSky.shader - the skybox. A three-stop gradient (dark ground, warm gold horizon haze, teal
zenith) plus a soft sun glow, chosen over a photographic cubemap because there's no source
photography for a jungle to build one from and a few flat colours match the rest of this game's
placeholder-material look better than a realistic sky would anyway.

Verified with Tools/Gorilla Warfare/Photograph the skybox, which renders it from several angles
to Library/skybox-check-*.png. Worth knowing: the first version looked like it had a black seam
right at the horizon in a screenshot, which turned out to be a genuine optical illusion, not a
bug - decoded the actual PNG's pixel values down that column and the gradient was perfectly
smooth and continuous the whole way through, just compressed into a narrow enough band next to a
much brighter colour that it read as a hard black line by eye. Simplified the blend to one
continuous two-segment gradient anyway (was two overlapping smoothsteps before), which isn't
wrong to have done even though the bug it was aimed at wasn't real - it's simpler code for the
same result. The lesson that mattered: read the pixels before trusting the screenshot.

The material lives at Assets/Resources/Sky/JungleSky.mat, assigned as the scene's skybox.

Retuned 2026-08-22 against a reference grid of dense fantasy-jungle art - canopy green pushed
much further up the dome (green is most of the sky now, open colour is what's left at the very
top rather than the other way round) and procedural god rays added around the sun direction:
several sine waves of the angle around the sun axis, added at different frequencies so the
streaks land irregular rather than a visible pinwheel, masked by both distance from the sun and
height (they fade approaching the horizon haze, reading as the canopy blocking them lower down).
Nothing photographic added - still a shader, no textures - the reference was for colour and mood,
not for a cubemap to trace over.

ToonOutline.shader / ScreenOutline.shader - two different toon outlines, only one of which is
actually wired into the game. Built the same day, in that order, because the first one turned out
not to be good enough on the gorilla specifically.

ToonOutline is the classic inverted-hull trick: render a mesh's back faces pushed out along their
own normals, in a flat colour, with the real mesh drawn on top. Applied as an extra material slot
on a renderer rather than a shader swap, so it never touches the object's own material - see
ToonOutline.cs. Looked clean on the pineapple (a simple convex shape) and gappy on the gorilla, and
the instinct to just widen it made that worse, not better. Isolating the outline pass alone (hiding
the real mesh) showed why: the shell isn't one continuous surface on this model, it's a lot of
separately-pushed triangles that overlap and fight each other at nearly equal depth wherever the
character's own geometry overlaps itself - the arm crossing the torso, fingers, joints. That's a
property of this specific low-poly, self-overlapping mesh, not a bug in the shader; a simpler
convex prop doesn't have it, which is exactly why the pineapple looked fine. Not deleted - still a
real, working technique for a simple prop that doesn't need a full-screen pass for one object - but
nothing in the game applies it any more.

ScreenOutline is what's actually running, on the local camera (see PlayerController.cs). Reads the
camera's own depth+normals buffer instead of mesh geometry - draws a line wherever depth or surface
normal jumps sharply between a pixel and its neighbours - so it can't tear the way the mesh version
did, by construction: there's no per-vertex push to disagree with itself. One component on one
camera outlines everything in view - players, projectiles, weapons, world geometry - which is also
the actual answer to "try it on other things too": there's no per-object step to remember.

Weapons were missing from all of this at first, reported back after playing - because the held
weapon renders through ViewModelCamera's own second camera (own depth buffer, drawn after the
world camera without clearing colour, so the gun can't clip through walls), which explicitly
never ran any post-processing. Added ScreenOutline there too now, and it's the one exception to
that "no post processing" rule. Verifying it needed one more step than the world camera did:
manually rendering both cameras back-to-back into one shared RenderTexture (reproducing by hand
what Unity's own camera stack normally does automatically) showed no outline on the weapon at
all, which looked like the same bug all over again. Rendering the weapon camera alone, into its
own fresh target, showed a perfectly clean outline - so the camera's own render was never the
problem, the manual two-camera compositing in the test was. Real gameplay renders both cameras
through the actual per-frame camera stack, not two manual Render() calls back to back, so this
was a test-harness gap rather than a real one.

Worth recording since it cost real time: the first attempt at testing this looked like it did
nothing at all, on a re-verified-clean shader, with depthTextureMode forced on, even bypassing
OnRenderImage with a direct Graphics.Blit. Every variant of that test was a one-shot
Camera.Render() call from editor script code outside Play Mode - and that turned out to be the
actual problem, not the shader. The depth+normals prepass and OnRenderImage both appear to be
scheduled as part of Unity's own per-frame camera pipeline, which a manual Render() call outside
Play Mode never actually runs. Confirmed by testing inside a real Play Mode session instead (enter
play mode, let a few real frames pass, then capture) - Tools/Gorilla Warfare/Photograph the screen
outline (play mode) - where it worked immediately and cleanly, gorilla included. The lesson: a
one-shot editor render is a fundamentally different code path from a frame that actually played,
not just a faster version of the same thing, and rendering-pipeline features that hook into the
per-frame loop need to be tested through it.
