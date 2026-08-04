# Portable UGC melee weapon

- Date: 2026-08-01
- Status: implemented; Unity live verification pending

An arbitrary placed object can be converted through the `melee_weapon`
meaning package. The package owns the required Triples and four Rule Block
bindings. Unity derives contact-query presentation from projected Weapon
meaning and does not use a prefab or object-name gameplay exception.

Verification covers the preset contract, generic adapter creation, and adapter
disablement after semantic removal.
