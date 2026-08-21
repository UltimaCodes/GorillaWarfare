# Progression: killstreaks and a token economy

Planned, not built, per instruction — this is a design doc, not a task list. New 2026-08-21.

## Killstreaks are already planned — this doesn't re-plan them

The ask was "plan killstreak abilities." That's already done: `weapon-ideas.md` section 3 has a
full design — 3 kills (Banana Rain), 5 kills (Go Ape), 7 kills (The Zookeeper) — with the
reasoning already worked out (kills already heal and overshield, snowball risk in a five-player
room, why each rung is short/loud/visible). Repeating it here would just be two copies to keep in
sync. If what's wanted is different from what's already written there, that's a conversation to
have against that document, not a reason to draft a second one. Still just planned, not built —
2026-08-21 built the momentum-melee/vine pair and the sandbox fix, not this.

What's actually new this round is the gambling layer, below.

## The token/crate system

A CS2-crate-shaped loop: win a match, earn tokens, spend tokens on a crate opening back at the
main menu. Two things need real design before this touches code — what's inside the crates, and
where the tokens actually live, because this game currently has no server to keep either of them
honest.

### Earning

"After every win" was the brief, and it's a good gate on its own — it means the currency tracks
*winning*, not just showing up, which fits a five-friend lobby better than a currency everyone
accumulates identically regardless of how the match went.

**Starting recommendation: 1 token per win, crate costs 3–5.** Not derived from anything but the
session shape already established elsewhere in this project — deathmatch is 5 minutes, gun game
10 (`roadmap.md`), so a casual night is maybe 4–8 matches, half of them plausibly won across a
group of friends who are all decent at the game. That puts a crate at roughly one per session or
two: often enough to matter, rare enough that opening one is an event rather than a formality. This
is exactly the kind of number the project has already been explicit about elsewhere — "a feel
number and no check will ever tell you it's wrong" (weapon-ideas.md, on the Pineapple's
self-knockback) — so treat 1/3–5 as the number to start playtesting with, not the number to ship.

### Opening one

"Spin the wheel" and "play roulette" read as two names for the same reveal rather than two
separate minigames — CS2 doesn't have a roulette wheel, so this is most likely the user reaching
for two familiar casino images for one idea rather than asking for two systems. **Assuming one
reveal screen**, built like a crate opening: a horizontal reel of items scrolls past and lands on
what you won, weighted by rarity. Flag this assumption explicitly rather than build two screens on
a guess — cheap to confirm, expensive to build twice.

"You gotta leave the game for this" places it as its own panel off the main menu (already rebuilt
to the ULTRAKILL/Cruelty Squad direction per M5), not an in-match overlay. That also sidesteps a
real problem: nobody wants a slot-reel playing while four other people are mid-fight waiting on
them.

### What's actually inside the crate

This is the one place this doc disagrees with a literal reading of "CS2 crates" rather than just
filling in a gap: **cosmetic only, nothing that touches gameplay.** CS2's skins don't affect play
either, and that's not incidental — anything a crate could give that changes a fight (a damage
boost, a starting weapon, extra health) turns "I won more crates" into "I'm now harder to beat for
reasons that have nothing to do with this match," which is a bad feeling in a five-person friend
lobby in a way it isn't in a matchmade game with thousands of strangers to absorb the unfairness.
Keep the stakes social — bragging rights over a rare pull — not competitive.

Candidates that don't require new systems:

- **Kill-feed icons** — the feed already draws an icon per weapon; a cosmetic swap is a sprite
  lookup, not new plumbing.
- **Victory lines** — a line shown on the post-match scoreboard for whoever's holding one, cheap
  to add given the scoreboard already exists (M3).
- **Banana/weapon skins** — a colour or pattern swap on the existing banana models, same shape of
  change as `WeaponNaming` already does for on-screen names.

**Deliberately not on this list: the 12 player colours in `PlayerColours.cs`.** They're already
free, already in the lobby picker, and already played and confirmed working across 3–4 clients —
gating them behind crates would take a shipped, liked feature and make it worse to claw back a
progression hook. Any colour-flavoured reward should be a new cosmetic slot (a trail, an outline,
a muzzle tint) that doesn't touch the existing palette, not a lock bolted onto it after the fact.

### Where the tokens actually live

The honest constraint, from `roadmap.md`'s own "Known limitations": **there is no server, no
database, and no anti-cheat of any kind.** Hit registration is already client-authoritative and
that was an accepted, deliberate call at this scale. A token balance has the same shape of
problem — it can only live in local `PlayerPrefs` on each person's own machine, which means:

- it doesn't follow a player across machines or reinstalls
- it's trivially editable by anyone who'd want to (a text editor and `PlayerPrefs` is all it takes)
- "gambling" implies stakes worth protecting, and there's currently nothing to protect them with

None of that is disqualifying for a private game played only with friends — the project has
already made this exact trade-off once and it's been fine. The recommendation is to make it
explicit rather than silently inherit it: treat the whole system as **for-fun, not fair** — a
slot machine you could technically cheat if you wanted to, same trust model as everything else
here, and the payoff is a cosmetic bragging right, not a competitive edge. If that stops being
good enough later, the fix is a real backend, which is a much bigger project than this one.

## If this gets built

Roughly: kill-feed icon swap first (smallest, reuses the most), then the crate-opening screen
itself, then victory lines, then banana skins last (touches the model pipeline, the most work of
the four). Not a milestone number — this is pure meta-progression and doesn't block anything else
on `roadmap.md`, so it can slot in whenever, not before.
