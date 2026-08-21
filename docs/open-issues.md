# Open issues

Reported from play and not yet worked on. `bug-log.md` is the historical list of what was
already broken and fixed. This is the live one.

Nothing open right now. Everything that was here — the slide's five complaints and the sandbox
loadout bug — was addressed 2026-08-21. The reasoning and the numbers behind each fix live where
the fix itself lives rather than being duplicated a third time in a document about to go stale:

- Slide kick/drag/exit/jump-boost retune, the bhop speed ceiling, and the SpeedRush threshold
  change are all explained in their own field tooltips in
  [`PlayerMovement.cs`](../Assets/Scripts/PlayerMovement.cs) and
  [`SpeedRush.cs`](../Assets/Scripts/SpeedRush.cs).
- The scrape's attack/release timing fix is explained inline in `SpeedRush.UpdateScrape()`.
- The rank text (copy, size, punch-on-rank-up) is explained inline in
  `GameHud.UpdateSlideCombo()`.
- The sandbox loadout bug — the one item here that was a genuine logic bug rather than a tuning
  problem — is written up properly in `bug-log.md`, below, since that's where a fixed bug
  belongs.

None of the retuning has been played yet. The numbers are worked out from the game's own
constants, not guessed, but "the maths says this should feel better" and "it feels better" are
different claims — this still needs a person on it before it's trusted, same as every other feel
number in this project.

The vine grapple, planned in `weapon-ideas.md` as closer to Attack on Titan's ODM gear than the
original entry, was also built the same day rather than only planned. New file
[`VineGrapple.cs`](../Assets/Scripts/VineGrapple.cs), bound to G by default. Also unplayed.
