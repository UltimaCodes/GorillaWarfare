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
