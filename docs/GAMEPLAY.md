# Aircraft gameplay

Build aircraft using the fourth tile in the game's native building menu. Airfields and the fictional **Aster Twin Spires** are added to that same menu; native build templates keep their original order, visibility and template numbers. The menu restores native entries if a Build 91 menu refresh omits them. Native blueprints that the game has not revealed remain hidden.

| Project | Materials |
| --- | --- |
| Aircraft | 32 logs, 16 stones |
| Airfield | 16 logs, 32 stones |
| Aster Twin Spires | 48 logs, 24 stones |
| Wreck repair | 8 logs, 4 stones |

The spires are an original, game-scale fictional structure. An aircraft impact damages or collapses the contacted section and unsupported sections above it. Debris uses local cosmetic physics, lasts 15 seconds and is capped at 32 pieces per tower pair. This is a gameplay effect, not a structural engineering simulation.

Children use native construction delivery. Build 37 accepts the complete native recipe. Build 91 records each delivered material and continues the native construction command; progress and the original project's family owner survive saving and joining.

An aircraft seats one pilot and eight passengers. Aim at an aircraft within 10 metres and press R to board. The host selects a free pilot seat when the aircraft belongs to your family or is shared; otherwise you board as a passenger. R toggles entry and exit. F keeps the native child go-to gesture. Open the flight controls with F8 near an aircraft. Board as a pilot or passenger, assign a nearby available child as pilot, or order nearby available children aboard. Children reserve seats and walk to the boarding door with native navigation. They must arrive before boarding; reservations expire after 30 seconds. Assigned children remain unavailable for competing native commands while seated or descending. Hold F and select a native go-to point to send your children there: airborne passenger children jump in sequence with visible parachutes, while a child pilot returns to the nearest clear airfield, lands and walks to that point. Grounded children leave the aircraft and walk there. A crash order takes priority over the pilot return. Fly higher or closer if the full passenger drop cannot reach the point. Children belonging to another family remain aboard.

A child pilot can fly to a built airfield or crash at a chosen location. Choose **Crash here: choose target**, aim at terrain or an object, and right-click. Escape cancels targeting. Airfields require a flat, dry, unobstructed strip. Runways are reserved during departure and arrival. The child autopilot uses simplified flight: it climbs above terrain sampled along the route, travels to the destination, descends vertically onto the runway entrance and moves to its parking position. Crash orders also clear intervening terrain before diving toward the selected point.

As the player pilot, S/Down Arrow raises the nose and W/Up Arrow lowers it. A/D or Left/Right Arrow rolls the aircraft; banking redirects lift sideways. Q/E turns the nose left/right. Shift increases throttle. Holding Ctrl or Space cuts engine thrust and applies the brake. Rebind keys in `BepInEx/config/dzejdies.familytime.aircraft.cfg`. Passengers and pilots cannot simultaneously walk, use tools or jump. Press R to exit. On the ground, children remain seated. In flight, your parachute opens; if you were the pilot, the aircraft continues without a pilot and eventually crashes. Exiting as a passenger preserves the child pilot’s flight order. Native mouse tool and command clicks are consumed while seated or descending; F remains available for child go-to. Camera movement, pause and save remain available.

Serious impacts leave a wreck and create a large fireball, sparks, smoke, flash and positional impact sound. Building parts intersecting the impact are destroyed; nearby parts outside it remain. The host determines collision damage and passenger trajectories, then replicates building removal to clients. Passengers are protected during ejection and return to native movement when they land. To repair, stand near the wreck with eight loose logs and four loose stones nearby; held materials are excluded.

Solo uses the same aircraft rules without requiring multiplayer. In a multiplayer session, all participants need matching aircraft versions and model assets plus Family Time Multiplayer 0.1.14 (protocol 20). The host validates tower placement, owns tower damage and collapse, and sends typed state events; clients render those host results. The host also assigns seats, simulates flight and validates commands against family ownership. Other players can occupy passenger seats, but only the owner's family controls its aircraft and children.
